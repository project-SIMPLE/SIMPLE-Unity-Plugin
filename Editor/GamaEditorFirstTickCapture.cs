using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Captures a stabilized GAMA preview state through the middleware WebSocket
/// (simple.webplatform, ws://host:port/), following the same route as <see cref="ConnectionManager"/> in Play Mode:
/// sends the <c>connection</c> handshake, waits for authenticated <c>json_state</c>, then sends
/// <c>send_init_data</c> and <c>player_ready_to_receive_geometries</c>. It collects multiple ticks
/// until dynamic agents appear or the preview window ends.
/// The editor remains asynchronous: <see cref="CaptureAsync"/> returns a Task.
/// </summary>
internal static class GamaEditorFirstTickCapture
{
    /// <summary>Uses the same target as <see cref="ConnectionManager"/> (private AgentToSendInfo field).</summary>
    private const string UnityLinkerAgent = "simulation[0].unity_linker[0]";

    private static readonly bool VerboseCaptureDebug = true;

    /// <summary>
    /// Unity-managed preview: as soon as json_state reports in_game=true, immediately send send_init_data + player_ready
    /// (without waiting for GAMA create_player or port 1000).
    /// </summary>
    private const bool EDITOR_PREVIEW_IMMEDIATE_INIT_BURST = true;

    private static int s_captureSessionCounter;
    private static readonly SemaphoreSlim GamaPort1000Lock = new SemaphoreSlim(1, 1);

    private static int NextCaptureSessionId()
    {
        return Interlocked.Increment(ref s_captureSessionCounter);
    }

    /// <summary>Logging that is always visible (Unity Console + capture trail).</summary>
    private static void CapLog(Action<string> append, int sessionId, string channel, string message)
    {
        string line = "[GAMA][CAPTURE][#" + sessionId + "][" + channel + "] " + message;
        append(line);
        GamaLog.Dev(line);
    }

    private static void CapLog8080(Action<string> append, string direction, string message)
    {
        string line = "[GAMA][CAPTURE][8080][" + direction + "] " + message;
        append(line);
    }

    private static void Dbg(Action<string> append, int sessionId, string channel, string message)
    {
        if (!VerboseCaptureDebug)
        {
            return;
        }

        append("[GAMA][DBG][#" + sessionId + "][" + channel + "] " + message);
    }

    /// <summary>
    /// Uses the explicit capture id when Unity starts an isolated preview session; otherwise uses the current runtime id.
    /// </summary>
    public static string ResolveMiddlewarePlayerId(string connectionId)
    {
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            return connectionId.Trim();
        }

