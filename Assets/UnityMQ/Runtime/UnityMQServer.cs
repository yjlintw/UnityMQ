using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMQ
{
    public class UnityMQServer : MonoBehaviour
    {
        private ServerManager _server;

        private void Awake()
        {
            _server = new ServerManager();
        }

        private async void OnEnable()
        {
            await _server.StartServerAsync();
        }

        private async void OnDisable()
        {
            _server.StopServer();
        }
    }
}