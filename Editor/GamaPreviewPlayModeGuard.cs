using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading;

[InitializeOnLoad]
public static class GamaPreviewPlayModeGuard
{
    private const string SessionStateKey = "GamaPreviewWasActiveBeforePlay";
    private const string AutoHidePreviewOnPlayPrefKey = "ProjectSimple.GamaUnity.Panel.AutoHidePreviewOnPlay";
    private const string AutoLaunchGamaOnPlayPrefKey = "ProjectSimple.GamaUnity.Play.AutoLaunchMonitor";
    private const string PauseGamaOnPlayExitPrefKey = "ProjectSimple.GamaUnity.Play.PauseOnExit";
    private const string GamaCaptureHostPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureHost";
    private const string GamaCapturePortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCapturePort";
    private const string GamaCaptureMonitorPortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureMonitorPort";
    private const string PlayModelPathPrefKey = "ProjectSimple.GamaUnity.Play.ModelPath";
    private const string PlayExperimentPrefKey = "ProjectSimple.GamaUnity.Play.Experiment";
    private const string PlaySessionIdPrefKey = "ProjectSimple.GamaUnity.Play.PlayerId";
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";

    static GamaPreviewPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.pauseStateChanged += OnPauseStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();
            EnsureStableUnityPlayId();

            if (StaticInformation.TryGetCurrentId(out string previousPlayerId))
            {
                TryRemoveRuntimePlayerFromUnity("Before new Unity Play", previousPlayerId);
            }

            GamaLog.Dev("[GAMA][PLAY] Unity Play player id: " + StaticInformation.getId());
            AssignSpeciesOverrideContextForPlay();

            GameObject root = FindPreviewRoot();
            if (root != null)
            {
                bool wasActive = root.activeSelf;
                SessionState.SetBool(SessionStateKey, wasActive);

                bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                if (autoHide && wasActive)
                {
                    root.SetActive(false);
                    GamaLog.Dev("[GAMA][PREVIEW][PLAY] Static preview hidden before Play mode.");
                }
            }

            TryPrepareGamaForPlay();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            string runtimePlayerId = StaticInformation.getId();
            GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();

            TryPauseGamaFromUnity("Unity Play stopped", 2);
            TryDisconnectRuntimePlayer("Unity Play stopped");
            TryRemoveRuntimePlayerFromUnity("Unity Play stopped", runtimePlayerId);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();

