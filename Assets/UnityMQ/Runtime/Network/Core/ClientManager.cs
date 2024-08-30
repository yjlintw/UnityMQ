using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor.PackageManager;
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
        private readonly TimeSpan _statusUpdateInterval = TimeSpan.FromSeconds(5);
        private readonly TimeSpan _discoveryRetryInterval =
            TimeSpan.FromSeconds(NetworkConfig.DiscoveryRetryIntervalSeconds);
        private readonly MessageQueueManager _messageQueueManager = new MessageQueueManager();
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public Action onServerConnected;
        public string ClientId { get; private set; }

        private CommandHandlerGenerator _commandHandlerGenerator;
        private GameObject _rootObject;
        private Dictionary<string, Action<Dictionary<string,object>>> _commandHandlers = new Dictionary<string, Action<Dictionary<string,object>>>();

        public async Task StartClientAsync(GameObject rootObject)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            ClientId = GenerateClientId();
            await StartClientDiscoveryAsync(_cancellationTokenSource.Token);
            
            this._rootObject = rootObject;
            
            // Subscribe to all relevant command for this client
            SubscribeToClientCommands();
            
            // Use CommandHandlerGenerator to create handlers
            _commandHandlerGenerator = new CommandHandlerGenerator(RegisterCommandHandler);
            _commandHandlerGenerator.GenerateHandlersForHierarchy(rootObject, ClientId);
            
            _ = StartStatusUpdateAsync(_cancellationTokenSource.Token);
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
            
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }
        
        private string GenerateClientId()
        {
            return Guid.NewGuid().ToString();
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
                    continue;
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

            if (receivedMessage == TopicConfig.ServerResponse)
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
                        
                        // Handle web command if applicable
                        if (_commandHandlers.ContainsKey(message.Topic))
                        {
                            _commandHandlers[message.Topic]?.Invoke(message.Values);
                        }
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

        private void SubscribeToClientCommands()
        {
            string commandSubscriptionTopic = $"{TopicConfig.CommandBase}/{ClientId}";
            SubscribeAsync(commandSubscriptionTopic, HandleIncomingCommand);
            Debug.Log($"Subscribed to {commandSubscriptionTopic}");
        }
        private void HandleIncomingCommand(Message message)
        {
            if (_commandHandlers.ContainsKey(message.Topic))
            {
                try
                {
                    var deserializedData = new Dictionary<string, object>();

                    var jsonSetting = new JsonSerializerSettings
                    {
                        Converters = new List<JsonConverter>
                        {
                            new Vector3Converter(),
                            new ColorConverter(),
                            new QuaternionConverter(),
                            new Vector2Converter(),
                        }
                    };
                    
                    foreach (var kvp in message.Values)
                    {
                        // Deserialize each value to its appropriate type
                        object deserializedValue = JsonConvert.DeserializeObject<object>(kvp.Value.ToString(), jsonSetting);
                        deserializedData[kvp.Key] = deserializedValue;
                    }

                    _commandHandlers[message.Topic]?.Invoke(deserializedData);
                    Debug.Log($"Handled command for topic {message.Topic} with data: {deserializedData}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error processing command for topic {message.Topic}: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"No command handler registered for topic [{message.Topic}].");
            }
        }

        public void RegisterCommandHandler(string topic, Action<Dictionary<string, object>> handler)
        {
            if (_commandHandlers.TryAdd(topic, handler))
            {
                Debug.Log($"Registered handler for topic [{topic}].");
            }
        }

        public void UnregisterCommandHandler(string topic)
        {
            if (_commandHandlers.ContainsKey(topic))
            {
                _commandHandlers.Remove(topic);
                Debug.Log($"Unregistered command handler for topic [{topic}].");
            }
        }

        private async Task StartStatusUpdateAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    SendStatusUpdate();
                    await Task.Delay(_statusUpdateInterval, cancellationToken);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Status update task was canceled or encountered an error: " + e.Message);
            }
        }

        private void SendStatusUpdate()
        {
            var statusData = new Dictionary<string, object>();
            MonoBehaviour[] components = _rootObject.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var component in components)
            {
                var fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    var attr = field.GetCustomAttribute<RemoteStatusAttribute>();
                    if (attr != null)
                    {
                        string statusKey = $"{_rootObject.GetInstanceID()}.{attr.DisplayName}";
                        object fieldValue = field.GetValue(component);
                        
                        // Configure JSON settings to ignore self-referencing loop
                        var jsonSetting = new JsonSerializerSettings
                        {
                            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                            Converters = new List<JsonConverter>
                            {
                                new Vector3Converter(),
                                new ColorConverter(),
                                new QuaternionConverter(),
                                new Vector2Converter(),
                            }
                        };
                        
                        // Log the field value before serialization
                        string serializedValue = JsonConvert.SerializeObject(fieldValue, jsonSetting);
                        statusData[statusKey] = serializedValue;
                        // Log the serialized value
                        Debug.Log($"Serialized value for {statusKey}: {serializedValue}");
                    }
                }
            }
            
            // Log the full status data dictionary before sending
            Debug.Log($"Status Data to Send: {JsonConvert.SerializeObject(statusData)}");
            
            // Create and send the status message
            string topic = $"{TopicConfig.StatusBase}/{ClientId}";
            var message = new Message(topic, DateTime.UtcNow, statusData);
            byte[] data = message.Serialize();
            try
            {
                _udpClient.SendAsync(data, data.Length);
                Debug.Log($"Sent status update to topic [{topic}].");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to send status update: {e.Message}");
            }
        }
    }
}