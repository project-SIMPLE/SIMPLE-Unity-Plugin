using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Replicates the simple.webplatform web UI workflow (WebSocket monitor, default port 8001):
/// VU selection, <c>launch_experiment</c>, and <c>resume_experiment</c> when needed.
/// Does not modify the middleware repository.
/// </summary>
internal static class GamaEditorMiddlewareOrchestrator
{
    public const int DefaultMonitorPort = 8001;

    public sealed class ManagedExperimentResult
    {
        public bool Success;
        public string Error;
        public string FinalExperimentState = string.Empty;
        public string ExperimentId = string.Empty;
        public int? SelectedModelIndex;
        public string LogTrail = string.Empty;
    }

    public enum CatalogMatchStatus
    {
        MatchOk,
        MiddlewareNotReachable,
        CatalogEmpty,
        ModelNotFound,
        ExperimentNotFound,
        ModelAndExperimentNotFound,
        Ambiguous
    }

    public sealed class CatalogDiagnosisResult
    {
        public bool Success;
        public CatalogMatchStatus Status;
        public string Error = string.Empty;
        public string RequestedModelPath = string.Empty;
        public string RequestedExperimentName = string.Empty;
        public readonly List<string> AvailableModels = new List<string>();
        public readonly List<string> AvailableExperiments = new List<string>();
        public string LogTrail = string.Empty;
    }

    private sealed class CatalogEntryDiagnostics
    {
        public int CatalogIndex;
        public string Name = string.Empty;
        public string ModelPath = string.Empty;
        public string GamlFile = string.Empty;
        public readonly List<string> Experiments = new List<string>();
        public JObject RawSettings;
    }

    public static async Task<bool> IsTcpPortOpenAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        if (port <= 0 || port > 65535)
        {
            return false;
        }

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        using (TcpClient client = new TcpClient())
        {
            using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(Math.Max(500, timeoutMs));
                try
                {
                    await client.ConnectAsync(hostNorm, port).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    public static List<int> GetListeningPidsOnTcpPort(int port, Action<string> log = null)
    {
        List<int> pids = new List<int>();
        if (port <= 0 || port > 65535)
        {
            return pids;
        }

        try
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano -p tcp",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
            {
                if (process == null)
                {
                    return pids;
                }

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);
                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                string suffix = ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5)
                    {
                        continue;
                    }

                    string localAddress = parts[1];
                    if (!localAddress.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string pidText = parts[parts.Length - 1];
                    if (int.TryParse(pidText, out int pid) && pid > 0 && !pids.Contains(pid))
                    {
                        pids.Add(pid);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("[GAMA][MW] netstat failed: " + ex.Message);
        }

        return pids;
    }

    public static bool KillProcessByPid(int pid, Action<string> log = null)
    {
        if (pid <= 0)
        {
            return false;
        }

        try
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "taskkill.exe",
                Arguments = "/PID " + pid + " /F",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(psi))
            {
                if (process == null)
                {
                    return false;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    log?.Invoke("[GAMA][MW] taskkill PID=" + pid + " : " + output.Trim());
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    log?.Invoke("[GAMA][MW] taskkill PID=" + pid + " stderr : " + error.Trim());
                }

                return process.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("[GAMA][MW] taskkill PID=" + pid + " failed: " + ex.Message);
            return false;
        }
    }

    public static async Task<bool> WaitForTcpPortClosedAsync(int port, int timeoutMs, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(500, timeoutMs));
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (GetListeningPidsOnTcpPort(port).Count == 0)
            {
                return true;
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return GetListeningPidsOnTcpPort(port).Count == 0;
    }

    public static async Task<CatalogDiagnosisResult> DiagnoseCatalogAsync(
        string host,
        int monitorPort,
        string experimentName,
        string modelFilePathHint,
        CancellationToken ct,
        Action<string> log = null)
    {
        CatalogDiagnosisResult diagnosis = new CatalogDiagnosisResult
        {
            RequestedExperimentName = experimentName ?? string.Empty,
            RequestedModelPath = modelFilePathHint ?? string.Empty,
            Status = CatalogMatchStatus.ModelAndExperimentNotFound
        };

        StringBuilder trail = new StringBuilder();
        void Append(string line)
        {
            trail.AppendLine(line);
            try
            {
                if (log != null)
                {
                    log.Invoke(line);
                }
                else
                {
                    GamaLog.Dev(line);
                }
            }
            catch
            {
                // ignore
            }
        }

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");
        Append("[GAMA][ORCH][DIAG] Connecting to monitor " + monitorUri);

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            diagnosis.Status = CatalogMatchStatus.MiddlewareNotReachable;
            diagnosis.Error = "The middleware monitor is unreachable at ws://" + hostNorm + ":" + port + "/.";
            diagnosis.LogTrail = trail.ToString();
            return diagnosis;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            try
            {
                using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                    await ws.ConnectAsync(monitorUri, connectCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                diagnosis.Status = CatalogMatchStatus.MiddlewareNotReachable;
                diagnosis.Error = "Could not connect to the monitor: " + ex.Message;
                diagnosis.LogTrail = trail.ToString();
                return diagnosis;
            }

            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);
            await Task.Delay(200, ct).ConfigureAwait(false);
            Append("[GAMA][ORCH][DIAG] → get_simulation_informations");
            await session.SendAsync(new JObject { ["type"] = "get_simulation_informations" }, ct).ConfigureAwait(false);
            JArray catalog = await session.WaitForCatalogAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (catalog == null)
            {
                diagnosis.Status = CatalogMatchStatus.CatalogEmpty;
                diagnosis.Error = "Catalog was not received (monitor timeout).";
                diagnosis.LogTrail = trail.ToString();
                session.Stop();
                try { await receiveTask.ConfigureAwait(false); } catch { /* ignore */ }
                return diagnosis;
            }

            JArray lookupCatalog =
                await BuildEnrichedFlatCatalogAsync(session, catalog, Append, ct).ConfigureAwait(false);
            List<CatalogEntryDiagnostics> entries = BuildCatalogDiagnostics(lookupCatalog);
            LogCatalogDiagnostics(lookupCatalog, Append);
            Append("[GAMA][ORCH][DIAG] Unity selection: model=" + diagnosis.RequestedModelPath +
                   " experiment=" + diagnosis.RequestedExperimentName);
            FillCatalogAvailability(entries, diagnosis.AvailableModels, diagnosis.AvailableExperiments);
            JsonSettingsLookupResult lookup = TryFindJsonSettings(lookupCatalog, experimentName, modelFilePathHint);

            diagnosis.Success = lookup.Found && lookup.Settings != null;
            if (diagnosis.Success)
            {
                diagnosis.Status = CatalogMatchStatus.MatchOk;
                Append("[GAMA][ORCH] MATCH OK model=" + (modelFilePathHint ?? string.Empty) +
                       " experiment=" + (experimentName ?? string.Empty));
            }
            else
            {
                bool hasModel = ContainsModelLike(diagnosis.AvailableModels, diagnosis.RequestedModelPath);
                bool hasExperiment = ContainsExperimentExact(diagnosis.AvailableExperiments, diagnosis.RequestedExperimentName);
                if (!hasModel && !hasExperiment)
                {
                    diagnosis.Status = CatalogMatchStatus.ModelAndExperimentNotFound;
                }
                else if (!hasModel)
                {
                    diagnosis.Status = CatalogMatchStatus.ModelNotFound;
                }
                else if (!hasExperiment)
                {
                    diagnosis.Status = CatalogMatchStatus.ExperimentNotFound;
                }
                else if (lookup.Ambiguous)
                {
                    diagnosis.Status = CatalogMatchStatus.Ambiguous;
                }
            }

            diagnosis.Error = diagnosis.Success
                ? string.Empty
                : "Catalog mismatch: " + (lookup.Details ?? "No matching json_settings.");
            diagnosis.LogTrail = trail.ToString();

            session.Stop();
            try { await receiveTask.ConfigureAwait(false); } catch { /* ignore */ }
            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "diag done", closeCts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return diagnosis;
    }

    /// <summary>
    /// Waits for the monitor TCP port (for example, 8001) to respond after a Node process starts.
    /// </summary>
    public static async Task<bool> WaitForMonitorReachableAsync(
        string host,
        int monitorPort,
        int timeoutMs,
        CancellationToken ct)
    {
        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(500, timeoutMs));
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await IsTcpPortOpenAsync(hostNorm, port, 800, ct).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return false;
    }