            if (SessionState.GetBool(SessionStateKey, false))
            {
                GameObject root = FindPreviewRoot();
                if (root != null)
                {
                    bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                    if (autoHide && !root.activeSelf)
                    {
                        root.SetActive(true);
                        GamaLog.Dev("[GAMA][PREVIEW][PLAY] Static preview restored after Play mode.");
                    }
                }
            }
            SessionState.EraseBool(SessionStateKey);
        }
    }

    private static void OnPauseStateChanged(PauseState state)
    {
        if (state == PauseState.Paused && EditorApplication.isPlaying)
        {
            TryPauseGamaFromUnity("Unity Play paused");
        }
        else if (state == PauseState.Unpaused && EditorApplication.isPlaying)
        {
            TryResumeGamaFromUnity("Unity Play resumed");
        }
    }

    private static void TryPauseGamaFromUnity(string reason, int attempts = 1)
    {
        if (!EditorPrefs.GetBool(PauseGamaOnPlayExitPrefKey, true))
        {
            return;
        }

        string host = EditorPrefs.GetString(GamaCaptureHostPrefKey, PlayerPrefs.GetString("IP", "localhost"));
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }
        host = host.Trim();

        int monitorPort = EditorPrefs.GetInt(
            GamaCaptureMonitorPortPrefKey,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);

        try
        {
            attempts = Math.Max(1, attempts);
            bool paused = false;
            for (int attempt = 1; attempt <= attempts && !paused; attempt++)
            {
                using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    paused = GamaEditorMiddlewareOrchestrator.PauseExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            reason: reason + (attempts > 1 ? " attempt " + attempt : string.Empty))
                        .GetAwaiter()
                        .GetResult();
                }

                if (!paused && attempt < attempts)
                {
                    Thread.Sleep(350);
                }
            }

            if (!paused)
            {
                GamaLog.Warning("[GAMA][PLAY] " + reason + ", but pause_experiment was not confirmed on monitor " + monitorPort + ".");
            }
            else
            {
                GamaLog.Info("[GAMA] GAMA experiment paused after Play Mode.");
            }
        }
        catch (Exception ex)
        {
            GamaLog.Warning("[GAMA][PLAY] Failed to pause GAMA after " + reason + ": " + ex.Message);
        }
    }

    private static void TryResumeGamaFromUnity(string reason)
    {
        if (!EditorPrefs.GetBool(PauseGamaOnPlayExitPrefKey, true))
        {
            return;
        }

        string host = EditorPrefs.GetString(GamaCaptureHostPrefKey, PlayerPrefs.GetString("IP", "localhost"));
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }
        host = host.Trim();

        int monitorPort = EditorPrefs.GetInt(
            GamaCaptureMonitorPortPrefKey,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);

        try
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                bool resumed = GamaEditorMiddlewareOrchestrator.ResumeExperimentAsync(
                        host,
                        monitorPort,
                        cts.Token,
                        reason: reason)
                    .GetAwaiter()
                    .GetResult();
                if (!resumed)
                {
                    GamaLog.Warning("[GAMA][PLAY] " + reason + ", but resume_experiment was not confirmed on monitor " + monitorPort + ".");
                }
            }
        }
        catch (Exception ex)
        {
            GamaLog.Warning("[GAMA][PLAY] Failed to resume GAMA after " + reason + ": " + ex.Message);
        }
    }

    private static void TryDisconnectRuntimePlayer(string reason)
    {
        ConnectionManager manager = ConnectionManager.Instance;
        if (manager == null)
        {
            return;
        }

        try
        {
            manager.DisconnectProperlyAsync().GetAwaiter().GetResult();
            Thread.Sleep(150);
            GamaLog.Dev("[GAMA][PLAY] " + reason + ": runtime websocket disconnected cleanly.");
        }
        catch (Exception ex)
        {
            GamaLog.DevWarning("[GAMA][PLAY] Failed to disconnect runtime websocket after " + reason + ": " + ex.Message);
        }
    }

    private static void TryRemoveRuntimePlayerFromUnity(string reason, string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
        {
            playerId = StaticInformation.getId();
        }

        if (string.IsNullOrWhiteSpace(playerId) ||
            !playerId.Trim().StartsWith("unity_play_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string host = EditorPrefs.GetString(GamaCaptureHostPrefKey, PlayerPrefs.GetString("IP", "localhost"));
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }
        host = host.Trim();

        int monitorPort = EditorPrefs.GetInt(
            GamaCaptureMonitorPortPrefKey,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);

        try
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                bool removed = GamaEditorMiddlewareOrchestrator.RemovePlayerHeadsetAsync(
                        host,
                        monitorPort,
                        playerId,
                        cts.Token,
                        GamaLog.Dev)
                    .GetAwaiter()
                    .GetResult();
                GamaLog.Dev("[GAMA][PLAY] " + reason + ": runtime player cleanup id=" + playerId + " removed=" + removed);
            }
        }
        catch (Exception ex)
        {
            GamaLog.DevWarning("[GAMA][PLAY] Failed to remove runtime player after " + reason + ": " + ex.Message);
        }
    }

    private static GameObject FindPreviewRoot()
    {
        GamaPreviewSession session = UnityEngine.Object.FindFirstObjectByType<GamaPreviewSession>(FindObjectsInactive.Include);
        if (session != null)
        {
            return session.gameObject;
        }

        return GameObject.Find(StaticPreviewRootName);
    }

    private static void EnsureStableUnityPlayId()
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

    private static void AssignSpeciesOverrideContextForPlay()
    {
        GamaSpeciesRenderOverrides asset = null;
        string modelPath = string.Empty;
        string experimentName = string.Empty;

        GamaPreviewSession session = FindCurrentPreviewSession();
        if (session != null)
        {
            asset = session.speciesOverrides != null
                ? session.speciesOverrides
                : GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
            if (asset != null && session.speciesOverrides == null)
            {
                session.speciesOverrides = asset;
                EditorUtility.SetDirty(session);
            }

            modelPath = session.modelPath ?? string.Empty;
            experimentName = session.experimentName ?? string.Empty;
        }

        if (asset == null)
        {
            asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        }

        if (asset == null)
        {
            return;
        }

        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null)
            {
                managers[i].SetSpeciesRenderOverridesContext(asset, modelPath, experimentName);
            }
        }
    }

    private static GamaPreviewSession FindCurrentPreviewSession()
    {
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        GamaPreviewSession fallback = null;
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (session == null)
            {
                continue;
            }

            if (session.useThisPreviewForPlay && !session.stale)
            {
                return session;
            }

            if (fallback == null)
            {
                fallback = session;
            }

            if (!session.stale && session.gameObject != null && session.gameObject.name == StaticPreviewRootName)
            {
                fallback = session;
            }
        }

        return fallback;
    }

    private static void TryPrepareGamaForPlay()
    {
        if (!EditorPrefs.GetBool(AutoLaunchGamaOnPlayPrefKey, true))
        {
            GamaLog.Dev("[GAMA][PLAY] Auto-launch disabled; Play will only connect to the existing middleware state.");
            return;
        }

        string host = EditorPrefs.GetString(GamaCaptureHostPrefKey, PlayerPrefs.GetString("IP", "localhost"));
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }
        host = host.Trim();

        string playerPort = EditorPrefs.GetString(GamaCapturePortPrefKey, PlayerPrefs.GetString("PORT", "8080"));
        if (string.IsNullOrWhiteSpace(playerPort) || string.Equals(playerPort.Trim(), "1000", StringComparison.Ordinal))
        {
            playerPort = "8080";
        }
        playerPort = playerPort.Trim();
        PlayerPrefs.SetString("IP", host);
        PlayerPrefs.SetString("PORT", playerPort);
        PlayerPrefs.Save();

        int monitorPort = EditorPrefs.GetInt(
            GamaCaptureMonitorPortPrefKey,
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);

        bool hasTarget = GamaEditorPlayTargetResolver.TryResolve(
            out string modelPath,
            out string experimentName,
            out string source);

        GamaLog.Info("[GAMA] Preparing GAMA experiment before Play Mode.");

        try
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(130)))
            {
                CleanupEditorPreviewPlayersBeforePlay(host, playerPort, monitorPort, cts.Token);

                GamaEditorMiddlewareOrchestrator.ManagedExperimentResult result = hasTarget
                    ? GamaEditorMiddlewareOrchestrator.StartMiddlewareManagedExperimentAsync(
                            host,
                            monitorPort,
                            experimentName,
                            modelPath,
                            cts.Token,
                            GamaLog.Dev)
                        .GetAwaiter()
                        .GetResult()
                    : GamaEditorMiddlewareOrchestrator.LaunchCurrentMonitorExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            GamaLog.Dev)
                        .GetAwaiter()
                        .GetResult();

                if (hasTarget && result != null && !result.Success && ShouldAttachToCurrentMonitorFallback(result.Error))
                {
                    GamaLog.DevWarning("[GAMA][PLAY] Strict middleware catalog launch failed; attaching to current monitor experiment instead. " +
                                     "The Unity selection remains model=" + (modelPath ?? string.Empty) +
                                     " experiment=" + (experimentName ?? string.Empty) +
                                     ". Error: " + (result.Error ?? "unknown"));

                    result = GamaEditorMiddlewareOrchestrator.LaunchCurrentMonitorExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            GamaLog.Dev)
                        .GetAwaiter()
                        .GetResult();
                }

                if (result != null && result.Success)
                {
                    if (hasTarget)
                    {
                        EditorPrefs.SetString(PlayModelPathPrefKey, modelPath);
                        EditorPrefs.SetString(PlayExperimentPrefKey, experimentName);
                    }

                    GamaLog.Info("[GAMA] GAMA experiment ready before Play Mode.");
                    return;
                }

                string error = result != null && !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : "unknown reason";
                if (hasTarget)
                {
                    GamaLog.Warning("[GAMA][PLAY] GAMA auto-launch failed before Play: " + error);
                }
                else
                {
                    GamaLog.Dev("[GAMA][PLAY] No Unity .gaml target for auto-launch; continuing Play attached to current GAMA/middleware state. " + error);
                }
            }
        }
        catch (Exception ex)
        {
            GamaLog.Warning("[GAMA][PLAY] GAMA auto-launch exception before Play: " + ex.Message);
        }
    }

    private static bool ShouldAttachToCurrentMonitorFallback(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.IndexOf("catalogue middleware", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("introuvable", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("Aucun match", StringComparison.OrdinalIgnoreCase) >= 0 ||
               error.IndexOf("absent du catalogue", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CleanupEditorPreviewPlayersBeforePlay(
        string host,
        string playerPort,
        int monitorPort,
        CancellationToken ct)
    {
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (sessions == null || sessions.Length == 0)
        {
            return;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < sessions.Length; i++)
        {
            string id = sessions[i] != null ? sessions[i].playerId : string.Empty;
            if (IsEditorPreviewPlayerId(id))
            {
                ids.Add(id.Trim());
            }
        }

        foreach (string id in ids)
        {
            try
            {
                GamaLog.Dev("[GAMA][PLAY] Cleaning preview player before Play: " + id);
                string outcome = GamaEditorFirstTickCapture.PurgeGhostPlayerAsync(
                        host,
                        playerPort,
                        id,
                        4000,
                        ct)
                    .GetAwaiter()
                    .GetResult();
                GamaLog.Dev("[GAMA][PLAY] " + outcome);
            }
            catch (Exception ex)
            {
                GamaLog.DevWarning("[GAMA][PLAY] Preview player websocket cleanup failed for " + id + ": " + ex.Message);
            }

            try
            {
                bool removed = GamaEditorMiddlewareOrchestrator.RemovePlayerHeadsetAsync(
                        host,
                        monitorPort,
                        id,
                        ct,
                            GamaLog.Dev)
                    .GetAwaiter()
                    .GetResult();
                GamaLog.Dev("[GAMA][PLAY] Preview player monitor cleanup " + id + " removed=" + removed);
            }
            catch (Exception ex)
            {
                GamaLog.DevWarning("[GAMA][PLAY] Preview player monitor cleanup failed for " + id + ": " + ex.Message);
            }
        }
    }

    private static bool IsEditorPreviewPlayerId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Trim().StartsWith("editor_capture", StringComparison.OrdinalIgnoreCase);
    }
}
