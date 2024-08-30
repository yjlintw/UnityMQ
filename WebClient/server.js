const express = require('express');
const WebSocket = require('ws');
const dgram = require('dgram');

// Configuration
const UDP_PORT = 9999; // Example port for UnityMQ
const WS_PORT = 3000;  // WebSocket and Express server port
const DISCOVERY_PORT = 8888; // Discovery port for UnityMQ
const HEARTBEAT_INTERVAL = 10000; // 10 seconds
const RECONNECT_DELAY = 5000; // 5 seconds delay for reconnect

// Set up UDP client for discovery and communication
const udpClient = dgram.createSocket('udp4');
let serverAddress = null;
let topics = {}; // Store all non-status topics
let statusData = {}; // Store all status topics organized by guid and instanceID
let udpConnected = false; // Track UDP connection state

// Discover UnityMQ server
function discoverServer() {
    return new Promise((resolve, reject) => {
        const discoveryMessage = Buffer.from('DISCOVERY_SERVER');
        const discoveryClient = dgram.createSocket('udp4');

        discoveryClient.on('listening', () => {
            discoveryClient.setBroadcast(true);
            console.log('Discovery client ready to send broadcasts');
        });

        discoveryClient.bind(() => {
            discoveryClient.setBroadcast(true);
        });

        discoveryClient.on('message', (msg, rinfo) => {
            if (msg.toString() === 'SERVER_RESPONSE') {
                serverAddress = rinfo.address;
                discoveryClient.close();
                resolve(serverAddress);
            }
        });

        discoveryClient.send(discoveryMessage, 0, discoveryMessage.length, DISCOVERY_PORT, '255.255.255.255', (err) => {
            if (err) {
                reject(err);
            }
        });
    });
}

// Function to handle incoming UDP messages
function handleIncomingMessage(msg) {
    const message = JSON.parse(msg.toString());
    const { Topic, Values } = message;

    if (Topic.startsWith('status/')) {
        console.log(`Received status update: ${msg.toString()}`);
        updateStatusData(Topic, Values);
    } else if (!Topic.startsWith('heartbeat') && !Topic.startsWith('command')) {
        console.log(`Received topic update: ${msg.toString()}`);
        topics[Topic] = Values;
        broadcastUpdate({ type: 'generalUpdate', topic: Topic, data: Values });
    }
}

// Update the statusData structure based on the topic and values received
function updateStatusData(topic, values) {
    const parts = topic.split('/');
    if (parts.length === 2) { // status/guid
        const guid = parts[1];

        if (!statusData[guid]) {
            statusData[guid] = {};
        }

        for (const [key, value] of Object.entries(values)) {
            const [instanceID, fieldName] = key.split('.');
            if (!statusData[guid][instanceID]) {
                statusData[guid][instanceID] = {};
            }
            statusData[guid][instanceID][fieldName] = value; // Update specific field value

            // Debugging output
            console.log(`Broadcasting status update for guid: ${guid}, instanceID: ${instanceID}, field: ${fieldName}, value: ${value}`);
        }

        broadcastUpdate({ type: 'statusUpdate', guid: guid, data: values });
    }
}

// Subscribe to all topics
function subscribeToAllTopics() {
    if (!serverAddress) {
        console.error('Server address not discovered. Cannot subscribe to topics.');
        return;
    }

    const subscribeMessage = JSON.stringify({
        Topic: 'subscribe',
        Timestamp: new Date().toISOString(),
        Values: { topic: '#' },
        IsPersistent: false
    });

    const messageBuffer = Buffer.from(subscribeMessage, 'utf8');
    udpClient.send(messageBuffer, 0, messageBuffer.length, UDP_PORT, serverAddress, (err) => {
        if (err) {
            console.error('Failed to send subscription message:', err);
            handleDisconnect(); // Handle disconnection
        } else {
            console.log('Subscribed to all topics.');
            udpConnected = true; // Mark as connected
        }
    });
}