  /// <summary>
    /// Monitor sequence: get_simulation_informations → send_simulation → launch_experiment → resume when PAUSED.
    /// </summary>
    public static async Task<ManagedExperimentResult> StartMiddlewareManagedExperimentAsync(
        string host,
        int monitorPort,
        string experimentName,
        string modelFilePathHint,
        CancellationToken ct,
        Action<string> log = null)
    {
        ManagedExperimentResult result = new ManagedExperimentResult();
        StringBuilder trail = new StringBuilder();
        void Append(string line)
        {
            trail.AppendLine(line);
            try
            {
                if (log != null)
                {
                    log.Invoke(line);
                }
                else
                {
                    GamaLog.Dev(line);
                }
            }
            catch
            {
                // ignore
            }
        }

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");

        Append("[GAMA][ORCH] Unity-managed mode: monitor " + monitorUri + " (same workflow as the web UI, without a browser).");

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            result.Error = "The middleware monitor is unreachable at ws://" + hostNorm + ":" + port +
                           "/. Start simple.webplatform in the background.";
            result.LogTrail = trail.ToString();
            return result;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            try
            {
                using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                    await ws.ConnectAsync(monitorUri, connectCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                result.Error = "Could not connect to the monitor: " + ex.Message;
                result.LogTrail = trail.ToString();
                return result;
            }

            Append("[GAMA][ORCH] Connected to the monitor.");

            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);

            await Task.Delay(300, ct).ConfigureAwait(false);

            Append("[GAMA][ORCH] → get_simulation_informations");
            await session.SendAsync(new JObject { ["type"] = "get_simulation_informations" }, ct).ConfigureAwait(false);

            JArray catalog = await session.WaitForCatalogAsync(TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            if (catalog == null)
            {
                result.Error = "The VU catalog was not received from the monitor (get_simulation_informations). " +
                               "A strict catalog launch is not possible; Play will try to attach to the monitor's current experiment.";
                session.Stop();
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }

                result.LogTrail = trail.ToString();
                return result;
            }

            JArray lookupCatalog =
                await BuildEnrichedFlatCatalogAsync(session, catalog, Append, ct).ConfigureAwait(false);
            JsonSettingsLookupResult lookup = TryFindJsonSettings(lookupCatalog, experimentName, modelFilePathHint);
            LogCatalogDiagnostics(lookupCatalog, Append);
            if (!lookup.Found || lookup.Settings == null)
            {
                string why = string.IsNullOrWhiteSpace(lookup.Details) ? "No strict experiment/model match." : lookup.Details;
                string fallback =
                    "The model is selected in Unity but is missing from the middleware catalog. " +
                    "Monitor 8001 cannot launch this experiment from the catalog. " +
                    "Play must instead attach to the GAMA experiment that is already open in the monitor.";
                result.Error = "The Unity experiment was not found in the middleware catalog. " + why + " " +
                               "Expected Unity selection: experiment=\"" + (experimentName ?? "?") + "\", modelPath=\"" +
                               (modelFilePathHint ?? "?") + "\". " + fallback;
                session.Stop();
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }

                result.LogTrail = trail.ToString();
                return result;
            }

