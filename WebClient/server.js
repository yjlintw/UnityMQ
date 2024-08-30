// File: server.js
const express = require('express');
const WebSocket = require('ws');
const dgram = require('dgram');

// Configuration
const UDP_PORT = 9999; // Example port for UnityMQ
const WS_PORT = 3000;  // WebSocket and Express server port
const DISCOVERY_PORT = 8888; // Discovery port for UnityMQ
const HEARTBEAT_INTERVAL = 10000; // 5 seconds

// Set up UDP client for discovery and communication
const udpClient = dgram.createSocket('udp4');
let serverAddress = null;

// Discover UnityMQ server
function discoverServer() {
    return new Promise((resolve, reject) => {
        const discoveryMessage = Buffer.from('DISCOVERY_SERVER');
        const discoveryClient = dgram.createSocket('udp4');

        // Enable broadcasting for discovery client
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

// Subscribe to all topics
function subscribeToAllTopics() {
    if (!serverAddress) {
        console.error('Server address not discovered. Cannot subscribe to topics.');
        return;
    }

    // JSON-formatted subscription message
    const subscribeMessage = JSON.stringify({
        Topic: 'subscribe',
        Timestamp: new Date().toISOString(),
        Values: { topic: '#' },
        IsPersistent: false
    });
    
    const messageBuffer = Buffer.from(subscribeMessage, 'utf8');
    udpClient.send(messageBuffer, 0, messageBuffer.length, UDP_PORT, serverAddress);
    console.log('Subscribed to all topics.');
}

// Send heartbeat to the server
function sendHeartbeat() {
    if (!serverAddress) {
        console.error('Server address not discovered. Cannot send heartbeat.');
        return;
    }

    // JSON-formatted heartbeat message
    const heartbeatMessage = JSON.stringify({
        Topic: 'heartbeat',
        Timestamp: new Date().toISOString(),
        Values: {},
        IsPersistent: false
    });

    const messageBuffer = Buffer.from(heartbeatMessage, 'utf8');
    udpClient.send(messageBuffer, 0, messageBuffer.length, UDP_PORT, serverAddress);
    console.log('Sent heartbeat to server.');
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

    ws.on('close', () => {
        console.log('WebSocket connection closed.');
    });
});

// Listen for incoming UDP messages from UnityMQ
udpClient.on('message', (msg, rinfo) => {
    const message = msg.toString();
    console.log(`Message from UnityMQ: ${message}`);

    // Broadcast message to all connected WebSocket clients
    wss.clients.forEach((client) => {
        if (client.readyState === WebSocket.OPEN) {
            client.send(message);
        }
    });
});

// Start discovery and then subscribe to topics
discoverServer()
    .then(() => {
        console.log(`Discovered UnityMQ server at ${serverAddress}`);
        subscribeToAllTopics();
        setInterval(sendHeartbeat, HEARTBEAT_INTERVAL); // Start sending heartbeats
    })
    .catch((err) => {
        console.error('Failed to discover UnityMQ server:', err);
    });

// Handle errors
udpClient.on('error', (err) => {
    console.error(`UDP error:\n${err.stack}`);
    udpClient.close();
});

// Start the HTTP and WebSocket server
server.listen(WS_PORT, () => {
    console.log(`WebSocket and HTTP server listening on port ${WS_PORT}`);
});