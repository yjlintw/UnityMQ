using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMQ
{
    /// <summary>
    ///     Message for each pub/sub message
    /// </summary>
    public class Message
    {
        public string Topic { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> Values { get; set; }
        public bool IsPersistent { get; set; }

        public Message(string topic, DateTime timestamp, Dictionary<string, object> values, bool isPersistent = false)
        {
            Topic = topic;
            Timestamp = timestamp;
            Values = values;
            IsPersistent = isPersistent;
        }

        public byte[] Serialize()
        {
            string json = JsonConvert.SerializeObject(this);
            return System.Text.Encoding.UTF8.GetBytes(json);
        }

        public static Message Deserialize(byte[] data)
        {
            string json = System.Text.Encoding.UTF8.GetString(data);
            try
            {
                return JsonConvert.DeserializeObject<Message>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"{e.Message}\n{json}");
                return null;
            }
        }
    }
}