            JObject simulationSettings = lookup.Settings;
            result.SelectedModelIndex = lookup.ModelIndex;
            Append("[GAMA][ORCH] VU selected (strict Unity match): name=" + simulationSettings["name"] +
                   " exp=" + simulationSettings["experiment_name"] +
                   " model=" + simulationSettings["model_file_path"] +
                   " model_index=" + lookup.ModelIndex +
                   " details=" + (lookup.Details ?? string.Empty));
            Append("[GAMA][ORCH] MATCH OK model=" + (modelFilePathHint ?? string.Empty) +
                   " experiment=" + (experimentName ?? string.Empty));

            JObject sendSim = new JObject
            {
                ["type"] = "send_simulation",
                ["simulation"] = simulationSettings
            };
            Append("[GAMA][ORCH] → send_simulation");
            await session.SendAsync(sendSim, ct).ConfigureAwait(false);

            await session.WaitForMessageTypeAsync("get_simulation_by_index", TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);

            if (!session.LastGamaConnected)
            {
                Append("[GAMA][ORCH] GAMA is not connected to the middleware yet. Start GAMA (choose Yes), then try again.");
            }

            Append("[GAMA][ORCH] → launch_experiment");
            await session.SendAsync(new JObject { ["type"] = "launch_experiment" }, ct).ConfigureAwait(false);

            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                string state = session.LastExperimentState ?? string.Empty;
                if (!string.IsNullOrEmpty(state) &&
                    !string.Equals(state, "NONE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(state, "NOTREADY", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            string expState = session.LastExperimentState ?? string.Empty;
            if (string.Equals(expState, "NONE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expState, "NOTREADY", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(expState))
            {
                result.Error = "launch_experiment did not move the experiment out of NONE/NOTREADY (state=" +
                               (string.IsNullOrEmpty(expState) ? "?" : expState) + "). Is GAMA connected to the middleware?";
                session.Stop();
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch
                {
                    // ignore
                }

                result.LogTrail = trail.ToString();
                return result;
            }

            if (string.Equals(expState, "PAUSED", StringComparison.OrdinalIgnoreCase))
            {
                Append("[GAMA][ORCH] → resume_experiment (state is PAUSED after load)");
                await session.SendAsync(new JObject { ["type"] = "resume_experiment" }, ct).ConfigureAwait(false);
                DateTime resumeDeadline = DateTime.UtcNow.AddSeconds(60);
                while (DateTime.UtcNow < resumeDeadline && !ct.IsCancellationRequested)
                {
                    expState = session.LastExperimentState ?? expState;
                    if (string.Equals(expState, "RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }

            result.FinalExperimentState = session.LastExperimentState ?? expState;
            result.ExperimentId = session.LastExperimentId ?? string.Empty;
            result.Success = true;
            Append("[GAMA][ORCH] Experiment is ready: experiment_state=" + result.FinalExperimentState +
                   (string.IsNullOrEmpty(result.ExperimentId) ? string.Empty : " exp_id=" + result.ExperimentId));

            session.Stop();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "orch done", closeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        result.LogTrail = trail.ToString();
        return result;
    }

    /// <summary>
    /// Attaches to the experiment already visible to the running middleware monitor.
    /// Used when Unity generated a preview from GAMA's active selection and therefore has no
    /// local .gaml path to match against the middleware catalog. This must not force
    /// launch_experiment, because GAMA may otherwise ask to close the current experiment.
    /// </summary>
    public static async Task<ManagedExperimentResult> LaunchCurrentMonitorExperimentAsync(
        string host,
        int monitorPort,
        CancellationToken ct,
        Action<string> log = null)
    {
        ManagedExperimentResult result = new ManagedExperimentResult();
        StringBuilder trail = new StringBuilder();
        void Append(string line)
        {
            trail.AppendLine(line);
            try
            {
                if (log != null)
                {
                    log.Invoke(line);
                }
                else
                {
                    GamaLog.Dev(line);
                }
            }
            catch
            {
                // ignore
            }
        }

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");

        Append("[GAMA][ORCH][PLAY] Attach to current GAMA monitor state via " + monitorUri + ".");

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            result.Error = "The middleware monitor is unreachable at ws://" + hostNorm + ":" + port +
                           "/. Start simple.webplatform in the background.";
            result.LogTrail = trail.ToString();
            return result;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            try
            {
                using (CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                    await ws.ConnectAsync(monitorUri, connectCts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                result.Error = "Could not connect to the monitor: " + ex.Message;
                result.LogTrail = trail.ToString();
                return result;
            }

            Append("[GAMA][ORCH][PLAY] Connected to the monitor.");

            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);

            await Task.Delay(200, ct).ConfigureAwait(false);
            Append("[GAMA][ORCH][PLAY] -> get_simulation_informations");
            await session.SendAsync(new JObject { ["type"] = "get_simulation_informations" }, ct).ConfigureAwait(false);

            DateTime initialStateDeadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < initialStateDeadline && !ct.IsCancellationRequested)
            {
                string state = session.LastExperimentState ?? string.Empty;
                if (!string.IsNullOrEmpty(state))
                {
                    break;
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            string expState = session.LastExperimentState ?? string.Empty;
            if (string.IsNullOrEmpty(expState) ||
                string.Equals(expState, "NONE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(expState, "NOTREADY", StringComparison.OrdinalIgnoreCase))
            {
                Append("[GAMA][ORCH][PLAY] No active experiment detected (state=" +
                       (string.IsNullOrEmpty(expState) ? "?" : expState) +
                       "), launching current monitor selection.");
                await session.SendAsync(new JObject { ["type"] = "launch_experiment" }, ct).ConfigureAwait(false);

                DateTime launchDeadline = DateTime.UtcNow.AddSeconds(90);
                while (DateTime.UtcNow < launchDeadline && !ct.IsCancellationRequested)
                {
                    expState = session.LastExperimentState ?? expState;
                    if (!string.IsNullOrEmpty(expState) &&
                        !string.Equals(expState, "NONE", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(expState, "NOTREADY", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }

            if (string.Equals(expState, "PAUSED", StringComparison.OrdinalIgnoreCase))
            {
                Append("[GAMA][ORCH][PLAY] → resume_experiment (state is PAUSED)");
                await session.SendAsync(new JObject { ["type"] = "resume_experiment" }, ct).ConfigureAwait(false);
                DateTime resumeDeadline = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < resumeDeadline && !ct.IsCancellationRequested)
                {
                    expState = session.LastExperimentState ?? expState;
                    if (string.Equals(expState, "RUNNING", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }

            result.FinalExperimentState = session.LastExperimentState ?? expState;
            result.ExperimentId = session.LastExperimentId ?? string.Empty;
            result.Success = !string.IsNullOrEmpty(result.FinalExperimentState) &&
                             !string.Equals(result.FinalExperimentState, "NONE", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(result.FinalExperimentState, "NOTREADY", StringComparison.OrdinalIgnoreCase);
            if (!result.Success)
            {
                result.Error = "No active GAMA experiment was detected by the monitor (state=" +
                                 (string.IsNullOrEmpty(result.FinalExperimentState) ? "?" : result.FinalExperimentState) +
                                 ") after attempting to launch the monitor's current selection.";
            }
            else
            {
                Append("[GAMA][ORCH][PLAY] Active experiment detected: experiment_state=" + result.FinalExperimentState +
                       (string.IsNullOrEmpty(result.ExperimentId) ? string.Empty : " exp_id=" + result.ExperimentId));
            }

            session.Stop();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "play launch done", closeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        result.LogTrail = trail.ToString();
        return result;
    }

    private struct JsonSettingsLookupResult
    {
        public bool Found;
        public JObject Settings;
        public int? ModelIndex;
        public bool Ambiguous;
        public string Details;
    }

    private static JsonSettingsLookupResult TryFindJsonSettings(
        JToken catalogRoot,
        string experimentName,
        string modelFilePathHint)
    {
        JsonSettingsLookupResult result = new JsonSettingsLookupResult();
        string expWanted = NormalizeLookupToken(experimentName);
        string pathWanted = NormalizeLookupPath(modelFilePathHint);
        string fileWanted = NormalizeLookupToken(string.IsNullOrWhiteSpace(modelFilePathHint)
            ? string.Empty
            : Path.GetFileName(modelFilePathHint.Trim()));

        if (string.IsNullOrEmpty(expWanted) || (string.IsNullOrEmpty(pathWanted) && string.IsNullOrEmpty(fileWanted)))
        {
            result.Details = "The Unity selection is incomplete (experimentName/modelPath).";
            return result;
        }

        List<JsonSettingsCandidate> candidates = new List<JsonSettingsCandidate>();
        List<JObject> experimentOnlyMatches = new List<JObject>();
        List<string> experimentModelPaths = new List<string>();

        HashSet<string> availableExperiments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> availableModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject entry in EnumerateJsonSettingsEntries(catalogRoot))
        {
            string entryExp = NormalizeLookupToken(entry["experiment_name"]?.ToString() ?? entry["name"]?.ToString() ?? string.Empty);
            if (string.IsNullOrEmpty(entryExp) || !string.Equals(entryExp, expWanted, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(entry["experiment_name"]?.ToString()))
                {
                    availableExperiments.Add(entry["experiment_name"]?.ToString().Trim());
                }

                string nonMatchingModel = entry["model_file_path"]?.ToString();
                if (!string.IsNullOrWhiteSpace(nonMatchingModel))
                {
                    availableModels.Add(nonMatchingModel.Trim());
                }

                continue;
            }

            experimentOnlyMatches.Add(entry);
            string entryModelRaw = entry["model_file_path"]?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(entryModelRaw))
            {
                experimentModelPaths.Add(entryModelRaw.Trim());
                availableModels.Add(entryModelRaw.Trim());
            }

            availableExperiments.Add(entry["experiment_name"]?.ToString()?.Trim() ?? experimentName ?? string.Empty);

            string entryPath = NormalizeLookupPath(entryModelRaw);
            string entryFile = NormalizeLookupToken(Path.GetFileName(entryModelRaw ?? string.Empty));
            bool fileMatch = !string.IsNullOrEmpty(fileWanted) && string.Equals(entryFile, fileWanted, StringComparison.Ordinal);
            bool pathExact = !string.IsNullOrEmpty(pathWanted) && !string.IsNullOrEmpty(entryPath) &&
                             string.Equals(entryPath, pathWanted, StringComparison.Ordinal);
            bool pathSuffix = !pathExact &&
                              !string.IsNullOrEmpty(pathWanted) &&
                              !string.IsNullOrEmpty(entryPath) &&
                              (pathWanted.EndsWith("/" + entryPath, StringComparison.Ordinal) ||
                               entryPath.EndsWith("/" + pathWanted, StringComparison.Ordinal));

            if (!fileMatch && !pathExact && !pathSuffix)
            {
                continue;
            }

            int score = 1000;
            if (pathExact) score += 300;
            if (pathSuffix) score += 200;
            if (fileMatch) score += 100;
            candidates.Add(new JsonSettingsCandidate
            {
                settings = entry,
                score = score,
                pathScore = entryPath.Length
            });
        }

        if (candidates.Count == 0)
        {
            if (experimentOnlyMatches.Count == 1)
            {
                JObject sole = experimentOnlyMatches[0];
                string solePath = sole["model_file_path"]?.ToString() ?? string.Empty;
                bool unityWantsPath = !string.IsNullOrEmpty(pathWanted) || !string.IsNullOrEmpty(fileWanted);
                if (unityWantsPath && !string.IsNullOrWhiteSpace(solePath))
                {
                    result.Details =
                        "Experiment \"" + experimentName +
                        "\" exists in the middleware catalog, but its cataloged .gaml file does not match the Unity selection (" +
                        (modelFilePathHint ?? "?") + "). Catalog model: " + solePath.Trim() +
                        ". The strict catalog launch will fall back to attach-to-current-monitor if a GAMA experiment is already active.";
                    return result;
                }

                // No Unity path to apply, or the entry has no model_file_path: preserve legacy behavior.
                result.Settings = sole;
                if (result.Settings["model_index"] != null && result.Settings["model_index"].Type == JTokenType.Integer)
                {
                    result.ModelIndex = result.Settings["model_index"].Value<int>();
                }

                result.Found = true;
                result.Details =
                    "Fallback match: exact experiment, with no usable Unity model_path constraint.";
                return result;
            }

            if (experimentOnlyMatches.Count > 1)
            {
                string listedModels = experimentModelPaths.Count == 0
                    ? "(no model_file_path)"
                    : string.Join(" | ", experimentModelPaths);
                result.Details =
                    "Multiple json_settings entries exist for experiment=\"" + experimentName +
                    "\", but no model_path is compatible with \"" + modelFilePathHint +
                    "\". Catalog models: " + listedModels;
                return result;
            }

            string experimentsList = JoinOrNone(availableExperiments);
            string modelsList = JoinOrNone(availableModels);
            result.Details = "No json_settings entry has the exact experiment \"" + experimentName + "\". " +
                             "Available experiments: " + experimentsList + ". " +
                             "Available models: " + modelsList + ".";
            return result;
        }

        candidates.Sort((a, b) =>
        {
            int cmp = b.score.CompareTo(a.score);
            if (cmp != 0) return cmp;
            return b.pathScore.CompareTo(a.pathScore);
        });

        JsonSettingsCandidate best = candidates[0];
        if (candidates.Count > 1 &&
            candidates[1].score == best.score &&
            candidates[1].pathScore == best.pathScore)
        {
            result.Ambiguous = true;
            result.Details = "Ambiguous match: multiple json_settings entries match the selected experiment/model.";
            return result;
        }

        result.Settings = best.settings;
        if (best.settings["model_index"] != null && best.settings["model_index"].Type == JTokenType.Integer)
        {
            result.ModelIndex = best.settings["model_index"].Value<int>();
        }

        result.Found = true;
        result.Details = "Strict experiment/model match applied.";
        return result;
    }

    private static string JoinOrNone(HashSet<string> values)
    {
        if (values == null || values.Count == 0)
        {
            return "(none)";
        }

        List<string> sorted = new List<string>(values);
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(" | ", sorted);
    }

    private static void FillCatalogAvailability(
        List<CatalogEntryDiagnostics> entries,
        List<string> models,
        List<string> experiments)
    {
        HashSet<string> modelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> expSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            CatalogEntryDiagnostics entry = entries[i];
            if (!string.IsNullOrWhiteSpace(entry.ModelPath))
            {
                modelSet.Add(entry.ModelPath.Trim());
            }

            for (int e = 0; e < entry.Experiments.Count; e++)
            {
                string exp = entry.Experiments[e];
                if (!string.IsNullOrWhiteSpace(exp))
                {
                    expSet.Add(exp.Trim());
                }
            }
        }

        models.Clear();
        models.AddRange(modelSet);
        models.Sort(StringComparer.OrdinalIgnoreCase);

        experiments.Clear();
        experiments.AddRange(expSet);
        experiments.Sort(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsExperimentExact(List<string> availableExperiments, string requestedExperiment)
    {
        if (string.IsNullOrWhiteSpace(requestedExperiment))
        {
            return false;
        }

        string wanted = requestedExperiment.Trim();
        for (int i = 0; i < availableExperiments.Count; i++)
        {
            if (string.Equals(availableExperiments[i], wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsModelLike(List<string> availableModels, string requestedModelPath)
    {
        if (string.IsNullOrWhiteSpace(requestedModelPath))
        {
            return false;
        }

        string requestedPath = NormalizeLookupPath(requestedModelPath);
        string requestedFile = NormalizeLookupToken(Path.GetFileName(requestedModelPath));
        for (int i = 0; i < availableModels.Count; i++)
        {
            string model = availableModels[i];
            string modelPath = NormalizeLookupPath(model);
            string modelFile = NormalizeLookupToken(Path.GetFileName(model ?? string.Empty));
            if (string.Equals(modelPath, requestedPath, StringComparison.Ordinal) ||
                requestedPath.EndsWith("/" + modelPath, StringComparison.Ordinal) ||
                modelPath.EndsWith("/" + requestedPath, StringComparison.Ordinal) ||
                string.Equals(modelFile, requestedFile, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The monitor sends a lightweight catalog (name, model_index) without
    /// <c>experiment_name</c> / <c>model_file_path</c>. Fetches the full JSON through
    /// <c>get_simulation_by_index</c> so Unity can match it correctly.
    /// </summary>
    private static async Task<JArray> BuildEnrichedFlatCatalogAsync(
        MonitorSession session,
        JArray catalogRoot,
        Action<string> append,
        CancellationToken ct)
    {
        JArray flat = new JArray();
        Dictionary<int, JObject> cache = new Dictionary<int, JObject>();
        foreach (JObject entry in EnumerateJsonSettingsEntries(catalogRoot))
        {
            JToken miTok = entry["model_index"];
            if (miTok == null || miTok.Type != JTokenType.Integer)
            {
                flat.Add(entry);
                append?.Invoke("[GAMA][ORCH][ENRICH] Keeping entry without model_index (raw catalog).");
                continue;
            }

            int mi = miTok.Value<int>();
            if (!cache.TryGetValue(mi, out JObject full))
            {
                full = await session.TryGetFullSimulationSettingsAsync(mi, ct).ConfigureAwait(false);
                if (full != null)
                {
                    cache[mi] = full;
                }
            }

            if (full != null)
            {
                flat.Add((JObject)full.DeepClone());
                append?.Invoke("[GAMA][ORCH][ENRICH] model_index=" + mi +
                               " experiment=" + (full["experiment_name"]?.ToString() ?? "?") +
                               " model_file_path=" + (full["model_file_path"]?.ToString() ?? "?"));
            }
            else
            {
                flat.Add(entry);
                append?.Invoke("[GAMA][ORCH][ENRICH] Failed to enrich model_index=" + mi + " (falling back to the lightweight catalog).");
            }
        }

        return flat;
    }

    private static IEnumerable<JObject> EnumerateJsonSettingsEntries(JToken node)
    {
        if (node == null)
        {
            yield break;
        }

        if (node is JArray array)
        {
            foreach (JToken child in array)
            {
                foreach (JObject entry in EnumerateJsonSettingsEntries(child))
                {
                    yield return entry;
                }
            }

            yield break;
        }

        if (node.Type != JTokenType.Object)
        {
            yield break;
        }

        JObject obj = (JObject)node;
        string type = obj["type"]?.ToString() ?? string.Empty;
        if (string.Equals(type, "catalog", StringComparison.OrdinalIgnoreCase))
        {
            foreach (JObject entry in EnumerateJsonSettingsEntries(obj["entries"]))
            {
                yield return entry;
            }

            yield break;
        }

        if (string.Equals(type, "json_settings", StringComparison.OrdinalIgnoreCase))
        {
            yield return obj;
        }
    }

    private static void LogCatalogDiagnostics(JToken catalogRoot, Action<string> append)
    {
        if (append == null)
        {
            return;
        }

        List<CatalogEntryDiagnostics> entries = BuildCatalogDiagnostics(catalogRoot);
        if (entries.Count == 0)
        {
            append("[GAMA][ORCH][CATALOG] No json_settings entries detected.");
            return;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            CatalogEntryDiagnostics entry = entries[i];
            string experiments = entry.Experiments.Count == 0 ? "(none)" : string.Join(", ", entry.Experiments);
            append("[GAMA][ORCH][CATALOG] index=" + entry.CatalogIndex +
                   " name=" + entry.Name +
                   " model=" + entry.ModelPath +
                   " gaml=" + entry.GamlFile +
                   " experiments=[" + experiments + "]");
            append("[GAMA][ORCH][CATALOG][JSON] " + entry.RawSettings?.ToString(Formatting.None));
        }
    }

    private static List<CatalogEntryDiagnostics> BuildCatalogDiagnostics(JToken catalogRoot)
    {
        List<CatalogEntryDiagnostics> diagnostics = new List<CatalogEntryDiagnostics>();
        int index = 0;
        foreach (JObject entry in EnumerateJsonSettingsEntries(catalogRoot))
        {
            CatalogEntryDiagnostics d = new CatalogEntryDiagnostics
            {
                CatalogIndex = index++,
                Name = entry["name"]?.ToString() ?? string.Empty,
                ModelPath = entry["model_file_path"]?.ToString() ?? string.Empty,
                GamlFile = entry["gaml_file"]?.ToString() ?? string.Empty,
                RawSettings = entry
            };

            string experimentName = entry["experiment_name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(experimentName))
            {
                d.Experiments.Add(experimentName.Trim());
            }

            JToken experimentsToken = entry["experiments"];
            if (experimentsToken is JArray experimentsArray)
            {
                for (int i = 0; i < experimentsArray.Count; i++)
                {
                    string exp = experimentsArray[i]?.ToString();
                    if (!string.IsNullOrWhiteSpace(exp) && !d.Experiments.Contains(exp))
                    {
                        d.Experiments.Add(exp.Trim());
                    }
                }
            }

            diagnostics.Add(d);
        }

        return diagnostics;
    }

    private sealed class JsonSettingsCandidate
    {
        public JObject settings;
        public int score;
        public int pathScore;
    }

    private static string NormalizeLookupToken(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static string NormalizeLookupPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().Replace('\\', '/').ToLowerInvariant();
        while (normalized.Contains("//"))
        {
            normalized = normalized.Replace("//", "/");
        }

        return normalized;
    }

    private sealed class MonitorSession
    {
        private readonly ClientWebSocket _ws;
        private readonly Action<string> _log;
        private readonly object _gate = new object();
        private readonly List<JObject> _inbox = new List<JObject>();
        private volatile bool _running = true;
        private TaskCompletionSource<JArray> _catalogTcs;

        public string LastExperimentState { get; private set; } = string.Empty;
        public string LastExperimentId { get; private set; } = string.Empty;
        public bool LastGamaConnected { get; private set; }

        public MonitorSession(ClientWebSocket ws, Action<string> log)
        {
            _ws = ws;
            _log = log;
        }

        public void Stop()
        {
            _running = false;
        }

        public async Task SendAsync(JObject payload, CancellationToken ct)
        {
            string json = payload.ToString(Formatting.None);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
        }

        public async Task<JArray> WaitForCatalogAsync(TimeSpan timeout, CancellationToken ct)
        {
            _catalogTcs = new TaskCompletionSource<JArray>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(timeout);
                try
                {
                    Task delay = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                    Task done = await Task.WhenAny(_catalogTcs.Task, delay).ConfigureAwait(false);
                    if (done != _catalogTcs.Task)
                    {
                        return null;
                    }

                    return await _catalogTcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        public async Task WaitForMessageTypeAsync(string type, TimeSpan timeout, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                lock (_gate)
                {
                    for (int i = _inbox.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(_inbox[i]["type"]?.ToString(), type, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                    }
                }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Fetches the complete settings (including <c>experiment_name</c> and <c>model_file_path</c>)
        /// for a middleware <c>model_index</c>.
        /// </summary>
        public async Task<JObject> TryGetFullSimulationSettingsAsync(int modelIndex, CancellationToken ct)
        {
            await SendAsync(
                new JObject
                {
                    ["type"] = "get_simulation_by_index",
                    ["simulationIndex"] = modelIndex
                },
                ct).ConfigureAwait(false);

            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                lock (_gate)
                {
                    for (int i = 0; i < _inbox.Count; i++)
                    {
                        JObject m = _inbox[i];
                        if (!string.Equals(
                                m["type"]?.ToString(),
                                "get_simulation_by_index",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        JObject sim = m["simulation"] as JObject;
                        if (sim == null)
                        {
                            continue;
                        }

                        _inbox.RemoveAt(i);
                        JObject clone = (JObject)sim.DeepClone();
                        clone["model_index"] = modelIndex;
                        return clone;
                    }
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            return null;
        }

        public async Task RunReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[64 * 1024];
            StringBuilder pending = new StringBuilder();
            try
            {
                while (_running && !ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult rr;
                    using (CancellationTokenSource slice = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        slice.CancelAfter(TimeSpan.FromSeconds(2));
                        try
                        {
                            rr = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), slice.Token)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            continue;
                        }
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
                    HandleMessage(text);
                }
            }
            catch (OperationCanceledException)
            {
                // normal
            }
            catch (Exception ex)
            {
                _log("[GAMA][ORCH] Monitor receive error: " + ex.Message);
            }
        }

        private void HandleMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch
            {
                return;
            }

            if (token.Type == JTokenType.String)
            {
                try
                {
                    token = JToken.Parse(token.Value<string>());
                }
                catch
                {
                    return;
                }
            }

            if (token is JArray rootArray)
            {
                _log("[GAMA][ORCH][in] VU catalog (root array, " + rootArray.Count + " entries)");
                if (_catalogTcs != null)
                {
                    _catalogTcs.TrySetResult(rootArray);
                }

                return;
            }

            if (token.Type != JTokenType.Object)
            {
                return;
            }

            JObject json = (JObject)token;
            string type = json["type"]?.ToString() ?? string.Empty;
            if (type.Length > 0)
            {
                _log("[GAMA][ORCH][in] type=" + type);
            }

            JObject gama = json["gama"] as JObject;
            if (gama != null)
            {
                LastGamaConnected = gama["connected"]?.Value<bool>() ?? LastGamaConnected;
                string state = gama["experiment_state"]?.ToString();
                if (!string.IsNullOrEmpty(state))
                {
                    LastExperimentState = state;
                }

                string expId = gama["experiment_id"]?.ToString();
                if (!string.IsNullOrEmpty(expId))
                {
                    LastExperimentId = expId;
                }
            }

            lock (_gate)
            {
                _inbox.Add(json);
            }

            if (json["entries"] is JArray catalogEntries && _catalogTcs != null)
            {
                _catalogTcs.TrySetResult(catalogEntries);
            }
        }
    }

    /// <summary>Pauses the experiment through the monitor.</summary>
    public static async Task<bool> PauseExperimentAsync(
        string host,
        int monitorPort,
        CancellationToken ct,
        Action<string> log = null,
        string reason = "preview capture completed")
    {
        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");

        void Append(string line)
        {
            try
            {
                log?.Invoke(line);
            }
            catch
            {
                // ignore
            }

            GamaLog.Dev(line);
        }

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            Append("[GAMA][ORCH] pause_experiment: monitor is unreachable at " + monitorUri);
            return false;
        }

        bool pauseConfirmed = false;
        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            await ws.ConnectAsync(monitorUri, ct).ConfigureAwait(false);
            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);
            await Task.Delay(200, ct).ConfigureAwait(false);
            Append("[GAMA][ORCH] → pause_experiment (" + reason + ")");
            await session.SendAsync(new JObject { ["type"] = "pause_experiment" }, ct).ConfigureAwait(false);
            DateTime pauseDeadline = DateTime.UtcNow.AddSeconds(3);
            string lastState = session.LastExperimentState ?? string.Empty;
            while (DateTime.UtcNow < pauseDeadline && !ct.IsCancellationRequested)
            {
                lastState = session.LastExperimentState ?? lastState;
                if (string.Equals(lastState, "PAUSED", StringComparison.OrdinalIgnoreCase))
                {
                    pauseConfirmed = true;
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            if (pauseConfirmed)
            {
                Append("[GAMA][ORCH] pause_experiment confirmed: experiment_state=PAUSED");
            }
            else
            {
                Append("[GAMA][ORCH] pause_experiment was not confirmed: experiment_state=" +
                       (string.IsNullOrEmpty(lastState) ? "?" : lastState));
            }

            session.Stop();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "pause done", closeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return pauseConfirmed;
    }

    /// <summary>Resumes the experiment through the monitor.</summary>
    public static async Task<bool> ResumeExperimentAsync(
        string host,
        int monitorPort,
        CancellationToken ct,
        Action<string> log = null,
        string reason = "Unity Play resumed")
    {
        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");

        void Append(string line)
        {
            try
            {
                log?.Invoke(line);
            }
            catch
            {
                // ignore
            }

            GamaLog.Dev(line);
        }

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            Append("[GAMA][ORCH] resume_experiment: monitor is unreachable at " + monitorUri);
            return false;
        }

        bool resumeConfirmed = false;
        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            await ws.ConnectAsync(monitorUri, ct).ConfigureAwait(false);
            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);
            await Task.Delay(200, ct).ConfigureAwait(false);
            Append("[GAMA][ORCH] → resume_experiment (" + reason + ")");
            await session.SendAsync(new JObject { ["type"] = "resume_experiment" }, ct).ConfigureAwait(false);
            DateTime resumeDeadline = DateTime.UtcNow.AddSeconds(3);
            string lastState = session.LastExperimentState ?? string.Empty;
            while (DateTime.UtcNow < resumeDeadline && !ct.IsCancellationRequested)
            {
                lastState = session.LastExperimentState ?? lastState;
                if (string.Equals(lastState, "RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    resumeConfirmed = true;
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            if (resumeConfirmed)
            {
                Append("[GAMA][ORCH] resume_experiment confirmed: experiment_state=RUNNING");
            }
            else
            {
                Append("[GAMA][ORCH] resume_experiment was not confirmed: experiment_state=" +
                       (string.IsNullOrEmpty(lastState) ? "?" : lastState));
            }

            session.Stop();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "resume done", closeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return resumeConfirmed;
    }

    /// <summary>Removes a player from the middleware/GAMA through the monitor socket.</summary>
    public static async Task<bool> RemovePlayerHeadsetAsync(
        string host,
        int monitorPort,
        string playerId,
        CancellationToken ct,
        Action<string> log = null)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            return false;
        }

        string hostNorm = string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();
        int port = monitorPort > 0 ? monitorPort : DefaultMonitorPort;
        Uri monitorUri = new Uri("ws://" + hostNorm + ":" + port + "/");

        void Append(string line)
        {
            try
            {
                log?.Invoke(line);
            }
            catch
            {
                // ignore
            }

            GamaLog.Dev(line);
        }

        if (!await IsTcpPortOpenAsync(hostNorm, port, 3000, ct).ConfigureAwait(false))
        {
            Append("[GAMA][ORCH] remove_player_headset: monitor is unreachable at " + monitorUri);
            return false;
        }

        using (ClientWebSocket ws = new ClientWebSocket())
        {
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);
            await ws.ConnectAsync(monitorUri, ct).ConfigureAwait(false);
            MonitorSession session = new MonitorSession(ws, Append);
            Task receiveTask = session.RunReceiveLoopAsync(ct);
            await Task.Delay(200, ct).ConfigureAwait(false);
            Append("[GAMA][ORCH] → remove_player_headset id=" + playerId.Trim());
            await session.SendAsync(
                new JObject
                {
                    ["type"] = "remove_player_headset",
                    ["id"] = playerId.Trim()
                },
                ct).ConfigureAwait(false);
            await Task.Delay(700, ct).ConfigureAwait(false);
            session.Stop();
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            try
            {
                if (ws.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "remove player done", closeCts.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        return true;
    }
}
