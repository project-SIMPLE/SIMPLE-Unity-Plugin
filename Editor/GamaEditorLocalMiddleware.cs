using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Starts a mini WebSocket server in Unity to act as the middleware (simple.webplatform).
/// This allows GAMA to connect directly to the Unity editor without Node.js.
/// </summary>
internal static class GamaEditorLocalMiddleware
{
    private const string UnityLinkerAgent = "simulation[0].unity_linker[0]";

    public sealed class CaptureState
    {
        public bool GotPrecision;
        public bool GotProperties;
        public bool GotWorld;
        public int OtherOutputCount;
        public bool HasAllThree => GotPrecision && GotProperties && GotWorld;
    }

    public static async Task<GamaEditorFirstTickCapture.CaptureResult> CaptureAsServerAsync(
        int port,
        string outputDirectory,
        int timeoutMs,
        Action<string> log,
        CancellationToken externalToken)
    {
        var result = new GamaEditorFirstTickCapture.CaptureResult();
        var logBuilder = new StringBuilder();
        Action<string> append = s =>
        {
            logBuilder.AppendLine(s);
            try { log?.Invoke(s); } catch { /* ignore */ }
        };

        try { Directory.CreateDirectory(outputDirectory); }
        catch (Exception ex)
        {
            result.Error = "Cannot create output folder: " + ex.Message;
            result.LogTrail = logBuilder.ToString();
            return result;
        }

        HttpListener listener = new HttpListener();
        string prefix = $"http://localhost:{port}/";
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
            append($"[GAMA] Local server started on {prefix}. Waiting for GAMA connection...");
        }
        catch (Exception ex)
        {
            result.Error = "Cannot start local server: " + ex.Message + "\nTry another port or launch Unity as administrator.";
            result.LogTrail = logBuilder.ToString();
            return result;
        }