        return StaticInformation.getId();
    }

    private static IEnumerable<string> CollectGhostPlayerIdsToPurge(string capturePlayerId)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        ids.Add("Editor_Capture");
        ids.Add("editor_capture");

        string runtimeId = StaticInformation.getId();
        if (!string.IsNullOrWhiteSpace(runtimeId) &&
            !string.Equals(runtimeId.Trim(), capturePlayerId, StringComparison.OrdinalIgnoreCase))
        {
            ids.Add(runtimeId.Trim());
        }

        if (!string.IsNullOrWhiteSpace(capturePlayerId))
        {
            ids.Remove(capturePlayerId.Trim());
        }

        return ids;
    }

    private static async Task PreCapturePurgeOccupyingPlayersAsync(
        string host,
        string port,
        string capturePlayerId,
        int sessionId,
        Action<string> append,
        CancellationToken ct)
    {
        CapLog(append, sessionId, "pre",
            "Preventive middleware purge ws://" + (string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim()) + ":" +
            (string.IsNullOrWhiteSpace(port) ? "8080" : port.Trim()) + "/ before capture id=\"" + capturePlayerId + "\"");

        foreach (string ghostId in CollectGhostPlayerIdsToPurge(capturePlayerId))
        {
            CapLog(append, sessionId, "pre", "-> clean shutdown id=\"" + ghostId + "\"...");
            string outcome;
            try
            {
                outcome = await PurgeGhostPlayerAsync(host, port, ghostId, 6000, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome = "Purge exception for \"" + ghostId + "\": " + ex.Message;
            }

            CapLog(append, sessionId, "pre", outcome);
            try
            {
                await Task.Delay(600, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        CapLog(append, sessionId, "pre", "Preventive purge completed.");
    }

    private static void DbgRaw(Action<string> append, int sessionId, string channel, string text)
    {
        if (!VerboseCaptureDebug || string.IsNullOrEmpty(text))
        {
            return;
        }

        const int max = 4000;
        string payload = text.Length <= max ? text : text.Substring(0, max) + "…(" + text.Length + " chars)";
        append("[GAMA][DBG][#" + sessionId + "][RAW][" + channel + "] " + payload);
    }

    private static void DbgOutgoing(Action<string> append, int sessionId, string channel, string payload)
    {
        if (!VerboseCaptureDebug)
        {
            return;
        }

        DbgRaw(append, sessionId, "OUT→" + channel, payload);
    }

    private static string FormatCaptureStateSnapshot(CaptureState s, string connectionId, WebSocketState? wsState)
    {
        double deadlineSec = Math.Max(0, (s.CaptureDeadlineUtc - DateTime.UtcNow).TotalSeconds);
        return "ws=" + (wsState?.ToString() ?? "?") +
               " id=" + connectionId +
               " mode=" + (s.DirectGamaMode ? "direct" : "middleware") +
               " mwAuth=" + s.MiddlewareAuthenticated +
               " mwConn=" + s.MiddlewareConnected +
               " loadSent=" + s.DirectLoadSent +
               " loadOK=" + s.DirectLoadCompleted +
               " playSent=" + s.DirectPlaySent +
               " playOK=" + s.DirectPlayCompleted +
               " createSent=" + s.DirectCreatePlayerSent +
               " createOK=" + s.DirectCreatePlayerConfirmedUtc.HasValue +
               " createTry=" + s.DirectCreatePlayerAttempts +
               " simStatus=" + (string.IsNullOrEmpty(s.LastSimulationStatus) ? "-" : s.LastSimulationStatus) +
               " exp_id=" + s.DirectExperimentId +
               " running=" + (s.DirectRunningSinceUtc.HasValue ? "yes" : "no") +
               " json: prec=" + s.GotPrecision +
               " prop=" + s.GotProperties +
               " worldFrames=" + s.WorldFrameCount +
               " agents=" + s.WorldHasAgents +
               " otherOut=" + s.OtherOutputCount +
               " deadline_s=" + deadlineSec.ToString("0") +
               " initOK=" + s.SendInitDataSuccessCount +
               " initFail=" + s.SendInitDataFailureCount;
    }

    public sealed class CaptureResult
    {
        public bool Success;
        public string Error;
        public string PrecisionJsonPath;
        public string PropertiesJsonPath;
        public string WorldJsonPath;
        public int WorldFrameCount;
        public int BestWorldTickIndex;
        public string BestWorldJsonPath;
        public bool DynamicAgentsFound;
        public string PreviewWarning;
        public string LogTrail;
    }

    public static async Task<CaptureResult> CaptureAsync(
        string host,
        string port,
        string outputDirectory,
        string connectionId,
        int handshakeHeartbeatMs,
        int connectTimeoutMs,
        int captureTimeoutMs,
        Action<string> log,
        CancellationToken externalToken)
    {
        return await CaptureAsync(
            host,
            port,
            outputDirectory,
            connectionId,
            handshakeHeartbeatMs,
            connectTimeoutMs,
            captureTimeoutMs,
            false,
            null,
            null,
            20,
            25f,
            false,
            false,
            false,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort,
            GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex,
            true,
            true,
            true,
            5f,
            log,
            externalToken);
    }

    public static async Task<CaptureResult> CaptureAsync(
        string host,
        string port,
        string outputDirectory,
        string connectionId,
        int handshakeHeartbeatMs,
        int connectTimeoutMs,
        int captureTimeoutMs,
        bool directGamaServer,
        Action<string> log,
        CancellationToken externalToken)
    {
        return await CaptureAsync(
            host,
            port,
            outputDirectory,
            connectionId,
            handshakeHeartbeatMs,
            connectTimeoutMs,
            captureTimeoutMs,
            directGamaServer,
            null,
            null,
            20,
            25f,
            false,
            false,
            false,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort,
            GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex,
            true,
            true,
            true,
            5f,
            log,
            externalToken);
    }

    public static async Task<CaptureResult> CaptureAsync(
        string host,
        string port,
        string outputDirectory,
        string connectionId,
        int handshakeHeartbeatMs,
        int connectTimeoutMs,
        int captureTimeoutMs,
        bool directGamaServer,
        string directModelPath,
        string directExperimentName,
        Action<string> log,
        CancellationToken externalToken)
    {
        return await CaptureAsync(
            host,
            port,
            outputDirectory,
            connectionId,
            handshakeHeartbeatMs,
            connectTimeoutMs,
            captureTimeoutMs,
            directGamaServer,
            directModelPath,
            directExperimentName,
            20,
            25f,
            false,
            false,
            false,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort,
            GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex,
            true,
            true,
            true,
            5f,
            log,
            externalToken);
    }

    public static async Task<CaptureResult> CaptureAsync(
        string host,
        string port,
        string outputDirectory,
        string connectionId,
        int handshakeHeartbeatMs,
        int connectTimeoutMs,
        int captureTimeoutMs,
        bool directGamaServer,
        string directModelPath,
        string directExperimentName,
        int maxWorldFrames,
        float worldPhaseExtraSeconds,
        bool skipRemoteLoad,
        bool managedFromUnity,
        bool launchExperimentViaMonitor,
        int monitorPort,
        string dynamicSpeciesRegex,
        bool stopWhenDynamicAgentsFound,
        bool pauseExperimentAfterPreview,
        bool stopWhenPreviewCacheStable,
        float previewStableSeconds,
        Action<string> log,
        CancellationToken externalToken)
    {
        CaptureResult result = new CaptureResult();
        StringBuilder logBuilder = new StringBuilder();
        int sessionId = NextCaptureSessionId();
        Action<string> append = s =>
        {
            logBuilder.AppendLine(s);
            try { log?.Invoke(s); } catch { /* ignore */ }
        };

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        string portNorm = string.IsNullOrWhiteSpace(port) ? "8080" : port.Trim();
        string effectiveIdEarly = ResolveMiddlewarePlayerId(connectionId);

        CapLog(append, sessionId, "capture",
            "START uri=ws://" + hostNorm + ":" + portNorm + "/" +
            " direct=" + directGamaServer +
            " playerId=" + effectiveIdEarly +
            " runtimeId=" + StaticInformation.getId() +
            " model=" + (directModelPath ?? "(null)") +
            " exp=" + (directExperimentName ?? "(null)") +
            " timeoutMs=" + captureTimeoutMs +
            " connectTimeoutMs=" + connectTimeoutMs +
            " skipRemoteLoad=" + skipRemoteLoad +
            " managedFromUnity=" + managedFromUnity +
            " launchExperimentViaMonitor=" + launchExperimentViaMonitor +
            " monitorPort=" + monitorPort +
            " previewMaxFrames=" + maxWorldFrames +
            " previewWarmupSec=" + worldPhaseExtraSeconds +
            " previewStableSec=" + previewStableSeconds +
            " dynamicRegex=" + (dynamicSpeciesRegex ?? "(default)"));

        if (managedFromUnity)
        {
            skipRemoteLoad = false;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            ClearPreviousCaptureFiles(outputDirectory);
        }
        catch (Exception ex)
        {
            result.Error = "Cannot create output folder: " + ex.Message;
            result.LogTrail = logBuilder.ToString();
            return result;
        }

        Uri uri;
        try
        {
            string h = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
            string p = string.IsNullOrWhiteSpace(port) ? "8080" : port.Trim();
            uri = new Uri("ws://" + h + ":" + p + "/");
        }
        catch (Exception ex)
        {
            result.Error = "Invalid middleware URL: " + ex.Message;
            result.LogTrail = logBuilder.ToString();
            return result;
        }

        append(directGamaServer ? "[GAMA] Connecting directly to the GAMA server " + uri : "[GAMA] Connecting to the middleware " + uri);

        using (CancellationTokenSource captureCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken))
        {
            if (managedFromUnity && launchExperimentViaMonitor && !directGamaServer)
            {
                if (string.IsNullOrWhiteSpace(directModelPath) || string.IsNullOrWhiteSpace(directExperimentName))
                {
                    result.Error = "Unity-managed mode: modelPath + experimentName are required (Unity selection).";
                    result.LogTrail = logBuilder.ToString();
                    return result;
                }

                append("[GAMA] Unity-managed mode: monitor orchestration ws://" + hostNorm + ":" + monitorPort +
                       "/ followed by player capture " + uri + ". Forced target: model=\"" + directModelPath +
                       "\" experiment=\"" + directExperimentName + "\".");
                GamaEditorMiddlewareOrchestrator.ManagedExperimentResult orch =
                    await GamaEditorMiddlewareOrchestrator.StartMiddlewareManagedExperimentAsync(
                            hostNorm,
                            monitorPort,
                            directExperimentName ?? string.Empty,
                            directModelPath ?? string.Empty,
                            captureCts.Token,
                            append)
                        .ConfigureAwait(false);
                if (!orch.Success)
                {
                    result.Error = orch.Error ?? "Middleware orchestration failed (monitor).";
                    result.LogTrail = logBuilder + orch.LogTrail;
                    return result;
                }
            }
            else if (managedFromUnity && !directGamaServer)
            {
                CapLog8080(append, "INFO", "EXTERNAL MIDDLEWARE MODE — no kill, no restart");
                append("[GAMA] Unity-managed mode: external middleware is already running; no monitor/catalog orchestration.");
            }

            if (!directGamaServer && !skipRemoteLoad && !managedFromUnity)
            {
                try
                {
                    await PreCapturePurgeOccupyingPlayersAsync(
                        hostNorm, portNorm, effectiveIdEarly, sessionId, append, captureCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    result.Error = "Capture cancelled during preventive purge.";
                    result.LogTrail = logBuilder.ToString();
                    return result;
                }
            }

            using (ClientWebSocket ws = new ClientWebSocket())
            {
                ws.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(Math.Max(1000, handshakeHeartbeatMs));

                bool connected = false;
                using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(captureCts.Token))
                {
                    connectCts.CancelAfter(Math.Max(2000, connectTimeoutMs));
                    try
                    {
                        await ws.ConnectAsync(uri, connectCts.Token);
                        connected = true;
                    }
                    catch (OperationCanceledException)
                    {
                        result.Error = "Cannot connect to middleware (timeout " + (connectTimeoutMs / 1000) + " s) on " + uri + ".";
                    }
                    catch (Exception ex)
                    {
                        result.Error = "Cannot connect to middleware: " + ex.Message;
                    }
                }

                if (!connected)
                {
                    result.LogTrail = logBuilder.ToString();
                    return result;
                }

                string effectiveId = ResolveMiddlewarePlayerId(connectionId);

                if (directGamaServer)
                {
                    append("[GAMA] Connected to the GAMA server. The middleware 'connection' message is not sent.");
                }
                else
                {
                    append("[GAMA] Connected. Sending connection handshake (id=" + effectiveId + ", as in Play Mode).");

                    string handshake = JsonConvert.SerializeObject(new
                    {
                        type = "connection",
                        id = effectiveId,
                        heartbeat = handshakeHeartbeatMs.ToString()
                    });

                    try
                    {
                        ArraySegment<byte> handshakeBytes = new ArraySegment<byte>(Encoding.UTF8.GetBytes(handshake));
                        await ws.SendAsync(handshakeBytes, WebSocketMessageType.Text, true, captureCts.Token);
                        CapLog8080(append, "OUT", "connection id=" + effectiveId);
                    }
                    catch (Exception ex)
                    {
                        result.Error = "Failed to send handshake: " + ex.Message;
                        result.LogTrail = logBuilder.ToString();
                        return result;
                    }
                }

                CaptureState state = new CaptureState
                {
                    MaxWorldFrames = Math.Max(1, maxWorldFrames),
                    WorldPhaseExtraSeconds = Math.Max(5f, worldPhaseExtraSeconds),
                    CaptureDeadlineUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(2000, captureTimeoutMs)),
                    ConnectionId = effectiveId,
                    DebugSessionId = sessionId,
                    MiddlewareHost = hostNorm,
                    MiddlewarePort = portNorm,
                    SkipRemoteLoad = skipRemoteLoad,
                    ManagedFromUnity = managedFromUnity,
                    LaunchExperimentViaMonitor = launchExperimentViaMonitor,
                    DynamicSpeciesRegex = string.IsNullOrWhiteSpace(dynamicSpeciesRegex)
                        ? GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex
                        : dynamicSpeciesRegex.Trim(),
                    StopWhenDynamicAgentsFound = stopWhenDynamicAgentsFound,
                    StopWhenPreviewCacheStable = stopWhenPreviewCacheStable,
                    PreviewStableSeconds = Math.Max(1f, previewStableSeconds),
                    PauseExperimentAfterPreview = pauseExperimentAfterPreview,
                    DynamicSpeciesRegexCompiled = GamaEditorPreviewCapture.CompileDynamicSpeciesRegex(dynamicSpeciesRegex)
                };

                Dbg(append, sessionId, "capture",
                    "Connected effectiveId=" + effectiveId + " deadlineUtc=" + state.CaptureDeadlineUtc.ToString("O"));
                if (directGamaServer)
                {
                    state.DirectGamaMode = true;
                    state.DirectLoadRequested = !skipRemoteLoad &&
                                                !string.IsNullOrWhiteSpace(directModelPath) &&
                                                !string.IsNullOrWhiteSpace(directExperimentName);
                    state.DirectLoadCompleted = skipRemoteLoad || !state.DirectLoadRequested;
                    state.DirectPlayCompleted = skipRemoteLoad || !state.DirectLoadRequested;
                    if (skipRemoteLoad)
                    {
                        state.DirectRunningSinceUtc = DateTime.UtcNow;
                        state.LastSimulationStatus = "ASSUMED_OPEN";
                    }
                    state.DirectModelPath = directModelPath;
                    state.DirectExperimentName = directExperimentName;
                    state.SkipRemoteLoad = skipRemoteLoad;

                    append(skipRemoteLoad
                        ? "[GAMA] Experiment already open: direct GAMA WebSocket (port " + portNorm +
                          "), no load/play; create_player followed by send_init_data."
                        : state.DirectLoadRequested
                        ? "[GAMA] Waiting for ConnectionSuccessful before loading: " + directExperimentName + " (" + directModelPath + ")."
                        : "[GAMA] No model/experiment supplied for direct loading; using the current experiment.");
                }

                bool gamaPortLockHeld = false;
                if (directGamaServer)
                {
                    try
                    {
                        Dbg(append, sessionId, "direct", "Waiting for port 1000 lock (only one GAMA capture/load at a time)...");
                        await GamaPort1000Lock.WaitAsync(captureCts.Token).ConfigureAwait(false);
                        gamaPortLockHeld = true;
                        Dbg(append, sessionId, "direct", "Port 1000 lock acquired.");
                    }
                    catch (Exception ex)
                    {
                        result.Error = "Cannot acquire GAMA lock (another capture in progress?): " + ex.Message;
                        result.LogTrail = logBuilder.ToString();
                        return result;
                    }
                }

                using (CancellationTokenSource captureWindowCts = CancellationTokenSource.CreateLinkedTokenSource(captureCts.Token))
                {
                    Task bootstrapTask = Task.CompletedTask;
                    bool shouldBootstrapMiddleware = !directGamaServer &&
                        !skipRemoteLoad &&
                        !string.IsNullOrWhiteSpace(directModelPath) &&
                        !string.IsNullOrWhiteSpace(directExperimentName);
                    if (managedFromUnity && !directGamaServer)
                    {
                        state.GamaBootstrapPhase = 2;
                        state.ManagedFromUnity = true;
                        state.HybridGamaCommandChannel = false;
                        append("[GAMA] Unity-managed mode: player socket 8080 only (experiment launched through the monitor).");
                        Dbg(append, sessionId, "capture", "managedFromUnity -> Play-like pump on " + uri);
                    }
                    else if (skipRemoteLoad && !directGamaServer)
                    {
                        state.GamaBootstrapPhase = 2;
                        state.HybridGamaCommandChannel = false;
                        append("[GAMA] Open-experiment mode: middleware-only 8080, no port 1000 (Play-like diagnostic).");
                        Dbg(append, sessionId, "capture",
                            "skipRemoteLoad -> middleware-only " + uri + ", no HybridGamaCommandChannel");
                    }
                    else if (shouldBootstrapMiddleware)
                    {
                        string bootstrapPort = IsGamaNativeWebSocketPort(port) ? port.Trim() : "1000";
                        state.GamaBootstrapPhase = 1;
                        Dbg(append, sessionId, "bootstrap",
                            "Starting load/play/create_player in parallel on port " + bootstrapPort +
                            " (middleware on " + port + ")");
                        bootstrapTask = RunGamaSimulationBootstrapAsync(
                            sessionId,
                            string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim(),
                            bootstrapPort,
                            directModelPath ?? string.Empty,
                            directExperimentName ?? string.Empty,
                            effectiveId,
                            skipRemoteLoad,
                            state,
                            captureWindowCts.Token,
                            append);
                    }
                    else if (!directGamaServer)
                    {
                        state.GamaBootstrapPhase = 2;
                        Dbg(append, sessionId, "bootstrap", "No bootstrap (model or experiment missing).");
                    }

                    Task pumpTask = RunMiddlewarePumpAsync(
                        ws, state, effectiveId, bootstrapTask, result, captureWindowCts.Token, append);

                    byte[] buffer = new byte[64 * 1024];
                    StringBuilder pending = new StringBuilder();

                    try
                    {
                        while (!captureWindowCts.IsCancellationRequested &&
                               !state.IsCaptureComplete &&
                               !state.CaptureAbortRequested &&
                               ws.State == WebSocketState.Open)
                        {
                            if (DateTime.UtcNow >= state.CaptureDeadlineUtc)
                            {
                                append("[GAMA] Capture window ended (timeout).");
                                Dbg(append, sessionId, "capture",
                                    "TIMEOUT " + FormatCaptureStateSnapshot(state, effectiveId, ws.State));
                                break;
                            }

                            WebSocketReceiveResult rr;
                            try
                            {
                                using (CancellationTokenSource receiveSliceCts =
                                    CancellationTokenSource.CreateLinkedTokenSource(captureWindowCts.Token))
                                {
                                    receiveSliceCts.CancelAfter(TimeSpan.FromSeconds(1));
                                    rr = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), receiveSliceCts.Token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                if (DateTime.UtcNow >= state.CaptureDeadlineUtc)
                                {
                                    break;
                                }

                                if (state.DirectGamaMode && state.DirectLoadSent && !state.DirectLoadCompleted)
                                {
                                    state.DebugReceiveIdleTicks++;
                                    if (state.DebugReceiveIdleTicks % 15 == 0)
                                    {
                                        Dbg(append, sessionId, "direct",
                                            "Still waiting for Load confirmation (" + state.DebugReceiveIdleTicks +
                                            " s). Is GAMA showing a close-simulation dialog? Click Yes. " +
                                            FormatCaptureStateSnapshot(state, effectiveId, ws.State));
                                    }
                                }

                                continue;
                            }

                            if (rr.MessageType == WebSocketMessageType.Close)
                            {
                                string closeInfo = "WebSocket Close (status=" + rr.CloseStatus + " desc=" +
                                                   (rr.CloseStatusDescription ?? "") + ")";
                                append("[GAMA] The server closed the connection. " + closeInfo);
                                Dbg(append, sessionId, "capture", closeInfo);
                                if (state.DirectGamaMode && state.DirectLoadSent && !state.DirectLoadCompleted)
                                {
                                    result.Error =
                                        "GAMA closed the connection immediately after load (without confirming Load). " +
                                        "Close/stop the simulation in the GAMA GUI, or select 'Experiment already open in GAMA (no load)'.";
                                }
                                else if (state.SkipRemoteLoad && !state.HybridGamaCommandChannel && !state.IsCaptureComplete)
                                {
                                    append("[GAMA] Middleware closed without JSON (middleware-only 8080 mode; no GAMA:1000 fallback).");
                                }

                                break;
                            }

                            pending.Append(Encoding.UTF8.GetString(buffer, 0, rr.Count));
                            if (!rr.EndOfMessage)
                            {
                                continue;
                            }

                            string text = pending.ToString();
                            pending.Length = 0;

                            await HandleIncomingAsync(
                                ws,
                                text,
                                outputDirectory,
                                result,
                                append,
                                state,
                                captureWindowCts.Token);

                            if (!string.IsNullOrWhiteSpace(result.Error))
                            {
                                Dbg(append, sessionId, "capture", "Stopping because of error: " + result.Error);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] Receive error: " + ex.Message);
                    }

                    try
                    {
                        try
                        {
                            await pumpTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected while stopping.
                        }
                        catch (Exception ex)
                        {
                            append("[GAMA] Middleware pump: " + ex.Message);
                        }

                        try
                        {
                            await bootstrapTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected while stopping.
                        }
                        catch (Exception ex)
                        {
                            append("[GAMA] GAMA bootstrap: " + ex.Message);
                        }

                    }
                    finally
                    {
                        if (gamaPortLockHeld)
                        {
                            GamaPort1000Lock.Release();
                            gamaPortLockHeld = false;
                            Dbg(append, sessionId, "direct", "Port 1000 lock released.");
                        }
                    }
                }

                if (!directGamaServer && IsEditorPreviewCaptureId(effectiveId))
                {
                    append("[GAMA] Editor preview: disconnect_properly id=\"" + effectiveId + "\" before closing.");
                    try
                    {
                        using (CancellationTokenSource disconnectCts =
                               CancellationTokenSource.CreateLinkedTokenSource(captureCts.Token))
                        {
                            disconnectCts.CancelAfter(TimeSpan.FromSeconds(2));
                            await SendDisconnectProperlyAsync(ws, disconnectCts.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] Preview disconnect_properly failed: " + ex.Message);
                    }
                }
                else if (!directGamaServer && !state.SkipRemoteLoad)
                {
                    append("[GAMA] Closing the preview connection cleanly without disconnect_properly to avoid purging the active middleware socket.");
                }
                else if (!directGamaServer && state.SkipRemoteLoad)
                {
                    append("[GAMA] Experiment already open: disconnect_properly is not sent (player '" + effectiveId +
                           "' remains in GAMA).");
                }

                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "capture done", closeCts.Token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    append("[GAMA] CloseAsync failed: " + ex.Message);
                }

                if (!state.GotPrecision || !state.GotProperties || state.WorldFrameCount <= 0)
                {
                    if (string.IsNullOrWhiteSpace(result.Error))
                    {
                        result.Error = BuildMissingError(state);
                        if (directGamaServer)
                        {
                        result.Error += " In direct mode, if the model does not emit SimulationOutput, clear 'Direct Capture', start simple.webplatform (8080), then use 'Capture as in Play Mode'.";
                        }
                    }

                    Dbg(append, sessionId, "capture", "FAILED " + result.Error);
                    Dbg(append, sessionId, "capture",
                        "Final state " + FormatCaptureStateSnapshot(state, effectiveId, ws.State));
                    result.LogTrail = logBuilder.ToString();
                    return result;
                }

                FinalizePreviewBestFrame(state, outputDirectory, result, append);

                Dbg(append, sessionId, "capture",
                    "SUCCESS frames=" + state.WorldFrameCount + " bestTick=" + state.WorldBestFrameIndex +
                    " dynamic=" + state.PreviewDynamicAgentsFound);

                result.WorldFrameCount = state.WorldFrameCount;
                result.BestWorldTickIndex = state.WorldBestFrameIndex;
                result.BestWorldJsonPath = state.WorldBestJsonPath;
                result.DynamicAgentsFound = state.PreviewDynamicAgentsFound;

                if (state.GeometryExportErrorDetected && string.IsNullOrWhiteSpace(result.PreviewWarning))
                {
                    result.PreviewWarning = state.GeometryExportErrorMessage;
                }

                if (!state.PreviewDynamicAgentsFound)
                {
                    string dynamicWarning =
                        "No agents matching the dynamic regex ('" + state.DynamicSpeciesRegex +
                        "') were received in json_output. The preview is still built from the cumulative cache.";
                    result.PreviewWarning = string.IsNullOrWhiteSpace(result.PreviewWarning)
                        ? dynamicWarning
                        : result.PreviewWarning + " " + dynamicWarning;
                    append("[GAMA][PREVIEW] WARNING: " + dynamicWarning);
                }
                else
                {
                    append("[GAMA][PREVIEW] Dynamic agents are present in the cumulative cache at tick " +
                           state.WorldBestFrameIndex + ".");
                }

                if (!state.WorldHasAgents)
                {
                    append("[GAMA] Capture completed with no agents in the world. Browse the ticks (world_tick_*.json) in the static preview.");
                }

                if (!directGamaServer)
                {
                    try
                    {
                        bool paused = await GamaEditorMiddlewareOrchestrator.PauseExperimentAsync(
                                hostNorm,
                                monitorPort,
                                captureCts.Token,
                                append,
                                "successful preview")
                            .ConfigureAwait(false);
                        if (!paused)
                        {
                            append("[GAMA][PREVIEW] pause_experiment was not confirmed (monitor " + monitorPort + ").");
                        }
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA][PREVIEW] pause_experiment failed: " + ex.Message);
                    }
                }
            }
        }

        result.Success = true;
        result.LogTrail = logBuilder.ToString();
        return result;
    }

    private sealed class CaptureState
    {
        public volatile bool MiddlewareAuthenticated;
        public volatile bool MiddlewareConnected;
        public bool LoggedMiddlewareInGameHint;
        /// <summary>0 = none, 1 = in progress, 2 = complete (create_player succeeded or no bootstrap), 3 = failed.</summary>
        public volatile int GamaBootstrapPhase;
        public string MiddlewareOccupiedPlayerId;
        public bool LoggedPlayerSlotConflict;
        public bool NeedsOccupiedPlayerPurge;
        public int AutoPurgeAttemptCount;
        public volatile bool CaptureAbortRequested;
        public string MiddlewareHost = "localhost";
        public string MiddlewarePort = "8080";
        public string ConnectionId = string.Empty;
        public bool GotPrecision;
        public bool GotProperties;
        public bool GotWorld;
        public int WorldFrameCount;
        public int WorldBestAgentCount;
        public int WorldBestFrameIndex;
        public bool WorldHasAgents;
        public string WorldBestJsonPath;
        public DateTime? WorldPhaseStartedUtc;
        public int MaxWorldFrames = 50;
        public float WorldPhaseExtraSeconds = 25f;
        public string DynamicSpeciesRegex = GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex;
        public Regex DynamicSpeciesRegexCompiled;
        public bool StopWhenDynamicAgentsFound = true;
        public bool StopWhenPreviewCacheStable = true;
        public float PreviewStableSeconds = 5f;
        public bool PauseExperimentAfterPreview = true;
        public bool PreviewDynamicAgentsFound;
        /// <summary>
        /// After warmup, do not stop on a stable cache until a dynamic agent
        /// (for example, species <c>people</c>) has been seen. This avoids stopping when only walls/roads are cached.
        /// </summary>
        public float DynamicSpeciesGraceSeconds = 40f;
        public DateTime? LastPreviewCacheGrowthUtc;
        public int LastWorldFrameIndex = -1;
        public string LastWorldTickPath;
        public GamaEditorPreviewWorldAccumulator PreviewAccumulator;
        public Dictionary<string, PropertiesGAMA> PropertyMapById;
        public bool GeometryExportErrorDetected;
        public string GeometryExportErrorMessage;
        public int OtherOutputCount;
        public bool DirectGamaMode;
        public volatile bool DirectCreatePlayerSent;
        public bool DirectLoadRequested;
        public volatile bool DirectLoadCompleted;
        public volatile bool DirectLoadSent;
        public volatile bool DirectLoadPending;
        public bool SkipRemoteLoad;
        public volatile bool MiddlewareCreatePlayerSent;
        public DateTime? MiddlewareCreatePlayerConfirmedUtc;
        public DateTime? MiddlewareAuthenticatedSinceUtc;
        public bool LoggedUnityLinkerMissingHint;
        public DateTime MiddlewareInitDataAllowedUtc = DateTime.MinValue;
        public DateTime MiddlewarePlayPhaseStartedUtc = DateTime.MinValue;
        public DateTime MiddlewareGeomReadyBurstUtc = DateTime.MinValue;
        public volatile bool MiddlewareGeomReadyBurstSent;
        public volatile bool ImmediateInitBurstStarted;
        public volatile bool ImmediateInitBurstGeomSent;
        public volatile bool ReceivedJsonOutput;
        public int ImmediateInitBurstSendCount;
        public DateTime ImmediateInitBurstNextSendUtc = DateTime.MinValue;
        public DateTime ImmediateInitBurstEndUtc = DateTime.MinValue;
        /// <summary>Experiment already open: commands on GAMA:1000, JSON on the middleware.</summary>
        public volatile bool HybridGamaCommandChannel;

        /// <summary>Unity-managed editor preview on player socket 8080.</summary>
        public volatile bool ManagedFromUnity;
        /// <summary>Legacy catalog flow: the experiment was launched through monitor 8001.</summary>
        public volatile bool LaunchExperimentViaMonitor;
        public volatile bool HybridGamaCommandsStarted;
        public volatile bool HybridGamaCommandsDone;
        public volatile int HybridGamaInitSentCount;
        public volatile bool HybridGamaGeomReadySent;
        public volatile bool DirectPlaySent;
        public volatile bool DirectPlayCompleted;
        public string DirectExperimentId = "0";
        public long DirectRunningSinceUtcTicks;
        public int DirectCreatePlayerAttempts;
        public DateTime NextDirectCreatePlayerUtc = DateTime.MinValue;

        public DateTime? DirectRunningSinceUtc
        {
            get
            {
                long ticks = Interlocked.Read(ref DirectRunningSinceUtcTicks);
                return ticks == 0 ? (DateTime?)null : new DateTime(ticks, DateTimeKind.Utc);
            }
            set
            {
                Interlocked.Exchange(ref DirectRunningSinceUtcTicks, value?.Ticks ?? 0);
            }
        }
        public string DirectModelPath;
        public string DirectExperimentName;
        public string LastSimulationStatus = string.Empty;
        public DateTime? DirectCreatePlayerConfirmedUtc;
        public DateTime NextDirectResumePlayUtc = DateTime.MinValue;
        public DateTime NextDirectInitAskUtc = DateTime.MinValue;
        public int SendInitDataFailureCount;
        public int SendInitDataSuccessCount;
        public bool LoggedDirectInitHint;
        public DateTime CaptureDeadlineUtc;
        public int DebugSessionId;
        public int DebugReceiveIdleTicks;
        public int DebugPumpTicks;
        public DateTime DebugLastPumpLogUtc = DateTime.MinValue;

        public void ExtendCaptureDeadline(float extraSeconds)
        {
            DateTime candidate = DateTime.UtcNow.AddSeconds(Math.Max(5f, extraSeconds));
            if (candidate > CaptureDeadlineUtc)
            {
                CaptureDeadlineUtc = candidate;
            }
        }

        public void ExtendCaptureDeadline(float extraSeconds, Action<string> append, string reason)
        {
            DateTime before = CaptureDeadlineUtc;
            ExtendCaptureDeadline(extraSeconds);
            if (VerboseCaptureDebug && append != null && CaptureDeadlineUtc > before)
            {
                append("[GAMA][DBG][#" + DebugSessionId + "][deadline] +" + extraSeconds + "s (" + reason +
                    ") -> end " + CaptureDeadlineUtc.ToString("HH:mm:ss"));
            }
        }

        public void TightenCaptureDeadline(DateTime latestUtc, Action<string> append, string reason)
        {
            if (latestUtc >= CaptureDeadlineUtc)
            {
                return;
            }

            CaptureDeadlineUtc = latestUtc;
            if (append != null)
            {
                append("[GAMA] Capture scheduled to end early (~" +
                       Math.Max(0, (int)(latestUtc - DateTime.UtcNow).TotalSeconds) + " s): " + reason + ".");
            }
        }

        public bool IsCaptureComplete
        {
            get
            {
                if (!GotPrecision || !GotProperties)
                {
                    return false;
                }

                if (WorldFrameCount <= 0)
                {
                    return false;
                }

                if (WorldFrameCount >= MaxWorldFrames)
                {
                    return true;
                }

                if (!WorldPhaseStartedUtc.HasValue)
                {
                    return false;
                }

                double elapsed = (DateTime.UtcNow - WorldPhaseStartedUtc.Value).TotalSeconds;
                bool warmupDone = elapsed >= WorldPhaseExtraSeconds;
                if (!warmupDone)
                {
                    return false;
                }

                if (StopWhenDynamicAgentsFound && PreviewDynamicAgentsFound)
                {
                    return true;
                }

                if (StopWhenPreviewCacheStable && LastPreviewCacheGrowthUtc.HasValue)
                {
                    double stableFor = (DateTime.UtcNow - LastPreviewCacheGrowthUtc.Value).TotalSeconds;
                    if (stableFor >= PreviewStableSeconds)
                    {
                        bool stillWaitingForDynamics = !PreviewDynamicAgentsFound &&
                            elapsed < WorldPhaseExtraSeconds + Math.Max(10f, DynamicSpeciesGraceSeconds);
                        if (stillWaitingForDynamics)
                        {
                            return false;
                        }

                        return true;
                    }
                }

                return false;
            }
        }

        public bool HasAllThree => IsCaptureComplete;
    }

    private static async Task SendExecutableAskAsync(
        ClientWebSocket ws,
        string action,
        string connectionId,
        CancellationToken ct,
        int sessionId = 0,
        Action<string> append = null)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            return;
        }

        string payload = JsonConvert.SerializeObject(new System.Collections.Generic.Dictionary<string, string> {
            { "type", "ask" },
            { "action", action },
            { "args", JsonConvert.SerializeObject(new Dictionary<string, string> { { "id", connectionId } }) },
            { "agent", "simulation[0].unity_linker[0]" }
        });
        if (append != null)
        {
            DbgOutgoing(append, sessionId, "ask/" + action, payload);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GAMA expression through the middleware (<c>expression</c> type, like PlayerManager).
    /// Useful for <c>create_player</c> / <c>remove_player</c> when the experiment is already running in GAMA.
    /// </summary>
    private static async Task SendMiddlewareExpressionAsync(
        ClientWebSocket ws,
        string gamlExpression,
        CancellationToken ct,
        int sessionId = 0,
        Action<string> append = null)
    {
        if (ws == null || ws.State != WebSocketState.Open || string.IsNullOrWhiteSpace(gamlExpression))
        {
            return;
        }

        string payload = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            { "type", "expression" },
            { "expr", gamlExpression.Trim() }
        });
        if (append != null)
        {
            DbgOutgoing(append, sessionId, "expression", payload);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task SendDisconnectProperlyAsync(ClientWebSocket ws, CancellationToken ct)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;
        string payload = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            { "type", "disconnect_properly" }
        });
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        await Task.Delay(150, ct).ConfigureAwait(false);
    }

    private static bool IsEditorPreviewCaptureId(string playerId)
    {
        return !string.IsNullOrWhiteSpace(playerId) &&
               playerId.Trim().StartsWith("editor_capture", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Connects to the middleware as the specified player, sends <c>disconnect_properly</c>, then closes the connection.
    /// </summary>
    public static async Task<string> PurgeGhostPlayerAsync(
        string host,
        string port,
        string ghostId,
        int connectTimeoutMs,
        CancellationToken externalToken)
    {
        if (string.IsNullOrWhiteSpace(ghostId))
        {
            return "No ghost player ID to purge.";
        }

        Uri uri;
        try
        {
            string h = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
            string p = string.IsNullOrWhiteSpace(port) ? "8080" : port.Trim();
            uri = new Uri("ws://" + h + ":" + p + "/");
        }
        catch (Exception ex)
        {
            return "Invalid middleware URL: " + ex.Message;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken))
        {
            cts.CancelAfter(Math.Max(2000, connectTimeoutMs));

            try
            {
                await ws.ConnectAsync(uri, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return "Could not connect to the middleware: " + ex.Message;
            }

            try
            {
                string handshake = JsonConvert.SerializeObject(new Dictionary<string, string>
                {
                    { "type", "connection" },
                    { "id", ghostId.Trim() },
                    { "heartbeat", "5000" }
                });
                byte[] handshakeBytes = Encoding.UTF8.GetBytes(handshake);
                await ws.SendAsync(new ArraySegment<byte>(handshakeBytes), WebSocketMessageType.Text, true, cts.Token)
                    .ConfigureAwait(false);

                await Task.Delay(300, cts.Token).ConfigureAwait(false);
                await SendDisconnectProperlyAsync(ws, cts.Token).ConfigureAwait(false);

                using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "purge ghost", closeCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                return "Purge interrupted: " + ex.Message;
            }
        }

        return "Ghost connection \"" + ghostId + "\" was closed cleanly on the middleware.";
    }

    private static async Task SendExecutableExpressionAsync(ClientWebSocket ws, string expression, CancellationToken ct)
    {
        await SendDirectGamaExpressionAsync(ws, expression, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// GAMA server expression (like simple.webplatform <see cref="GamaConnector.jsonTogglePlayer"/>).
    /// The <c>exp_id</c> field is required for create_player / remove_player.
    /// </summary>
    private static async Task SendDirectGamaExpressionAsync(
        ClientWebSocket ws,
        string expression,
        string experimentId,
        CancellationToken ct)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;

        string expId = string.IsNullOrWhiteSpace(experimentId) ? "0" : experimentId.Trim();
        string payload = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            { "type", "expression" },
            { "exp_id", expId },
            { "expr", expression }
        });
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task SendLoadAsync(
        ClientWebSocket ws,
        string modelPath,
        string experimentName,
        CancellationToken ct,
        int sessionId = 0,
        Action<string> append = null)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            if (append != null)
            {
                Dbg(append, sessionId, "send", "load SKIPPED ws=" + (ws == null ? "null" : ws.State.ToString()));
            }

            return;
        }

        string payload = JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            { "type", "load" },
            { "model", modelPath },
            { "experiment", experimentName }
        });
        if (append != null)
        {
            DbgOutgoing(append, sessionId, "load", payload);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>Ports used by the integrated GAMA WebSocket server (not simple.webplatform).</summary>
    public static bool IsGamaNativeWebSocketPort(string port)
    {
        if (string.IsNullOrWhiteSpace(port))
        {
            return false;
        }

        string p = port.Trim();
        return string.Equals(p, "1000", StringComparison.Ordinal) ||
               string.Equals(p, "8000", StringComparison.Ordinal);
    }

    private static async Task TryQuietCloseWebSocketAsync(ClientWebSocket ws, Action<string> append)
    {
        if (ws == null)
        {
            return;
        }

        try
        {
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "phase handoff", closeCts.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            append("[GAMA] Closing phase 1 socket: " + ex.Message);
        }
    }

    /// <summary>
    /// Phase 2: listen to the middleware (reusing the phase 1 socket when possible) while sending GAMA:1000 commands in parallel.
    /// </summary>
    private static async Task RunHybridPhase2MiddlewareListenAndGamaCommandsAsync(
        string middlewareHost,
        string middlewarePort,
        string connectionId,
        CaptureState state,
        CaptureResult result,
        string outputDirectory,
        CancellationToken ct,
        int sessionId,
        int handshakeHeartbeatMs,
        ClientWebSocket phase1WebSocket,
        Action<string> append)
    {
        string hostNorm = string.IsNullOrWhiteSpace(middlewareHost) ? "localhost" : middlewareHost.Trim();
        string portNorm = string.IsNullOrWhiteSpace(middlewarePort) ? "8080" : middlewarePort.Trim();
        DateTime phaseDeadline = DateTime.UtcNow.AddSeconds(90);
        state.TightenCaptureDeadline(phaseDeadline, null, "hybrid phase 2 (max ~90 s)");

        append("[GAMA] Phase 2: listening to middleware while sending GAMA:1000 commands in parallel.");
        Dbg(append, sessionId, "phase2", "listen∥gama (socket phase1=" +
            (phase1WebSocket != null && phase1WebSocket.State == WebSocketState.Open ? "yes" : "no") + ")");

        using (CancellationTokenSource listenCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            Task listenTask;
            bool reusePhase1Socket = phase1WebSocket != null && phase1WebSocket.State == WebSocketState.Open;
            if (reusePhase1Socket)
            {
                listenTask = RunMiddlewareJsonReceiveLoopAsync(
                    phase1WebSocket,
                    outputDirectory,
                    result,
                    state,
                    listenCts.Token,
                    sessionId,
                    append,
                    phaseDeadline);
            }
            else
            {
                listenTask = RunMiddlewareJsonListenWithReconnectAsync(
                    hostNorm,
                    portNorm,
                    connectionId,
                    outputDirectory,
                    result,
                    state,
                    listenCts.Token,
                    sessionId,
                    handshakeHeartbeatMs,
                    phaseDeadline,
                    append,
                    maxReconnects: 3);
            }

            DateTime authDeadline = DateTime.UtcNow.AddSeconds(reusePhase1Socket ? 2 : 15);
            while (!state.MiddlewareAuthenticated && !state.IsCaptureComplete &&
                   DateTime.UtcNow < authDeadline && !listenCts.IsCancellationRequested)
            {
                await Task.Delay(150, listenCts.Token).ConfigureAwait(false);
            }

            if (!state.MiddlewareAuthenticated)
            {
                append("[GAMA] Phase 2: middleware is not in_game yet; sending GAMA:1000 commands anyway.");
            }
            else
            {
                append("[GAMA] Phase 2: middleware is in_game; sending create_init_player + send_init_data on GAMA:1000.");
                if (reusePhase1Socket)
                {
                    try
                    {
                        await SendExecutableAskAsync(
                                phase1WebSocket, "send_init_data", connectionId, listenCts.Token, sessionId, append)
                            .ConfigureAwait(false);
                        append("[GAMA] Phase 2: send_init_data was also sent on the middleware (8080).");
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] Phase 2 middleware send_init_data: " + ex.Message);
                    }
                }
            }

            try
            {
                await RunHybridGamaCommandChannelAsync(
                        hostNorm, connectionId, state, result, outputDirectory, ct, sessionId, append)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                append("[GAMA] Phase 2 GAMA:1000 cancelled (new capture or panel closed).");
            }

            state.TightenCaptureDeadline(
                DateTime.UtcNow.AddSeconds(state.IsCaptureComplete ? 2 : 30),
                append,
                state.IsCaptureComplete ? "JSON received" : "phase 2 ended without json_output");

            DateTime listenUntil = DateTime.UtcNow.AddSeconds(state.IsCaptureComplete ? 1 : 30);
            if (phaseDeadline < listenUntil)
            {
                listenUntil = phaseDeadline;
            }

            append("[GAMA] Phase 2: waiting for middleware json_output for up to " +
                   Math.Max(0, (int)(listenUntil - DateTime.UtcNow).TotalSeconds) + " s…");
            while (!state.IsCaptureComplete && DateTime.UtcNow < listenUntil && !listenCts.IsCancellationRequested)
            {
                await Task.Delay(300, listenCts.Token).ConfigureAwait(false);
            }

            listenCts.Cancel();
            try
            {
                await listenTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal
            }
        }
    }

    private static async Task RunMiddlewareJsonListenWithReconnectAsync(
        string middlewareHost,
        string middlewarePort,
        string connectionId,
        string outputDirectory,
        CaptureResult result,
        CaptureState state,
        CancellationToken ct,
        int sessionId,
        int handshakeHeartbeatMs,
        DateTime deadlineUtc,
        Action<string> append,
        int maxReconnects = 8)
    {
        string hostNorm = string.IsNullOrWhiteSpace(middlewareHost) ? "localhost" : middlewareHost.Trim();
        string portNorm = string.IsNullOrWhiteSpace(middlewarePort) ? "8080" : middlewarePort.Trim();
        Uri mwUri = new Uri("ws://" + hostNorm + ":" + portNorm + "/");
        int reconnectAttempt = 0;

        while (!ct.IsCancellationRequested && !state.IsCaptureComplete &&
               DateTime.UtcNow < deadlineUtc && reconnectAttempt <= maxReconnects)
        {
            using (ClientWebSocket ws = new ClientWebSocket())
            {
                ws.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(Math.Max(1000, handshakeHeartbeatMs));

                try
                {
                    await ws.ConnectAsync(mwUri, ct).ConfigureAwait(false);
                    if (reconnectAttempt > 0)
                    {
                        append("[GAMA] Phase 2: middleware reconnected (" + reconnectAttempt + ").");
                    }
                }
                catch (Exception ex)
                {
                    append("[GAMA] Phase 2 middleware connection: " + ex.Message);
                    reconnectAttempt++;
                    await Task.Delay(400, ct).ConfigureAwait(false);
                    continue;
                }

                string handshake = JsonConvert.SerializeObject(new
                {
                    type = "connection",
                    id = connectionId,
                    heartbeat = handshakeHeartbeatMs.ToString()
                });

                try
                {
                    await ws.SendAsync(
                            new ArraySegment<byte>(Encoding.UTF8.GetBytes(handshake)),
                            WebSocketMessageType.Text,
                            true,
                            ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    append("[GAMA] Phase 2 handshake: " + ex.Message);
                }

                if (reconnectAttempt == 0)
                {
                    state.MiddlewareAuthenticated = false;
                    state.MiddlewareConnected = false;
                }

                await RunMiddlewareJsonReceiveLoopAsync(
                        ws, outputDirectory, result, state, ct, sessionId, append, deadlineUtc)
                    .ConfigureAwait(false);

                if (state.IsCaptureComplete || ct.IsCancellationRequested || DateTime.UtcNow >= deadlineUtc)
                {
                    return;
                }
            }

            reconnectAttempt++;
            Dbg(append, sessionId, "phase2-listen",
                "middleware reconnection #" + reconnectAttempt + " (socket closed too early)");
            await Task.Delay(350, ct).ConfigureAwait(false);
        }
    }

    private static async Task RunMiddlewareJsonReceiveLoopAsync(
        ClientWebSocket ws,
        string outputDirectory,
        CaptureResult result,
        CaptureState state,
        CancellationToken ct,
        int sessionId,
        Action<string> append,
        DateTime deadlineUtc)
    {
        byte[] buffer = new byte[64 * 1024];
        StringBuilder pending = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && !state.IsCaptureComplete &&
                   ws.State == WebSocketState.Open && DateTime.UtcNow < deadlineUtc)
            {
                WebSocketReceiveResult rr;
                try
                {
                    using (CancellationTokenSource sliceCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        sliceCts.CancelAfter(TimeSpan.FromSeconds(1));
                        rr = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), sliceCts.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    continue;
                }

                if (rr.MessageType == WebSocketMessageType.Close)
                {
                    Dbg(append, sessionId, "phase2-listen",
                        "middleware Close status=" + rr.CloseStatus + " desc=" + (rr.CloseStatusDescription ?? ""));
                    break;
                }

                pending.Append(Encoding.UTF8.GetString(buffer, 0, rr.Count));
                if (!rr.EndOfMessage)
                {
                    continue;
                }

                string text = pending.ToString();
                pending.Length = 0;

                await HandleIncomingAsync(ws, text, outputDirectory, result, append, state, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            append("[GAMA] Phase 2 middleware listener: " + ex.Message);
        }
    }

    /// <summary>
    /// GAMA:1000 commands: create_init_player + send_init_data (SimulationOutput is relayed by the middleware to 8080).
    /// </summary>
    private static async Task RunHybridGamaCommandChannelAsync(
        string gamaHost,
        string connectionId,
        CaptureState state,
        CaptureResult result,
        string outputDirectory,
        CancellationToken ct,
        int sessionId,
        Action<string> append)
    {
        if (state.HybridGamaCommandsStarted)
        {
            return;
        }

        state.HybridGamaCommandsStarted = true;

        bool lockTaken = false;
        try
        {
            await GamaPort1000Lock.WaitAsync(TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
            lockTaken = true;
        }
        catch (Exception ex)
        {
            append("[GAMA] Phase 2 GAMA:1000: port 1000 lock unavailable: " + ex.Message);
            state.HybridGamaCommandsDone = true;
            return;
        }

        string expId = "0";
        int initSent = 0;

        try
        {
            using (CancellationTokenSource channelCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                channelCts.CancelAfter(TimeSpan.FromSeconds(45));

                using (ClientWebSocket gamaWs = new ClientWebSocket())
                {
                    Uri gamaUri = new Uri("ws://" + gamaHost + ":1000/");
                    append("[GAMA] Phase 2 GAMA:1000: connecting to " + gamaUri);
                    await gamaWs.ConnectAsync(gamaUri, channelCts.Token).ConfigureAwait(false);

                    Task receiveTask = ReceiveGamaDirectJsonLoopAsync(
                        gamaWs, outputDirectory, result, state, sessionId, append, channelCts.Token);

                    await Task.Delay(400, channelCts.Token).ConfigureAwait(false);

                    string escapedId = connectionId.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    await SendDirectGamaExpressionAsync(
                            gamaWs, "do create_init_player(\"" + escapedId + "\");", expId, channelCts.Token)
                        .ConfigureAwait(false);
                    append("[GAMA] Phase 2 GAMA:1000: create_init_player(\"" + connectionId + "\").");
                    await Task.Delay(2000, channelCts.Token).ConfigureAwait(false);

                    for (int i = 0; i < 3 && !state.IsCaptureComplete && !channelCts.IsCancellationRequested; i++)
                    {
                        await SendExecutableAskAsync(
                                gamaWs, "send_init_data", connectionId, channelCts.Token, sessionId, append)
                            .ConfigureAwait(false);
                        initSent++;
                        state.HybridGamaInitSentCount = initSent;
                        append("[GAMA] Phase 2 GAMA:1000: send_init_data (" + initSent + "/3).");
                        if (i < 2)
                        {
                            await Task.Delay(2000, channelCts.Token).ConfigureAwait(false);
                        }
                    }

                    if (!state.IsCaptureComplete && !channelCts.IsCancellationRequested)
                    {
                        await Task.Delay(1500, channelCts.Token).ConfigureAwait(false);
                        await SendExecutableAskAsync(
                                gamaWs, "player_ready_to_receive_geometries", connectionId, channelCts.Token, sessionId, append)
                            .ConfigureAwait(false);
                        state.HybridGamaGeomReadySent = true;
                        append("[GAMA] Phase 2 GAMA:1000: player_ready_to_receive_geometries.");
                    }

                    if (lockTaken)
                    {
                        GamaPort1000Lock.Release();
                        lockTaken = false;
                    }

                    DateTime waitJsonUntil = DateTime.UtcNow.AddSeconds(12);
                    while (!state.IsCaptureComplete && DateTime.UtcNow < waitJsonUntil &&
                           !channelCts.IsCancellationRequested)
                    {
                        await Task.Delay(400, channelCts.Token).ConfigureAwait(false);
                    }

                    try
                    {
                        channelCts.Cancel();
                        await receiveTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // normal
                    }

                    try
                    {
                        if (gamaWs.State == WebSocketState.Open)
                        {
                            using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                            {
                                await gamaWs.CloseAsync(
                                    WebSocketCloseStatus.NormalClosure, "phase2 done", closeCts.Token);
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            if (initSent > 0)
            {
                append("[GAMA] Phase 2 GAMA:1000 completed (" + initSent + " send_init_data, create_init_player sent).");
            }
        }
        catch (OperationCanceledException)
        {
            // normal
        }
        catch (Exception ex)
        {
            append("[GAMA] Phase 2 GAMA:1000: " + ex.Message);
        }
        finally
        {
            if (lockTaken)
            {
                GamaPort1000Lock.Release();
                lockTaken = false;
            }

            state.HybridGamaCommandsDone = true;
        }
    }

    private static void TryRespondToMiddlewarePing(ClientWebSocket ws, string connectionId)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            return;
        }

        string pong = string.IsNullOrWhiteSpace(connectionId)
            ? "{\"type\":\"pong\"}"
            : JsonConvert.SerializeObject(new { type = "pong", id = connectionId });
        byte[] bytes = Encoding.UTF8.GetBytes(pong);
        try
        {
            _ = ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // ignore
        }
    }

    private static async Task ReceiveGamaDirectJsonLoopAsync(
        ClientWebSocket gamaWs,
        string outputDirectory,
        CaptureResult result,
        CaptureState state,
        int sessionId,
        Action<string> append,
        CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        StringBuilder pending = new StringBuilder();

        while (!ct.IsCancellationRequested && gamaWs.State == WebSocketState.Open && !state.IsCaptureComplete)
        {
            WebSocketReceiveResult rr;
            try
            {
                using (CancellationTokenSource slice = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    slice.CancelAfter(TimeSpan.FromSeconds(2));
                    rr = await gamaWs.ReceiveAsync(new ArraySegment<byte>(buffer), slice.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            if (rr.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            pending.Append(Encoding.UTF8.GetString(buffer, 0, rr.Count));
            if (!rr.EndOfMessage)
            {
                continue;
            }

            string text = pending.ToString();
            pending.Length = 0;

            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch
            {
                continue;
            }

            string type = json.Value<string>("type") ?? string.Empty;
            if (type == "SimulationOutput")
            {
                JToken contentToken = json["content"];
                string content = contentToken?.Type == JTokenType.String
                    ? contentToken.Value<string>()
                    : contentToken?.ToString(Formatting.None);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    CapLog(append, sessionId, "phase2-1000", "SimulationOutput received.");
                    try
                    {
                        TryProcessGamaOutputPayload(JToken.Parse(content), outputDirectory, result, append, state);
                    }
                    catch
                    {
                        // ignore malformed payloads
                    }
                }
            }
            else if (type == "CommandExecutedSuccessfully")
            {
                JToken cmd = json["command"];
                string action = cmd?["action"]?.ToString() ?? string.Empty;
                string expr = cmd?["expr"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(action) || !string.IsNullOrEmpty(expr))
                {
                    Dbg(append, sessionId, "phase2-1000",
                        "CommandExecutedSuccessfully action=" + action + " expr=" +
                        (expr.Length > 80 ? expr.Substring(0, 80) + "…" : expr));
                }
            }
            else if (type == "ConnectionSuccessful")
            {
                Dbg(append, sessionId, "phase2-1000", "ConnectionSuccessful.");
            }
            else if (type == "output")
            {
                ProcessOutputEnvelope(json["contents"], outputDirectory, result, append, state);
            }
            else if (type == "json_output")
            {
                ProcessJsonOutputContents(json["contents"], outputDirectory, result, append, state);
            }
        }
    }

    private static async Task TryDirectCreatePlayerAsync(
        ClientWebSocket ws,
        CaptureState state,
        Action<string> append,
        CancellationToken ct,
        bool force = false)
    {
        int sid = state.DebugSessionId;
        if (!state.DirectGamaMode)
        {
            Dbg(append, sid, "create_player", "SKIP not in direct mode");
            return;
        }

        if (state.DirectCreatePlayerSent)
        {
            Dbg(append, sid, "create_player", "SKIP already marked sent/confirmed");
            return;
        }

        if (!state.DirectLoadCompleted)
        {
            Dbg(append, sid, "create_player", "SKIP load not complete");
            return;
        }

        if (!state.DirectPlayCompleted)
        {
            Dbg(append, sid, "create_player", "SKIP play/simulation not ready status=" + state.LastSimulationStatus);
            return;
        }

        if (!force && DateTime.UtcNow < state.NextDirectCreatePlayerUtc)
        {
            return;
        }

        if (state.DirectCreatePlayerAttempts >= 20)
        {
            if (!state.DirectCreatePlayerSent)
            {
                state.DirectCreatePlayerSent = true;
                append("[GAMA] create_player abandoned after 20 attempts.");
            }

            return;
        }

        string connectionId = string.IsNullOrWhiteSpace(state.ConnectionId) ? StaticInformation.getId() : state.ConnectionId;
        state.DirectCreatePlayerAttempts++;
        state.NextDirectCreatePlayerUtc = DateTime.UtcNow.AddSeconds(1);
        try
        {
            string expr = "do create_player(\"" + connectionId + "\");";
            await SendDirectGamaExpressionAsync(ws, expr, state.DirectExperimentId, ct).ConfigureAwait(false);
            append("[GAMA] create_player attempt " + state.DirectCreatePlayerAttempts +
                   " (exp_id=" + state.DirectExperimentId + ").");
        }
        catch (Exception ex)
        {
            append("[GAMA] create_player failed: " + ex.Message);
        }
    }

    private static async Task SendPlayAsync(
        ClientWebSocket ws,
        CancellationToken ct,
        int sessionId = 0,
        Action<string> append = null)
    {
        if (ws == null || ws.State != WebSocketState.Open)
        {
            if (append != null)
            {
                Dbg(append, sessionId, "send", "play SKIPPED ws=" + (ws == null ? "null" : ws.State.ToString()));
            }

            return;
        }

        string payload = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            { "type", "play" }
        });
        if (append != null)
        {
            DbgOutgoing(append, sessionId, "play", payload);
        }

        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts load/play/create_player on the GAMA server (port 1000) while a middleware capture listens on 8080.
    /// </summary>
    private static async Task RunGamaSimulationBootstrapAsync(
        int sessionId,
        string host,
        string port,
        string modelPath,
        string experimentName,
        string connectionId,
        bool skipRemoteLoad,
        CaptureState mainCaptureState,
        CancellationToken ct,
        Action<string> append)
    {
        append(skipRemoteLoad
            ? "[GAMA] Bootstrap create_player GAMA ws://" + host + ":" + port + "/ id=\"" + connectionId + "\""
            : "[GAMA] Bootstrap simulation GAMA ws://" + host + ":" + port + "/ → " + experimentName);
        Dbg(append, sessionId, "bootstrap", "Waiting for GAMA port lock (prevents two parallel WebSocket loads)...");
        bool lockTaken = false;
        try
        {
            await GamaPort1000Lock.WaitAsync(TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
            lockTaken = true;
            Dbg(append, sessionId, "bootstrap", "GAMA port lock acquired.");
        }
        catch (Exception ex)
        {
            append("[GAMA] Bootstrap: could not acquire the GAMA lock: " + ex.Message);
            return;
        }

        try
        {
            await RunGamaSimulationBootstrapCoreAsync(
                sessionId, host, port, modelPath, experimentName, connectionId, skipRemoteLoad, mainCaptureState, ct, append)
                .ConfigureAwait(false);
        }
        finally
        {
            if (lockTaken)
            {
                GamaPort1000Lock.Release();
                Dbg(append, sessionId, "bootstrap", "GAMA port lock released.");
            }

            if (mainCaptureState != null && mainCaptureState.GamaBootstrapPhase == 1)
            {
                mainCaptureState.GamaBootstrapPhase = 3;
            }
        }
    }

    private static async Task RunGamaSimulationBootstrapCoreAsync(
        int sessionId,
        string host,
        string port,
        string modelPath,
        string experimentName,
        string connectionId,
        bool skipRemoteLoad,
        CaptureState mainCaptureState,
        CancellationToken ct,
        Action<string> append)
    {
        Dbg(append, sessionId, "bootstrap", "Core started model=" + modelPath + " exp=" + experimentName);

        Uri uri;
        try
        {
            uri = new Uri("ws://" + host + ":" + port.Trim() + "/");
        }
        catch (Exception ex)
        {
            append("[GAMA] Bootstrap: invalid URL: " + ex.Message);
            return;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            linked.CancelAfter(TimeSpan.FromMinutes(4));
            try
            {
                await ws.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                append("[GAMA] Bootstrap: could not connect: " + ex.Message + " (is GAMA open?)");
                Dbg(append, sessionId, "bootstrap", "ConnectAsync failed: " + ex);
                return;
            }

            Dbg(append, sessionId, "bootstrap", "WebSocket opened state=" + ws.State);

            CaptureState boot = new CaptureState
            {
                DirectGamaMode = true,
                DirectLoadRequested = !skipRemoteLoad,
                DirectLoadCompleted = skipRemoteLoad,
                DirectPlayCompleted = skipRemoteLoad,
                DirectRunningSinceUtc = skipRemoteLoad ? DateTime.UtcNow : (DateTime?)null,
                LastSimulationStatus = skipRemoteLoad ? "ASSUMED_OPEN" : null,
                DirectModelPath = modelPath,
                DirectExperimentName = experimentName,
                ConnectionId = connectionId,
                DebugSessionId = sessionId,
                SkipRemoteLoad = skipRemoteLoad
            };

            if (skipRemoteLoad)
            {
                Dbg(append, sessionId, "bootstrap", "skipRemoteLoad=true -> create_player without load/play");
            }

            byte[] buffer = new byte[64 * 1024];
            StringBuilder pending = new StringBuilder();
            DateTime deadline = DateTime.UtcNow.AddMinutes(3);
            DateTime startedUtc = DateTime.UtcNow;
            int idleSlices = 0;

            while (!linked.Token.IsCancellationRequested && DateTime.UtcNow < deadline && ws.State == WebSocketState.Open)
            {
                if (boot.DirectCreatePlayerConfirmedUtc.HasValue)
                {
                    append("[GAMA] Bootstrap completed (create_player confirmed).");
                    Dbg(append, sessionId, "bootstrap", "OK " + FormatCaptureStateSnapshot(boot, connectionId, ws.State));
                    if (mainCaptureState != null)
                    {
                        mainCaptureState.GamaBootstrapPhase = 2;
                    }

                    return;
                }

                WebSocketReceiveResult rr;
                try
                {
                    using (CancellationTokenSource slice = CancellationTokenSource.CreateLinkedTokenSource(linked.Token))
                    {
                        slice.CancelAfter(TimeSpan.FromSeconds(1));
                        rr = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), slice.Token).ConfigureAwait(false);
                    }

                    idleSlices = 0;
                }
                catch (OperationCanceledException)
                {
                    idleSlices++;
                    if (idleSlices == 1 || idleSlices % 10 == 0)
                    {
                        Dbg(append, sessionId, "bootstrap",
                            "idle " + idleSlices + "s | " + FormatCaptureStateSnapshot(boot, connectionId, ws.State));
                    }

                    if (boot.DirectLoadSent && !boot.DirectLoadCompleted && idleSlices % 20 == 0)
                    {
                        append("[GAMA] Bootstrap: Load is still not confirmed. Click Yes in GAMA if a dialog is open.");
                    }

                    if (!boot.DirectPlaySent && boot.DirectLoadCompleted && !boot.DirectPlayCompleted)
                    {
                        Dbg(append, sessionId, "bootstrap", "Retrying play (load OK, play not sent yet).");
                        try
                        {
                            await SendPlayAsync(ws, linked.Token, sessionId).ConfigureAwait(false);
                            boot.DirectPlaySent = true;
                        }
                        catch (Exception ex)
                        {
                            Dbg(append, sessionId, "bootstrap", "play failed: " + ex.Message);
                        }
                    }

                    if (boot.DirectPlayCompleted && !boot.DirectCreatePlayerConfirmedUtc.HasValue)
                    {
                        await TryDirectCreatePlayerAsync(ws, boot, append, linked.Token, force: true).ConfigureAwait(false);
                    }

                    continue;
                }

                if (rr.MessageType == WebSocketMessageType.Close)
                {
                    CapLog(append, sessionId, "bootstrap",
                        "WebSocket CLOSED status=" + rr.CloseStatus + " desc=" + (rr.CloseStatusDescription ?? "") +
                        " | loadSent=" + boot.DirectLoadSent + " loadOK=" + boot.DirectLoadCompleted +
                        " playSent=" + boot.DirectPlaySent + " createOK=" + boot.DirectCreatePlayerConfirmedUtc.HasValue);
                    if (boot.DirectLoadSent && !boot.DirectLoadCompleted)
                    {
                        append("[GAMA] Bootstrap: GAMA closed the connection after load. Is a close-simulation dialog open? Select 'Experiment already open' if the simulation is already running.");
                    }
                    else if (!boot.DirectLoadSent && !boot.SkipRemoteLoad)
                    {
                        append("[GAMA] Bootstrap: connection closed before load. Is GAMA busy with another client on port 1000? Purge Player_* and try again.");
                    }

                    break;
                }

                pending.Append(Encoding.UTF8.GetString(buffer, 0, rr.Count));
                if (!rr.EndOfMessage)
                {
                    Dbg(append, sessionId, "bootstrap", "Partial fragment (" + rr.Count + " bytes), waiting for end of message...");
                    continue;
                }

                string text = pending.ToString();
                pending.Length = 0;
                DbgRaw(append, sessionId, "bootstrap", text);

                JObject json;
                try
                {
                    json = JObject.Parse(text);
                }
                catch (Exception ex)
                {
                    Dbg(append, sessionId, "bootstrap", "Could not parse JSON: " + ex.Message);
                    continue;
                }

                string type = json.Value<string>("type") ?? string.Empty;
                Dbg(append, sessionId, "bootstrap", "Message type=" + type);

                if (type == "ConnectionSuccessful" && !boot.DirectLoadSent && !boot.DirectLoadPending)
                {
                    if (boot.SkipRemoteLoad)
                    {
                        boot.DirectLoadCompleted = true;
                        boot.DirectPlayCompleted = true;
                        boot.DirectRunningSinceUtc = DateTime.UtcNow;
                        boot.LastSimulationStatus = "ASSUMED_OPEN";
                        CapLog(append, sessionId, "bootstrap", "ConnectionSuccessful -> create_player (without load)");
                        await TryDirectCreatePlayerAsync(ws, boot, append, linked.Token, force: true).ConfigureAwait(false);
                    }
                    else
                    {
                        boot.DirectLoadSent = true;
                        CapLog(append, sessionId, "bootstrap",
                            "ConnectionSuccessful -> sending load IMMEDIATELY " + experimentName + " | ws=" + ws.State);
                        append("[GAMA] Bootstrap: sending load " + experimentName);
                        try
                        {
                            await SendLoadAsync(ws, modelPath, experimentName, linked.Token, sessionId, append)
                                .ConfigureAwait(false);
                            CapLog(append, sessionId, "bootstrap", "load sent, waiting for CommandExecutedSuccessfully...");
                        }
                        catch (Exception ex)
                        {
                            CapLog(append, sessionId, "bootstrap", "FAILED to send load: " + ex.Message);
                        }
                    }

                    continue;
                }

                if (boot.DirectLoadPending && !boot.DirectLoadSent)
                {
                    boot.DirectLoadPending = false;
                    boot.DirectLoadSent = true;
                    CapLog(append, sessionId, "bootstrap", "load pending (safety net) -> sending load");
                    append("[GAMA] Bootstrap: sending load " + experimentName);
                    try
                    {
                        await SendLoadAsync(ws, modelPath, experimentName, linked.Token, sessionId, append).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        CapLog(append, sessionId, "bootstrap", "load send failed: " + ex.Message);
                    }

                    continue;
                }

                if (type == "CommandExecutedSuccessfully")
                {
                    JObject command = json["command"] as JObject;
                    string commandType = command != null ? command.Value<string>("type") : string.Empty;
                    Dbg(append, sessionId, "bootstrap", "CommandExecutedSuccessfully cmd=" + commandType);
                    if (commandType == "load")
                    {
                        boot.DirectLoadCompleted = true;
                        boot.DirectPlayCompleted = false;
                        if (!boot.DirectPlaySent)
                        {
                            await SendPlayAsync(ws, linked.Token, sessionId).ConfigureAwait(false);
                            boot.DirectPlaySent = true;
                            append("[GAMA] Bootstrap: play sent.");
                        }
                    }
                    else if (commandType == "play")
                    {
                        boot.DirectPlayCompleted = true;
                        await TryDirectCreatePlayerAsync(ws, boot, append, linked.Token, force: true).ConfigureAwait(false);
                    }
                    else if (command?["expr"]?.Value<string>()?.IndexOf("create_player", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        boot.DirectCreatePlayerSent = true;
                        boot.DirectCreatePlayerConfirmedUtc = DateTime.UtcNow;
                        append("[GAMA] Bootstrap: create_player confirmed.");
                        if (mainCaptureState != null)
                        {
                            mainCaptureState.GamaBootstrapPhase = 2;
                        }

                        return;
                    }

                    continue;
                }

                if (type == "UnableToExecuteRequest" || type == "GamaServerError" || type == "RuntimeError")
                {
                    append("[GAMA] Bootstrap: " + type + " -> " + (json["content"]?.ToString(Formatting.None) ?? ""));
                }
                else if (type == "SimulationStatus")
                {
                    string status = json["content"]?.ToString() ?? string.Empty;
                    boot.LastSimulationStatus = status;
                    Dbg(append, sessionId, "bootstrap", "SimulationStatus=" + status + " exp_id=" + json["exp_id"]);
                    if (string.Equals(status, "RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        boot.DirectPlayCompleted = true;
                        string expId = json["exp_id"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(expId))
                        {
                            boot.DirectExperimentId = expId;
                        }

                        if (boot.DirectRunningSinceUtc == null)
                        {
                            boot.DirectRunningSinceUtc = DateTime.UtcNow;
                        }

                        await TryDirectCreatePlayerAsync(ws, boot, append, linked.Token, force: true).ConfigureAwait(false);
                    }
                }
                else
                {
                    Dbg(append, sessionId, "bootstrap", "Unhandled bootstrap type: " + type);
                }
            }

            double elapsed = (DateTime.UtcNow - startedUtc).TotalSeconds;
            if (!boot.DirectCreatePlayerConfirmedUtc.HasValue)
            {
                append("[GAMA] Bootstrap: create_player was not confirmed (simulation or blocking GAMA dialog).");
                Dbg(append, sessionId, "bootstrap",
                    "END after " + elapsed.ToString("0") + "s | " + FormatCaptureStateSnapshot(boot, connectionId, ws.State));
            }
        }
    }

    /// <summary>
    /// Repeats <c>send_init_data</c> like <see cref="SimulationManager"/> in LOADING_DATA, then sends
    /// <c>player_ready_to_receive_geometries</c> once precision+properties are received (like entering GAME).
    /// </summary>
    private static async Task RunMiddlewarePumpAsync(
        ClientWebSocket ws,
        CaptureState state,
        string connectionId,
        Task bootstrapTask,
        CaptureResult result,
        CancellationToken ct,
        Action<string> append)
    {
        DateTime nextGeomReadyUtc = DateTime.MinValue;
        DateTime nextInitUtc = DateTime.UtcNow;
        DateTime nextDirectInitUtc = DateTime.UtcNow;
        DateTime pumpStartedUtc = DateTime.UtcNow;
        bool loggedAuthFallback = false;
        bool loggedDirectInitStrategy = false;
        int sid = state.DebugSessionId;
        try
        {
            while (!ct.IsCancellationRequested && !state.IsCaptureComplete && !state.CaptureAbortRequested)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
                state.DebugPumpTicks++;

                if (state.NeedsOccupiedPlayerPurge && state.AutoPurgeAttemptCount < 4)
                {
                    state.NeedsOccupiedPlayerPurge = false;
                    string ghost = state.MiddlewareOccupiedPlayerId;
                    if (!string.IsNullOrEmpty(ghost) &&
                        !string.Equals(ghost, connectionId, StringComparison.OrdinalIgnoreCase))
                    {
                        state.AutoPurgeAttemptCount++;
                        CapLog(append, sid, "auto-purge",
                            "Attempt " + state.AutoPurgeAttemptCount + "/4: remove_player GAMA '" + ghost + "' (without a second WebSocket)...");
                        if (ws.State == WebSocketState.Open)
                        {
                            try
                            {
                                string escapedGhost = ghost.Replace("\\", "\\\\").Replace("\"", "\\\"");
                                await SendMiddlewareExpressionAsync(
                                        ws, "do remove_player(\"" + escapedGhost + "\");", ct, sid, append)
                                    .ConfigureAwait(false);
                                CapLog(append, sid, "auto-purge", "remove_player(\"" + ghost + "\") sent.");
                            }
                            catch (Exception ex)
                            {
                                CapLog(append, sid, "auto-purge", "remove_player failed: " + ex.Message);
                            }
                        }

                        try
                        {
                            await Task.Delay(2000, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        state.MiddlewareOccupiedPlayerId = null;
                        state.MiddlewareAuthenticated = false;
                    }
                    else if (!string.IsNullOrEmpty(ghost) &&
                             string.Equals(ghost, connectionId, StringComparison.OrdinalIgnoreCase))
                    {
                        state.MiddlewareOccupiedPlayerId = null;
                        state.MiddlewareAuthenticated = true;
                        CapLog(append, sid, "auto-purge",
                            "json_state in_game=\"" + ghost + "\" = capture ID (same middleware IP) -> OK.");
                    }
                }

                double slotAbortSeconds = state.SkipRemoteLoad ? 45 : 25;
                if (!string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId) &&
                    (DateTime.UtcNow - pumpStartedUtc).TotalSeconds > slotAbortSeconds)
                {
                    result.Error = BuildMissingError(state);
                    state.CaptureAbortRequested = true;
                    CapLog(append, sid, "pump", "ABORT player slot still occupied after purges.");
                    break;
                }
                if (VerboseCaptureDebug &&
                    (state.DebugLastPumpLogUtc == DateTime.MinValue ||
                     (DateTime.UtcNow - state.DebugLastPumpLogUtc).TotalSeconds >= 3))
                {
                    state.DebugLastPumpLogUtc = DateTime.UtcNow;
                    Dbg(append, state.DebugSessionId, "pump",
                        "tick #" + state.DebugPumpTicks + " | " +
                        FormatCaptureStateSnapshot(state, connectionId, ws.State));
                }

                if (ws.State != WebSocketState.Open)
                {
                    Dbg(append, state.DebugSessionId, "pump", "STOP WebSocket closed");
                    break;
                }

                if (EDITOR_PREVIEW_IMMEDIATE_INIT_BURST &&
                    state.ManagedFromUnity &&
                    !state.DirectGamaMode &&
                    state.MiddlewareAuthenticated &&
                    string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId))
                {
                    if (!state.ImmediateInitBurstStarted)
                    {
                        state.ImmediateInitBurstStarted = true;
                        state.ImmediateInitBurstEndUtc = DateTime.UtcNow.AddSeconds(5);
                        state.ImmediateInitBurstNextSendUtc = DateTime.UtcNow;
                    }

                    if (!state.ReceivedJsonOutput &&
                        DateTime.UtcNow < state.ImmediateInitBurstEndUtc &&
                        DateTime.UtcNow >= state.ImmediateInitBurstNextSendUtc)
                    {
                        state.ImmediateInitBurstNextSendUtc = DateTime.UtcNow.AddMilliseconds(500);
                        state.ImmediateInitBurstSendCount++;
                        int burstN = state.ImmediateInitBurstSendCount;
                        try
                        {
                            CapLog8080(append, "OUT",
                                "send_init_data #" + burstN + " id=" + connectionId);
                            await SendExecutableAskAsync(
                                    ws, "send_init_data", connectionId, ct, state.DebugSessionId, append)
                                .ConfigureAwait(false);
                            state.SendInitDataSuccessCount++;

                            if (!state.ImmediateInitBurstGeomSent)
                            {
                                state.ImmediateInitBurstGeomSent = true;
                                CapLog8080(append, "OUT",
                                    "player_ready_to_receive_geometries id=" + connectionId);
                                await SendExecutableAskAsync(
                                        ws, "player_ready_to_receive_geometries", connectionId, ct,
                                        state.DebugSessionId, append)
                                    .ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex)
                        {
                            append("[GAMA][CAPTURE][8080] burst failed: " + ex.Message);
                        }

                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId) &&
                    !state.LoggedPlayerSlotConflict)
                {
                    state.LoggedPlayerSlotConflict = true;
                    append("[GAMA] Player slot occupied by '" + state.MiddlewareOccupiedPlayerId +
                           "'. Stop Unity Play Mode, purge this player, then start capture again (id '" + connectionId + "').");
                    if (state.SkipRemoteLoad)
                    {
                        append("[GAMA] Tip: when the experiment was launched in GAMA (Yes button), keep 'Experiment already open' selected.");
                    }
                }

                if (state.HybridGamaCommandChannel && !state.DirectGamaMode &&
                    state.DebugPumpTicks % 25 == 0 && !state.IsCaptureComplete)
                {
                    Dbg(append, state.DebugSessionId, "hybrid",
                        "Play-like middleware pump (init=" + state.HybridGamaInitSentCount +
                        ", fallback1000=" + (state.HybridGamaCommandsDone ? "complete" : "in progress") + ")...");
                }

                if (state.SkipRemoteLoad &&
                    !state.DirectGamaMode &&
                    !state.HybridGamaCommandChannel &&
                    !state.MiddlewareCreatePlayerSent &&
                    string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId) &&
                    ws.State == WebSocketState.Open &&
                    (DateTime.UtcNow - pumpStartedUtc).TotalSeconds >= 0.5)
                {
                    state.MiddlewareCreatePlayerSent = true;
                    state.MiddlewareInitDataAllowedUtc = DateTime.UtcNow.AddSeconds(1.5);
                    string escapedId = connectionId.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    append("[GAMA] create_player through middleware (experiment already open) id=\"" + connectionId + "\".");
                    try
                    {
                        await SendMiddlewareExpressionAsync(
                                ws, "do create_player(\"" + escapedId + "\");", ct, state.DebugSessionId, append)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] Middleware create_player failed: " + ex.Message);
                    }
                }

                if (!state.DirectGamaMode && state.GamaBootstrapPhase == 1)
                {
                    if (bootstrapTask != null && bootstrapTask.IsCompleted)
                    {
                        state.GamaBootstrapPhase = bootstrapTask.IsFaulted ? 3 : state.GamaBootstrapPhase;
                    }
                    else if ((DateTime.UtcNow - pumpStartedUtc).TotalSeconds < 45)
                    {
                        if (state.DebugPumpTicks % 20 == 0)
                        {
                            Dbg(append, state.DebugSessionId, "pump", "waiting for GAMA bootstrap (create_player/load)...");
                        }

                        continue;
                    }
                    else
                    {
                        append("[GAMA] GAMA bootstrap is taking too long; sending send_init_data anyway.");
                        state.GamaBootstrapPhase = 3;
                    }
                }

                if (!state.MiddlewareAuthenticated &&
                    !state.DirectGamaMode)
                {
                    bool waitConnected = state.MiddlewareConnected &&
                                         (DateTime.UtcNow - pumpStartedUtc) > TimeSpan.FromSeconds(2);
                    bool waitLong = (DateTime.UtcNow - pumpStartedUtc) > TimeSpan.FromSeconds(15);
                    bool skipLoadReady = state.SkipRemoteLoad &&
                                         string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId) &&
                                         (state.MiddlewareCreatePlayerSent ||
                                          (DateTime.UtcNow - pumpStartedUtc).TotalSeconds >= 4);
                    if ((waitConnected || waitLong || skipLoadReady) &&
                        string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId))
                    {
                        state.MiddlewareAuthenticated = true;
                        if (!loggedAuthFallback)
                        {
                            loggedAuthFallback = true;
                            if (state.SkipRemoteLoad)
                            {
                                append("[GAMA] Middleware slot is free; sending send_init_data (GAMA experiment already open).");
                            }
                            else
                            {
                                append(waitConnected
                                    ? "[GAMA] Middleware connected (in_game may be false); sending send_init_data."
                                    : "[GAMA] No useful json_state after 15 s; sending send_init_data anyway.");
                            }
                        }
                    }
                }

                if (state.DirectGamaMode && state.DirectLoadPending && !state.DirectLoadSent)
                {
                    state.DirectLoadPending = false;
                    state.DirectLoadSent = true;
                    state.ExtendCaptureDeadline(240f, append, "load (from pump)");
                    append("[GAMA] Sending load from the pump (avoids a send/receive conflict). If GAMA asks to close the simulation, click Yes.");
                    try
                    {
                        await SendLoadAsync(ws, state.DirectModelPath, state.DirectExperimentName, ct, state.DebugSessionId, append)
                            .ConfigureAwait(false);
                        append("[GAMA] Load request sent: " + state.DirectExperimentName);
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] load failed: " + ex.Message);
                        Dbg(append, state.DebugSessionId, "direct", "failed to send load: " + ex.Message);
                    }
                }

                if (state.DirectGamaMode && state.DirectLoadCompleted && !state.DirectPlaySent && !state.DirectPlayCompleted)
                {
                    try
                    {
                        await SendPlayAsync(ws, ct, state.DebugSessionId, append).ConfigureAwait(false);
                        state.DirectPlaySent = true;
                        append("[GAMA] play command sent after load.");
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] play failed: " + ex.Message);
                    }
                }

                if (state.DirectGamaMode && !state.DirectCreatePlayerSent && DateTime.UtcNow >= nextDirectInitUtc)
                {
                    if (!state.DirectPlayCompleted)
                    {
                        if (state.DebugPumpTicks % 30 == 0)
                        {
                            Dbg(append, state.DebugSessionId, "create_player", "pump waiting for DirectPlayCompleted...");
                        }

                        continue;
                    }

                    bool simReady = state.DirectRunningSinceUtc != null ||
                                    string.Equals(state.LastSimulationStatus, "RUNNING", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(state.LastSimulationStatus, "PAUSED", StringComparison.OrdinalIgnoreCase);
                    if (!simReady)
                    {
                        if (state.DebugPumpTicks % 30 == 0)
                        {
                            Dbg(append, state.DebugSessionId, "create_player",
                            "pump waiting for RUNNING/PAUSED (status=" + state.LastSimulationStatus + ")");
                        }

                        continue;
                    }

                    if (state.DirectRunningSinceUtc.HasValue &&
                        (DateTime.UtcNow - state.DirectRunningSinceUtc.Value) < TimeSpan.FromSeconds(0.5))
                    {
                        continue;
                    }

                    await TryDirectCreatePlayerAsync(ws, state, append, ct).ConfigureAwait(false);
                }

                if (state.DirectGamaMode && state.DirectCreatePlayerSent && !state.GotPrecision)
                {
                    if (!loggedDirectInitStrategy)
                    {
                        loggedDirectInitStrategy = true;
                        append("[GAMA] Direct mode: waiting for SimulationOutput after create_player (play if PAUSED, then spaced send_init_data calls).");
                    }

                    if (string.Equals(state.LastSimulationStatus, "PAUSED", StringComparison.OrdinalIgnoreCase) &&
                        DateTime.UtcNow >= state.NextDirectResumePlayUtc)
                    {
                        state.NextDirectResumePlayUtc = DateTime.UtcNow.AddSeconds(2);
                        try
                        {
                            await SendPlayAsync(ws, ct).ConfigureAwait(false);
                            append("[GAMA] Resuming play (simulation paused after create_player).");
                        }
                        catch (Exception ex)
                        {
                            append("[GAMA] play (resume): " + ex.Message);
                        }
                    }

                    if (state.DirectCreatePlayerConfirmedUtc.HasValue &&
                        DateTime.UtcNow >= state.NextDirectInitAskUtc &&
                        state.SendInitDataFailureCount < 6 &&
                        state.SendInitDataSuccessCount < 6)
                    {
                        state.NextDirectInitAskUtc = DateTime.UtcNow.AddSeconds(2.5);
                        try
                        {
                            await SendExecutableAskAsync(
                                ws, "send_init_data", connectionId, ct, state.DebugSessionId, append)
                                .ConfigureAwait(false);
                            if (state.SkipRemoteLoad && state.MiddlewareGeomReadyBurstUtc == DateTime.MinValue)
                            {
                                state.MiddlewareGeomReadyBurstUtc = DateTime.UtcNow.AddSeconds(2);
                            }
                        }
                        catch (Exception ex)
                        {
                            append("[GAMA] send_init_data failed: " + ex.Message);
                        }
                    }
                }
                else if (state.MiddlewareAuthenticated && DateTime.UtcNow >= nextInitUtc)
                {
                    if (state.HybridGamaCommandsStarted)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId))
                    {
                        continue;
                    }

                    if (state.DirectGamaMode && (!state.DirectPlayCompleted || !state.DirectCreatePlayerSent))
                    {
                        continue;
                    }

                    if (state.SkipRemoteLoad &&
                        !state.DirectGamaMode &&
                        state.MiddlewareInitDataAllowedUtc > DateTime.MinValue &&
                        DateTime.UtcNow < state.MiddlewareInitDataAllowedUtc)
                    {
                        continue;
                    }

                    if (EDITOR_PREVIEW_IMMEDIATE_INIT_BURST &&
                        state.ManagedFromUnity &&
                        state.ImmediateInitBurstStarted)
                    {
                        continue;
                    }

                    if (state.ManagedFromUnity &&
                        !state.DirectGamaMode &&
                        state.MiddlewareInitDataAllowedUtc > DateTime.MinValue &&
                        DateTime.UtcNow < state.MiddlewareInitDataAllowedUtc)
                    {
                        continue;
                    }

                    if (!EDITOR_PREVIEW_IMMEDIATE_INIT_BURST &&
                        state.ManagedFromUnity &&
                        !state.DirectGamaMode &&
                        !state.MiddlewareCreatePlayerConfirmedUtc.HasValue &&
                        state.MiddlewareAuthenticatedSinceUtc.HasValue &&
                        (DateTime.UtcNow - state.MiddlewareAuthenticatedSinceUtc.Value).TotalSeconds < 4)
                    {
                        if (state.DebugPumpTicks % 25 == 0)
                        {
                            Dbg(append, state.DebugSessionId, "middleware",
                            "waiting for GAMA create_player before send_init_data (Unity-managed mode)...");
                        }

                        continue;
                    }

                    nextInitUtc = DateTime.UtcNow.AddSeconds(
                        state.SkipRemoteLoad ? 2.5 : (state.ManagedFromUnity ? 2.0 : 0.5));
                    try
                    {
                        Dbg(append, state.DebugSessionId, "middleware", "→ send_init_data id=" + connectionId);
                        await SendExecutableAskAsync(
                            ws, "send_init_data", connectionId, ct, state.DebugSessionId, append)
                            .ConfigureAwait(false);
                        if (state.SkipRemoteLoad && !state.DirectGamaMode && state.MiddlewareGeomReadyBurstUtc == DateTime.MinValue)
                        {
                            state.MiddlewareGeomReadyBurstUtc = DateTime.UtcNow.AddSeconds(2);
                        }
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] send_init_data failed: " + ex.Message);
                        Dbg(append, state.DebugSessionId, "middleware", "send_init_data exception: " + ex.Message);
                    }
                }

                if (state.SkipRemoteLoad &&
                    !state.MiddlewareGeomReadyBurstSent &&
                    state.MiddlewareGeomReadyBurstUtc > DateTime.MinValue &&
                    DateTime.UtcNow >= state.MiddlewareGeomReadyBurstUtc &&
                    ws.State == WebSocketState.Open)
                {
                    state.MiddlewareGeomReadyBurstSent = true;
                    try
                    {
                        await SendExecutableAskAsync(
                                ws, "player_ready_to_receive_geometries", connectionId, ct, state.DebugSessionId, append)
                            .ConfigureAwait(false);
                        append("[GAMA] player_ready_to_receive_geometries (experiment-already-open mode, as in Play Mode).");
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] player_ready_to_receive_geometries failed: " + ex.Message);
                    }
                }

                if (state.GotPrecision && state.GotProperties && !state.WorldHasAgents && DateTime.UtcNow >= nextGeomReadyUtc)
                {
                    nextGeomReadyUtc = DateTime.UtcNow.AddSeconds(2);
                    try
                    {
                        await SendExecutableAskAsync(
                                ws, "player_ready_to_receive_geometries", connectionId, ct, state.DebugSessionId, append)
                            .ConfigureAwait(false);
                        if (state.WorldFrameCount == 0)
                        {
                            append("[GAMA] Requesting player_ready_to_receive_geometries (as in Play Mode).");
                        }
                    }
                    catch (Exception ex)
                    {
                        append("[GAMA] player_ready_to_receive_geometries failed: " + ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
                // Normal end (timeout or cancellation).
        }
    }

    private static async Task HandleIncomingAsync(
        ClientWebSocket ws,
        string text,
        string outputDirectory,
        CaptureResult result,
        Action<string> append,
        CaptureState state,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        LogRawIncomingMessage("client", text);
        DetectGeometryExportError(text, result, append, state);

        JObject json;
        try
        {
            json = JObject.Parse(text);
        }
        catch
        {
            if (text.Contains("|||"))
            {
                string[] segments = text.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < segments.Length; i++)
                {
                    await HandleIncomingAsync(ws, segments[i], outputDirectory, result, append, state, ct);
                }
            }

            return;
        }

        string type = (string)json["type"];
        if (string.IsNullOrEmpty(type))
        {
            Dbg(append, state.DebugSessionId, "in", "message has no type field");
            return;
        }

        Dbg(append, state.DebugSessionId, "in", "type=" + type);

        if (type == "ConnectionSuccessful")
        {
            append("[GAMA] ConnectionSuccessful received.");
            if (state.DirectGamaMode && state.DirectLoadRequested && !state.DirectLoadSent)
            {
            if (state.SkipRemoteLoad)
            {
                state.DirectLoadCompleted = true;
                state.DirectPlayCompleted = true;
                if (state.DirectRunningSinceUtc == null)
                {
                    state.DirectRunningSinceUtc = DateTime.UtcNow;
                }

                if (string.IsNullOrWhiteSpace(state.LastSimulationStatus))
                {
                    state.LastSimulationStatus = "ASSUMED_OPEN";
                }

                append("[GAMA] No-load mode: experiment assumed to be open in GAMA; play is not sent, attempting create_player.");
                Dbg(append, state.DebugSessionId, "direct", "SkipRemoteLoad=true -> no load/play command");
            }
            else
            {
                    state.DirectLoadPending = true;
                    append("[GAMA] Load scheduled (sent from the pump, not while receiving). Click Yes if GAMA prompts you.");
                    Dbg(append, state.DebugSessionId, "direct", "DirectLoadPending=true");
                }
            }

            return;
        }

        if (type == "UnableToExecuteRequest" || type == "GamaServerError" || type == "RuntimeError")
        {
            string content = json["content"] != null ? json["content"].ToString(Formatting.None) : string.Empty;
            DetectGeometryExportError(content, result, append, state);
            JObject failedCommand = json["command"] as JObject;
            string failedExpr = failedCommand?["expr"]?.Value<string>() ?? string.Empty;
            string failedAction = failedCommand?["action"]?.Value<string>() ?? string.Empty;
            bool isCreatePlayer = failedExpr.IndexOf("create_player", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isRemovePlayer = failedExpr.IndexOf("remove_player", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isSendInitData = string.Equals(failedAction, "send_init_data", StringComparison.OrdinalIgnoreCase) ||
                                  content.IndexOf("send_init_data", StringComparison.OrdinalIgnoreCase) >= 0;
            string failedCommandType = failedCommand != null ? failedCommand.Value<string>("type") : string.Empty;
            bool controllerIsFullOnPlay =
                string.Equals(failedCommandType, "play", StringComparison.OrdinalIgnoreCase) &&
                content.IndexOf("Controller is full", StringComparison.OrdinalIgnoreCase) >= 0;
            bool simUnavailable = content.IndexOf("Unable to find the experiment", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  (content.IndexOf("simulation", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                   content.IndexOf("Unable to find", StringComparison.OrdinalIgnoreCase) >= 0);
            bool invalidMiddlewareOnGamaPort = content.IndexOf("Invalid command type: connection", StringComparison.OrdinalIgnoreCase) >= 0;

            if (invalidMiddlewareOnGamaPort)
            {
                append("[GAMA] Port " + (state.DirectGamaMode ? "?" : "1000") +
                       " is the GAMA server, not the Node middleware. Select 'Direct Capture' or start simple.webplatform on port 8080.");
                result.Error = "Incorrect protocol: target port is GAMA Server ('connection' command refused). Use port 8080 with simple.webplatform, or check 'Direct GAMA GUI Capture' for port 1000.";
                return;
            }

            if (state.DirectGamaMode && state.SkipRemoteLoad && isCreatePlayer && simUnavailable)
            {
                result.Error = "Cannot control an already opened GAMA experiment manually from this WebSocket: GAMA Server does not know its exp_id for Unity. Uncheck 'Experiment already open in GAMA' to let Unity load it, or use simple.webplatform on port 8080.";
                state.DirectCreatePlayerSent = true;
                state.CaptureDeadlineUtc = DateTime.UtcNow;
                append("[GAMA] " + result.Error);
                return;
            }

            if (isRemovePlayer || (isCreatePlayer && simUnavailable))
            {
                append("[GAMA] Command ignored (simulation missing): " + content);
                return;
            }

            if (controllerIsFullOnPlay && state.DirectGamaMode)
            {
                state.DirectPlayCompleted = true;
                if (state.DirectRunningSinceUtc == null)
                {
                    state.DirectRunningSinceUtc = DateTime.UtcNow;
                }

                state.LastSimulationStatus = "ASSUMED_OPEN";
                append("[GAMA] play ignored: GAMA reports 'Controller is full'. Continuing with the already-open experiment.");
                await TryDirectCreatePlayerAsync(ws, state, append, ct, force: true).ConfigureAwait(false);
                return;
            }

            if (simUnavailable && state.DirectGamaMode)
            {
                append("[GAMA] Simulation not found in GAMA. Click 'Yes' if GAMA asks to close the experiment, then start capture again.");
                state.ExtendCaptureDeadline(60f);
                return;
            }

            if (isSendInitData)
            {
                state.SendInitDataFailureCount++;
                bool unityLinkerMissing = content.IndexOf("unity_linker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          content.IndexOf("Agent does not exist", StringComparison.OrdinalIgnoreCase) >= 0;
                if (unityLinkerMissing)
                {
                    state.MiddlewareAuthenticated = false;
                    state.MiddlewareInitDataAllowedUtc = DateTime.UtcNow.AddSeconds(3);
                    if (!state.LoggedUnityLinkerMissingHint)
                    {
                        state.LoggedUnityLinkerMissingHint = true;
                        append("[GAMA] send_init_data refused: agent simulation[0].unity_linker[0] does not exist yet. " +
                               "Waiting for create_player / Unity experiment startup (type: unity). " +
                               "If the model is not *-VR.gaml, Unity capture is not supported.");
                    }

                    return;
                }

                if (state.SendInitDataFailureCount <= 2)
                {
                    append("[GAMA] send_init_data refused (" + content + "); waiting for RUNNING or SimulationOutput.");
                }
                else if (!state.LoggedDirectInitHint && state.DirectGamaMode)
                {
                    state.LoggedDirectInitHint = true;
                    append("[GAMA] send_init_data failed in direct mode: start simple.webplatform (8080), clear 'Direct Capture', or stop the simulation in GAMA and try again.");
                }

                return;
            }

            append("[GAMA] " + type + ": " + content + (isCreatePlayer ? " (another create_player attempt will follow)" : string.Empty));
            if (!isCreatePlayer && type != "UnableToExecuteRequest")
            {
                result.Error = type + ": " + content;
            }

            return;
        }

        if (type == "ping")
        {
            string pongId = state?.ConnectionId;
            if (string.IsNullOrWhiteSpace(pongId))
            {
                pongId = json["id_player"]?.ToString() ?? json["id"]?.ToString();
            }

            TryRespondToMiddlewarePing(ws, pongId);
            return;
        }

        if (type == "json_state")
        {
            bool connected = json["connected"]?.Value<bool>() ?? false;
            bool inGame = json["in_game"]?.Value<bool>() ?? false;
            string reportedId = json["id_player"]?.ToString() ?? json["id"]?.ToString() ?? string.Empty;
            append("[GAMA] json_state connected=" + connected + " in_game=" + inGame +
                   (string.IsNullOrEmpty(reportedId) ? string.Empty : " id_player=" + reportedId));
            if (state.ManagedFromUnity && !state.DirectGamaMode)
            {
                CapLog8080(append, "IN",
                    "json_state connected=" + connected + " in_game=" + inGame +
                    (string.IsNullOrEmpty(reportedId) ? string.Empty : " id_player=" + reportedId));
            }
            if (connected)
            {
                state.MiddlewareConnected = true;
            }

            if (connected && inGame && !string.IsNullOrWhiteSpace(reportedId) &&
                !string.Equals(reportedId, state.ConnectionId, StringComparison.OrdinalIgnoreCase))
            {
                state.MiddlewareOccupiedPlayerId = reportedId;
                state.NeedsOccupiedPlayerPurge = true;
                if (!state.LoggedPlayerSlotConflict)
                {
                    state.LoggedPlayerSlotConflict = true;
                    CapLog(append, state.DebugSessionId, "slot",
                    "Conflict: in_game=\"" + reportedId + "\" != capture=\"" + state.ConnectionId +
                    "\" (max_num_players=1). GAMA remove_player in progress...");
                append("[GAMA] GAMA player slot occupied by '" + reportedId + "'; releasing it through remove_player. " +
                       "Tip: leave the connection ID empty to use " + StaticInformation.getId() + " (as in Play Mode).");
                }
            }

            if (connected && inGame &&
                (string.IsNullOrWhiteSpace(reportedId) ||
                 string.Equals(reportedId, state.ConnectionId, StringComparison.OrdinalIgnoreCase)))
            {
                state.MiddlewareAuthenticated = true;
                state.MiddlewareOccupiedPlayerId = null;
                state.NeedsOccupiedPlayerPurge = false;
                if (!state.MiddlewareAuthenticatedSinceUtc.HasValue)
                {
                    state.MiddlewareAuthenticatedSinceUtc = DateTime.UtcNow;
                }

                if (state.ManagedFromUnity && EDITOR_PREVIEW_IMMEDIATE_INIT_BURST)
                {
                    state.MiddlewareCreatePlayerConfirmedUtc = DateTime.UtcNow;
                    state.MiddlewareInitDataAllowedUtc = DateTime.UtcNow;
                    if (!state.ImmediateInitBurstStarted)
                    {
                        CapLog8080(append, "INFO", "in_game=true → immediate init burst");
                    }
                }
                else
                {
                    double initDelaySeconds = state.ManagedFromUnity ? 3.0 : (state.SkipRemoteLoad ? 0.5 : 1.5);
                    DateTime allowedUtc = DateTime.UtcNow.AddSeconds(initDelaySeconds);
                    if (state.MiddlewareInitDataAllowedUtc == DateTime.MinValue ||
                        allowedUtc > state.MiddlewareInitDataAllowedUtc)
                    {
                        state.MiddlewareInitDataAllowedUtc = allowedUtc;
                    }
                }

                append("[GAMA] Client authenticated on the middleware (in_game=true, id=" + state.ConnectionId + ")." +
                    (state.ManagedFromUnity && EDITOR_PREVIEW_IMMEDIATE_INIT_BURST
                        ? " Immediate send_init_data burst on 8080 (does not wait for create_player)."
                        : state.ManagedFromUnity
                            ? " Waiting for GAMA create_player before send_init_data."
                            : state.SkipRemoteLoad
                                ? " Middleware pump: send_init_data + player_ready (as in Play Mode, 8080 only)."
                                : " Sending send_init_data."));
                if (state.SkipRemoteLoad && state.MiddlewarePlayPhaseStartedUtc == DateTime.MinValue)
                {
                    state.MiddlewarePlayPhaseStartedUtc = DateTime.UtcNow;
                }
            }
            else if (connected && !inGame && !state.LoggedMiddlewareInGameHint)
            {
                state.LoggedMiddlewareInGameHint = true;
                if (state.ManagedFromUnity && !state.LaunchExperimentViaMonitor)
                {
                    append("[GAMA] Middleware connected but in_game=false; keeping the Play-like sequence and initializing on 8080 if the session does not enter in_game quickly.");
                }
                else
                {
                    append(state.SkipRemoteLoad
                        ? "[GAMA] Middleware connected (in_game=false); waiting for create_player / send_init_data."
                        : "[GAMA] Middleware connected but in_game=false; automatically starting the GAMA simulation (port 1000) if the experiment is imported.");
                }
            }

            return;
        }

        if (type == "CommandExecutedSuccessfully")
        {
            JObject command = json["command"] as JObject;
            string commandType = command != null ? command.Value<string>("type") : string.Empty;
            string action = command != null ? command.Value<string>("action") : string.Empty;
            if (commandType == "load")
            {
                state.DirectLoadCompleted = true;
                state.ExtendCaptureDeadline(120f);
                append("[GAMA] Load confirmed by GAMA.");
            }
            else if (commandType == "play")
            {
                state.DirectPlayCompleted = true;
                append("[GAMA] Play confirmed by GAMA.");
                await TryDirectCreatePlayerAsync(ws, state, append, ct, force: true).ConfigureAwait(false);
            }
            else if (commandType == "expression")
            {
                string expr = command.Value<string>("expr") ?? string.Empty;
                if (expr.IndexOf("create_player", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    state.DirectCreatePlayerSent = true;
                    state.DirectCreatePlayerConfirmedUtc = DateTime.UtcNow;
                    state.MiddlewareCreatePlayerConfirmedUtc = DateTime.UtcNow;
                    state.NextDirectInitAskUtc = DateTime.UtcNow.AddMilliseconds(400);
                    DateTime initAllowed = DateTime.UtcNow.AddMilliseconds(state.ManagedFromUnity ? 1200 : 800);
                    if (state.MiddlewareInitDataAllowedUtc == DateTime.MinValue || initAllowed > state.MiddlewareInitDataAllowedUtc)
                    {
                        state.MiddlewareInitDataAllowedUtc = initAllowed;
                    }

                    state.ExtendCaptureDeadline(Math.Max(90f, state.WorldPhaseExtraSeconds + 45f));
                    append("[GAMA] create_player confirmed by GAMA. Capture window extended to receive JSON.");
                }
            }
            else if (commandType == "ask" &&
                     string.Equals(action, "send_init_data", StringComparison.OrdinalIgnoreCase))
            {
                state.SendInitDataSuccessCount++;
                if (state.SendInitDataSuccessCount == 1)
                {
                    append("[GAMA] send_init_data confirmed by GAMA, but no JSON data has arrived yet.");
                }
                else if (state.SendInitDataSuccessCount == 3 && state.DirectGamaMode && state.OtherOutputCount == 0)
                {
                    append("[GAMA] send_init_data confirmed multiple times without SimulationOutput/json_output. The GAMA server executes the action but does not publish Unity data on this WebSocket.");
                }
            }

            TryProcessGamaOutputPayload(json["content"], outputDirectory, result, append, state);
            TryProcessGamaOutputPayload(json, outputDirectory, result, append, state);

            return;
        }

        if (type == "SimulationStatus")
        {
            string status = json["content"] != null ? json["content"].ToString() : string.Empty;
            state.LastSimulationStatus = status;
            string expId = json["exp_id"]?.ToString();
            if (!string.IsNullOrWhiteSpace(expId))
            {
                state.DirectExperimentId = expId;
            }

            if (string.Equals(status, "RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                state.DirectPlayCompleted = true;
                if (state.DirectRunningSinceUtc == null)
                {
                    state.DirectRunningSinceUtc = DateTime.UtcNow;
                    append("[GAMA] Simulation RUNNING (exp_id=" + state.DirectExperimentId + ").");
                }

                await TryDirectCreatePlayerAsync(ws, state, append, ct).ConfigureAwait(false);
            }

            return;
        }

        if (type == "SimulationOutput")
        {
            JToken contentToken = json["content"];
            string content = contentToken?.Type == JTokenType.String
                ? contentToken.Value<string>()
                : contentToken?.ToString(Formatting.None);
            if (!string.IsNullOrWhiteSpace(content))
            {
                await HandleIncomingAsync(ws, content, outputDirectory, result, append, state, ct);
            }

            return;
        }

        if (type == "output")
        {
            ProcessOutputEnvelope(json["contents"], outputDirectory, result, append, state);
            return;
        }

        if (type != "json_output")
        {
            Dbg(append, state.DebugSessionId, "in", "handler complete for type=" + type + " (not json_output)");
            return;
        }

        if (!state.ReceivedJsonOutput)
        {
            state.ReceivedJsonOutput = true;
            if (state.ManagedFromUnity && EDITOR_PREVIEW_IMMEDIATE_INIT_BURST)
            {
                CapLog8080(append, "IN", "json_output");
            }
        }

        Dbg(append, state.DebugSessionId, "json", "json_output received");
        ProcessJsonOutputContents(json["contents"], outputDirectory, result, append, state);
    }

    private static void LogRawIncomingMessage(string source, string text)
    {
        const int chunkSize = 12000;
        if (string.IsNullOrEmpty(text))
        {
            GamaLog.Dev("[GAMA][RAW][" + source + "] <empty>");
            return;
        }

        if (text.Length <= chunkSize)
        {
            GamaLog.Dev("[GAMA][RAW][" + source + "] " + text);
            return;
        }

        int total = Mathf.CeilToInt(text.Length / (float)chunkSize);
        for (int i = 0; i < total; i++)
        {
            int start = i * chunkSize;
            int length = Math.Min(chunkSize, text.Length - start);
            GamaLog.Dev("[GAMA][RAW][" + source + "] chunk " + (i + 1) + "/" + total + ": " + text.Substring(start, length));
        }
    }

    private static void TryProcessGamaOutputPayload(
        JToken payload,
        string outputDirectory,
        CaptureResult result,
        Action<string> append,
        CaptureState state)
    {
        if (payload == null)
        {
            return;
        }

        if (payload.Type == JTokenType.String)
        {
            string nested = payload.Value<string>();
            if (string.IsNullOrWhiteSpace(nested))
            {
                return;
            }

            try
            {
                TryProcessGamaOutputPayload(JToken.Parse(nested), outputDirectory, result, append, state);
            }
            catch
            {
                // ignore
            }

            return;
        }

        JObject obj = payload as JObject;
        if (obj == null)
        {
            return;
        }

        string nestedType = obj.Value<string>("type");
        if (string.Equals(nestedType, "json_output", StringComparison.OrdinalIgnoreCase))
        {
            ProcessJsonOutputContents(obj["contents"], outputDirectory, result, append, state);
            return;
        }

        if (obj["contents"] != null)
        {
            ProcessOutputEnvelope(obj["contents"], outputDirectory, result, append, state);
        }

        if (obj["precision"] != null || obj["properties"] != null || obj["pointsLoc"] != null)
        {
            ProcessJsonOutputContents(obj, outputDirectory, result, append, state);
        }
    }

    private static void ProcessOutputEnvelope(
        JToken contentsToken,
        string outputDirectory,
        CaptureResult result,
        Action<string> append,
        CaptureState state)
    {
        if (contentsToken == null)
        {
            return;
        }

        JArray array = contentsToken as JArray;
        if (array != null)
        {
            foreach (JToken item in array)
            {
                JObject itemObject = item as JObject;
                if (itemObject != null)
                {
                    ProcessJsonOutputContents(itemObject["contents"], outputDirectory, result, append, state);
                }
            }

            return;
        }

        ProcessJsonOutputContents(contentsToken, outputDirectory, result, append, state);
    }

    private static void ProcessJsonOutputContents(
        JToken contentsToken,
        string outputDirectory,
        CaptureResult result,
        Action<string> append,
        CaptureState state)
    {
        if (contentsToken == null)
        {
            return;
        }

        if (!state.ReceivedJsonOutput)
        {
            state.ReceivedJsonOutput = true;
            if (state.ManagedFromUnity && EDITOR_PREVIEW_IMMEDIATE_INIT_BURST)
            {
                CapLog8080(append, "IN", "json_output (nested)");
            }
        }

        if (contentsToken.Type == JTokenType.String)
        {
            string nested = contentsToken.Value<string>();
            if (!string.IsNullOrWhiteSpace(nested))
            {
                try
                {
                    ProcessJsonOutputContents(JToken.Parse(nested), outputDirectory, result, append, state);
                }
                catch
                {
                    // ignore malformed nested strings
                }
            }

            return;
        }

        JObject contents = contentsToken as JObject;
        if (contents == null)
        {
            return;
        }

        string contentSerialized = contents.ToString(Formatting.Indented);
        string firstKey = null;
        foreach (JProperty prop in contents.Properties())
        {
            firstKey = prop.Name;
            break;
        }

        if (string.IsNullOrEmpty(firstKey))
        {
            firstKey = GuessFirstKey(contentSerialized);
        }

        string written = null;

        switch (firstKey)
        {
            case "precision":
                if (!state.GotPrecision)
                {
                    written = WriteFile(outputDirectory, "precision.json", contentSerialized);
                    if (written != null)
                    {
                        result.PrecisionJsonPath = written;
                        state.GotPrecision = true;
                        append("[GAMA] precision.json captured.");
                        Dbg(append, state.DebugSessionId, "json", "precision OK → " + written);
                    }
                }
                break;

            case "properties":
                if (!state.GotProperties)
                {
                    written = WriteFile(outputDirectory, "properties.json", contentSerialized);
                    if (written != null)
                    {
                        result.PropertiesJsonPath = written;
                        state.GotProperties = true;
                        try
                        {
                            AllProperties allProps = AllProperties.CreateFromJSON(contentSerialized);
                            state.PropertyMapById = GamaEditorPreviewCapture.BuildPropertyMap(allProps);
                        }
                        catch
                        {
                            state.PropertyMapById = new Dictionary<string, PropertiesGAMA>(StringComparer.OrdinalIgnoreCase);
                        }

                        append("[GAMA] properties.json captured.");
                        Dbg(append, state.DebugSessionId, "json", "properties OK → " + written);
                    }
                }
                break;

            case "pointsLoc":
            case "pointsGeom":
            case "names":
            case "world":
                TryCaptureWorldFrame(contents, contentSerialized, state, result, outputDirectory, append);
                break;

            default:
                state.OtherOutputCount++;
                if (contents["pointsLoc"] != null || contents["pointsGeom"] != null || contents["names"] != null)
                {
                    TryCaptureWorldFrame(contents, contentSerialized, state, result, outputDirectory, append);
                }
                break;
        }
    }

    private static void TryCaptureWorldFrame(
        JObject contents,
        string contentSerialized,
        CaptureState state,
        CaptureResult result,
        string outputDirectory,
        Action<string> append)
    {
        if (contents?["world"] is JObject nestedWorld)
        {
            contents = nestedWorld;
            contentSerialized = contents.ToString(Formatting.Indented);
        }

        int frameIndex = state.WorldFrameCount;
        string chunkFileName = "world_chunk_" + frameIndex + ".json";
        string chunkPath = WriteFile(outputDirectory, chunkFileName, contentSerialized);

        state.WorldFrameCount++;
        state.GotWorld = true;
        state.LastWorldFrameIndex = frameIndex;
        state.LastWorldTickPath = chunkPath;
        if (!state.WorldPhaseStartedUtc.HasValue)
        {
            state.WorldPhaseStartedUtc = DateTime.UtcNow;
            append("[GAMA][PREVIEW] Cumulative preview window: warmup " +
                   state.WorldPhaseExtraSeconds.ToString("0") + " s, stability " +
                   state.PreviewStableSeconds.ToString("0") + " s, max " + state.MaxWorldFrames + " ticks.");
        }

        Dictionary<string, PropertiesGAMA> propertyMap = state.PropertyMapById ??
                                                          new Dictionary<string, PropertiesGAMA>(StringComparer.OrdinalIgnoreCase);
        if (state.PreviewAccumulator == null)
        {
            state.PreviewAccumulator = new GamaEditorPreviewWorldAccumulator();
        }

        GamaEditorPreviewWorldAccumulator.MergeResult merge = state.PreviewAccumulator.Merge(
            contents,
            frameIndex,
            propertyMap,
            state.DynamicSpeciesRegexCompiled);

        if (merge.ExplicitReset)
        {
            append("[GAMA][PREVIEW] Explicit reset received: preview cache cleared before merging.");
        }

        append(GamaEditorPreviewCapture.FormatChunkSpeciesCountsLine(frameIndex, merge.ChunkSpeciesCounts));
        append(GamaEditorPreviewCapture.FormatCacheSpeciesCountsLine(merge.CacheSpeciesCounts));

        if (merge.DynamicCacheAgentCount > 0)
        {
            state.PreviewDynamicAgentsFound = true;
            append("[GAMA][PREVIEW] Dynamic-agent cache (regex): " + merge.DynamicCacheAgentCount);
        }
        if (merge.ReplacedDynamicAgentCount > 0)
        {
            append("[GAMA][PREVIEW] Dynamic positions replaced: " + merge.ReplacedDynamicAgentCount);
        }

        if (merge.CacheGrew || !state.LastPreviewCacheGrowthUtc.HasValue)
        {
            state.LastPreviewCacheGrowthUtc = DateTime.UtcNow;
        }

        string cumulativeJson = state.PreviewAccumulator.ToWorldJson();
        string tickFileName = "world_tick_" + frameIndex + ".json";
        string tickPath = WriteFile(outputDirectory, tickFileName, cumulativeJson);
        string worldPath = WriteFile(outputDirectory, "world.json", cumulativeJson);
        string bestPath = WriteFile(outputDirectory, "world_best.json", cumulativeJson);
        if (tickPath != null)
        {
            state.LastWorldTickPath = tickPath;
        }

        if (bestPath != null)
        {
            state.WorldBestJsonPath = bestPath;
            result.WorldJsonPath = bestPath;
        }
        else if (worldPath != null)
        {
            result.WorldJsonPath = worldPath;
        }

        state.WorldBestFrameIndex = frameIndex;
        state.WorldBestAgentCount = merge.CacheAgentCount;

        if (merge.CacheAgentCount > 0)
        {
            state.WorldHasAgents = true;
        }

        append("[GAMA] " + tickFileName + " cumulative (chunk " + merge.ChunkAgentCount +
               " agent(s), " + merge.ChunkGeometryCount + " geometry item(s); cache " +
               merge.CacheAgentCount + " agent(s), +" + merge.NewAgentCount +
               " new, " + merge.UpdatedAgentCount + " updated). Raw chunk: " + chunkFileName + ".");
    }

    private static void FinalizePreviewBestFrame(
        CaptureState state,
        string outputDirectory,
        CaptureResult result,
        Action<string> append)
    {
        if (state == null || state.WorldFrameCount <= 0)
        {
            return;
        }

        if (state.PreviewDynamicAgentsFound || state.WorldBestAgentCount > 0)
        {
            return;
        }

        if (state.LastWorldFrameIndex < 0 || string.IsNullOrEmpty(state.LastWorldTickPath) || !File.Exists(state.LastWorldTickPath))
        {
            return;
        }

        try
        {
            string content = File.ReadAllText(state.LastWorldTickPath);
            state.WorldBestFrameIndex = state.LastWorldFrameIndex;
            string bestPath = WriteFile(outputDirectory, "world_best.json", content);
            if (bestPath != null)
            {
                state.WorldBestJsonPath = bestPath;
                result.WorldJsonPath = bestPath;
                append("[GAMA][PREVIEW] No agents: cumulative preview uses the last received tick (tick " + state.LastWorldFrameIndex + ").");
            }
        }
        catch (Exception ex)
        {
            append("[GAMA][PREVIEW] Could not finalize the last tick: " + ex.Message);
        }
    }

    private static string GuessFirstKey(string content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        if (content.Contains("pointsLoc")) return "pointsLoc";
        if (content.Contains("precision")) return "precision";
        if (content.Contains("properties")) return "properties";
        return null;
    }

    private static string WriteFile(string outputDirectory, string fileName, string contents)
    {
        try
        {
            string fullPath = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
            return fullPath;
        }
        catch (Exception ex)
        {
            GamaLog.Warning("[GAMA] Could not write " + fileName + ": " + ex.Message);
            return null;
        }
    }

    private static void ClearPreviousCaptureFiles(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
        {
            return;
        }

        string[] patterns =
        {
            "precision.json",
            "properties.json",
            "world.json",
            "world_best.json",
            "world_tick_*.json",
            "world_chunk_*.json"
        };

        for (int i = 0; i < patterns.Length; i++)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(outputDirectory, patterns[i]);
            }
            catch
            {
                continue;
            }

            for (int f = 0; f < files.Length; f++)
            {
                try
                {
                    File.Delete(files[f]);
                }
                catch
                {
                    // Best effort cleanup: stale files should not block capture.
                }
            }
        }
    }

    private static void DetectGeometryExportError(
        string text,
        CaptureResult result,
        Action<string> append,
        CaptureState state)
    {
        if (string.IsNullOrWhiteSpace(text) || state == null || state.GeometryExportErrorDetected)
        {
            return;
        }

        bool looksLikeGeometryExportError =
            text.IndexOf("send_geometries", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("add_geometries_to_send", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("nil value detected", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("NullPointerException", StringComparison.OrdinalIgnoreCase) >= 0 ||
            text.IndexOf("getAttribute", StringComparison.OrdinalIgnoreCase) >= 0;

        if (!looksLikeGeometryExportError)
        {
            return;
        }

        state.GeometryExportErrorDetected = true;
        state.GeometryExportErrorMessage =
            "GAMA raised an error while exporting geometries. The preview may be incomplete.";
        if (result != null && string.IsNullOrWhiteSpace(result.PreviewWarning))
        {
            result.PreviewWarning = state.GeometryExportErrorMessage;
        }

        append("[GAMA][PREVIEW] WARNING: " + state.GeometryExportErrorMessage);
    }

    private static string BuildMissingError(CaptureState state)
    {
        if (!state.DirectGamaMode && !string.IsNullOrEmpty(state.MiddlewareOccupiedPlayerId))
        {
            return "Player slot occupied by '" + state.MiddlewareOccupiedPlayerId +
                   "' (max_num_players=1). Stop Unity Play, purge this player in the panel, then capture with id '" +
                   state.ConnectionId + "'.";
        }

        if (state.HybridGamaCommandChannel && state.HybridGamaCommandsDone && !state.GotPrecision)
        {
            if (state.HybridGamaInitSentCount == 0)
            {
                return "Capture (experiment already open): no send_init_data could be sent (neither middleware nor GAMA:1000). " +
                       "Check simple.webplatform (8080), GAMA (Yes), empty ID -> " + StaticInformation.getId() +
                       ", stop Unity Play.";
            }

            return "Capture (as in Play): send_init_data on GAMA:1000 OK, but no json_output on middleware. " +
                   "8080 commands are ignored if the experiment was only launched from GAMA (Yes): launch it once from the middleware web UI, " +
                   "check simple.webplatform is connected to GAMA, empty ID -> " + StaticInformation.getId() +
                   ", stop Unity Play before capture.";
        }

        if (!state.DirectGamaMode && state.ManagedFromUnity && !state.GotPrecision)
        {
            if (!state.LaunchExperimentViaMonitor)
            {
                return "Could not generate the preview: no experiment is selected or ready in GAMA. " +
                       "Select the experiment in GAMA, then try again.";
            }

            return "Unity-managed capture: experiment launched via monitor but no json_output on 8080. " +
                   "Check GAMA (Yes) is connected to middleware, VU catalog (LEARNING_PACKAGE_PATH), empty ID -> " +
                   StaticInformation.getId() + ".";
        }

        if (!state.DirectGamaMode && state.SkipRemoteLoad && !state.HybridGamaCommandChannel &&
            state.SendInitDataSuccessCount == 0 && !state.GotPrecision)
        {
            return "Pure 8080 middleware capture (experiment already open): send_init_data sent but no json_output. " +
                   "Try the 'Managed from Unity' mode to launch the experiment via monitor without web UI.";
        }

        if (!state.DirectGamaMode && state.GamaBootstrapPhase == 3 && state.SendInitDataSuccessCount == 0)
        {
            return "GAMA bootstrap (create_player/load) did not succeed before middleware capture. " +
                   "If you launched the experiment in GAMA (Yes), check 'Experiment already open'. " +
                   "Otherwise close the simulation in GAMA and try again.";
        }

        if (state.DirectGamaMode &&
            state.DirectCreatePlayerConfirmedUtc.HasValue &&
            state.SendInitDataSuccessCount > 0 &&
            state.OtherOutputCount == 0 &&
            !state.GotPrecision &&
            !state.GotProperties &&
            state.WorldFrameCount <= 0)
        {
            return "Incomplete direct capture: create_player and send_init_data were confirmed by GAMA (" +
                   state.SendInitDataSuccessCount +
                   " times), but no SimulationOutput/json_output containing precision, properties or world was emitted on the GAMA WebSocket.";
        }

        StringBuilder sb = new StringBuilder();
        sb.Append("Cumulative preview not captured (timeout). Missing: ");
        bool first = true;
        if (!state.GotPrecision) { sb.Append("precision"); first = false; }
        if (!state.GotProperties) { if (!first) sb.Append(", "); sb.Append("properties"); first = false; }
        if (state.WorldFrameCount <= 0) { if (!first) sb.Append(", "); sb.Append("world (pointsLoc)"); first = false; }
        else if (!state.WorldHasAgents) { if (!first) sb.Append(", "); sb.Append("agents in world"); first = false; }
        sb.Append(". Other json_output messages received: ").Append(state.OtherOutputCount).Append('.');
        return state.GeometryExportErrorDetected
            ? state.GeometryExportErrorMessage + " " + sb
            : sb.ToString();
    }
}


