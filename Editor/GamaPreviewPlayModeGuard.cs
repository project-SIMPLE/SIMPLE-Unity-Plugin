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
    private const string ValidateActiveGamaOnPlayPrefKey = "ProjectSimple.GamaUnity.Play.AutoLaunchMonitor";
    private const string PauseGamaOnPlayExitPrefKey = "ProjectSimple.GamaUnity.Play.PauseOnExit";
    private const string GamaCaptureHostPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureHost";
    private const string GamaCapturePortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCapturePort";
    private const string GamaCaptureMonitorPortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureMonitorPort";
    private const string PlaySessionIdPrefKey = "ProjectSimple.GamaUnity.Play.PlayerId";
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";
    private const string CorrespondingPreviewStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.CorrespondingPreviewBeforePlay";
    private const string PlayExitHandledStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.PlayExitHandled";
    private const string PlayUsesMonitorSelectionStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.PlayUsesMonitorSelection";
    private const string CurrentPlayModelStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.CurrentPlayModel";
    private const string CurrentPlayExperimentStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.CurrentPlayExperiment";
    private const string CurrentPlayMonitorIdStateKey =
        "ProjectSimple.GamaUnity.PreviewSafety.CurrentPlayMonitorId";
    private const string PreparedSpeciesOverridesAssetPathStateKey =
        "ProjectSimple.GamaUnity.Play.PreparedSpeciesOverridesAssetPath";
    private const string PreparedSpeciesOverridesModelPathStateKey =
        "ProjectSimple.GamaUnity.Play.PreparedSpeciesOverridesModelPath";
    private const string PreparedSpeciesOverridesExperimentStateKey =
        "ProjectSimple.GamaUnity.Play.PreparedSpeciesOverridesExperiment";
    private const double PreparedSpeciesOverrideContextRetryTimeoutSeconds = 5.0d;

    private static double preparedSpeciesOverrideContextRetryDeadline;

    private enum PreparedSpeciesOverrideContextAssignmentResult
    {
        Assigned,
        MissingSessionContext,
        MissingAsset,
        MissingManager
    }

    static GamaPreviewPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.pauseStateChanged += OnPauseStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            CancelPreparedSpeciesOverrideContextRetry();
            ClearPreparedSpeciesOverrideContext();
            GamaEditorPlayExitPreviewCapture.ClearPendingSnapshot();
            GamaEditorPlayRuntimeRecorder.BeginPlaySession();
            ClearPlayPreviewTransitionState();

            ClearPreviewReuseAuthorization();
            GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();
            EnsureStableUnityPlayId();

            if (StaticInformation.TryGetCurrentId(out string previousPlayerId))
            {
                TryRemoveRuntimePlayerFromUnity("Before new Unity Play", previousPlayerId);
            }

            GamaLog.Dev("[GAMA][PLAY] Unity Play player id: " + StaticInformation.getId());
            PrepareSpeciesOverrideContextForPlay();

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

            PlayPreparationResult preparation = TryAttachToCurrentGamaForPlay();
            AuthorizePreviewReuse(preparation);
            RecordCurrentPlayPreviewContext(preparation);
        }
        else if (state == PlayModeStateChange.EnteredPlayMode)
        {
            PreparedSpeciesOverrideContextAssignmentResult assignmentResult =
                TryAssignPreparedSpeciesOverrideContextFromSession();
            if (assignmentResult == PreparedSpeciesOverrideContextAssignmentResult.MissingManager)
            {
                SchedulePreparedSpeciesOverrideContextRetry();
            }
            else if (assignmentResult != PreparedSpeciesOverrideContextAssignmentResult.Assigned)
            {
                LogPreparedSpeciesOverrideContextAssignmentFailure(assignmentResult);
            }
        }
        else if (state == PlayModeStateChange.ExitingPlayMode)
        {
            StopPausedSocketPump();
            StopResumeReconnectWatch();
            CancelPreparedSpeciesOverrideContextRetry();
            CaptureEditablePreviewBeforePlayModeExit();
            string runtimePlayerId = StaticInformation.getId();
            PrepareSimulationManagersForEditorPlayExit();
            RestorePersistedAppearanceBeforeLeavingPlay();

            TryPauseGamaFromUnity("Unity Play stopped", 2);
            TryDisconnectRuntimePlayer("Unity Play stopped");
            TryRemoveRuntimePlayerFromUnity("Unity Play stopped", runtimePlayerId);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            CancelPreparedSpeciesOverrideContextRetry();
            GamaEditorPlayRuntimeRecorder.EndPlaySession();
            ClearPreviewReuseAuthorization();
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
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
            GamaEditorPlayExitPreviewCapture.ScheduleRestoreAfterPlayModeExit();
            ClearPlayPreviewTransitionState();
            ClearPreparedSpeciesOverrideContext();
        }
    }

    private static void RecordCurrentPlayPreviewContext(PlayPreparationResult preparation)
    {
        bool usesMonitorSelection = !preparation.Success ||
                                    !preparation.HasStrictTarget ||
                                    preparation.UsedMonitorFallback;
        SessionState.SetBool(PlayUsesMonitorSelectionStateKey, usesMonitorSelection);
        SessionState.SetString(
            CurrentPlayModelStateKey,
            usesMonitorSelection ? string.Empty : preparation.ModelPath);
        SessionState.SetString(
            CurrentPlayExperimentStateKey,
            usesMonitorSelection ? string.Empty : preparation.ExperimentName);
        SessionState.SetString(CurrentPlayMonitorIdStateKey, preparation.ExperimentId);

        bool correspondingPreview = false;
        if (GamaEditorPreviewSafety.TryFindEditablePreview(out _, out GamaPreviewSession session) &&
            session != null)
        {
            correspondingPreview = session.reuseAuthorizedForPlay;
        }

        SessionState.SetBool(CorrespondingPreviewStateKey, correspondingPreview);
    }

    private static void CaptureEditablePreviewBeforePlayModeExit()
    {
        if (SessionState.GetBool(PlayExitHandledStateKey, false))
        {
            return;
        }
        SessionState.SetBool(PlayExitHandledStateKey, true);

        if (!GamaEditorPlayRuntimeRecorder.TryGetSnapshot(
                out GamaEditorPlayRuntimeSnapshot runtimeSnapshot))
        {
            return;
        }

        bool correspondingPreview = SessionState.GetBool(CorrespondingPreviewStateKey, false);
        if (!GamaEditorPreviewSafety.TryApprovePlayExitSave(
                true,
                correspondingPreview))
        {
            return;
        }

        bool activeGamaSelection = SessionState.GetBool(
            PlayUsesMonitorSelectionStateKey,
            true);
        string modelPath = SessionState.GetString(CurrentPlayModelStateKey, string.Empty);
        string experimentName = SessionState.GetString(CurrentPlayExperimentStateKey, string.Empty);
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(experimentName))
        {
            activeGamaSelection = true;
            modelPath = string.Empty;
            experimentName = string.Empty;
        }

        int monitorPort = PlayerPrefs.GetInt(
            "MONITOR_PORT",
            GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);
        string middlewarePortText = PlayerPrefs.GetString("PORT", "8080");
        int middlewarePort = int.TryParse(middlewarePortText, out int parsedPort)
            ? parsedPort
            : 8080;
        string monitorExperimentId = SessionState.GetString(
            CurrentPlayMonitorIdStateKey,
            PlayerPrefs.GetString("GAMA_EXPERIMENT_ID", string.Empty));

        GamaEditorPlayPreviewIdentity identity = new GamaEditorPlayPreviewIdentity(
            activeGamaSelection,
            modelPath,
            experimentName,
            monitorExperimentId,
            StaticInformation.getId(),
            monitorPort,
            middlewarePort);
        if (!GamaEditorPlayExitPreviewCapture.TryStorePendingSnapshot(
                runtimeSnapshot,
                identity,
                out string captureError))
        {
            GamaLog.Warning("[GAMA][PREVIEW] " + captureError);
        }
    }

    private static void ClearPlayPreviewTransitionState()
    {
        SessionState.EraseBool(CorrespondingPreviewStateKey);
        SessionState.EraseBool(PlayExitHandledStateKey);
        SessionState.EraseBool(PlayUsesMonitorSelectionStateKey);
        SessionState.EraseString(CurrentPlayModelStateKey);
        SessionState.EraseString(CurrentPlayExperimentStateKey);
        SessionState.EraseString(CurrentPlayMonitorIdStateKey);
    }

    private static bool pausedSocketPumpRegistered;

    private static void OnPauseStateChanged(PauseState state)
    {
        if (state == PauseState.Paused && EditorApplication.isPlaying)
        {
            StartPausedSocketPump();
            TryPauseGamaFromUnity("Unity Play paused");
        }
        else if (state == PauseState.Unpaused && EditorApplication.isPlaying)
        {
            TryResumeGamaFromUnity("Unity Play resumed");
            StopPausedSocketPump();
            StartResumeReconnectWatch();
        }
    }

    private static void StartPausedSocketPump()
    {
        if (pausedSocketPumpRegistered)
        {
            return;
        }

        EditorApplication.update -= PumpPausedSocketMessages;
        EditorApplication.update += PumpPausedSocketMessages;
        pausedSocketPumpRegistered = true;

        // Process anything already waiting in the queue immediately.
        PumpPausedSocketMessages();

        GamaLog.Dev("[GAMA][PLAY] Runtime WebSocket pump kept alive during Unity Pause.");
    }

    private static void StopPausedSocketPump()
    {
        EditorApplication.update -= PumpPausedSocketMessages;
        pausedSocketPumpRegistered = false;
    }

    private static void PumpPausedSocketMessages()
    {
        if (!EditorApplication.isPlaying || !EditorApplication.isPaused)
        {
            return;
        }

        ConnectionManager manager = ConnectionManager.Instance;
        if (manager != null)
        {
            manager.PumpSocketMessages();
        }
    }

    private static bool resumeReconnectWatchRegistered;
    private static double resumeReconnectWatchDeadline;
    private static double nextResumeReconnectAttemptTime;
    private static int resumeReconnectAttempts;

    private static void StartResumeReconnectWatch()
    {
        StopResumeReconnectWatch();

        resumeReconnectAttempts = 0;
        nextResumeReconnectAttemptTime =
            EditorApplication.timeSinceStartup + 0.25d;
        resumeReconnectWatchDeadline =
            EditorApplication.timeSinceStartup + 15.0d;

        EditorApplication.update += PumpResumeReconnect;
        resumeReconnectWatchRegistered = true;

        GamaLog.Dev(
            "[GAMA][PLAY] Watching runtime websocket after Unity Resume.");
    }

    private static void StopResumeReconnectWatch()
    {
        EditorApplication.update -= PumpResumeReconnect;
        resumeReconnectWatchRegistered = false;
        resumeReconnectWatchDeadline = 0d;
        nextResumeReconnectAttemptTime = 0d;
        resumeReconnectAttempts = 0;
    }

    private static void PumpResumeReconnect()
    {
        if (!EditorApplication.isPlaying)
        {
            StopResumeReconnectWatch();
            return;
        }

        if (EditorApplication.isPaused)
        {
            return;
        }

        ConnectionManager manager = ConnectionManager.Instance;
        if (manager == null)
        {
            return;
        }

        // Flush queued close/open/state callbacks.
        manager.PumpSocketMessages();

        if (manager.IsSocketOpen &&
            manager.IsConnectionState(ConnectionState.AUTHENTICATED))
        {
            GamaLog.Dev(
                "[GAMA][PLAY] Runtime websocket restored after Unity Resume.");

            StopResumeReconnectWatch();
            return;
        }

        double now = EditorApplication.timeSinceStartup;

        if (now >= resumeReconnectWatchDeadline)
        {
            GamaLog.Warning(
                "[GAMA][PLAY] Runtime websocket was not restored within 15 seconds after Unity Resume.");

            StopResumeReconnectWatch();
            return;
        }

        if (now < nextResumeReconnectAttemptTime)
        {
            return;
        }

        if (manager.IsConnectionState(ConnectionState.DISCONNECTED) &&
            !manager.IsSocketOpen)
        {
            resumeReconnectAttempts++;
            nextResumeReconnectAttemptTime = now + 1.0d;

            GamaLog.Dev(
                "[GAMA][PLAY] Runtime websocket reconnect after Resume, attempt " +
                resumeReconnectAttempts + ".");

            manager.Reconnect();
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

    private static void PrepareSpeciesOverrideContextForPlay()
    {
        if (!TryResolveSpeciesOverrideContextForPlay(
                GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset,
                out GamaSpeciesAppearanceContext context))
        {
            GamaLog.Warning(
                "[GAMA][RUNTIME][OVERRIDE] Could not prepare the shared species overrides asset before Play Mode.");
            return;
        }

        GamaSpeciesAppearanceEditorCoordinator.SetActiveContext(context);

        string assetPath = AssetDatabase.GetAssetPath(context.Asset);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            GamaLog.Warning(
                "[GAMA][RUNTIME][OVERRIDE] The active species overrides asset is not persistent and cannot be restored after a Domain Reload.");
            return;
        }

        SessionState.SetString(PreparedSpeciesOverridesAssetPathStateKey, assetPath);
        SessionState.SetString(PreparedSpeciesOverridesModelPathStateKey, context.ModelPath);
        SessionState.SetString(PreparedSpeciesOverridesExperimentStateKey, context.ExperimentName);
        GamaLog.Dev(
            "[GAMA][RUNTIME][OVERRIDE] Prepared Play Mode species context asset=" + assetPath +
            " model=" + context.ModelPath +
            " experiment=" + context.ExperimentName);
    }

    internal static bool TryResolveSpeciesOverrideContextForPlay(
        Func<GamaSpeciesRenderOverrides> fallbackAssetResolver,
        out GamaSpeciesAppearanceContext context)
    {
        if (GamaSpeciesAppearanceEditorCoordinator.TryResolveActiveContext(out context))
        {
            return true;
        }

        GamaSpeciesRenderOverrides fallbackAsset = fallbackAssetResolver != null
            ? fallbackAssetResolver()
            : null;
        context = new GamaSpeciesAppearanceContext(
            fallbackAsset,
            string.Empty,
            string.Empty);
        return context.IsValid;
    }

    internal static bool TryAssignPreparedSpeciesOverrideContext(
        GamaSpeciesRenderOverrides asset,
        string modelPath,
        string experimentName)
    {
        if (asset == null)
        {
            return false;
        }

        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        bool managerAvailable = false;
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null)
            {
                managerAvailable = true;
                break;
            }
        }

        if (!managerAvailable)
        {
            return false;
        }

        GamaSpeciesAppearanceEditorCoordinator.SetActiveContext(
            new GamaSpeciesAppearanceContext(asset, modelPath, experimentName));
        GamaRuntimePreviewOverrideApplier.RefreshNow();
        return true;
    }

    private static PreparedSpeciesOverrideContextAssignmentResult
        TryAssignPreparedSpeciesOverrideContextFromSession()
    {
        string assetPath = SessionState.GetString(
            PreparedSpeciesOverridesAssetPathStateKey,
            string.Empty);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return PreparedSpeciesOverrideContextAssignmentResult.MissingSessionContext;
        }

        GamaSpeciesRenderOverrides asset =
            AssetDatabase.LoadAssetAtPath<GamaSpeciesRenderOverrides>(assetPath);
        if (asset == null)
        {
            return PreparedSpeciesOverrideContextAssignmentResult.MissingAsset;
        }

        string modelPath = SessionState.GetString(
            PreparedSpeciesOverridesModelPathStateKey,
            string.Empty);
        string experimentName = SessionState.GetString(
            PreparedSpeciesOverridesExperimentStateKey,
            string.Empty);
        bool assigned = TryAssignPreparedSpeciesOverrideContext(
            asset,
            modelPath,
            experimentName);
        if (assigned)
        {
            GamaLog.Dev(
                "[GAMA][RUNTIME][OVERRIDE] Assigned the prepared species context to the Play Mode SimulationManager.");
            return PreparedSpeciesOverrideContextAssignmentResult.Assigned;
        }

        return PreparedSpeciesOverrideContextAssignmentResult.MissingManager;
    }

    private static void SchedulePreparedSpeciesOverrideContextRetry()
    {
        CancelPreparedSpeciesOverrideContextRetry();
        preparedSpeciesOverrideContextRetryDeadline =
            EditorApplication.timeSinceStartup + PreparedSpeciesOverrideContextRetryTimeoutSeconds;
        EditorApplication.update += RetryPreparedSpeciesOverrideContextAssignment;
    }

    private static void RetryPreparedSpeciesOverrideContextAssignment()
    {
        if (!EditorApplication.isPlaying)
        {
            CancelPreparedSpeciesOverrideContextRetry();
            return;
        }

        PreparedSpeciesOverrideContextAssignmentResult assignmentResult =
            TryAssignPreparedSpeciesOverrideContextFromSession();
        if (assignmentResult == PreparedSpeciesOverrideContextAssignmentResult.Assigned)
        {
            CancelPreparedSpeciesOverrideContextRetry();
            return;
        }

        if (assignmentResult == PreparedSpeciesOverrideContextAssignmentResult.MissingManager &&
            EditorApplication.timeSinceStartup < preparedSpeciesOverrideContextRetryDeadline)
        {
            return;
        }

        CancelPreparedSpeciesOverrideContextRetry();
        LogPreparedSpeciesOverrideContextAssignmentFailure(assignmentResult);
    }

    private static void CancelPreparedSpeciesOverrideContextRetry()
    {
        EditorApplication.update -= RetryPreparedSpeciesOverrideContextAssignment;
        preparedSpeciesOverrideContextRetryDeadline = 0d;
    }

    private static void LogPreparedSpeciesOverrideContextAssignmentFailure(
        PreparedSpeciesOverrideContextAssignmentResult assignmentResult)
    {
        switch (assignmentResult)
        {
            case PreparedSpeciesOverrideContextAssignmentResult.MissingSessionContext:
                GamaLog.Warning(
                    "[GAMA][RUNTIME][OVERRIDE] No prepared species context was available when Play Mode started.");
                break;
            case PreparedSpeciesOverrideContextAssignmentResult.MissingAsset:
                string assetPath = SessionState.GetString(
                    PreparedSpeciesOverridesAssetPathStateKey,
                    string.Empty);
                GamaLog.Warning(
                    "[GAMA][RUNTIME][OVERRIDE] Could not load the prepared species overrides asset at " +
                    assetPath + ".");
                break;
            case PreparedSpeciesOverrideContextAssignmentResult.MissingManager:
                GamaLog.Warning(
                    "[GAMA][RUNTIME][OVERRIDE] Could not assign the prepared species context because no Play Mode SimulationManager became available within " +
                    PreparedSpeciesOverrideContextRetryTimeoutSeconds + " seconds.");
                break;
        }
    }

    private static void ClearPreparedSpeciesOverrideContext()
    {
        SessionState.EraseString(PreparedSpeciesOverridesAssetPathStateKey);
        SessionState.EraseString(PreparedSpeciesOverridesModelPathStateKey);
        SessionState.EraseString(PreparedSpeciesOverridesExperimentStateKey);
    }

    private static void RestorePersistedAppearanceBeforeLeavingPlay()
    {
        GamaSpeciesAppearanceContext context = GamaSpeciesAppearanceStateStore.ActiveContext;
        IReadOnlyList<GamaSpeciesRenderOverrideEntry> overlayEntries =
            GamaSpeciesAppearanceStateStore.GetRuntimeOverlayEntries(context);
        List<string> speciesNames = new List<string>();
        for (int i = 0; i < overlayEntries.Count; i++)
        {
            string speciesName = overlayEntries[i] != null
                ? overlayEntries[i].GetSpeciesName()
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(speciesName) && !speciesNames.Contains(speciesName))
            {
                speciesNames.Add(speciesName);
            }
        }

        GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();
        if (speciesNames.Count > 0)
        {
            GamaRuntimePreviewOverrideApplier.RefreshNow();
            SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] == null)
                {
                    continue;
                }
                for (int speciesIndex = 0; speciesIndex < speciesNames.Count; speciesIndex++)
                {
                    managers[i].ApplyRuntimeSpeciesOverrideNow(speciesNames[speciesIndex]);
                }
            }
        }

        GamaRuntimeRendererAppearanceBaseline[] baselines =
            UnityEngine.Object.FindObjectsByType<GamaRuntimeRendererAppearanceBaseline>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        for (int i = 0; i < baselines.Length; i++)
        {
            if (baselines[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(baselines[i]);
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

    private readonly struct PlayPreparationResult
    {
        public readonly bool Success;
        public readonly bool HasStrictTarget;
        public readonly bool UsedMonitorFallback;
        public readonly string ModelPath;
        public readonly string ExperimentName;
        public readonly string ExperimentId;

        public PlayPreparationResult(
            bool success,
            bool hasStrictTarget,
            bool usedMonitorFallback,
            string modelPath,
            string experimentName,
            string experimentId)
        {
            Success = success;
            HasStrictTarget = hasStrictTarget;
            UsedMonitorFallback = usedMonitorFallback;
            ModelPath = modelPath ?? string.Empty;
            ExperimentName = experimentName ?? string.Empty;
            ExperimentId = experimentId ?? string.Empty;
        }
    }

    private static PlayPreparationResult TryAttachToCurrentGamaForPlay()
    {
        if (!EditorPrefs.GetBool(ValidateActiveGamaOnPlayPrefKey, true))
        {
            GamaLog.Dev("[GAMA][PLAY] Active-experiment validation disabled; the runtime will connect without an Editor monitor check.");
            return default;
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

        PlayerPrefs.SetInt("MONITOR_PORT", monitorPort);
        // Play Mode never reuses a model path or experiment name remembered by an
        // earlier preview/workspace action. The monitor's active experiment is the
        // sole source of truth for this automatic path.
        PlayerPrefs.SetString("GAMA_MODEL_PATH", string.Empty);
        PlayerPrefs.SetString("GAMA_EXPERIMENT_NAME", string.Empty);
        PlayerPrefs.SetString("GAMA_EXPERIMENT_STATE", string.Empty);
        PlayerPrefs.SetString("GAMA_EXPERIMENT_ID", string.Empty);
        PlayerPrefs.Save();

        GamaLog.Info("[GAMA] Checking the experiment already active in GAMA before Play Mode.");

        try
        {
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                CleanupEditorPreviewPlayersBeforePlay(host, playerPort, monitorPort, cts.Token);
                GamaEditorMiddlewareOrchestrator.ManagedExperimentResult result =
                    GamaEditorMiddlewareOrchestrator.AttachToCurrentMonitorExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            GamaLog.Dev)
                        .GetAwaiter()
                        .GetResult();

                if (result != null && result.Success)
                {
                    PlayerPrefs.SetString("GAMA_EXPERIMENT_STATE", result.FinalExperimentState ?? string.Empty);
                    PlayerPrefs.SetString("GAMA_EXPERIMENT_ID", result.ExperimentId ?? string.Empty);
                    PlayerPrefs.Save();

                    GamaLog.Info("[GAMA] Attached to the experiment already active in GAMA.");
                    return new PlayPreparationResult(
                        true,
                        false,
                        true,
                        string.Empty,
                        string.Empty,
                        result.ExperimentId);
                }

                string error = result != null && !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : "unknown reason";
                GamaLog.Warning(
                    "[GAMA][PLAY] No active experiment was attached. Unity did not select, launch, replace, or resume a GAMA experiment. " +
                    error);
            }
        }
        catch (Exception ex)
        {
            GamaLog.Warning(
                "[GAMA][PLAY] Active-experiment monitor check failed. No GAMA launch command was sent: " +
                ex.Message);
        }

        return default;
    }

    private static void AuthorizePreviewReuse(PlayPreparationResult preparation)
    {
        if (!preparation.Success)
        {
            return;
        }

        if (EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
        {
            GamaLog.DevWarning(
                "[GAMA][PREVIEW][REUSE] Preview GameObject reuse is disabled because Scene Reload is disabled. " +
                "This protects the Edit Mode objects from runtime mutations.");
            return;
        }

        GamaPreviewSession session = FindCurrentPreviewSession();
        if (session == null || session.stale || string.IsNullOrWhiteSpace(session.stableExperimentKey))
        {
            return;
        }

        string expectedKey = string.Empty;
        bool activeSelection = session.activeGamaSelection ||
                               string.Equals(
                                   session.modelPath,
                                   "GAMA_ACTIVE_SELECTION",
                                   StringComparison.OrdinalIgnoreCase);

        if (preparation.HasStrictTarget && !preparation.UsedMonitorFallback)
        {
            if (!GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                    preparation.ModelPath,
                    preparation.ExperimentName,
                    false,
                    preparation.ExperimentId,
                    out expectedKey))
            {
                return;
            }
        }
        else
        {
            if (!activeSelection ||
                string.IsNullOrWhiteSpace(session.monitorExperimentId) ||
                string.IsNullOrWhiteSpace(preparation.ExperimentId) ||
                !string.Equals(
                    session.monitorExperimentId.Trim(),
                    preparation.ExperimentId.Trim(),
                    StringComparison.Ordinal))
            {
                GamaLog.DevWarning(
                    "[GAMA][PREVIEW][REUSE] Active-monitor preview reuse was refused because the experiment id could not be matched exactly.");
                return;
            }

            expectedKey = session.stableExperimentKey;
        }

        if (!string.Equals(session.stableExperimentKey, expectedKey, StringComparison.Ordinal))
        {
            GamaLog.DevWarning(
                "[GAMA][PREVIEW][REUSE] Preview reuse was refused because the launched experiment does not exactly match the loaded preview.");
            return;
        }

        session.reuseAuthorizedForPlay = true;
        session.authorizedStableExperimentKey = expectedKey;
        session.authorizedMonitorExperimentId = preparation.ExperimentId;
        GamaLog.Dev("[GAMA][PREVIEW][REUSE] Existing preview GameObjects are eligible for this Play session.");
    }

    private static void ClearPreviewReuseAuthorization()
    {
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (session == null)
            {
                continue;
            }

            session.ClearRuntimeReuseAuthorization();
        }
    }

    private static void PrepareSimulationManagersForEditorPlayExit()
    {
        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null)
            {
                managers[i].PrepareForEditorPlayExit();
            }
        }
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
