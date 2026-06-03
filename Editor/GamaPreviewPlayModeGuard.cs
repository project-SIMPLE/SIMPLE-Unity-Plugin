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
    private const string GamaCaptureHostPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureHost";
    private const string GamaCapturePortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCapturePort";
    private const string GamaCaptureMonitorPortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureMonitorPort";
    private const string PlayModelPathPrefKey = "ProjectSimple.GamaUnity.Play.ModelPath";
    private const string PlayExperimentPrefKey = "ProjectSimple.GamaUnity.Play.Experiment";
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";

    static GamaPreviewPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            StaticInformation.ResetSessionId("unity_play");
            Debug.Log("[GAMA][PLAY] New Unity Play player id: " + StaticInformation.getId());

            GameObject root = FindPreviewRoot();
            if (root != null)
            {
                bool wasActive = root.activeSelf;
                SessionState.SetBool(SessionStateKey, wasActive);

                bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                if (autoHide && wasActive)
                {
                    root.SetActive(false);
                    Debug.Log("[GAMA][PREVIEW][PLAY] Static preview hidden before Play mode.");
                }
            }

            TryPrepareGamaForPlay();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (SessionState.GetBool(SessionStateKey, false))
            {
                GameObject root = FindPreviewRoot();
                if (root != null)
                {
                    bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                    if (autoHide && !root.activeSelf)
                    {
                        root.SetActive(true);
                        Debug.Log("[GAMA][PREVIEW][PLAY] Static preview restored after Play mode.");
                    }
                }
            }
            SessionState.EraseBool(SessionStateKey);
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

    private static void TryPrepareGamaForPlay()
    {
        if (!EditorPrefs.GetBool(AutoLaunchGamaOnPlayPrefKey, true))
        {
            Debug.Log("[GAMA][PLAY] Auto-launch disabled; Play will only connect to the existing middleware state.");
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

        Debug.Log("[GAMA][PLAY] Preparing GAMA before Play. middleware=ws://" + host + ":" + playerPort +
                  "/ monitor=ws://" + host + ":" + monitorPort + "/ targetSource=" + source +
                  " model=" + (modelPath ?? string.Empty) +
                  " experiment=" + (experimentName ?? string.Empty));

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
                            Debug.Log)
                        .GetAwaiter()
                        .GetResult()
                    : GamaEditorMiddlewareOrchestrator.LaunchCurrentMonitorExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            Debug.Log)
                        .GetAwaiter()
                        .GetResult();

                if (hasTarget && result != null && !result.Success && ShouldAttachToCurrentMonitorFallback(result.Error))
                {
                    Debug.LogWarning("[GAMA][PLAY] Strict middleware catalog launch failed; attaching to current monitor experiment instead. " +
                                     "The Unity selection remains model=" + (modelPath ?? string.Empty) +
                                     " experiment=" + (experimentName ?? string.Empty) +
                                     ". Error: " + (result.Error ?? "unknown"));

                    result = GamaEditorMiddlewareOrchestrator.LaunchCurrentMonitorExperimentAsync(
                            host,
                            monitorPort,
                            cts.Token,
                            Debug.Log)
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

                    Debug.Log("[GAMA][PLAY] GAMA ready before Play: state=" +
                              (result.FinalExperimentState ?? string.Empty) +
                              (string.IsNullOrEmpty(result.ExperimentId) ? string.Empty : " exp_id=" + result.ExperimentId));
                    return;
                }

                string error = result != null && !string.IsNullOrWhiteSpace(result.Error)
                    ? result.Error
                    : "unknown reason";
                if (hasTarget)
                {
                    Debug.LogWarning("[GAMA][PLAY] GAMA auto-launch failed before Play: " + error);
                }
                else
                {
                    Debug.Log("[GAMA][PLAY] No Unity .gaml target for auto-launch; continuing Play attached to current GAMA/middleware state. " + error);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA][PLAY] GAMA auto-launch exception before Play: " + ex.Message);
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
                Debug.Log("[GAMA][PLAY] Cleaning preview player before Play: " + id);
                string outcome = GamaEditorFirstTickCapture.PurgeGhostPlayerAsync(
                        host,
                        playerPort,
                        id,
                        4000,
                        ct)
                    .GetAwaiter()
                    .GetResult();
                Debug.Log("[GAMA][PLAY] " + outcome);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GAMA][PLAY] Preview player websocket cleanup failed for " + id + ": " + ex.Message);
            }

            try
            {
                bool removed = GamaEditorMiddlewareOrchestrator.RemovePlayerHeadsetAsync(
                        host,
                        monitorPort,
                        id,
                        ct,
                        Debug.Log)
                    .GetAwaiter()
                    .GetResult();
                Debug.Log("[GAMA][PLAY] Preview player monitor cleanup " + id + " removed=" + removed);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GAMA][PLAY] Preview player monitor cleanup failed for " + id + ": " + ex.Message);
            }
        }
    }

    private static bool IsEditorPreviewPlayerId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               id.Trim().StartsWith("editor_capture", StringComparison.OrdinalIgnoreCase);
    }
}
