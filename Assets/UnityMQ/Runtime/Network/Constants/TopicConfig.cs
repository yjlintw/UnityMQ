using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMQ.Constants
{
    public static class TopicConfig
    {
        public const string Subscribe = "subscribe";
        public const string Unsubscribe = "unsubscribe";
        public const string Disconnect = "disconnect";
        
        public const string DiscoveryServer = "DISCOVERY_SERVER";
        public const string ServerResponse = "SERVER_RESPONSE";
        
        public const string TopicKey = "topic";
        public const string Heartbeat = "heartbeat";

        public const string CommandBase = "command";
        public const string StatusBase = "status";
    }
}