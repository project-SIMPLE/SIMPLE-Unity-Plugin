using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Linq;

public class ConnectionManager : WebSocketConnector
{
     
    private ConnectionState currentState;
    private bool connectionRequested; 

    // called when the connection state is manually changed
    public event Action<ConnectionState> OnConnectionStateChanged;

    // called when a "json_simulation" message is received
    public event Action<String, String> OnServerMessageReceived;

    // called when a "json_state" message is received 
    public event Action<JObject> OnConnectionStateReceived;

    // called when a connection request fails
    public event Action<bool> OnConnectionAttempted;

    public static ConnectionManager Instance = null;

    //use to seperate messages in the case where the middleware is not used
    protected String MessageSeparator  = "|||";

private String AgentToSendInfo = "simulation[0].unity_linker[0]";
     
    
    // ############################################# UNITY FUNCTIONS #############################################
    void Awake() {
        Instance = this;
        UpdateConnectionState(ConnectionState.DISCONNECTED);
    }

   

    public string GetMessageSeparator()
    {
        return MessageSeparator; 
    }

    // ############################################# CONNECTION HANDLER #############################################
    public void UpdateConnectionState(ConnectionState newState) {
        
        switch (newState) {
            case ConnectionState.PENDING:
                break;
            case ConnectionState.CONNECTED:
                Debug.Log("[GAMA] WebSocket connected");
                break;
            case ConnectionState.AUTHENTICATED:
                Debug.Log("[GAMA] Player authenticated");
                break;
            case ConnectionState.DISCONNECTED:
                break;
            default:
                break;
        }

        currentState = newState;
        OnConnectionStateChanged?.Invoke(newState);        
    }

    // ############################################# HANDLERS #############################################

    protected override void HandleConnectionOpen()
    {
        
            var jsonId = new Dictionary<string, string> {
                {"type", "connection"},
                { "id", StaticInformation.getId() },
                { "heartbeat", ""+ HeartbeatInMs}
            }; 
            string jsonStringId = JsonConvert.SerializeObject(jsonId);
            SendMessageToServer(jsonStringId);
        
       
    }

    protected override void ManageMessage(string message)
    {
        try
        {
            JObject jsonObj = JObject.Parse(message);
            string type = (string)jsonObj["type"];
            switch (type)
                {
                    case "ping":
                        var jsonId = new Dictionary<string, string> {{"type", "pong"}};
                        string jsonStringId = JsonConvert.SerializeObject(jsonId);
                        SendMessageToServer(jsonStringId);
                        break;
                    case "json_state":
                        OnConnectionStateReceived?.Invoke(jsonObj);
                        bool authenticated = (bool)jsonObj["in_game"];
                        bool connected = (bool)jsonObj["connected"];

                        if (authenticated && connected)
                        {
                            if (!IsConnectionState(ConnectionState.AUTHENTICATED))
                            {
                                UpdateConnectionState(ConnectionState.AUTHENTICATED);
                            }

                        }
                        else if (connected && !authenticated)
                        {
                            if (!IsConnectionState(ConnectionState.CONNECTED))
                            {
                                connectionRequested = false;
                                UpdateConnectionState(ConnectionState.CONNECTED);
                                OnConnectionAttempted?.Invoke(true);
                            }
                            else
                            {
                            }

                        }  
                        break;  

                    case "json_output":
                        JObject content = (JObject)jsonObj["contents"];
                        String firstKey = content.Properties().Select(pp => pp.Name).FirstOrDefault();
                        OnServerMessageReceived?.Invoke(firstKey, content.ToString());
                        break;

                    default:
                        break;
                }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[GAMA] Error parsing message: " + ex.Message);
        }
    }

    protected override void HandleConnectionClosed() {
        if (connectionRequested) {
            connectionRequested = false;
            OnConnectionAttempted?.Invoke(false);
        } 
        UpdateConnectionState(ConnectionState.DISCONNECTED);
    }

    // ############################################# UTILITY FUNCTIONS #############################################
    public async void TryConnectionToServer() {
        if(IsConnectionState(ConnectionState.DISCONNECTED)) {
            connectionRequested = true;
            UpdateConnectionState(ConnectionState.PENDING);

            await GetSocket().Connect();
             
           
        } else {
        }
        
    }
     
    public async void DisconnectFromServer() {
        if(!IsConnectionState(ConnectionState.DISCONNECTED)) {
            await CloseSocketAsync();
            UpdateConnectionState(ConnectionState.DISCONNECTED);
        } else {
        }
    }

    public bool IsConnectionState(ConnectionState currentState) {
        return this.currentState == currentState;
    }

    public void SendExecutableExpression(string expression) {
        Dictionary<string, string> jsonExpression = null;
        jsonExpression = new Dictionary<string, string> {
            {"type", "expression"},
            {"expr", expression}
        };

        string jsonStringExpression = JsonConvert.SerializeObject(jsonExpression);
        SendMessageToServer(jsonStringExpression);

        /*, new Action<bool>((success) => {
            if (!success) {
                numErrors++;
                Debug.LogError("ConnectionManager: Failed to send executable expression");
                if (numErrors > numErrorsBeforeDeconnection)
                {
                    GetSocket().Close();
                   currentState = (ConnectionState.DISCONNECTED);
                    numErrors = 0;
                }
            } else
            {
                numErrors = 0;
            }
        }));*/
    }

    public void SendExecutableAsk(string action, Dictionary<string,string> arguments)
    {
        string argsJSON = JsonConvert.SerializeObject(arguments);
        Dictionary<string, string> jsonExpression = null;
        jsonExpression = new Dictionary<string, string> {
            {"type", "ask"},
            {"action", action},
            {"args", argsJSON},
            {"agent", AgentToSendInfo }
        };

        string jsonStringExpression = JsonConvert.SerializeObject(jsonExpression);

        SendMessageToServer(jsonStringExpression);

        /*, new Action<bool>((success) => {
            if (!success)
            {
                numErrors++;
                Debug.LogError("ConnectionManager: Failed to send executable ask");
                if (numErrors > numErrorsBeforeDeconnection)
                {
                    GetSocket().Close();
                    currentState = (ConnectionState.DISCONNECTED);
                    numErrors = 0;
                }
            } else
            {
                numErrors = 0;
            }
    }));*/
    }

    public async void DisconnectProperly() {
        Dictionary<string,string> jsonExpression = new Dictionary<string,string> {
            {"type", "disconnect_properly"}
        };
        string jsonStringExpression = JsonConvert.SerializeObject(jsonExpression);
        await SendMessageToServerAsync(jsonStringExpression);
        DisconnectFromServer();
    }

    public string GetConnectionId() {
        return StaticInformation.getId();
    }



    public void Reconnect()
    {

        currentState = ConnectionState.DISCONNECTED;
        TryConnectionToServer();
    }


}


public enum ConnectionState {
    DISCONNECTED,
    // waiting for connection to be established
    PENDING, 
    // connection established, waiting for authentication
    CONNECTED,
    // connection established and authenticated
    AUTHENTICATED
}
