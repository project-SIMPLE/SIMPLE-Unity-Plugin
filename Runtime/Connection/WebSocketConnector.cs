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
    private float nextSocketClosedWarningTime;
    private int receivedMessageLogCount;
    private const float SocketClosedWarningIntervalSeconds = 2f;

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

        string url = "ws://" + host + ":" + port + "/";
        Debug.Log("[GAMA][CONNECTION][START] url=" + url);
        socket = new WebSocket(url);

        socket.OnOpen += () =>
        {
            Debug.Log("[GAMA][CONNECTION][OPEN]");
            HandleConnectionOpen();
        };

        // Add OnMessage event listener
        socket.OnMessage += (byte[] msg) =>
        {
            string mes = Encoding.UTF8.GetString(msg);
            LogReceivedMessage(mes);
            ManageMessage(mes);
        };

        // Add OnError event listener
        socket.OnError += (string errMsg) =>
        {
            Debug.LogError("[GAMA][CONNECTION][ERROR] " + errMsg);
        };

        // Add OnClose event listener
        socket.OnClose += (WebSocketCloseCode code) =>
        {
            Debug.Log("[GAMA][CONNECTION][CLOSE] code=" + code);
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
        if (!IsSocketOpen)
        {
            float now = Time.realtimeSinceStartup;
            if (now >= nextSocketClosedWarningTime)
            {
                Debug.LogWarning("[GAMA][CONNECTION][WARN] socket not open; skipping send state=" + GetSocketStateForLog());
                nextSocketClosedWarningTime = now + SocketClosedWarningIntervalSeconds;
            }
            return;
        }

        await socket.SendText(message);
    }

    public bool IsSocketOpen
    {
        get { return socket != null && socket.State == WebSocketState.Open; }
    }

    protected string GetSocketStateForLog()
    {
        return socket == null ? "null" : socket.State.ToString();
    }

    protected WebSocket GetSocket() {
        return socket;
    }

    private void LogReceivedMessage(string message)
    {
        receivedMessageLogCount++;
        if (receivedMessageLogCount > 20 && receivedMessageLogCount % 100 != 0)
        {
            return;
        }

        Debug.Log(
            "[GAMA][CONNECTION][MESSAGE] type=" + ResolveMessageTypeForLog(message) +
            " length=" + (message != null ? message.Length : 0));
    }

    private static string ResolveMessageTypeForLog(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "unknown";
        }

        const string marker = "\"type\"";
        int typeIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (typeIndex < 0)
        {
            return "unknown";
        }

        int colonIndex = message.IndexOf(':', typeIndex + marker.Length);
        if (colonIndex < 0)
        {
            return "unknown";
        }

        int firstQuoteIndex = message.IndexOf('"', colonIndex + 1);
        if (firstQuoteIndex < 0)
        {
            return "unknown";
        }

        int secondQuoteIndex = message.IndexOf('"', firstQuoteIndex + 1);
        if (secondQuoteIndex <= firstQuoteIndex)
        {
            return "unknown";
        }

        return message.Substring(firstQuoteIndex + 1, secondQuoteIndex - firstQuoteIndex - 1);
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
