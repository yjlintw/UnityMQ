using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityMQ;
using Random = UnityEngine.Random;

public class TestClient : MonoBehaviour
{
    private ClientManager _clientManager;

    public string thisTopic = "ClientA";
    public double messageInterval = 1;
    private double _lastSentTime;
    
    [RemoteStatus("cubeScale", false, "OnScaleChanged")]
    public float scaleFloat = 0.5f;
    
    [RemoteStatus("testInt")]
    public int testint = 3;

    [RemoteStatus("testVector3")] 
    public Vector3 testVector3 = new Vector3(1, 1, 1);
    
    [RemoteStatus(displayName:"cubeColor", readOnly:false, callbackMethodName:"OnColorChanged")]
    public Color cubeColor = Color.gray;

    
    private Renderer _renderer;
    private void Awake()
    {
        _clientManager = new ClientManager();
        _renderer = GetComponentInChildren<Renderer>();
    }

    private async void OnEnable()
    {
        await _clientManager.StartClientAsync(gameObject);
    }

    private void OnDisable()
    {
        _clientManager.StopClient();
    }

    public async void Update()
    {
        if (Time.time - _lastSentTime > messageInterval)
        {
            var message = new Message($"{thisTopic}/Random1", DateTime.UtcNow,
                new Dictionary<string, object> { { "Value", Random.Range(0, 100) } });
            var message1 = new Message($"{thisTopic}/Random2", DateTime.UtcNow,
                new Dictionary<string, object> { { "Value", Random.Range(-100, 100) } });
            var message2 = new Message($"{thisTopic}/Random3", DateTime.UtcNow,
                new Dictionary<string, object> { { "Value", Random.Range(0.0f, 10.0f) } });
            
            // await _clientManager.SendMessageAsync(message);
            // await _clientManager.SendMessageAsync(message1);
            // await _clientManager.SendMessageAsync(message2);
            
            _lastSentTime = Time.time;
        }
    }

    public void OnScaleChanged(object newValue)
    {
        gameObject.transform.localScale = new Vector3(scaleFloat, scaleFloat, scaleFloat);
    }

    public void OnColorChanged(object newValue)
    {
        _renderer.material.color = cubeColor;
    }
}
