using System;
using UnityEngine;
using System.Text;
using System.Threading.Tasks;
using NativeWebSocket;

public abstract class WebSocketConnector : MonoBehaviour
{

    protected string DefaultIP = "localhost";
    protected string DefaultPort = "8080";


    protected string host;
    protected string port;

   
    private WebSocket socket;



    protected int HeartbeatInMs = 5000; //only for middleware mode
    protected bool DesktopMode = false;
    public bool fixedProperties = false;
    protected bool UseMiddlewareDM = true;

    protected int numErrorsBeforeDeconnection = 10;
    protected int numErrors = 0;

    async void Start()
    {
        host = PlayerPrefs.GetString("IP", DefaultIP);
        port = PlayerPrefs.GetString("PORT", DefaultPort);

        if (DesktopMode)
        {
            host = "localhost";
            port = DefaultPort;
            
        } else if (fixedProperties)
        {
            host = DefaultIP;
            port = DefaultPort;
            
        } else
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                host = DefaultIP;
            }

            if (string.IsNullOrWhiteSpace(port))
            {
                port = DefaultPort;
            }
        }


        socket = new WebSocket("ws://" + host + ":" + port + "/");

        socket.OnOpen += () =>
        {
            HandleConnectionOpen();
        };

        // Add OnMessage event listener
        socket.OnMessage += (byte[] msg) =>
        {
            string mes = Encoding.UTF8.GetString(msg);
            // Debug.Log("WS received message: " + mes);
            ManageMessage(mes);
        };

        // Add OnError event listener
        socket.OnError += (string errMsg) =>
        {
            Debug.LogError("WS error: " + errMsg);
        };

        // Add OnClose event listener
        socket.OnClose += (WebSocketCloseCode code) =>
        {
            HandleConnectionClosed();
        };

        // Connect to the server 
        await socket.Connect();

    }

    protected virtual void HandleConnectionClosed()
    {

    }
    protected virtual void ManageMessage(string message)
    {

    }

    protected virtual void HandleConnectionOpen()
    {

    }

    private async void OnApplicationQuit()
    {
        await CloseSocketAsync();
    }

    async void OnDestroy() {
        await CloseSocketAsync();
    }

    // ############################## HANDLERS ##############################

    // #######################################################################

    void Update()
    {
    #if !UNITY_WEBGL || UNITY_EDITOR
        if (socket != null)
        {
            socket.DispatchMessageQueue();
        }
    #endif
    }


    protected async void SendMessageToServer(string message)
    {
        await SendMessageToServerAsync(message);
    }

    protected async Task SendMessageToServerAsync(string message)
    {
        if (socket == null || socket.State != WebSocketState.Open)
        {
            Debug.LogWarning("WebSocketConnector: cannot send message because the socket is not open.");
            return;
        }

        await socket.SendText(message);
    }

    protected WebSocket GetSocket() {
        return socket;
    }

    protected async Task CloseSocketAsync()
    {
        if (socket == null)
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.Connecting)
            {
                await socket.Close();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("WebSocketConnector: error while closing socket: " + exception.Message);
        }
    }

}