        using (CancellationTokenSource captureCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken))
        {
            captureCts.CancelAfter(Math.Max(timeoutMs, 5000));
            try
            {
                HttpListenerContext context = null;
                using (captureCts.Token.Register(() => { try { listener.Stop(); } catch { } }))
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    result.Error = "The received connection is not a WebSocket request.";
                    return result;
                }

                HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                WebSocket ws = wsContext.WebSocket;
                append("[GAMA] WebSocket client connected!");

                var state = new CaptureState();
                Task pumpTask = RunServerPumpAsync(ws, state, captureCts.Token, append);

                byte[] buffer = new byte[64 * 1024];
                StringBuilder pending = new StringBuilder();

                while (!captureCts.IsCancellationRequested && !state.HasAllThree && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult rr;
                    try
                    {
                        rr = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), captureCts.Token);
                    }
                    catch (OperationCanceledException) { break; }

                    if (rr.MessageType == WebSocketMessageType.Close)
                    {
                        append("[GAMA] GAMA closed the WebSocket connection.");
                        break;
                    }

                    pending.Append(Encoding.UTF8.GetString(buffer, 0, rr.Count));
                    if (!rr.EndOfMessage) continue;

                    string text = pending.ToString();
                    pending.Length = 0;

                    await HandleIncomingAsync(ws, text, outputDirectory, result, append, state, captureCts.Token);
                }

                try
                {
                    await pumpTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { append("[GAMA] Server pump: " + ex.Message); }

                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "capture done", CancellationToken.None);
                    }
                }
                catch { }

                if (!state.HasAllThree)
                {
                    result.Error = "Capture completed without receiving all expected files (timeout or error).";
                }
            }
            catch (OperationCanceledException)
            {
                result.Error = "Capture timed out or was cancelled.";
            }
            catch (Exception ex)
            {
                if (ex.Message != "The listener is closed" && !(ex is HttpListenerException hl && hl.ErrorCode == 995))
                    result.Error = "Local server error: " + ex.Message;
            }
            finally
            {
                try { listener.Stop(); listener.Close(); } catch { }
            }
        }

        result.Success = string.IsNullOrEmpty(result.Error);
        result.LogTrail = logBuilder.ToString();
        return result;
    }

    private static async Task SendExecutableAskAsync(WebSocket ws, string action, CancellationToken ct)
    {
        if (ws == null || ws.State != WebSocketState.Open) return;

        string payload = JsonConvert.SerializeObject(new Dictionary<string, string>
        {
            { "type", "ask" },
            { "action", action },
            { "args", "{}" }, // empty
            { "agent", UnityLinkerAgent }
        });
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
    }

    private static async Task RunServerPumpAsync(
        WebSocket ws,
        CaptureState state,
        CancellationToken ct,
        Action<string> append)
    {
        bool sentInitData = false;
        bool sentGeomReady = false;

        try
        {
            while (!ct.IsCancellationRequested && !state.HasAllThree)
            {
                await Task.Delay(200, ct).ConfigureAwait(false);
                if (ws.State != WebSocketState.Open) break;

                // Once GAMA is connected, we send json_state to simulate that the Unity UI is connected
                if (!sentInitData)
                {
                    string jsonState = JsonConvert.SerializeObject(new
                    {
                        type = "json_state",
                        connected = true,
                        in_game = true
                    });
                    await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(jsonState)), WebSocketMessageType.Text, true, ct);
                    
                    await SendExecutableAskAsync(ws, "send_init_data", ct).ConfigureAwait(false);
                    append("[GAMA] send_init_data request sent.");
                    sentInitData = true;
                }

                if (state.GotPrecision && state.GotProperties && !state.GotWorld && !sentGeomReady)
                {
                    sentGeomReady = true;
                    await SendExecutableAskAsync(ws, "player_ready_to_receive_geometries", ct).ConfigureAwait(false);
                    append("[GAMA] player_ready_to_receive_geometries request sent.");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task HandleIncomingAsync(
        WebSocket ws,
        string text,
        string outputDirectory,
        GamaEditorFirstTickCapture.CaptureResult result,
        Action<string> append,
        CaptureState state,
        CancellationToken ct)
    {
        LogRawIncomingMessage("server", text);

        JObject json;
        try { json = JObject.Parse(text); } catch { return; }

        string type = (string)json["type"];
        if (string.IsNullOrEmpty(type)) return;

        if (type == "ping")
        {
            string pong = JsonConvert.SerializeObject(new { type = "pong" });
            try { await ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(pong)), WebSocketMessageType.Text, true, ct); } catch { }
            return;
        }

        if (type == "connection")
        {
            append("[GAMA] 'connection' handshake received from GAMA.");
            return;
        }

        if (type != "json_output") return;

        JObject contents = json["contents"] as JObject;
        if (contents == null) return;

        string contentSerialized = contents.ToString(Formatting.Indented);
        string firstKey = null;
        foreach (JProperty prop in contents.Properties()) { firstKey = prop.Name; break; }

        if (string.IsNullOrEmpty(firstKey))
        {
            if (contentSerialized.Contains("pointsLoc")) firstKey = "pointsLoc";
            else if (contentSerialized.Contains("precision")) firstKey = "precision";
            else if (contentSerialized.Contains("properties")) firstKey = "properties";
        }

        string written = null;
        await Task.Yield();

        switch (firstKey)
        {
            case "precision":
                if (!state.GotPrecision)
                {
                    written = WriteFile(outputDirectory, "precision.json", contentSerialized, append);
                    if (written != null) { result.PrecisionJsonPath = written; state.GotPrecision = true; append("[GAMA] precision.json received."); }
                }
                break;
            case "properties":
                if (!state.GotProperties)
                {
                    written = WriteFile(outputDirectory, "properties.json", contentSerialized, append);
                    if (written != null) { result.PropertiesJsonPath = written; state.GotProperties = true; append("[GAMA] properties.json received."); }
                }
                break;
            case "pointsLoc":
            case "names":
            case "world":
                if (!state.GotWorld)
                {
                    written = WriteFile(outputDirectory, "world.json", contentSerialized, append);
                    if (written != null) { result.WorldJsonPath = written; state.GotWorld = true; append("[GAMA] world.json received."); }
                }
                break;
            default:
                state.OtherOutputCount++;
                if (!state.GotWorld && contents["pointsLoc"] != null && contents["names"] != null)
                {
                    written = WriteFile(outputDirectory, "world.json", contentSerialized, append);
                    if (written != null) { result.WorldJsonPath = written; state.GotWorld = true; append("[GAMA] world.json deduced."); }
                }
                break;
        }
    }

    private static void LogRawIncomingMessage(string source, string text)
    {
        const int chunkSize = 12000;
        if (string.IsNullOrEmpty(text))
        {
            Debug.Log("[GAMA][RAW][" + source + "] <empty>");
            return;
        }

        if (text.Length <= chunkSize)
        {
            Debug.Log("[GAMA][RAW][" + source + "] " + text);
            return;
        }

        int total = Mathf.CeilToInt(text.Length / (float)chunkSize);
        for (int i = 0; i < total; i++)
        {
            int start = i * chunkSize;
            int length = Math.Min(chunkSize, text.Length - start);
            Debug.Log("[GAMA][RAW][" + source + "] chunk " + (i + 1) + "/" + total + ": " + text.Substring(start, length));
        }
    }

    private static string WriteFile(string outputDirectory, string fileName, string contents, Action<string> append)
    {
        try
        {
            string fullPath = Path.Combine(outputDirectory, fileName);
            File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
            return fullPath;
        }
        catch (Exception ex)
        {
            append("[GAMA] Cannot write " + fileName + ": " + ex.Message);
            return null;
        }
    }
}
