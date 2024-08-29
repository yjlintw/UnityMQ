using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityMQ.Constants;

namespace UnityMQ
{
    public class ClientManager
    {
        private UdpClient _udpClient;
        private UdpClient _discoveryClient;
        private bool _discoveryRunning = false;
        private bool _clientRunning = false;
        private DateTime _lastServerResponseTime;
        private readonly int _udpServerPort = NetworkConfig.UdpServerPort;
        private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(NetworkConfig.HeartbeatIntervalSeconds);
        private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(NetworkConfig.HeartbeatTimeoutSeconds);
        private readonly TimeSpan _reconnectInterval = TimeSpan.FromSeconds(NetworkConfig.ReconnectIntervalSeconds);
        private readonly TimeSpan _discoveryRetryInterval =
            TimeSpan.FromSeconds(NetworkConfig.DiscoveryRetryIntervalSeconds);
        private readonly MessageQueueManager _messageQueueManager = new MessageQueueManager();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public Action onServerConnected;

        public async Task StartClientAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            await StartClientDiscoveryAsync(_cancellationTokenSource.Token);
        }

        public void StopClient()
        {
            if (_clientRunning)
            {
                _clientRunning = false;
                _cancellationTokenSource.Cancel();
                _udpClient?.Close();
                _udpClient?.Dispose();
                _udpClient = null;
                Debug.Log("Client is stopped");
            }
            
            if (_discoveryRunning)
            {
                _discoveryRunning = false;
                _discoveryClient?.Close();
                _discoveryClient?.Dispose();
                _discoveryClient = null;
            }
        }

        public async Task StartClientDiscoveryAsync(CancellationToken cancellationToken)
        {
            _discoveryRunning = true;
            while (_discoveryRunning && !cancellationToken.IsCancellationRequested)
            {
                IPAddress serverAddress = await GetServerAddressAsync();
                if (serverAddress == null)
                {
                    Debug.Log("No Server Found. Retry server discovery...");
                    await Task.Delay(_discoveryRetryInterval, cancellationToken);
                }
                
                // Connect to server
                Debug.Log($"Server Discovered at: {serverAddress}");
                await ConnectToServerAsync(serverAddress, cancellationToken);
                break;
            }
        }

        public async Task<IPAddress> GetServerAddressAsync()
        {
            using var udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            try
            {
                await SendDiscoveryRequestAsync(udpClient);
                return await ReceiveServerResponseAsync(udpClient);
            }
            catch (Exception e)
            {
                Debug.LogError($"Discovery attempt failed: {e.Message}");
                return null;
            }
        }

        private async Task SendDiscoveryRequestAsync(UdpClient udpClient)
        {
            try
            {
                var discoveryEndpoint = new IPEndPoint(IPAddress.Broadcast, NetworkConfig.DiscoveryPort);
                var localhostEndpoint = new IPEndPoint(IPAddress.Loopback, NetworkConfig.DiscoveryPort);
                byte[] discoveryData = Encoding.UTF8.GetBytes(TopicConfig.DiscoveryServer);
                
                Debug.Log($"Sending discovery message...");
                await udpClient.SendAsync(discoveryData, discoveryData.Length, discoveryEndpoint);
                await udpClient.SendAsync(discoveryData, discoveryData.Length, localhostEndpoint);
            }
            catch (Exception e)
            {
                Debug.Log($"Send DiscoveryRequest failed: {e.Message}");
            }
        }

