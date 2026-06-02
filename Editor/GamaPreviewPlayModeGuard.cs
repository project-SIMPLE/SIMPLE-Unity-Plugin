using UnityEditor;
using UnityEngine;
using System;
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
            GameObject root = FindPreviewRoot();
            if (root != null)
            {
                bool wasActive = root.activeSelf;
                SessionState.SetBool(SessionStateKey, wasActive);

                bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                if (autoHide && wasActive)
                {
                    Debug.Log("[GAMA][PREVIEW][PLAY] Static preview kept visible until live runtime data is received.");
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
}
