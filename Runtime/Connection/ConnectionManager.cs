using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

public class ConnectionManager : WebSocketConnector
{
    private const string PlaySessionIdPrefKey = "ProjectSimple.GamaUnity.Play.PlayerId";
    public const string MiddlewareDiagnosticsEnabledPrefKey = "ProjectSimple.GamaUnity.Diagnostics.MiddlewareConsole";

    private ConnectionState currentState;
    private bool connectionRequested;
    private string lastStalePlayerStateWarning;
    private bool diagnosticsSentForCurrentSocket;

    public bool IsCurrentPlayerAuthenticated { get; private set; }
    public bool HasCurrentPlayerState { get; private set; }

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
    void Awake()
    {
        InitializeRuntimePlayId();
        GamaLog.Dev("[GAMA][CONNECTION][ID] Runtime player id=" + StaticInformation.getId());
        Instance = this;
        UpdateConnectionState(ConnectionState.DISCONNECTED);
    }

   

    public string GetMessageSeparator()
    {
        return MessageSeparator; 
    }

    // ############################################# CONNECTION HANDLER #############################################
    public void UpdateConnectionState(ConnectionState newState) {
        if (newState == ConnectionState.DISCONNECTED)
        {
            IsCurrentPlayerAuthenticated = false;
            HasCurrentPlayerState = false;
        }

        switch (newState) {
            case ConnectionState.PENDING:
                break;
            case ConnectionState.CONNECTED:
                GamaLog.Dev("[GAMA] Connected to simple.webplatform.");
                break;
            case ConnectionState.AUTHENTICATED:
                GamaLog.Dev("[GAMA] Unity player authenticated.");
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

    protected override async void HandleConnectionOpen()
    {
        diagnosticsSentForCurrentSocket = false;
        string id = StaticInformation.getId();
        GamaLog.Dev("[GAMA][CONNECTION][OPEN] id=" + id);
        var jsonId = new Dictionary<string, string> {
            {"type", "connection"},
            { "id", id },
            { "heartbeat", "" + HeartbeatInMs}
        };
        string jsonStringId = JsonConvert.SerializeObject(jsonId);
        await SendMessageToServerAsync(jsonStringId);
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
                        string serverPlayerId = (string)jsonObj["id_player"] ?? (string)jsonObj["id"];
                        bool authenticated = (bool)jsonObj["in_game"];
                        bool connected = (bool)jsonObj["connected"];
                        if (IsStaleUnityPlayState(serverPlayerId))
                        {
                            if (!TryAdoptMiddlewareUnityPlayState(serverPlayerId, connected, authenticated))
                            {
                                IsCurrentPlayerAuthenticated = false;
                                HasCurrentPlayerState = false;
                                if (connected && !IsConnectionState(ConnectionState.CONNECTED))
                                {
                                    connectionRequested = false;
                                    UpdateConnectionState(ConnectionState.CONNECTED);
                                    OnConnectionAttempted?.Invoke(true);
                                }

                                break;
                            }
                        }

                        if (connected)
                        {
                            AdoptMiddlewarePlayerIdIfNeeded(serverPlayerId);
                        }

                        HasCurrentPlayerState = connected;
                        IsCurrentPlayerAuthenticated = authenticated && connected;
                        OnConnectionStateReceived?.Invoke(jsonObj);

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

                        SendUnityDiagnosticsOnce(jsonObj);
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
            GamaLog.Warning("[GAMA] Error parsing message: " + ex.Message);
        }
    }

    private async void SendUnityDiagnosticsOnce(JObject middlewareState)
    {
        if (diagnosticsSentForCurrentSocket)
        {
            return;
        }

        diagnosticsSentForCurrentSocket = true;
        await SendUnityDiagnosticsAsync("connection_summary", middlewareState);
    }

    private async Task SendUnityDiagnosticsAsync(string trigger, JObject middlewareState)
    {
        if (!IsSocketOpen || PlayerPrefs.GetInt(MiddlewareDiagnosticsEnabledPrefKey, 1) == 0)
        {
            return;
        }

        int monitorPort = PlayerPrefs.GetInt("MONITOR_PORT", 8001);
        int webUiPort = PlayerPrefs.GetInt("WEB_UI_PORT", 8000);
        int gamaServerPort = PlayerPrefs.GetInt("GAMA_WS_PORT", 1000);
        Scene activeScene = SceneManager.GetActiveScene();
        JObject payload = new JObject
        {
            ["type"] = "unity_diagnostics",
            ["intentional_unknown_message"] = true,
            ["console_note"] = "Read-only diagnostic emitted intentionally by SIMPLE-Unity-Plugin.",
            ["package"] = "com.project-simple.unity-plugin@1.0.0",
            ["trigger"] = trigger ?? string.Empty,
            ["timestamp_utc"] = DateTime.UtcNow.ToString("o"),
            ["player_id"] = StaticInformation.getId(),
            ["ports"] = new JObject
            {
                ["unity_player_websocket"] = ParsePortForDiagnostics(port, 8080),
                ["middleware_monitor_websocket"] = monitorPort,
                ["middleware_web_ui"] = webUiPort,
                ["gama_server_websocket"] = gamaServerPort,
                ["active_unity_endpoint"] = "ws://" + host + ":" + port + "/",
                ["roles"] = new JObject
                {
                    ["unity_player_websocket"] = "Unity runtime/headset messages",
                    ["middleware_monitor_websocket"] = "middleware control and experiment state",
                    ["middleware_web_ui"] = "browser interface",
                    ["gama_server_websocket"] = "direct GAMA Server endpoint"
                }
            },
            ["unity"] = new JObject
            {
                ["version"] = Application.unityVersion,
                ["product"] = Application.productName,
                ["platform"] = Application.platform.ToString(),
                ["scene"] = activeScene.IsValid() ? activeScene.name : string.Empty,
                ["game_state"] = ResolveUnityGameState(),
                ["is_editor"] = Application.isEditor
            },
            ["connection"] = new JObject
            {
                ["socket_state"] = GetSocketStateForLog(),
                ["unity_connection_state"] = currentState.ToString(),
                ["has_player_state"] = HasCurrentPlayerState,
                ["authenticated"] = IsCurrentPlayerAuthenticated,
                ["heartbeat_ms"] = HeartbeatInMs,
                ["middleware_mode"] = UseMiddlewareDM,
                ["fixed_properties"] = fixedProperties,
                ["desktop_mode"] = DesktopMode
            },
            ["configuration"] = new JObject
            {
                ["playerprefs_ip"] = PlayerPrefs.GetString("IP", "localhost"),
                ["playerprefs_port"] = PlayerPrefs.GetString("PORT", "8080"),
                ["monitor_port"] = monitorPort,
                ["selected_model"] = PlayerPrefs.GetString("GAMA_MODEL_PATH", string.Empty),
                ["selected_experiment"] = PlayerPrefs.GetString("GAMA_EXPERIMENT_NAME", string.Empty)
            },
            ["gama_experiment"] = new JObject
            {
                ["state_from_monitor"] = PlayerPrefs.GetString("GAMA_EXPERIMENT_STATE", string.Empty),
                ["experiment_id"] = PlayerPrefs.GetString("GAMA_EXPERIMENT_ID", string.Empty)
            }
        };

        if (middlewareState != null)
        {
            payload["middleware_state"] = new JObject
            {
                ["connected"] = middlewareState["connected"]?.DeepClone(),
                ["in_game"] = middlewareState["in_game"]?.DeepClone(),
                ["date_connection"] = middlewareState["date_connection"]?.DeepClone(),
                ["reported_player_id"] = (middlewareState["id_player"] ?? middlewareState["id"])?.DeepClone()
            };
        }

        await SendMessageToServerAsync(payload.ToString(Formatting.None));
    }

    private static int ParsePortForDiagnostics(string rawPort, int fallback)
    {
        return int.TryParse(rawPort, out int parsed) && parsed > 0 && parsed <= 65535 ? parsed : fallback;
    }

    private static string ResolveUnityGameState()
    {
        return SimulationManager.Instance != null
            ? SimulationManager.Instance.GetCurrentState().ToString()
            : "NO_SIMULATION_MANAGER";
    }

    private void AdoptMiddlewarePlayerIdIfNeeded(string serverPlayerId)
    {
        if (string.IsNullOrWhiteSpace(serverPlayerId))
        {
            return;
        }

        string currentId = StaticInformation.getId();
        string cleanServerPlayerId = serverPlayerId.Trim();
        if (string.Equals(currentId, cleanServerPlayerId, StringComparison.Ordinal))
        {
            return;
        }

        if (IsUnityPlaySessionId(currentId))
        {
            GamaLog.DevWarning(
                "[GAMA][CONNECTION][REBIND] Middleware reports player id=" + cleanServerPlayerId +
                " while Unity requested id=" + currentId +
                ". Keeping the current Unity Play id and treating the middleware id as stale.");
            return;
        }

        if (StaticInformation.AdoptSessionId(cleanServerPlayerId))
        {
            GamaLog.DevWarning(
                "[GAMA][CONNECTION][REBIND] Middleware reports player id=" + cleanServerPlayerId +
                " while Unity requested id=" + currentId +
                ". Adopting middleware id for this Play session.");
        }
    }

    private bool IsStaleUnityPlayState(string serverPlayerId)
    {
        if (string.IsNullOrWhiteSpace(serverPlayerId))
        {
            return false;
        }

        string currentId = StaticInformation.getId();
        string cleanServerPlayerId = serverPlayerId.Trim();
        return IsUnityPlaySessionId(currentId) &&
               !string.Equals(currentId, cleanServerPlayerId, StringComparison.Ordinal);
    }

    private bool TryAdoptMiddlewareUnityPlayState(string serverPlayerId, bool connected, bool authenticated)
    {
        if (!connected || string.IsNullOrWhiteSpace(serverPlayerId))
        {
            return false;
        }

        string currentId = StaticInformation.getId();
        string cleanServerPlayerId = string.IsNullOrWhiteSpace(serverPlayerId)
            ? string.Empty
            : serverPlayerId.Trim();
        string warningKey = cleanServerPlayerId + "|" + currentId + "|" + connected + "|" + authenticated;
        if (string.Equals(lastStalePlayerStateWarning, warningKey, StringComparison.Ordinal))
        {
            StaticInformation.AdoptSessionId(cleanServerPlayerId);
            PlayerPrefs.SetString(PlaySessionIdPrefKey, cleanServerPlayerId);
            PlayerPrefs.Save();
            return true;
        }

        lastStalePlayerStateWarning = warningKey;
        GamaLog.DevWarning(
            "[GAMA][CONNECTION][REBIND] Middleware reports player id=" + cleanServerPlayerId +
            " while Unity requested id=" + currentId +
            ". Adopting the middleware id for this Play reconnect.");
        StaticInformation.AdoptSessionId(cleanServerPlayerId);
        PlayerPrefs.SetString(PlaySessionIdPrefKey, cleanServerPlayerId);
        PlayerPrefs.Save();
        return true;
    }

    private static void InitializeRuntimePlayId()
    {
        string persistedId = PlayerPrefs.GetString(PlaySessionIdPrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(persistedId))
        {
            StaticInformation.AdoptSessionId(persistedId.Trim());
            return;
        }

        StaticInformation.EnsureSessionIdPrefix("unity_play");
        PlayerPrefs.SetString(PlaySessionIdPrefKey, StaticInformation.getId());
        PlayerPrefs.Save();
    }

    private static bool IsUnityPlaySessionId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Trim().StartsWith("unity_play_", StringComparison.OrdinalIgnoreCase);
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

            var socket = GetSocket();
            if (socket == null)
            {
                GamaLog.DevWarning("[GAMA][CONNECTION][START] socket not initialized yet; waiting for connector startup");
                return;
            }

            await socket.Connect();

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

    public bool CanSendRuntimeMessages
    {
        get
        {
            return IsSocketOpen &&
                   (IsConnectionState(ConnectionState.CONNECTED) ||
                    IsConnectionState(ConnectionState.AUTHENTICATED));
        }
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
                GamaLog.Error("ConnectionManager: Failed to send executable expression");
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
                GamaLog.Error("ConnectionManager: Failed to send executable ask");
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
        await DisconnectProperlyAsync();
    }

    public async System.Threading.Tasks.Task DisconnectProperlyAsync() {
        await SendDisconnectProperlyAsync();
        DisconnectFromServer();
    }

    protected override async System.Threading.Tasks.Task BeforeSocketCloseAsync()
    {
        await SendDisconnectProperlyAsync();
    }

    private async System.Threading.Tasks.Task SendDisconnectProperlyAsync()
    {
        if (!IsSocketOpen)
        {
            return;
        }

        Dictionary<string,string> jsonExpression = new Dictionary<string,string> {
            {"type", "disconnect_properly"}
        };
        string jsonStringExpression = JsonConvert.SerializeObject(jsonExpression);
        await SendMessageToServerAsync(jsonStringExpression);
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