// Send heartbeat to the server
function sendHeartbeat() {
    if (!serverAddress) {
        console.error('Server address not discovered. Cannot send heartbeat.');
        return;
    }

    const heartbeatMessage = JSON.stringify({
        Topic: 'heartbeat',
        Timestamp: new Date().toISOString(),
        Values: {},
        IsPersistent: false
    });

    const messageBuffer = Buffer.from(heartbeatMessage, 'utf8');
    udpClient.send(messageBuffer, 0, messageBuffer.length, UDP_PORT, serverAddress, (err) => {
        if (err) {
            console.error('Failed to send heartbeat:', err);
            handleDisconnect(); // Handle disconnection
        } else {
            console.log('Sent heartbeat to server.');
        }
    });
}

// Handle disconnection and reconnection attempts
function handleDisconnect() {
    if (udpConnected) {
        console.log('Lost connection to UnityMQ server. Attempting to reconnect...');
        udpConnected = false;
        setTimeout(() => {
            startConnection(); // Attempt to reconnect
        }, RECONNECT_DELAY);
    }
}

// Start connection process
function startConnection() {
    discoverServer()
        .then(() => {
            console.log(`Discovered UnityMQ server at ${serverAddress}`);
            subscribeToAllTopics();
            setInterval(sendHeartbeat, HEARTBEAT_INTERVAL); // Start sending heartbeats
        })
        .catch((err) => {
            console.error('Failed to discover UnityMQ server:', err);
            handleDisconnect(); // Attempt to reconnect if discovery fails
        });
}

// Set up Express server
const app = express();
const server = require('http').createServer(app);

// Serve static files from 'public' directory
app.use(express.static('public'));

// Set up WebSocket server
const wss = new WebSocket.Server({ server });

wss.on('connection', (ws) => {
    console.log('New WebSocket connection established.');

    // Send the latest status data and topics to the newly connected client
    ws.send(JSON.stringify({ type: 'allTopics', data: topics }));
    ws.send(JSON.stringify({ type: 'allStatusData', data: statusData }));

    ws.on('message', (message) => {
        console.log(`Received message from WebSocket client: ${message}`);

        // Parse the incoming message as a command
        try {
            const command = JSON.parse(message);
            if (command.Topic && command.Values) {
                sendCommandToUnity(command);
            } else {
                console.error('Invalid command format');
            }
        } catch (err) {
            console.error('Failed to parse message:', err);
        }
    });

    ws.on('close', () => {
        console.log('WebSocket connection closed.');
    });
});

// Broadcast the latest message update to all connected WebSocket clients
function broadcastUpdate(updateMessage) {
    const message = JSON.stringify(updateMessage);
    console.log(`Broadcasting message to WebSocket clients: ${message}`); // Debugging output
    wss.clients.forEach((client) => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(message);
        }
    });
}

// Function to send a command to Unity
function sendCommandToUnity(command) {
    if (!serverAddress) {
        console.error('Server address not discovered. Cannot send command to Unity.');
        return;
    }

    const commandMessage = JSON.stringify(command);
    const messageBuffer = Buffer.from(commandMessage, 'utf8');

    udpClient.send(messageBuffer, 0, messageBuffer.length, UDP_PORT, serverAddress, (err) => {
        if (err) {
            console.error('Failed to send command to Unity:', err);
            handleDisconnect(); // Handle disconnection
        } else {
            console.log(`Sent command to Unity: ${commandMessage}`);
        }
    });
}

// Listen for incoming UDP messages from UnityMQ
udpClient.on('message', (msg, rinfo) => {
    handleIncomingMessage(msg);
});

// Start discovery and then subscribe to topics
startConnection();

// Handle errors
udpClient.on('error', (err) => {
    console.error(`UDP error:\n${err.stack}`);
    udpClient.close();
    handleDisconnect(); // Attempt to reconnect on error
});

// Start the HTTP and WebSocket server
server.listen(WS_PORT, () => {
    console.log(`WebSocket and HTTP server listening on port ${WS_PORT}`);
});