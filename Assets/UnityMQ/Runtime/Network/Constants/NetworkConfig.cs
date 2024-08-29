using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMQ.Constants
{
    public static class NetworkConfig
    {
        public static readonly int UdpServerPort = 9999;
        public static readonly int DiscoveryPort = 8888;
        public static readonly int ReconnectIntervalSeconds = 5;
        public static readonly int HeartbeatIntervalSeconds = 10;
        public static readonly int HeartbeatTimeoutSeconds = 30;
        public static readonly int DiscoveryRetryIntervalSeconds = 5;
    }
}
