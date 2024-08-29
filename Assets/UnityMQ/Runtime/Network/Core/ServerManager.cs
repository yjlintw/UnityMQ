using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityMQ.Constants;

namespace UnityMQ
{
    public class ServerManager
    {
        private UdpClient _udpServer;
        private UdpClient _discoveryServer;
        private Dictionary<IPEndPoint, DateTime> _clientLastSeen = new Dictionary<IPEndPoint, DateTime>();
        private bool _serverRunning = false;
        private bool _discoveryRunning = false;
        private readonly int _udpServerPort = NetworkConfig.UdpServerPort;
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(NetworkConfig.HeartbeatIntervalSeconds);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(NetworkConfig.HeartbeatTimeoutSeconds);
        private readonly MessageQueueManager _messageQueueManager = new MessageQueueManager();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        

        public async Task StartServerAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _udpServer = new UdpClient(_udpServerPort);
            _serverRunning = true;
            _discoveryRunning = true;
            
            Debug.Log("UDP Server started, waiting for clients...");

            _ = ListenForMessageAsync(_cancellationTokenSource.Token);
            _ = ListenForDiscoveryRequestAsync(_cancellationTokenSource.Token);
            _ = SendHeartbeatAsync(_cancellationTokenSource.Token);
        }

        public void StopServer()
        {
            if (_serverRunning) return;
            _serverRunning = false;
            _discoveryRunning = false;
            
            _cancellationTokenSource.Cancel();
            
            _udpServer?.Close();
            _udpServer?.Dispose();
            _udpServer = null;
            
            _discoveryServer?.Close();
            _discoveryServer?.Dispose();
            _discoveryServer = null;
            
            Debug.Log("UDP Server stopped");
        }

        private async Task ListenForMessageAsync(CancellationToken cancellationToken)
        {
            while (_serverRunning && _udpServer != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync();
                    var message = Message.Deserialize(result.Buffer);
                    if (message == null)
                    {
                        Debug.LogWarning("Received a malformed message. Skipping...");
                        continue;
                    }
                    
                    HandleServerMessage(result.RemoteEndPoint, message);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error processing message. Attempt to restart... \n{e.Message}");
                    break;
                }
            }
            Debug.Log("UDP Server stopped");
        }

        private async Task ListenForDiscoveryRequestAsync(CancellationToken cancellationToken)
        {
            _discoveryServer = new UdpClient(NetworkConfig.DiscoveryPort);
            while (_discoveryRunning && _discoveryServer != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _discoveryServer.ReceiveAsync();
                    string receivedMessage = Encoding.UTF8.GetString(result.Buffer);

                    if (receivedMessage == TopicConfig.DiscoveryServer)
                    {
                        byte[] responseData = Encoding.UTF8.GetBytes(TopicConfig.ServerResponse);
                        await _discoveryServer.SendAsync(responseData, responseData.Length, result.RemoteEndPoint);
                        Debug.Log("Send response to client");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in discovery process: {e.Message}");
                }
            }
                
        }

        private void HandleServerMessage(IPEndPoint endpoint, Message message)
        {
            if (!_clientLastSeen.ContainsKey(endpoint))
            {
                _clientLastSeen[endpoint] = DateTime.UtcNow;
            }

            switch (message.Topic)
            {
                case TopicConfig.Subscribe:
                    SubscribeClientToTopic(endpoint, message.Values[TopicConfig.TopicKey].ToString());
                    break;
                case TopicConfig.Unsubscribe:
                    UnsubscribeClientToTopic(endpoint, message.Values[TopicConfig.TopicKey].ToString());
                    break;
                case TopicConfig.Disconnect:
                    DisconnectClient(endpoint);
                    break;
                case TopicConfig.Heartbeat:
                    if (_clientLastSeen.ContainsKey(endpoint))
                    {
                        _clientLastSeen[endpoint] = DateTime.UtcNow;
                    }
                    
                    Debug.Log($"Received hearbeat message from {endpoint}");
                    break;
                default:
                    _messageQueueManager.Publish(message);
                    break;
            }
        }

        private void SubscribeClientToTopic(IPEndPoint endpoint, string topic)
        {
            Action<Message> callback = msg => SendMessageAsync(endpoint, msg);
            _messageQueueManager.Subscribe(endpoint, topic, callback);
            Debug.Log($"Client {endpoint} subscribed to topic {topic}");
        }

        private void UnsubscribeClientToTopic(IPEndPoint endpoint, string topic)
        {
            _messageQueueManager.Unsubscribe(endpoint, topic);
            Debug.Log($"Client {endpoint} unsubscribed to topic {topic}");
        }

        private void DisconnectClient(IPEndPoint endpoint)
        {
            var topicList = _messageQueueManager.GetClientSubscribedTopics(endpoint);
            foreach (var topic in topicList)
            {
                UnsubscribeClientToTopic(endpoint, topic);
            }
            
            Debug.Log($"Client {endpoint} disconnected");
        }

        public async Task SendMessageAsync(IPEndPoint endpoint, Message message)
        {
            byte[] data = message.Serialize();

            try
            {
                await _udpServer.SendAsync(data, data.Length, endpoint);
                Debug.Log($"Relayed message to subscriber {endpoint} on topic {message.Topic}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to relay message to {endpoint}: {e.Message}");
            }
        }

        public async Task SendHeartbeatAsync(CancellationToken cancellationToken)
        {
            while (_serverRunning && _udpServer != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Message heartbeatMessage = new Message(TopicConfig.Heartbeat, DateTime.UtcNow, null);
                    byte[] data = heartbeatMessage.Serialize();

                    foreach (var client in _clientLastSeen.Keys.ToList())
                    {
                        if (DateTime.UtcNow - _clientLastSeen[client] > _heartbeatTimeout)
                        {
                            Debug.Log($"Client {client} has timed out. Removing client from active list");
                            DisconnectClient(client);
                        }
                        else
                        {
                            await _udpServer.SendAsync(data, data.Length, client);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
            
            await Task.Delay(_heartbeatInterval, cancellationToken);
        }
        
    }
}