        private async Task<IPAddress> ReceiveServerResponseAsync(UdpClient udpClient)
        {
            var receiveTask = udpClient.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(_discoveryRetryInterval)) != receiveTask) return null;
            
            var result = receiveTask.Result;
            string receivedMessage = Encoding.UTF8.GetString(result.Buffer);

            if (receivedMessage == TopicConfig.DiscoveryServer)
            {
                Debug.Log($"Server discovered at: {result.RemoteEndPoint.Address}");
                return result.RemoteEndPoint.Address;
            }

            return null;
        }

        private async Task ConnectToServerAsync(IPAddress serverAddress, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _udpClient = new UdpClient();
                    _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    _udpClient.Connect(serverAddress, _udpServerPort);
                    _clientRunning = true;
                    _lastServerResponseTime = DateTime.UtcNow;
                    
                    Debug.Log("Connected to server");
                    _ = ListenForMessageAsync(cancellationToken);
                    _ = SendHeartbeatAsync(cancellationToken);
                    onServerConnected?.Invoke();
                    break;
                }
                catch (Exception e)
                {
                    Debug.Log($"Failed to connect to server: {serverAddress}. Retrying... \n{e.Message}");
                    await Task.Delay(_reconnectInterval, cancellationToken);
                    // TODO: return to discovery stage after it fails x times
                }
            }
        }

        private async Task ListenForMessageAsync(CancellationToken cancellationToken)
        {
            while (_clientRunning && _udpClient != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var message = Message.Deserialize(result.Buffer);

                    if (message == null)
                    {
                        Debug.Log("Received a malformed message. Skipping...");
                        continue;
                    }

                    if (message.Topic == TopicConfig.Heartbeat)
                    {
                        _lastServerResponseTime = DateTime.UtcNow;
                        Debug.Log("Receive server heartbeat");
                    }
                    else
                    {
                        _messageQueueManager.Publish(message);
                    }
                }
                catch (Exception e)
                {
                    Debug.Log($"Error processing message: {e.Message}");
                    _clientRunning = false;
                    break;
                }
            }

            Debug.Log("Stopped listening for messages.");
            await AttemptReconnectAsync(cancellationToken);
        }

        private async Task AttemptReconnectAsync(CancellationToken cancellationToken)
        {
            Debug.Log("Attempting to reconnect...");
            // Stop current client
            StopClient();
            
            // Re-enter discovery
            await StartClientDiscoveryAsync(cancellationToken);
        }

        private async Task SendHeartbeatAsync(CancellationToken cancellationToken)
        {
            while (_clientRunning && _udpClient != null && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Message heartbeatMessage = new Message(TopicConfig.Heartbeat, DateTime.UtcNow, null);
                    byte[] data = heartbeatMessage.Serialize();
                    await _udpClient.SendAsync(data, data.Length);
                    
                    await Task.Delay(_heartbeatInterval, cancellationToken);
                    if (DateTime.UtcNow - _lastServerResponseTime > _heartbeatTimeout)
                    {
                        Debug.Log($"Server is not responding. Stop sending heartbeat...");
                        _clientRunning = false;
                        _ = AttemptReconnectAsync(cancellationToken);
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            }
        }

        public async Task SendMessageAsync(Message message)
        {
            if (_udpClient == null || !_clientRunning)
            {
                Debug.LogWarning($"Cannot send message. Client is not connected");
                return;
            }

            try
            {
                byte[] data = message.Serialize();
                await _udpClient.SendAsync(data, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"Network error when sending message. Attempting to reconnect... \n{e.Message}");
                _clientRunning = false;
                await AttemptReconnectAsync(_cancellationTokenSource.Token);
            }
        }

        public async Task SubscribeAsync(string topic, Action<Message> callback)
        {
            if (_udpClient == null || !_clientRunning)
            {
                Debug.LogWarning($"Cannot subscribe to topic [{topic}]. Client is not connected");
                return;
            }

            var message = new Message(TopicConfig.Subscribe, DateTime.UtcNow,
                new Dictionary<string, object> { { TopicConfig.TopicKey, topic } });
            byte[] data = message.Serialize();
            await _udpClient.SendAsync(data, data.Length);
            _messageQueueManager.Subscribe(topic, callback);
            Debug.Log($"Subscribed to topic [{topic}].");
        }

        public async Task UnsubscribeAsync(string topic, Action<Message> callback)
        {
            _messageQueueManager.Unsubscribe(topic, callback);
            if (_udpClient == null || !_clientRunning)
            {
                Debug.LogWarning("Cannot unsubscribe. Client is not connected");
                return;
            }

            var message = new Message(TopicConfig.Unsubscribe, DateTime.UtcNow,
                new Dictionary<string, object> { { TopicConfig.TopicKey, topic } });
            
            byte[] data = message.Serialize();
            await _udpClient.SendAsync(data, data.Length);
            Debug.Log($"Unsubscribed to topic [{topic}].");
        }
    }
}