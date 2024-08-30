using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace UnityMQ
{
    public class MessageQueueManager
    {
        private Dictionary<string, HashSet<Action<Message>>> _subscriptions =
            new Dictionary<string, HashSet<Action<Message>>>();

        private Dictionary<string, Message> _persistentMessages = new Dictionary<string, Message>();

        private readonly Dictionary<IPEndPoint, Dictionary<string, Action<Message>>> _clientSubscriptions =
            new Dictionary<IPEndPoint, Dictionary<string, Action<Message>>>();

        public void Subscribe(IPEndPoint clientEndPoint, string topic, Action<Message> callback)
        {
            // client-specific subscription
            if (!_clientSubscriptions.ContainsKey(clientEndPoint))
            {
                _clientSubscriptions[clientEndPoint] = new Dictionary<string, Action<Message>>();
            }
            
            _clientSubscriptions[clientEndPoint][topic] = callback;
            Subscribe(topic, callback);
        }

        public void Subscribe(string topic, Action<Message> callback)
        {
            if (!_subscriptions.ContainsKey(topic))
            {
                _subscriptions[topic] = new HashSet<Action<Message>>();
            }
            
            _subscriptions[topic].Add(callback);

            foreach (var persistentTopic in _persistentMessages.Keys.Where(t => IsTopicMatch(t, topic)))
            {
                callback(_persistentMessages[persistentTopic]);
            }
        }

        public void Unsubscribe(IPEndPoint clientEndPoint, string topic)
        {
            // no previous subscription
            if (!_clientSubscriptions.ContainsKey(clientEndPoint) ||
                !_clientSubscriptions[clientEndPoint].ContainsKey(topic)) return;
            
            // has subscription
            var callback = _clientSubscriptions[clientEndPoint][topic];
            _clientSubscriptions[clientEndPoint].Remove(topic);

            if (_subscriptions.ContainsKey(topic))
            {
                _subscriptions[topic].Remove(callback);
                
                if (_subscriptions[topic].Count == 0)
                {
                    _subscriptions.Remove(topic);
                }
            }

            if (_clientSubscriptions[clientEndPoint].Count == 0)
            {
                _clientSubscriptions.Remove(clientEndPoint);
            }
        }

        public void Unsubscribe(string topic, Action<Message> callback)
        {
            if (_subscriptions.ContainsKey(topic))
            {
                _subscriptions[topic].Remove(callback);
            }
        }

        public void Publish(Message message)
        {
            foreach (var topic in _subscriptions.Keys)
            {
                if (!IsTopicMatch(message.Topic, topic)) continue;
                foreach (var callback in _subscriptions[topic])
                {
                    callback(message);
                }
            }

            if (message.IsPersistent)
            {
                _persistentMessages[message.Topic] = message;
            }
            else
            {
                _persistentMessages.Remove(message.Topic);
            }
        }

        public void ClearPersistentMessages(string topic)
        {
            _persistentMessages.Remove(topic);
        }

        public static bool IsTopicMatch(string messageTopic, string subscriptionTopic)
        {
            return subscriptionTopic == "#" || messageTopic == subscriptionTopic ||
                   messageTopic.StartsWith(subscriptionTopic + "/");
        }

        public bool IsClientSubscribed(IPEndPoint clientEndPoint, string topic)
        {
            return _clientSubscriptions.ContainsKey(clientEndPoint) &&
                   _clientSubscriptions[clientEndPoint].ContainsKey(topic);
        }

        public List<string> GetClientSubscribedTopics(IPEndPoint clientEndPoint)
        {
            if (_clientSubscriptions.TryGetValue(clientEndPoint, out var subscription))
            {
                return subscription.Keys.ToList();
            }
            
            return new List<string>();
        }
    }
}