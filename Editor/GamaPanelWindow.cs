using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class GamaPanelWindow : EditorWindow
{
    private const string PrefabSourcePrefKey = "ProjectSimple.GamaUnity.Panel.PrefabSource";
    private const string ExperimentPathPrefKey = "ProjectSimple.GamaUnity.Panel.ExperimentPath";
    private const string StaticPreviewPrecisionJsonPrefKey = "ProjectSimple.GamaUnity.Panel.StaticPreviewPrecisionJson";
    private const string StaticPreviewPropertiesJsonPrefKey = "ProjectSimple.GamaUnity.Panel.StaticPreviewPropertiesJson";
    private const string StaticPreviewWorldJsonPrefKey = "ProjectSimple.GamaUnity.Panel.StaticPreviewWorldJson";
    private const string StaticPreviewWorldTickPrefKey = "ProjectSimple.GamaUnity.Panel.StaticPreviewWorldTick";
    private const string CaptureMaxWorldFramesPrefKey = "ProjectSimple.GamaUnity.Panel.CaptureMaxWorldFrames";
    private const string CaptureWorldPhaseSecondsPrefKey = "ProjectSimple.GamaUnity.Panel.CaptureWorldPhaseSec";
    private const string CaptureDynamicSpeciesRegexPrefKey = "ProjectSimple.GamaUnity.Panel.CaptureDynamicRegex";
    private const string CaptureStopWhenDynamicPrefKey = "ProjectSimple.GamaUnity.Panel.CaptureStopDynamic";
    private const string CaptureStopWhenStablePrefKey = "ProjectSimple.GamaUnity.Panel.CaptureStopStable";
    private const string CaptureStableSecondsPrefKey = "ProjectSimple.GamaUnity.Panel.CaptureStableSec";
    private const string CapturePauseAfterPreviewPrefKey = "ProjectSimple.GamaUnity.Panel.CapturePauseAfterPreview";
    private const string SpeciesOverridesAssetPrefKey = "ProjectSimple.GamaUnity.Panel.SpeciesOverridesAsset";
    private const string GamaHeadlessBatPrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessBat";
    private const string GamaHeadlessWorkDirPrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessWorkDir";
    private const string GamaJsonExportOutDirPrefKey = "ProjectSimple.GamaUnity.Panel.GamaJsonExportOutDir";
    private const string GamaHeadlessBatchNamePrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessBatchName";
    private const string GamaHeadlessCustomCmdPrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessCustomCmd";
    private const string GamaHeadlessTimeoutSecPrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessTimeoutSec";
    private const string GamaHeadlessRunPreviewAfterPrefKey = "ProjectSimple.GamaUnity.Panel.GamaHeadlessRunPreviewAfter";
    private const string GamaCaptureHostPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureHost";
    private const string GamaCapturePortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCapturePort";
    private const string GamaCaptureIdPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureId";
    private const string GamaCaptureModePrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureMode";
    private const string GamaCaptureUseLocalMiddlewarePrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureUseLocalMw";
    private const string GamaCaptureSkipRemoteLoadPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureSkipLoad";
    private const string GamaCaptureManagedFromUnityPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureManaged";
    private const string GamaCaptureExternalMiddlewarePrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureExternalMw";
    private const string GamaCaptureMonitorPortPrefKey = "ProjectSimple.GamaUnity.Panel.GamaCaptureMonitorPort";
    private const string GamaMiddlewareScriptPrefKey = "ProjectSimple.GamaUnity.Panel.GamaMiddlewareScript";
    private const string SelectedCodeExampleIndexPrefKey = "ProjectSimple.GamaUnity.Panel.SelectedCodeExampleIndex";
    private const string WorkspaceTabAdvancedPrefKey = "ProjectSimple.GamaUnity.Panel.WorkspaceTabAdvancedExpanded";
    private const string AutoHidePreviewOnPlayPrefKey = "ProjectSimple.GamaUnity.Panel.AutoHidePreviewOnPlay";
    private const string ApplyPreviewSettingsToPlayPrefKey = "ProjectSimple.GamaUnity.Panel.ApplyPreviewSettingsToPlay";
    private const string AutoUpdatePreviewPrefKey = "ProjectSimple.GamaUnity.Panel.AutoUpdatePreview";
    private const string GeneratedRulePrefix = "[Workspace Import]";
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";

    private readonly string[] tabs =
    {
        "Setup Scene",
        "Workspace Explorer",
        "Import Prefabs",
        "Import Experiment"
    };

    private const int TabSetupScene = 0;
    private const int TabWorkspace = 1;
    private const int TabImportPrefabs = 2;
    private const int TabImportExperiment = 3;

    private int selectedTab;
    private readonly GamaWorkspaceExplorerPanel workspaceExplorerPanel = new GamaWorkspaceExplorerPanel();
    private bool workspaceExplorerHostReady;
    private bool workspaceTabAdvancedExpanded;
    private string pendingCaptureAbortUserMessage;
    private string ghostPlayerIdToPurge = string.Empty;
    private string prefabSourceFolder = string.Empty;
    private string experimentPath = string.Empty;
    private string staticPreviewPrecisionJsonPath = string.Empty;
    private string staticPreviewPropertiesJsonPath = string.Empty;
    private string staticPreviewWorldJsonPath = string.Empty;
    private int staticPreviewWorldTickIndex;
    private int captureMaxWorldFrames = 50;
    private float captureWorldPhaseSeconds = 25f;
    private string captureDynamicSpeciesRegex = GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex;
    private bool captureStopWhenDynamicAgentsFound = true;
    private bool captureStopWhenPreviewCacheStable = true;
    private float capturePreviewStableSeconds = 5f;
    private bool capturePauseExperimentAfterPreview = true;
    private bool autoHidePreviewOnPlay = true;
    private bool applyPreviewSettingsToPlay = true;
    private bool autoUpdatePreview = true;
    private GamaSpeciesRenderOverrides speciesRenderOverridesAsset;
    private readonly List<string> availableWorldTickPaths = new List<string>();
    private string gamaHeadlessBatPath = string.Empty;
    private string gamaHeadlessWorkingDir = string.Empty;
    private string gamaJsonExportOutputDir = string.Empty;
    private string gamaHeadlessBatchName = string.Empty;
    private string gamaHeadlessCustomCmd = string.Empty;
    private int gamaHeadlessTimeoutSeconds = 300;
    private bool gamaHeadlessRunPreviewAfter = true;
    private bool headlessExportSectionExpanded = false;
    private string captureHost = "localhost";
    private string capturePort = "8080";
    private string captureConnectionId = string.Empty;
    private bool captureUseLocalMiddleware;
    private bool captureSkipRemoteLoad;
    private bool captureManagedFromUnity;
    private bool captureUseExternalMiddleware = true;
    private bool autoLaunchGamaOnPlay = true;
    private int captureMonitorPort = GamaEditorMiddlewareOrchestrator.DefaultMonitorPort;
    private string captureMode = "batch";
    private string middlewareScriptPath = string.Empty;
    private string generatedLearningPackageRoot = string.Empty;
    private string lastGeneratedSettingsJsonPath = string.Empty;
    private string lastGeneratedSettingsJsonContent = string.Empty;
    private string lastMiddlewareWorkingDirectory = string.Empty;
    private int lastMiddlewareProcessId;
    private string lastMiddlewareLearningPackagePath = string.Empty;
    private string lastMiddlewareExtraLearningPackagePath = string.Empty;
    private bool middlewareSectionExpanded = false;
    private bool manualJsonSectionExpanded = false;
    private bool codeExampleAdvancedExpanded = false;

    private static int GamaPanelWindowCaptureSessionCounter;

    private System.Threading.Tasks.Task<GamaEditorFirstTickCapture.CaptureResult> captureTask;
    private System.Threading.Tasks.Task<GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult> catalogDiagnosisTask;
    private string catalogDiagnosisStatus = string.Empty;
    private bool captureFlowActive;
    private System.Threading.CancellationTokenSource captureCts;
    private GamaEditorBackgroundProcess captureGamaProcess;
    private GamaEditorBackgroundProcess captureMiddlewareProcess;
    private string captureRuntimeStatus = string.Empty;
    private double captureStartedAt;
    private string experimentStatus = "Enter a .gaml experiment file or a workspace folder, then click Explore.";
    private Vector2 experimentScroll;
    private Vector2 agentsScroll;
    private Vector2 workspaceScroll;
    private Vector2 workspaceExperimentListScroll;
    private Vector2 setupSceneScroll;
    private List<GamaCodeExampleSceneInfo> codeExampleScenes = new List<GamaCodeExampleSceneInfo>();
    private int selectedCodeExampleIndex;
    private string codeExampleStatus = "Code examples are generated by the package.";
    private List<GamaPanelExperimentOption> experimentOptions = new List<GamaPanelExperimentOption>();
    private int selectedExperimentIndex;
    private GamaPanelExperimentAnalysis analysis;
    private List<GamaPanelAgentOverride> agentOverrides = new List<GamaPanelAgentOverride>();

    private bool setupSceneBeforeApply = false;
    private bool replaceGeneratedRules = true;
    private bool enablePrefabRenderDistance = true;
    private float sceneCharacteristicSize = 100f;
    private float horizontalScale = 1f;
    private float verticalOffset = 0f;
    private float renderDistance = 1500f;
    private float cameraNearClip = 0.01f;
    private float cameraFarClip = 2000f;
    private int previewSamplesPerAgent = 6;
    private Color backgroundColor = Color.black;

    [MenuItem("GAMA/GAMA Panel")]
    public static void OpenWindow()
    {
        OpenWindow(0);
    }

    private static void OpenWindow(int tab)
    {
        GamaPanelWindow window = GetWindow<GamaPanelWindow>("GAMA Panel");
        window.minSize = new Vector2(760f, 480f);
        window.selectedTab = Mathf.Clamp(tab, TabSetupScene, TabImportExperiment);
        window.Show();
    }

    private void OnEnable()
    {
        prefabSourceFolder = EditorPrefs.GetString(PrefabSourcePrefKey, prefabSourceFolder);
        experimentPath = EditorPrefs.GetString(ExperimentPathPrefKey, experimentPath);
        staticPreviewPrecisionJsonPath = EditorPrefs.GetString(StaticPreviewPrecisionJsonPrefKey, staticPreviewPrecisionJsonPath);
        staticPreviewPropertiesJsonPath = EditorPrefs.GetString(StaticPreviewPropertiesJsonPrefKey, staticPreviewPropertiesJsonPath);
        staticPreviewWorldJsonPath = EditorPrefs.GetString(StaticPreviewWorldJsonPrefKey, staticPreviewWorldJsonPath);
        staticPreviewWorldTickIndex = EditorPrefs.GetInt(StaticPreviewWorldTickPrefKey, staticPreviewWorldTickIndex);
        captureMaxWorldFrames = EditorPrefs.GetInt(CaptureMaxWorldFramesPrefKey, captureMaxWorldFrames);
        captureWorldPhaseSeconds = EditorPrefs.GetFloat(CaptureWorldPhaseSecondsPrefKey, captureWorldPhaseSeconds);
        captureDynamicSpeciesRegex = EditorPrefs.GetString(
            CaptureDynamicSpeciesRegexPrefKey,
            captureDynamicSpeciesRegex);
        const string legacyDynamicSpeciesRegex =
            @"car|vehicle|voiture|traffic|vehicule|pedestrian|pieton|piéton|walker|person";
        if (string.Equals(captureDynamicSpeciesRegex?.Trim(), legacyDynamicSpeciesRegex, StringComparison.Ordinal))
        {
            captureDynamicSpeciesRegex = GamaEditorPreviewCapture.DefaultDynamicSpeciesRegex;
        }
        captureStopWhenDynamicAgentsFound = EditorPrefs.GetBool(
            CaptureStopWhenDynamicPrefKey,
            captureStopWhenDynamicAgentsFound);
        captureStopWhenPreviewCacheStable = EditorPrefs.GetBool(
            CaptureStopWhenStablePrefKey,
            captureStopWhenPreviewCacheStable);
        capturePreviewStableSeconds = EditorPrefs.GetFloat(
            CaptureStableSecondsPrefKey,
            capturePreviewStableSeconds);
        capturePauseExperimentAfterPreview = EditorPrefs.GetBool(
            CapturePauseAfterPreviewPrefKey,
            capturePauseExperimentAfterPreview);
        autoHidePreviewOnPlay = EditorPrefs.GetBool(
            AutoHidePreviewOnPlayPrefKey,
            autoHidePreviewOnPlay);
        applyPreviewSettingsToPlay = EditorPrefs.GetBool(
            ApplyPreviewSettingsToPlayPrefKey,
            applyPreviewSettingsToPlay);
        autoUpdatePreview = EditorPrefs.GetBool(
            AutoUpdatePreviewPrefKey,
            autoUpdatePreview);
        string overridesAssetPath = EditorPrefs.GetString(SpeciesOverridesAssetPrefKey, string.Empty);
        if (!string.IsNullOrEmpty(overridesAssetPath))
        {
            speciesRenderOverridesAsset = AssetDatabase.LoadAssetAtPath<GamaSpeciesRenderOverrides>(overridesAssetPath);
        }
        RefreshAvailableWorldTicks();
        gamaHeadlessBatPath = EditorPrefs.GetString(GamaHeadlessBatPrefKey, gamaHeadlessBatPath);
        gamaHeadlessWorkingDir = EditorPrefs.GetString(GamaHeadlessWorkDirPrefKey, gamaHeadlessWorkingDir);
        gamaJsonExportOutputDir = EditorPrefs.GetString(GamaJsonExportOutDirPrefKey, gamaJsonExportOutputDir);
        gamaHeadlessBatchName = EditorPrefs.GetString(GamaHeadlessBatchNamePrefKey, gamaHeadlessBatchName);
        gamaHeadlessCustomCmd = EditorPrefs.GetString(GamaHeadlessCustomCmdPrefKey, gamaHeadlessCustomCmd);
        gamaHeadlessTimeoutSeconds = Mathf.Clamp(EditorPrefs.GetInt(GamaHeadlessTimeoutSecPrefKey, gamaHeadlessTimeoutSeconds), 30, 7200);
        gamaHeadlessRunPreviewAfter = EditorPrefs.GetBool(GamaHeadlessRunPreviewAfterPrefKey, gamaHeadlessRunPreviewAfter);
        captureHost = EditorPrefs.GetString(GamaCaptureHostPrefKey, captureHost);
        capturePort = EditorPrefs.GetString(GamaCapturePortPrefKey, capturePort);
        captureConnectionId = EditorPrefs.GetString(GamaCaptureIdPrefKey, captureConnectionId);
        if (string.Equals(captureConnectionId?.Trim(), "Editor_Capture", StringComparison.OrdinalIgnoreCase))
        {
            captureConnectionId = string.Empty;
            EditorPrefs.SetString(GamaCaptureIdPrefKey, string.Empty);
        }
        captureManagedFromUnity = EditorPrefs.GetBool(GamaCaptureManagedFromUnityPrefKey, true);
        captureUseExternalMiddleware = EditorPrefs.GetBool(GamaCaptureExternalMiddlewarePrefKey, true);
        autoLaunchGamaOnPlay = EditorPrefs.GetBool("ProjectSimple.GamaUnity.Play.AutoLaunchMonitor", true);
        captureUseLocalMiddleware = EditorPrefs.GetBool(GamaCaptureUseLocalMiddlewarePrefKey, false);
        captureSkipRemoteLoad = EditorPrefs.GetBool(GamaCaptureSkipRemoteLoadPrefKey, false);
        captureMonitorPort = EditorPrefs.GetInt(GamaCaptureMonitorPortPrefKey, GamaEditorMiddlewareOrchestrator.DefaultMonitorPort);
        captureMode = EditorPrefs.GetString(GamaCaptureModePrefKey, captureMode);
        if (captureManagedFromUnity)
        {
            captureUseExternalMiddleware = true;
            captureUseLocalMiddleware = false;
            captureSkipRemoteLoad = false;
            if (string.IsNullOrWhiteSpace(capturePort) ||
                GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(capturePort))
            {
                capturePort = "8080";
            }

            EditorPrefs.SetBool(GamaCaptureUseLocalMiddlewarePrefKey, false);
            EditorPrefs.SetBool(GamaCaptureSkipRemoteLoadPrefKey, false);
            EditorPrefs.SetBool(GamaCaptureExternalMiddlewarePrefKey, true);
            EditorPrefs.SetString(GamaCapturePortPrefKey, capturePort);
        }
        else if (captureUseLocalMiddleware && string.Equals(capturePort?.Trim(), "8080", StringComparison.Ordinal))
        {
            capturePort = "1000";
        }
        else if (!captureUseLocalMiddleware && GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(capturePort))
        {
            capturePort = "8080";
        }
        middlewareScriptPath = EditorPrefs.GetString(GamaMiddlewareScriptPrefKey, middlewareScriptPath);
        selectedCodeExampleIndex = EditorPrefs.GetInt(SelectedCodeExampleIndexPrefKey, selectedCodeExampleIndex);
        workspaceTabAdvancedExpanded = EditorPrefs.GetBool(WorkspaceTabAdvancedPrefKey, false);
        RefreshCodeExampleScenes();
        
        GameObject previewRoot = GameObject.Find(StaticPreviewRootName);
        if (previewRoot != null)
        {
            UpdateAgentOverridesFromPreview(previewRoot);
        }
    }

    private void OnDisable()
    {
        AbortCaptureIfRunning("Window closed");
    }

    private void Update()
    {
        if (workspaceExplorerHostReady)
        {
            workspaceExplorerPanel.Tick(this);
        }

        if (captureTask != null)
        {
            if (captureTask.IsCompleted)
            {
                System.Threading.Tasks.Task<GamaEditorFirstTickCapture.CaptureResult> finished = captureTask;
                captureTask = null;
                OnCaptureFinished(finished);
            }
            else
            {
                double elapsed = EditorApplication.timeSinceStartup - captureStartedAt;
                captureRuntimeStatus = "Capture in progress... " + elapsed.ToString("0.0") + " s elapsed.";
                Repaint();
            }
        }

        if (catalogDiagnosisTask != null && catalogDiagnosisTask.IsCompleted)
        {
            System.Threading.Tasks.Task<GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult> finished = catalogDiagnosisTask;
            catalogDiagnosisTask = null;
            try
            {
                GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult diag = finished.Result;
                catalogDiagnosisStatus = BuildCatalogDiagnosisStatus(diag);
                captureRuntimeStatus = catalogDiagnosisStatus;
            }
            catch (Exception ex)
            {
                catalogDiagnosisStatus = "Catalog diagnosis failed: " + ex.Message;
                captureRuntimeStatus = catalogDiagnosisStatus;
            }

            Repaint();
        }
    }

    private void AbortCaptureIfRunning(string reason, bool purgePlayer = true)
    {
        if (captureTask == null && captureGamaProcess == null && captureMiddlewareProcess == null)
        {
            return;
        }

        pendingCaptureAbortUserMessage = reason;
        try { captureCts?.Cancel(); } catch { /* ignore */ }

        if (captureGamaProcess != null)
        {
            try { _ = captureGamaProcess.StopAsync(1500); } catch { /* ignore */ }
            try { captureGamaProcess.Dispose(); } catch { /* ignore */ }
            captureGamaProcess = null;
        }

        if (captureMiddlewareProcess != null)
        {
            try { _ = captureMiddlewareProcess.StopAsync(2500); } catch { /* ignore */ }
            try { captureMiddlewareProcess.Dispose(); } catch { /* ignore */ }
            captureMiddlewareProcess = null;
        }

        captureRuntimeStatus = "Cancelling... closing WebSocket gracefully (a few seconds).";
        if (captureTask == null)
        {
            try { captureCts?.Dispose(); } catch { /* ignore */ }
            captureCts = null;
            captureRuntimeStatus = "Capture cancelled: " + reason;
            pendingCaptureAbortUserMessage = null;
            captureFlowActive = false;
        }

        string idToFree = string.IsNullOrWhiteSpace(captureConnectionId) ? StaticInformation.getId() : captureConnectionId.Trim();
        if (purgePlayer && !string.IsNullOrWhiteSpace(idToFree))
        {
            _ = PurgeGhostPlayerInteractiveAsync(idToFree);
        }

        Repaint();
    }

    private void OnGUI()
    {
        selectedTab = GUILayout.Toolbar(selectedTab, tabs, GUILayout.Height(28f));
        EditorGUILayout.Space(8f);

        switch (selectedTab)
        {
            case TabSetupScene:
                DrawSetupSceneTab();
                break;
            case TabWorkspace:
                DrawWorkspaceTab();
                break;
            case TabImportPrefabs:
                DrawPrefabImportTab();
                break;
            default:
                DrawExperimentImportTab();
                break;
        }
    }

    private void EnsureWorkspaceExplorerHostReady()
    {
        if (workspaceExplorerHostReady)
        {
            return;
        }

        workspaceExplorerPanel.OnHostEnable();
        workspaceExplorerHostReady = true;
    }

    private void DrawWorkspaceTab()
    {
        workspaceScroll = EditorGUILayout.BeginScrollView(workspaceScroll);
        EnsureWorkspaceExplorerHostReady();
        workspaceExplorerPanel.DrawCompactWorkspaceUi();

        EditorGUILayout.Space(10f);
        EditorGUI.BeginChangeCheck();
        workspaceTabAdvancedExpanded = EditorGUILayout.Foldout(
            workspaceTabAdvancedExpanded,
            "Advanced Settings (scan: ports / auto-detection, .gaml exploration, prefabs, headless...)",
            true);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(WorkspaceTabAdvancedPrefKey, workspaceTabAdvancedExpanded);
        }

        if (workspaceTabAdvancedExpanded)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Offline Scan", EditorStyles.boldLabel);
            workspaceExplorerPanel.DrawAdvancedScannerOptions();
            EditorGUILayout.Space(10f);
            DrawWorkspaceConfigurationSection();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Explorer in Separate Window", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Opens the same scan view in a separate window.",
                MessageType.Info);
            if (GUILayout.Button("Open Explorer in a Window", GUILayout.Height(24f)))
            {
                GamaWorkspaceExplorerWindow.ShowDetachedWindow();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawPrefabImportTab()
    {
        EditorGUILayout.LabelField("Import Prefabs", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Copy an external prefab folder into Assets/Resources so GAMA prefab paths can be resolved by Unity.", MessageType.Info);

        EditorGUILayout.Space();
        DrawPrefabSourcePathRow();

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(prefabSourceFolder) || !Directory.Exists(prefabSourceFolder)))
        {
            if (GUILayout.Button("Import to Assets/Resources", GUILayout.Height(32f)))
            {
                ImportPrefabsToResources(prefabSourceFolder);
            }
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Open Legacy Prefab Importer", GUILayout.Width(220f)))
        {
            GAMAPrefabImporter.ShowWindow();
        }
    }

    private void DrawSetupSceneTab()
    {
        setupSceneScroll = EditorGUILayout.BeginScrollView(setupSceneScroll);
        DrawSetupSceneChooser();
        EditorGUILayout.EndScrollView();
    }

    private void DrawSetupSceneChooser()
    {
        EditorGUILayout.LabelField("Scene Configuration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Choose a template: classic minimal GAMA scene, VR version with XR device simulator, or a scene generated from the package examples (special cases documented in the code).",
            MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Setup (VR Simulator)", GUILayout.Height(36f)))
        {
            GAMAMenu.SetupScene();
        }

        if (GUILayout.Button("Setup (Headset Ready)", GUILayout.Height(36f)))
        {
            GAMAMenu.SetupSceneHeadsetReady();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Code Example Scenes", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Generates a scene file under Assets/Scenes/Code Examples from the package definitions. Useful for testing a specific flow (static data, interactions, multiplexer, etc.).",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh List", GUILayout.Width(150f)))
        {
            RefreshCodeExampleScenes();
        }

        GUILayout.Label(codeExampleStatus, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();

        if (codeExampleScenes.Count == 0)
        {
            EditorGUILayout.HelpBox("No example scenes are available in the package.", MessageType.Warning);
            return;
        }

        string[] labels = new string[codeExampleScenes.Count];
        for (int i = 0; i < codeExampleScenes.Count; i++)
        {
            labels[i] = codeExampleScenes[i].DisplayName;
        }

        EditorGUI.BeginChangeCheck();
        selectedCodeExampleIndex = Mathf.Clamp(selectedCodeExampleIndex, 0, codeExampleScenes.Count - 1);
        selectedCodeExampleIndex = EditorGUILayout.Popup("Example Scene", selectedCodeExampleIndex, labels);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetInt(SelectedCodeExampleIndexPrefKey, selectedCodeExampleIndex);
        }

        if (GUILayout.Button("Generate Selected Example Scene", GUILayout.Height(32f)))
        {
            SetupSelectedCodeExampleScene();
        }

        codeExampleAdvancedExpanded = EditorGUILayout.Foldout(codeExampleAdvancedExpanded, "Advanced Options (examples)", true);
        if (codeExampleAdvancedExpanded)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "'Generate All Scenes' writes all .unity files at once without opening each scene. Re-run after a package update.",
                MessageType.None);
            if (GUILayout.Button("Generate All Example Scenes", GUILayout.Height(28f)))
            {
                CreateAllCodeExampleScenes();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.HelpBox(
            "The classic or VR setup rebuilds the active scene (player, teleport, camera, managers). Compatible with an empty scene or a Unity template.",
            MessageType.None);
    }

    private void DrawWorkspaceConfigurationSection()
    {
        EditorGUILayout.LabelField("GAMA Workspace & Tools", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enter a workspace folder or .gaml file, then click Explore to analyze a model and prepare the Import Experiment tab. 'Import Experiment' opens that tab with the selected experiment. Also here: external prefabs and headless export.",
            MessageType.Info);

        DrawExperimentPathInput("Workspace or .gaml file");
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(experimentPath)))
        {
            if (GUILayout.Button("Explore", GUILayout.Width(120f)))
            {
                ExploreExperimentPathFromWorkspace();
            }
        }

        GUILayout.Label(experimentStatus, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
        EditorGUILayout.EndHorizontal();

        DrawWorkspaceExperimentList();

        EditorGUILayout.Space(12f);
        DrawPrefabSourcePathRow();

        EditorGUILayout.Space(8f);
        string runnerHint = string.Empty;
        if (GamaHeadlessPackagePaths.TryGetBundledRunnerBat(out string runnerAbs, out _))
        {
            runnerHint = "Bundled runner: " + runnerAbs;
        }
        else
        {
            runnerHint = "Package runner not found — check your package installation.";
        }

        EditorGUILayout.HelpBox(runnerHint, MessageType.None);

        DrawHeadlessSetupPathFields();
    }

    private void DrawPrefabSourcePathRow()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        prefabSourceFolder = EditorGUILayout.TextField("External Prefabs Folder", prefabSourceFolder);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(PrefabSourcePrefKey, prefabSourceFolder);
        }

        if (GUILayout.Button("Browse...", GUILayout.Width(100f)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("External Prefabs Folder", prefabSourceFolder, string.Empty);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                prefabSourceFolder = selectedPath;
                EditorPrefs.SetString(PrefabSourcePrefKey, prefabSourceFolder);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawHeadlessSetupPathFields()
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Export headless (GAMA Platform)", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        gamaHeadlessBatPath = EditorGUILayout.TextField("gama-headless.bat (installation)", gamaHeadlessBatPath);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFilePanel("gama-headless.bat", "", "bat");
            if (!string.IsNullOrEmpty(path))
            {
                gamaHeadlessBatPath = path;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        gamaHeadlessWorkingDir = EditorGUILayout.TextField("Working Directory (optional)", gamaHeadlessWorkingDir);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFolderPanel("GAMA Headless Working Directory", gamaHeadlessWorkingDir, string.Empty);
            if (!string.IsNullOrEmpty(path))
            {
                gamaHeadlessWorkingDir = path;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        gamaJsonExportOutputDir = EditorGUILayout.TextField("JSON Output Folder", gamaJsonExportOutputDir);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFolderPanel("JSON Output Folder", gamaJsonExportOutputDir, string.Empty);
            if (!string.IsNullOrEmpty(path))
            {
                gamaJsonExportOutputDir = path;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Custom Command (optional, overrides default call)", EditorStyles.miniLabel);
        gamaHeadlessCustomCmd = EditorGUILayout.TextArea(gamaHeadlessCustomCmd, GUILayout.MinHeight(44f));
        EditorGUILayout.HelpBox(
            "Leave empty to use the package runner (GamaUnityHeadlessRunner.bat) with gama-headless.bat. " +
            "Otherwise: string passed to cmd /c after expanding tokens {Batch}, {Gaml}, {OutputDir}, {GamaHeadlessBat}, {GamaHeadlessDir}.",
            MessageType.None);

        gamaHeadlessTimeoutSeconds = Mathf.Clamp(EditorGUILayout.IntField("Export Timeout (seconds)", gamaHeadlessTimeoutSeconds), 30, 7200);

        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(GamaHeadlessBatPrefKey, gamaHeadlessBatPath);
            EditorPrefs.SetString(GamaHeadlessWorkDirPrefKey, gamaHeadlessWorkingDir);
            EditorPrefs.SetString(GamaJsonExportOutDirPrefKey, gamaJsonExportOutputDir);
            EditorPrefs.SetString(GamaHeadlessCustomCmdPrefKey, gamaHeadlessCustomCmd);
            EditorPrefs.SetInt(GamaHeadlessTimeoutSecPrefKey, gamaHeadlessTimeoutSeconds);
        }
    }

    private void DrawExperimentImportTab()
    {
        EditorGUILayout.LabelField("Import Experiment", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Recommended workflow: 'Workspace Explorer' tab → folder path or .gaml → Explore → 'Import Experiment' on the desired row. " +
            "You can also enter the path here and click Explore. " +
            "Without middleware / cumulative preview, use the grid preview and agent settings (scale, color, visibility) as before. " +
            "For a static preview faithful to the world after warmup, launch the middleware, then use the preview section below.",
            MessageType.Info);

        EditorGUILayout.Space();
        DrawExperimentPathInput();
        DrawExperimentToolbar();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(experimentStatus, MessageType.None);

        if (experimentOptions.Count > 0)
        {
            DrawExperimentSelection();
        }

        if (analysis == null && experimentOptions.Count > 0)
        {
            AnalyzeSelectedExperiment();
        }

        experimentScroll = EditorGUILayout.BeginScrollView(experimentScroll);

        if (analysis != null)
        {
            DrawExperimentSummary();
            DrawSceneSettings();
        }

        EditorGUILayout.Space();
        DrawStaticPreviewMiddlewareJsonSection();

        EditorGUILayout.EndScrollView();
    }

    private void DrawExperimentPathInput()
    {
        DrawExperimentPathInput("Experiment Path");
    }

    private void DrawExperimentPathInput(string fieldLabel)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        experimentPath = EditorGUILayout.TextField(fieldLabel, experimentPath);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(ExperimentPathPrefKey, experimentPath);
        }

        if (GUILayout.Button("File...", GUILayout.Width(72f)))
        {
            string selectedPath = EditorUtility.OpenFilePanel("Select GAMA Experiment", experimentPath, "gaml");
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                experimentPath = selectedPath;
                EditorPrefs.SetString(ExperimentPathPrefKey, experimentPath);
            }
        }

        if (GUILayout.Button("Folder...", GUILayout.Width(82f)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select GAMA Workspace", experimentPath, string.Empty);
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                experimentPath = selectedPath;
                EditorPrefs.SetString(ExperimentPathPrefKey, experimentPath);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawExperimentToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(experimentPath)))
        {
            if (GUILayout.Button("Explore", GUILayout.Width(120f)))
            {
                ExploreExperimentPath();
            }
        }

        if (GUILayout.Button("Workspace Explorer", GUILayout.Width(170f)))
        {
            selectedTab = TabWorkspace;
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawExperimentSelection()
    {
        string[] labels = new string[experimentOptions.Count];
        for (int i = 0; i < experimentOptions.Count; i++)
        {
            labels[i] = experimentOptions[i].DisplayName;
        }

        EditorGUI.BeginChangeCheck();
        selectedExperimentIndex = EditorGUILayout.Popup("Experiment", selectedExperimentIndex, labels);
        if (EditorGUI.EndChangeCheck())
        {
            AnalyzeSelectedExperiment();
            InvalidateCaptureSelectionCache();
        }
    }

    private void DrawExperimentSummary()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Detected Experiment", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Name", analysis.Name);
        EditorGUILayout.LabelField("Capability", analysis.CapabilityLabel);
        EditorGUILayout.LabelField("Source", analysis.SourcePath);
        EditorGUILayout.LabelField("Agents", analysis.Agents.Count.ToString(CultureInfo.InvariantCulture));
    }

    private void DrawSceneSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene Properties", EditorStyles.boldLabel);
        setupSceneBeforeApply = EditorGUILayout.Toggle("Setup Scene Before Apply", setupSceneBeforeApply);
        replaceGeneratedRules = EditorGUILayout.Toggle("Replace Generated Rules", replaceGeneratedRules);
        sceneCharacteristicSize = Mathf.Max(1f, EditorGUILayout.FloatField("Scene Size", sceneCharacteristicSize));
        horizontalScale = Mathf.Max(0.0001f, EditorGUILayout.FloatField("GAMA XY Scale", horizontalScale));
        verticalOffset = EditorGUILayout.FloatField("GAMA Z Offset", verticalOffset);
        enablePrefabRenderDistance = EditorGUILayout.Toggle("Render Distance Culling", enablePrefabRenderDistance);
        renderDistance = Mathf.Max(0f, EditorGUILayout.FloatField("Render Distance", renderDistance));
        cameraNearClip = Mathf.Max(0.001f, EditorGUILayout.FloatField("Camera Near Clip", cameraNearClip));
        cameraFarClip = Mathf.Max(cameraNearClip + 1f, EditorGUILayout.FloatField("Camera Far Clip", cameraFarClip));
        previewSamplesPerAgent = Mathf.Clamp(EditorGUILayout.IntField("Preview Samples / Agent", previewSamplesPerAgent), 1, 100);
        backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);
    }

    private void DrawStaticPreviewMiddlewareJsonSection()
    {
        DrawHeadlessJsonExportSection();
    }

    private void DrawHeadlessJsonExportSection()
    {
        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Preview from the Open GAMA Experiment", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Start GAMA and simple.webplatform, then open the desired experiment in GAMA. The experiment does not need to be already running. " +
            "Unity uses middleware port 8080 to generate a static preview. " +
            "The static preview uses the cumulative cache, not the last isolated chunk. Empty ID = " + StaticInformation.getId() + ".",
            MessageType.Info);

        bool busy = captureFlowActive || captureTask != null;

        using (new EditorGUI.DisabledScope(busy || captureUseLocalMiddleware))
        {
            if (GUILayout.Button("Generate Preview from GAMA", GUILayout.Height(34f)))
            {
                captureManagedFromUnity = true;
                captureUseExternalMiddleware = true;
                captureUseLocalMiddleware = false;
                captureSkipRemoteLoad = false;
                capturePort = "8080";
                EditorPrefs.SetBool(GamaCaptureManagedFromUnityPrefKey, true);
                EditorPrefs.SetBool(GamaCaptureExternalMiddlewarePrefKey, true);
                EditorPrefs.SetBool(GamaCaptureUseLocalMiddlewarePrefKey, false);
                EditorPrefs.SetBool(GamaCaptureSkipRemoteLoadPrefKey, false);
                EditorPrefs.SetString(GamaCapturePortPrefKey, capturePort);
                StartCaptureFlow(launchGama: false, managedFromUnity: true);
            }
        }

        using (new EditorGUI.DisabledScope(!busy))
        {
            if (GUILayout.Button("Cancel Capture", GUILayout.Height(22f)))
            {
                AbortCaptureIfRunning("Cancelled by user");
            }
        }

        EditorGUILayout.Space(6f);
        if (!string.IsNullOrEmpty(captureRuntimeStatus))
        {
            EditorGUILayout.HelpBox(captureRuntimeStatus, MessageType.None);
        }

        if (analysis != null || agentOverrides.Count > 0)
        {
            EditorGUILayout.Space(8f);
            DrawAgentSettings();

            if (analysis != null)
            {
                DrawApplyControls();
            }

            DrawPreviewValidationControls();
        }
        else if (GameObject.Find(StaticPreviewRootName) != null)
        {
            DrawPreviewValidationControls();
        }

        EditorGUILayout.Space(12f);
        headlessExportSectionExpanded = EditorGUILayout.Foldout(headlessExportSectionExpanded, "Advanced Preview Settings", true);
        if (!headlessExportSectionExpanded)
        {
            return;
        }

        EditorGUI.BeginChangeCheck();
        captureUseLocalMiddleware = EditorGUILayout.ToggleLeft("Direct GAMA GUI Capture (advanced diagnostics)", captureUseLocalMiddleware, EditorStyles.boldLabel);
        captureManagedFromUnity = EditorGUILayout.ToggleLeft(
            "Preview from the selected GAMA experiment (Play-like sequence, external middleware)",
            captureManagedFromUnity);
        using (new EditorGUI.DisabledScope(!captureManagedFromUnity))
        {
            captureUseExternalMiddleware = EditorGUILayout.ToggleLeft(
                "Use already running middleware / External middleware",
                captureUseExternalMiddleware);

            if (!captureUseExternalMiddleware)
            {
                EditorGUILayout.HelpBox(
                    "Advanced mode only: Unity can stop processes listening on 8001/8080 and restart Node with a generated LEARNING_PACKAGE_PATH. " +
                    "The main button below always forces the external middleware.",
                    MessageType.Warning);
            }
        }
        using (new EditorGUI.DisabledScope(captureManagedFromUnity))
        {
            captureSkipRemoteLoad = EditorGUILayout.ToggleLeft(
                "Legacy 8080-only diagnostic (no launch monitor)",
                captureSkipRemoteLoad);
        }

        if (captureManagedFromUnity)
        {
            autoLaunchGamaOnPlay = EditorGUILayout.ToggleLeft(
                "At Play Runtime: launch GAMA experiment via monitor (8001)",
                autoLaunchGamaOnPlay);
            captureMonitorPort = EditorGUILayout.IntField("Monitor Port (web UI)", captureMonitorPort);
        }

        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(captureUseLocalMiddleware))
        {
            captureHost = EditorGUILayout.TextField("Middleware Host", captureHost);
        }
        capturePort = EditorGUILayout.TextField(captureUseLocalMiddleware ? "Port GAMA" : "Port", capturePort, GUILayout.Width(120f));
        EditorGUILayout.EndHorizontal();

        using (new EditorGUI.DisabledScope(captureUseLocalMiddleware))
        {
            captureConnectionId = EditorGUILayout.TextField(
                "Connection ID (empty = " + StaticInformation.getId() + ")", captureConnectionId);
        }
        if (analysis != null && !string.IsNullOrWhiteSpace(analysis.Name))
        {
            gamaHeadlessBatchName = analysis.Name;
        }

        using (new EditorGUI.DisabledScope(analysis != null && !string.IsNullOrWhiteSpace(analysis.Name)))
        {
            gamaHeadlessBatchName = EditorGUILayout.TextField("GAMA Experiment Name", gamaHeadlessBatchName);
        }

        string[] modes = { "batch", "script", "custom" };
        int currentMode = Array.IndexOf(modes, string.IsNullOrEmpty(captureMode) ? "batch" : captureMode);
        if (currentMode < 0) currentMode = 0;
        currentMode = EditorGUILayout.Popup("GAMA Launch Mode", currentMode, modes);
        captureMode = modes[currentMode];

        using (new EditorGUI.DisabledScope(true))
        {
            string gamlHint = analysis != null && !string.IsNullOrEmpty(analysis.SourcePath)
                ? analysis.SourcePath
                : "(import from Workspace Explorer or click Explore here)";
            EditorGUILayout.TextField(".gaml file used", gamlHint);
        }

        gamaHeadlessRunPreviewAfter = EditorGUILayout.Toggle("Generate Static Preview After Success", gamaHeadlessRunPreviewAfter);

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Preview Capture (Stabilization)", EditorStyles.boldLabel);
        captureWorldPhaseSeconds = EditorGUILayout.Slider("Preview Warmup Seconds", captureWorldPhaseSeconds, 5f, 120f);
        captureMaxWorldFrames = EditorGUILayout.IntSlider("Max Frames", captureMaxWorldFrames, 3, 120);
        captureDynamicSpeciesRegex = EditorGUILayout.TextField("Dynamic Species (regex)", captureDynamicSpeciesRegex);
        EditorGUILayout.HelpBox(
            "For Traffic and Pollution VR, the GAMA species is called 'people' (mobile geometries). " +
            "Include people|human in the regex. Capture waits up to ~40 s after warmup " +
            "before stopping on 'stable cache' if no pedestrian has arrived yet.",
            MessageType.None);
        captureStopWhenDynamicAgentsFound = EditorGUILayout.Toggle(
            "Stop When Cache Is Stable",
            captureStopWhenDynamicAgentsFound);
        captureStopWhenPreviewCacheStable = EditorGUILayout.Toggle(
            "Stop When Preview Cache Is Stable",
            captureStopWhenPreviewCacheStable);
        using (new EditorGUI.DisabledScope(!captureStopWhenPreviewCacheStable))
        {
            capturePreviewStableSeconds = EditorGUILayout.Slider(
                "Agent Wait Time (s)",
                capturePreviewStableSeconds,
                1f,
                30f);
        }
        capturePauseExperimentAfterPreview = EditorGUILayout.Toggle(
            "Automatic Warmup",
            capturePauseExperimentAfterPreview);
        autoHidePreviewOnPlay = EditorGUILayout.Toggle(
            "Hide Preview During Play",
            autoHidePreviewOnPlay);
        applyPreviewSettingsToPlay = EditorGUILayout.Toggle(
            "Apply Preview Settings to Play",
            applyPreviewSettingsToPlay);
        speciesRenderOverridesAsset = (GamaSpeciesRenderOverrides)EditorGUILayout.ObjectField(
            "Species Render Overrides",
            speciesRenderOverridesAsset,
            typeof(GamaSpeciesRenderOverrides),
            false);
        using (new EditorGUI.DisabledScope(speciesRenderOverridesAsset != null))
        {
            if (GUILayout.Button("Create GamaSpeciesRenderOverrides.asset", GUILayout.Height(22f)))
            {
                speciesRenderOverridesAsset = CreateSpeciesRenderOverridesAsset();
            }
        }
        using (new EditorGUI.DisabledScope(speciesRenderOverridesAsset == null))
        {
            if (GUILayout.Button("Apply Overrides to Live Mode", GUILayout.Height(22f)))
            {
                ApplySpeciesRenderOverridesToSimulationManager();
            }
            if (GUILayout.Button("Apply Settings to Preview", GUILayout.Height(22f)))
            {
                GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
            }
        }
        
        autoUpdatePreview = EditorGUILayout.Toggle(
            "Live Update Preview",
            autoUpdatePreview);

        if (EditorGUI.EndChangeCheck())
        {
            if (captureUseLocalMiddleware && string.Equals(capturePort?.Trim(), "8080", StringComparison.Ordinal))
            {
                capturePort = "1000";
            }
            else if (!captureUseLocalMiddleware && GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(capturePort))
            {
                capturePort = "8080";
            }

            EditorPrefs.SetString(GamaHeadlessBatchNamePrefKey, gamaHeadlessBatchName);
            EditorPrefs.SetBool(GamaHeadlessRunPreviewAfterPrefKey, gamaHeadlessRunPreviewAfter);
            EditorPrefs.SetString(GamaCaptureHostPrefKey, captureHost);
            EditorPrefs.SetString(GamaCapturePortPrefKey, capturePort);
            string idToStore = captureConnectionId?.Trim() ?? string.Empty;
            if (string.Equals(idToStore, "Editor_Capture", StringComparison.OrdinalIgnoreCase))
            {
                idToStore = string.Empty;
            }

            EditorPrefs.SetString(GamaCaptureIdPrefKey, idToStore);
            captureConnectionId = idToStore;
            EditorPrefs.SetBool(GamaCaptureUseLocalMiddlewarePrefKey, captureUseLocalMiddleware);
            EditorPrefs.SetBool(GamaCaptureSkipRemoteLoadPrefKey, captureSkipRemoteLoad);
            EditorPrefs.SetBool(GamaCaptureManagedFromUnityPrefKey, captureManagedFromUnity);
            EditorPrefs.SetBool(GamaCaptureExternalMiddlewarePrefKey, captureUseExternalMiddleware);
            EditorPrefs.SetBool("ProjectSimple.GamaUnity.Play.AutoLaunchMonitor", autoLaunchGamaOnPlay);
            EditorPrefs.SetInt(GamaCaptureMonitorPortPrefKey, captureMonitorPort);
            if (captureManagedFromUnity)
            {
                captureSkipRemoteLoad = false;
            }
            EditorPrefs.SetString(GamaCaptureModePrefKey, captureMode);
            EditorPrefs.SetInt(CaptureMaxWorldFramesPrefKey, captureMaxWorldFrames);
            EditorPrefs.SetFloat(CaptureWorldPhaseSecondsPrefKey, captureWorldPhaseSeconds);
            EditorPrefs.SetString(CaptureDynamicSpeciesRegexPrefKey, captureDynamicSpeciesRegex ?? string.Empty);
            EditorPrefs.SetBool(CaptureStopWhenDynamicPrefKey, captureStopWhenDynamicAgentsFound);
            EditorPrefs.SetBool(CaptureStopWhenStablePrefKey, captureStopWhenPreviewCacheStable);
            EditorPrefs.SetFloat(CaptureStableSecondsPrefKey, capturePreviewStableSeconds);
            EditorPrefs.SetBool(CapturePauseAfterPreviewPrefKey, capturePauseExperimentAfterPreview);
            EditorPrefs.SetBool(AutoHidePreviewOnPlayPrefKey, autoHidePreviewOnPlay);
            EditorPrefs.SetBool(ApplyPreviewSettingsToPlayPrefKey, applyPreviewSettingsToPlay);
            EditorPrefs.SetBool(AutoUpdatePreviewPrefKey, autoUpdatePreview);
            string overridesPath = speciesRenderOverridesAsset != null
                ? AssetDatabase.GetAssetPath(speciesRenderOverridesAsset)
                : string.Empty;
            EditorPrefs.SetString(SpeciesOverridesAssetPrefKey, overridesPath ?? string.Empty);
        }

        DrawMiddlewareSection(busy);

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "If the middleware keeps repeating 'Reconnecting player of id ...' after a cancelled capture, the player slot is stuck on the GAMA side (max_num_players = 1). " +
            "Use 'Purge Ghost Player' to send disconnect_properly for that id and free the simulation.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        string defaultGhostId = StaticInformation.getId();
        if (string.IsNullOrWhiteSpace(defaultGhostId))
        {
            defaultGhostId = "Editor_Capture";
        }

        ghostPlayerIdToPurge = EditorGUILayout.TextField(
            "ID to purge (usually " + defaultGhostId + ")",
            string.IsNullOrEmpty(ghostPlayerIdToPurge) ? defaultGhostId : ghostPlayerIdToPurge);
        using (new EditorGUI.DisabledScope(busy || string.IsNullOrWhiteSpace(ghostPlayerIdToPurge)))
        {
            if (GUILayout.Button("Purge Ghost Player", GUILayout.Width(200f), GUILayout.Height(22f)))
            {
                _ = PurgeGhostPlayerInteractiveAsync(ghostPlayerIdToPurge.Trim());
            }
        }

        EditorGUILayout.EndHorizontal();

        DrawManualJsonFallbackSection();

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(gamaJsonExportOutputDir) || !Directory.Exists(gamaJsonExportOutputDir)))
        {
            if (GUILayout.Button("Open Output Folder", GUILayout.Height(22f), GUILayout.Width(180f)))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetFullPath(gamaJsonExportOutputDir),
                    UseShellExecute = true
                });
            }
        }
    }

    private void DrawMiddlewareSection(bool busy)
    {
        EditorGUILayout.Space(6f);
        middlewareSectionExpanded = EditorGUILayout.Foldout(middlewareSectionExpanded, "Advanced / Middleware Catalog Launch", true);
        if (!middlewareSectionExpanded) return;

        EditorGUILayout.HelpBox(
            "Advanced section kept for monitor catalog tests: settings.json generation, LEARNING_PACKAGE_PATH, catalog diagnosis, " +
            "monitor launch and Node restart. The main preview button does not use this flow.",
            MessageType.Warning);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        middlewareScriptPath = EditorGUILayout.TextField("Middleware Script", middlewareScriptPath);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFilePanel("Middleware Script", "", "bat,cmd,sh");
            if (!string.IsNullOrEmpty(path))
            {
                middlewareScriptPath = path;
            }
        }

        EditorGUILayout.EndHorizontal();
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(GamaMiddlewareScriptPrefKey, middlewareScriptPath);
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(busy || captureUseLocalMiddleware || catalogDiagnosisTask != null))
        {
            if (GUILayout.Button("Diagnose Middleware Catalog", GUILayout.Height(24f)))
            {
                StartCatalogDiagnosis();
            }
        }

        using (new EditorGUI.DisabledScope(busy || captureUseLocalMiddleware))
        {
            if (GUILayout.Button("Prepare/Generate Middleware Package", GUILayout.Height(24f)))
            {
                SyncSelectedModelWithMiddleware();
            }
        }

        using (new EditorGUI.DisabledScope(busy || captureUseLocalMiddleware))
        {
            if (GUILayout.Button("Manage/Restart Middleware from Unity", GUILayout.Height(24f)))
            {
                ConfigureAndOfferMiddlewareRestart();
            }
        }

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(busy))
        {
            if (GUILayout.Button("Legacy Capture (current options above)", GUILayout.Height(28f)))
            {
                StartCaptureFlow(launchGama: false, managedFromUnity: captureManagedFromUnity);
            }
        }

        using (new EditorGUI.DisabledScope(busy || analysis == null || string.IsNullOrEmpty(analysis.SourcePath)))
        {
            if (GUILayout.Button("Launch GAMA (headless) + Capture", GUILayout.Height(28f), GUILayout.Width(240f)))
            {
                StartCaptureFlow(launchGama: true, managedFromUnity: false);
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawManualJsonFallbackSection()
    {
        EditorGUILayout.Space(6f);
        manualJsonSectionExpanded = EditorGUILayout.Foldout(manualJsonSectionExpanded, "Advanced / Captured JSON", true);
        if (!manualJsonSectionExpanded) return;

        EditorGUILayout.HelpBox(
            "Use this section only if you have already manually captured the 3 files. " +
            "Otherwise use 'Generate Preview from GAMA' or 'Launch GAMA (headless) + Capture'.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.BeginHorizontal();
        staticPreviewPrecisionJsonPath = EditorGUILayout.TextField("precision.json", staticPreviewPrecisionJsonPath);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFilePanel("precision.json", "", "json");
            if (!string.IsNullOrEmpty(path)) staticPreviewPrecisionJsonPath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        staticPreviewPropertiesJsonPath = EditorGUILayout.TextField("properties.json", staticPreviewPropertiesJsonPath);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFilePanel("properties.json", "", "json");
            if (!string.IsNullOrEmpty(path)) staticPreviewPropertiesJsonPath = path;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        staticPreviewWorldJsonPath = EditorGUILayout.TextField("world / pointsLoc.json", staticPreviewWorldJsonPath);
        if (GUILayout.Button("…", GUILayout.Width(28f)))
        {
            string path = EditorUtility.OpenFilePanel("world.json", "", "json");
            if (!string.IsNullOrEmpty(path)) staticPreviewWorldJsonPath = path;
        }
        EditorGUILayout.EndHorizontal();

        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetString(StaticPreviewPrecisionJsonPrefKey, staticPreviewPrecisionJsonPath);
            EditorPrefs.SetString(StaticPreviewPropertiesJsonPrefKey, staticPreviewPropertiesJsonPath);
            EditorPrefs.SetString(StaticPreviewWorldJsonPrefKey, staticPreviewWorldJsonPath);
            RefreshAvailableWorldTicks();
        }

        DrawStaticPreviewTickSection();
    }

    private void DrawStaticPreviewTickSection()
    {
        RefreshAvailableWorldTicks();
        if (availableWorldTickPaths.Count <= 1)
        {
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Tick to Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each tick is a cumulative snapshot of the preview cache. Drag the slider to inspect the progression.",
            MessageType.Info);

        int maxTick = availableWorldTickPaths.Count - 1;
        EditorGUI.BeginChangeCheck();
        staticPreviewWorldTickIndex = EditorGUILayout.IntSlider("Tick Index", staticPreviewWorldTickIndex, 0, maxTick);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetInt(StaticPreviewWorldTickPrefKey, staticPreviewWorldTickIndex);
            staticPreviewWorldJsonPath = ResolvePreviewWorldJsonPath();
            EditorPrefs.SetString(StaticPreviewWorldJsonPrefKey, staticPreviewWorldJsonPath);
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("world.json used", ResolvePreviewWorldJsonPath());
        }

        if (GUILayout.Button("Regenerate Static Preview", GUILayout.Height(24f)))
        {
            GenerateStaticPreview();
        }
    }

    private async System.Threading.Tasks.Task PurgeGhostPlayerInteractiveAsync(string ghostId)
    {
        string host = string.IsNullOrWhiteSpace(captureHost) ? PlayerPrefs.GetString("IP", "localhost") : captureHost.Trim();
        string port = string.IsNullOrWhiteSpace(capturePort) ? PlayerPrefs.GetString("PORT", "8080") : capturePort.Trim();
        captureRuntimeStatus = "Purging ghost player \"" + ghostId + "\" on ws://" + host + ":" + port + "/...";
        Repaint();

        string outcome;
        try
        {
            outcome = await GamaEditorFirstTickCapture.PurgeGhostPlayerAsync(host, port, ghostId, 4000, System.Threading.CancellationToken.None);
        }
        catch (Exception ex)
        {
            outcome = "Purge error: " + ex.Message;
        }

        captureRuntimeStatus = outcome;
        UnityEngine.Debug.Log("[GAMA] " + outcome);
        Repaint();
    }

    private bool RestartMiddlewareForUnitySelection(
        string host,
        string runtimeModelPath,
        string runtimeExperimentName,
        System.Threading.CancellationToken ct,
        out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(generatedLearningPackageRoot) ||
            !Directory.Exists(generatedLearningPackageRoot))
        {
            error = "Generated learning package not found: " + generatedLearningPackageRoot;
            return false;
        }

        if (!GamaEditorMiddlewareLauncher.TryResolveWebplatformRoot(out string webplatformRoot))
        {
            error = "simple.webplatform repository not found (expected on the Desktop or next to the Unity project).";
            return false;
        }

        int playerPort = ResolveCapturePlayerPort();
        if (!EnsureMiddlewarePortsFreeForRelaunch(captureMonitorPort, playerPort, ct, out error))
        {
            return false;
        }

        if (captureMiddlewareProcess != null)
        {
            try { captureMiddlewareProcess.StopAsync(1500).GetAwaiter().GetResult(); } catch { /* ignore */ }
            try { captureMiddlewareProcess.Dispose(); } catch { /* ignore */ }
            captureMiddlewareProcess = null;
        }

        Dictionary<string, string> middlewareEnv = BuildMiddlewareEnvironmentForSelection(runtimeModelPath);
        if (!middlewareEnv.TryGetValue("LEARNING_PACKAGE_PATH", out string learningPackagePath) ||
            string.IsNullOrWhiteSpace(learningPackagePath))
        {
            error = "Empty LEARNING_PACKAGE_PATH: cannot launch middleware for Unity selection.";
            return false;
        }

        if (!middlewareEnv.ContainsKey("EXTRA_LEARNING_PACKAGE_PATH"))
        {
            middlewareEnv["EXTRA_LEARNING_PACKAGE_PATH"] = string.Empty;
        }

        string cmdExe = ResolveCmdExe();
        string cmdArguments = BuildMiddlewareCmdArguments(middlewareEnv);
        lastMiddlewareWorkingDirectory = webplatformRoot;
        lastMiddlewareLearningPackagePath = learningPackagePath;
        lastMiddlewareExtraLearningPackagePath = middlewareEnv["EXTRA_LEARNING_PACKAGE_PATH"] ?? string.Empty;

        Debug.Log("[GAMA][SYNC] Restarting middleware with LEARNING_PACKAGE_PATH=" + learningPackagePath);
        Debug.Log("[GAMA][MW] Starting simple.webplatform");
        Debug.Log("[GAMA][MW] FileName=" + cmdExe);
        Debug.Log("[GAMA][MW] Arguments=" + cmdArguments);
        Debug.Log("[GAMA][MW] WorkingDirectory=" + webplatformRoot);
        Debug.Log("[GAMA][MW] LEARNING_PACKAGE_PATH=" + learningPackagePath);
        Debug.Log("[GAMA][MW] EXTRA_LEARNING_PACKAGE_PATH=" + lastMiddlewareExtraLearningPackagePath);
        Debug.Log("[GAMA][MW] MONITOR_WS_PORT=" + captureMonitorPort);
        Debug.Log("[GAMA][MW] HEADSET_WS_PORT=" + playerPort);
        LogMiddlewareEnvironmentSnapshot(middlewareEnv);

        string startError;
        captureMiddlewareProcess = GamaEditorBackgroundProcess.StartCommand(
            "Middleware",
            cmdExe,
            cmdArguments,
            webplatformRoot,
            middlewareEnv,
            out startError,
            LogMiddlewareProcessLine);

        if (captureMiddlewareProcess == null)
        {
            error = "Middleware startup failed: " + startError;
            return false;
        }

        lastMiddlewareProcessId = captureMiddlewareProcess.ProcessId;
        Debug.Log("[GAMA][MW] New middleware PID=" + lastMiddlewareProcessId + " cwd=" + webplatformRoot);
        captureRuntimeStatus = "Waiting for monitor ports " + captureMonitorPort + " and player " + playerPort + "...";
        Repaint();

        bool monitorReady;
        bool playerReady;
        try
        {
            monitorReady = GamaEditorMiddlewareOrchestrator.WaitForMonitorReachableAsync(
                    host,
                    captureMonitorPort,
                    60000,
                    ct)
                .GetAwaiter()
                .GetResult();
            playerReady = monitorReady && GamaEditorMiddlewareOrchestrator.WaitForMonitorReachableAsync(
                    host,
                    playerPort,
                    30000,
                    ct)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            error = "Wait for middleware ports interrupted: " + ex.Message;
            return false;
        }

        if (!monitorReady)
        {
            error = "The middleware was launched (PID " + lastMiddlewareProcessId + ") but the monitor port " +
                    captureMonitorPort + " is not responding after 60 s. Cwd=" + webplatformRoot +
                    ". Process logs:\n" + captureMiddlewareProcess.LogSnapshot;
            return false;
        }

        if (!playerReady)
        {
            error = "Monitor " + captureMonitorPort + " ready, but the player socket " + playerPort +
                    " is not responding. Process logs:\n" + captureMiddlewareProcess.LogSnapshot;
            return false;
        }

        List<int> monitorListenerPids = GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(captureMonitorPort, Debug.Log);
        List<int> playerListenerPids = GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(playerPort, Debug.Log);
        string monitorListener = monitorListenerPids.Count == 0 ? "?" : string.Join(",", monitorListenerPids);
        string playerListener = playerListenerPids.Count == 0 ? "?" : string.Join(",", playerListenerPids);
        Debug.Log("[GAMA][MW] Monitor TCP ready on port " + captureMonitorPort + " (listener PID=" + monitorListener + ").");
        Debug.Log("[GAMA][MW] Player socket ready on port " + playerPort + " (listener PID=" + playerListener + ").");
        return true;
    }

    private bool EnsureMiddlewarePortsFreeForRelaunch(
        int monitorPort,
        int playerPort,
        System.Threading.CancellationToken ct,
        out string error)
    {
        error = null;
        HashSet<int> allPids = new HashSet<int>();
        List<int> monitorPids = GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(monitorPort, Debug.Log);
        List<int> playerPids = GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(playerPort, Debug.Log);
        for (int i = 0; i < monitorPids.Count; i++)
        {
            allPids.Add(monitorPids[i]);
            Debug.Log("[GAMA][MW] Port " + monitorPort + " occupied by PID=" + monitorPids[i]);
        }

        for (int i = 0; i < playerPids.Count; i++)
        {
            allPids.Add(playerPids[i]);
            Debug.Log("[GAMA][MW] Port " + playerPort + " occupied by PID=" + playerPids[i]);
        }

        if (allPids.Count == 0)
        {
            Debug.Log("[GAMA][MW] Port " + monitorPort + " free");
            Debug.Log("[GAMA][MW] Port " + playerPort + " free");
            return true;
        }

        string pidList = string.Join(", ", allPids);
        bool kill = EditorUtility.DisplayDialog(
            "Restart GAMA Middleware",
            "A middleware is already listening on monitor port " + monitorPort + " and/or player port " + playerPort +
            " (PID " + pidList + ").\n\nStop it to load the selected Unity model?",
            "Stop old middleware",
            "Cancel");

        if (!kill)
        {
            error = "Ports " + monitorPort + "/" + playerPort + " occupied by PID " + pidList +
                    ". Relaunch cancelled: Unity cannot guarantee the catalog matches the selection.";
            return false;
        }

        foreach (int pid in allPids)
        {
            Debug.Log("[GAMA][MW] Stopping PID " + pid + "...");
            GamaEditorMiddlewareOrchestrator.KillProcessByPid(pid, Debug.Log);
        }

        bool monitorClosed;
        bool playerClosed;
        try
        {
            monitorClosed = GamaEditorMiddlewareOrchestrator.WaitForTcpPortClosedAsync(monitorPort, 10000, ct)
                .GetAwaiter()
                .GetResult();
            playerClosed = GamaEditorMiddlewareOrchestrator.WaitForTcpPortClosedAsync(playerPort, 10000, ct)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            error = "Wait for middleware ports release interrupted: " + ex.Message;
            return false;
        }

        if (!monitorClosed || !playerClosed)
        {
            error = "Ports " + monitorPort + "/" + playerPort + " remain occupied after taskkill. Monitor PIDs=" +
                    string.Join(", ", GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(monitorPort, Debug.Log)) +
                    " player=" +
                    string.Join(", ", GamaEditorMiddlewareOrchestrator.GetListeningPidsOnTcpPort(playerPort, Debug.Log));
            return false;
        }

        Debug.Log("[GAMA][MW] Port " + monitorPort + " free");
        Debug.Log("[GAMA][MW] Port " + playerPort + " free");
        return true;
    }

    private int ResolveCapturePlayerPort()
    {
        if (int.TryParse(capturePort, out int parsed) && parsed > 0 && parsed <= 65535)
        {
            return parsed;
        }

        return 8080;
    }

    private static string BuildMiddlewareCmdArguments(IDictionary<string, string> middlewareEnv)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("/c ");
        AppendCmdSet(sb, "LEARNING_PACKAGE_PATH", middlewareEnv);
        AppendCmdSet(sb, "EXTRA_LEARNING_PACKAGE_PATH", middlewareEnv);
        AppendCmdSet(sb, "MONITOR_WS_PORT", middlewareEnv);
        AppendCmdSet(sb, "HEADSET_WS_PORT", middlewareEnv);

        sb.Append("npx tsx src/api/index.ts");
        return sb.ToString();
    }

    private static void AppendCmdSet(StringBuilder sb, string key, IDictionary<string, string> env)
    {
        string value = string.Empty;
        if (env != null && env.TryGetValue(key, out string found))
        {
            value = found ?? string.Empty;
        }

        sb.Append("set \"");
        sb.Append(key);
        sb.Append('=');
        sb.Append(value.Replace("\"", "\"\""));
        sb.Append("\" && ");
    }

    private static void LogMiddlewareProcessLine(string line, bool isStdErr)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string prefix = isStdErr ? "[GAMA][MW][stderr] " : "[GAMA][MW][stdout] ";
        Debug.Log(prefix + line);
    }

    private static void LogMiddlewareEnvironmentSnapshot(IDictionary<string, string> middlewareEnv)
    {
        if (middlewareEnv == null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> kv in middlewareEnv)
        {
            if (string.IsNullOrEmpty(kv.Key))
            {
                continue;
            }

            if (kv.Key.IndexOf("PATH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                kv.Key.StartsWith("LEARNING_", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.EndsWith("_WS_PORT", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log("[GAMA][MW] env " + kv.Key + "=" + (kv.Value ?? string.Empty));
            }
        }
    }

    private bool VerifyExistingMiddlewareReachable(
        string host,
        System.Threading.CancellationToken ct,
        out string error)
    {
        error = null;
        int playerPort = ResolveCapturePlayerPort();
        Debug.Log("[GAMA][CAPTURE][8080][INFO] EXTERNAL MIDDLEWARE MODE — no kill, no restart");
        Debug.Log("[GAMA][MW] Existing monitor connection ws://" + host + ":" + captureMonitorPort + "/");
        Debug.Log("[GAMA][MW] Existing player socket connection ws://" + host + ":" + playerPort + "/");

        bool monitorReady;
        bool playerReady;
        try
        {
            monitorReady = GamaEditorMiddlewareOrchestrator.IsTcpPortOpenAsync(host, captureMonitorPort, 3000, ct)
                .GetAwaiter()
                .GetResult();
            playerReady = GamaEditorMiddlewareOrchestrator.IsTcpPortOpenAsync(host, playerPort, 3000, ct)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex)
        {
            error = "Existing middleware verification interrupted: " + ex.Message;
            return false;
        }

        if (!monitorReady)
        {
            error = "The existing monitor ws://" + host + ":" + captureMonitorPort +
                    "/ is not responding. Launch simple.webplatform manually, then try again.";
            return false;
        }

        if (!playerReady)
        {
            error = "The existing player socket ws://" + host + ":" + playerPort +
                    "/ is not responding. Launch simple.webplatform manually, then try again.";
            return false;
        }

        return true;
    }

    private static string ResolveCmdExe()
    {
        string comspec = Environment.GetEnvironmentVariable("COMSPEC");
        if (!string.IsNullOrWhiteSpace(comspec) && File.Exists(comspec))
        {
            return comspec;
        }

        string systemCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        return File.Exists(systemCmd) ? systemCmd : "cmd.exe";
    }

    private void StartCaptureFlow(bool launchGama, bool managedFromUnity = false)
    {
        if (captureFlowActive || captureTask != null)
        {
            EditorUtility.DisplayDialog("Capture", "A capture is already in progress.", "OK");
            return;
        }

        bool selectedGamaPreviewMode = managedFromUnity && captureUseExternalMiddleware;
        string uiSelectedModelPath = string.Empty;
        string runtimeModelPath = string.Empty;
        string runtimeExperimentName = string.Empty;
        if (selectedGamaPreviewMode)
        {
            if (analysis != null)
            {
                runtimeModelPath = SafeFullPath(analysis.SourcePath);
                uiSelectedModelPath = runtimeModelPath;
                runtimeExperimentName = analysis.Name ?? string.Empty;
            }
        }
        else if (!TryResolveRuntimeSelection(
                out uiSelectedModelPath,
                out runtimeModelPath,
                out runtimeExperimentName,
                out string selectionError))
        {
            EditorUtility.DisplayDialog("Capture", selectionError, "OK");
            captureFlowActive = false;
            return;
        }

        Debug.Log("[SYNC] UI selected model = " + uiSelectedModelPath);
        Debug.Log("[SYNC] Runtime capture model = " + runtimeModelPath);
        Debug.Log("[SYNC] Runtime experiment = " + runtimeExperimentName);

        if (managedFromUnity)
        {
            captureManagedFromUnity = true;
            captureSkipRemoteLoad = false;
            captureUseLocalMiddleware = false;
            if (string.IsNullOrWhiteSpace(capturePort) ||
                GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(capturePort))
            {
                capturePort = "8080";
            }

            EditorPrefs.SetBool(GamaCaptureUseLocalMiddlewarePrefKey, false);
            EditorPrefs.SetBool(GamaCaptureSkipRemoteLoadPrefKey, false);
            EditorPrefs.SetString(GamaCapturePortPrefKey, capturePort);
        }

        if (!captureUseLocalMiddleware && !captureSkipRemoteLoad && !managedFromUnity)
        {
            UnityEngine.Debug.LogWarning(
                "[GAMA] Capture with remote load (port 1000): prefer 'Managed by Unity' or 'Launch and capture'.");
        }

        captureFlowActive = true;
        int panelSession = System.Threading.Interlocked.Increment(ref GamaPanelWindowCaptureSessionCounter);
        UnityEngine.Debug.Log("[GAMA][DBG][panel #" + panelSession + "] StartCaptureFlow launchGama=" + launchGama +
            " managed=" + managedFromUnity +
            " externalMw=" + (managedFromUnity && captureUseExternalMiddleware) +
            " directMw=" + captureUseLocalMiddleware + " port=" + (capturePort ?? "?") +
            " monitorPort=" + captureMonitorPort +
            " exp=" + runtimeExperimentName +
            " model=" + runtimeModelPath);

        string host = string.IsNullOrWhiteSpace(captureHost) ? PlayerPrefs.GetString("IP", "localhost") : captureHost.Trim();
        string port = string.IsNullOrWhiteSpace(capturePort) ? PlayerPrefs.GetString("PORT", "8080") : capturePort.Trim();
        string rawId = string.IsNullOrWhiteSpace(captureConnectionId) ? string.Empty : captureConnectionId.Trim();
        string id = string.IsNullOrEmpty(rawId) ? StaticInformation.getId() : rawId;
        string outDir = string.IsNullOrWhiteSpace(gamaJsonExportOutputDir) ? null : gamaJsonExportOutputDir.Trim();

        if (string.IsNullOrEmpty(outDir))
        {
            string defaultOut = Path.Combine(Application.temporaryCachePath, "GamaFirstTickCapture");
            gamaJsonExportOutputDir = defaultOut;
            EditorPrefs.SetString(GamaJsonExportOutDirPrefKey, gamaJsonExportOutputDir);
            outDir = defaultOut;
        }

        try { Directory.CreateDirectory(outDir); }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("Capture", "Could not create output folder: " + ex.Message, "OK");
            captureFlowActive = false;
            return;
        }

        if ((launchGama || managedFromUnity) &&
            !selectedGamaPreviewMode &&
            string.IsNullOrWhiteSpace(runtimeModelPath))
        {
            EditorUtility.DisplayDialog("Capture", "First explore a valid experiment from the Workspace tab.", "OK");
            captureFlowActive = false;
            return;
        }

        if (!selectedGamaPreviewMode &&
            !string.IsNullOrWhiteSpace(runtimeModelPath) &&
            !GamaEditorUnityModelValidation.TryValidateUnityCaptureTarget(
                runtimeModelPath,
                runtimeExperimentName,
                out string unityCompatError))
        {
            EditorUtility.DisplayDialog("Capture — incompatible Unity model", unityCompatError, "OK");
            captureFlowActive = false;
            captureRuntimeStatus = unityCompatError;
            UnityEngine.Debug.LogWarning("[GAMA] Capture refused: " + unityCompatError);
            return;
        }

        bool useExternalMiddleware = managedFromUnity && captureUseExternalMiddleware;
        bool manageMiddlewareFromUnity = managedFromUnity && !captureUseExternalMiddleware;

        if (manageMiddlewareFromUnity)
        {
            if (!EnsureGeneratedLearningPackage(runtimeModelPath, runtimeExperimentName, out string learningRoot, out string learningError))
            {
                EditorUtility.DisplayDialog("Capture", learningError, "OK");
                captureFlowActive = false;
                return;
            }

            generatedLearningPackageRoot = learningRoot;
            Debug.Log("[GAMA][SYNC] Generated middleware package root: " + generatedLearningPackageRoot);
        }

        captureCts?.Dispose();
        captureCts = new System.Threading.CancellationTokenSource();
        captureRuntimeStatus = "Starting...";
        captureStartedAt = EditorApplication.timeSinceStartup;

        if (useExternalMiddleware)
        {
            string existingMwError;
            if (!VerifyExistingMiddlewareReachable(host, captureCts.Token, out existingMwError))
            {
                EditorUtility.DisplayDialog("Capture", existingMwError, "OK");
                captureCts?.Dispose(); captureCts = null;
                captureFlowActive = false;
                captureRuntimeStatus = existingMwError;
                return;
            }
        }
        else if (manageMiddlewareFromUnity)
        {
            string mwError;
            if (!RestartMiddlewareForUnitySelection(host, runtimeModelPath, runtimeExperimentName, captureCts.Token, out mwError))
            {
                EditorUtility.DisplayDialog("Capture", mwError, "OK");
                captureCts?.Dispose(); captureCts = null;
                captureFlowActive = false;
                captureRuntimeStatus = mwError;
                return;
            }
        }
        if (managedFromUnity && !selectedGamaPreviewMode)
        {
            GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult diagnosis;
            try
            {
                diagnosis = GamaEditorMiddlewareOrchestrator.DiagnoseCatalogAsync(
                        host,
                        captureMonitorPort,
                        runtimeExperimentName,
                        runtimeModelPath,
                        captureCts.Token,
                        UnityEngine.Debug.Log)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                AbortCaptureIfRunning("Catalog diagnosis failed", purgePlayer: false);
                EditorUtility.DisplayDialog("Capture",
                    "Monitor catalog diagnosis impossible: " + ex.Message,
                    "OK");
                captureFlowActive = false;
                return;
            }

            if (!diagnosis.Success)
            {
                string message = BuildCatalogDiagnosisStatus(diagnosis);
                if (useExternalMiddleware)
                {
                    message = "The running middleware doesn't know this model. " +
                              "Restart it manually with the correct package or select an already catalogued experiment.\n\n" +
                              message;
                }

                AbortCaptureIfRunning("Incompatible middleware catalog", purgePlayer: false);
                EditorUtility.DisplayDialog("Capture blocked (middleware catalog)", message, "OK");
                captureFlowActive = false;
                captureRuntimeStatus = message;
                return;
            }
        }
        else if (selectedGamaPreviewMode)
        {
            Debug.Log("[GAMA][PREVIEW][SELECTED] Preview de l'expérience sélectionnée dans GAMA");
            Debug.Log("[GAMA][PREVIEW][SELECTED] Aucun catalogue middleware");
            Debug.Log("[GAMA][PREVIEW][SELECTED] No settings.json");
            Debug.Log("[GAMA][PREVIEW][SELECTED] No middleware restart");
            Debug.Log("[GAMA][PREVIEW][SELECTED] Play-like Runtime sequence");
        }

        if (launchGama)
        {
            if (!GamaHeadlessPackagePaths.TryGetBundledRunnerBat(out string runnerPath, out string runnerError))
            {
                EditorUtility.DisplayDialog("Capture", runnerError, "OK");
                AbortCaptureIfRunning(runnerError);
                return;
            }

            string outDirFull = SafeFullPath(outDir);
            string gamlFull = SafeFullPath(runtimeModelPath);
            string headlessBatFull = SafeFullPath(gamaHeadlessBatPath);

            if (captureMode != "custom" && string.IsNullOrEmpty(headlessBatFull))
            {
                EditorUtility.DisplayDialog(
                    "Capture",
                    "Please specify the path to gama-headless.bat in the Workspace tab (or choose 'custom' mode).",
                    "OK");
                AbortCaptureIfRunning("Missing gama-headless.bat");
                return;
            }

            System.Collections.Generic.Dictionary<string, string> env = new System.Collections.Generic.Dictionary<string, string>
            {
                ["GAMA_HEADLESS_BAT"] = headlessBatFull,
                ["GAMA_GAML_PATH"] = gamlFull,
                ["GAMA_BATCH_NAME"] = string.IsNullOrEmpty(gamaHeadlessBatchName) ? runtimeExperimentName : gamaHeadlessBatchName,
                ["GAMA_JSON_OUTPUT_DIR"] = outDirFull,
                ["GAMA_HEADLESS_MODE"] = captureMode,
                ["GAMA_HEADLESS_CUSTOM"] = gamaHeadlessCustomCmd ?? string.Empty,
                ["UNITY_GAMA_JSON_EXPORT_DIR"] = outDirFull,
                ["GAMA_UNITY_JSON_OUT"] = outDirFull
            };

            if (!string.IsNullOrWhiteSpace(gamaHeadlessWorkingDir))
            {
                env["GAMA_HEADLESS_CWD"] = SafeFullPath(gamaHeadlessWorkingDir);
            }

            string gamaError;
            captureGamaProcess = GamaEditorBackgroundProcess.StartCmdScript(
                "GAMA",
                runnerPath,
                Path.GetDirectoryName(runnerPath),
                env,
                out gamaError);

            if (captureGamaProcess == null)
            {
                EditorUtility.DisplayDialog("Capture", "Failed to start GAMA: " + gamaError, "OK");
                AbortCaptureIfRunning(gamaError);
                return;
            }
        }

        int captureTimeoutMs = Mathf.Clamp(gamaHeadlessTimeoutSeconds, 30, 7200) * 1000;
        bool useDirectGama = !managedFromUnity &&
                             (captureUseLocalMiddleware ||
                              GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(port));
        if (useDirectGama)
        {
            captureTimeoutMs = Math.Max(captureTimeoutMs, 180_000);
        }

        if (!captureUseLocalMiddleware && GamaEditorFirstTickCapture.IsGamaNativeWebSocketPort(port))
        {
            UnityEngine.Debug.LogWarning(
                "[GAMA] Port " + port + " = integrated GAMA server: direct capture mode (load/play protocol). " +
                "For the Node middleware, launch simple.webplatform and use port 8080.");
        }

        if (managedFromUnity && !captureUseLocalMiddleware)
        {
            if (selectedGamaPreviewMode)
            {
                UnityEngine.Debug.Log(
                    "[GAMA] Preview from GAMA: external middleware ws://" + host + ":" + port +
                    "/, Play-like sequence, no monitor catalog.");
            }
            else
            {
                UnityEngine.Debug.Log(
                    "[GAMA] Managed by Unity: monitor ws://" + host + ":" + captureMonitorPort +
                    "/ (launch_experiment) then headset ws://" + host + ":" + port + "/.");
            }
        }
        else if (captureSkipRemoteLoad && !captureUseLocalMiddleware)
        {
            UnityEngine.Debug.Log(
                "[GAMA] Experiment already open: 8080 middleware only (diagnostic, no launch monitor).");
        }

        int maxWorldFrames = Mathf.Clamp(captureMaxWorldFrames, 3, 120);
        float worldPhaseSec = Mathf.Clamp(captureWorldPhaseSeconds, 5f, 120f);
        float stableSec = Mathf.Clamp(capturePreviewStableSeconds, 1f, 30f);

        if (useDirectGama)
        {
            string gamaPort = string.IsNullOrWhiteSpace(port) || port == "8080" ? "1000" : port;
            captureTask = GamaEditorFirstTickCapture.CaptureAsync(
                "localhost",
                gamaPort,
                outDir,
                id,
                5000,
                10_000,
                captureTimeoutMs,
                true,
                runtimeModelPath,
                runtimeExperimentName,
                maxWorldFrames,
                worldPhaseSec,
                captureSkipRemoteLoad,
                false,
                false,
                captureMonitorPort,
                captureDynamicSpeciesRegex,
                captureStopWhenDynamicAgentsFound,
                false,
                captureStopWhenPreviewCacheStable,
                stableSec,
                UnityEngine.Debug.Log,
                captureCts.Token);

            captureRuntimeStatus = "Direct capture started to GAMA ws://localhost:" + gamaPort + "/  (min. " +
                (captureTimeoutMs / 1000) + " s, extended after load/create_player). id = " + id + ".";
            UnityEngine.Debug.Log("[GAMA] Direct GAMA Capture: ws://localhost:" + gamaPort + "/ id=\"" + id + "\".");
        }
        else
        {
            captureTimeoutMs = Math.Max(captureTimeoutMs, 120_000);
            int connectTimeoutMs = launchGama ? Mathf.Min(60_000, captureTimeoutMs) : 30_000;
            captureTask = GamaEditorFirstTickCapture.CaptureAsync(
                host,
                port,
                outDir,
                id,
                5000,
                connectTimeoutMs,
                captureTimeoutMs,
                false,
                runtimeModelPath,
                runtimeExperimentName,
                maxWorldFrames,
                worldPhaseSec,
                managedFromUnity ? false : captureSkipRemoteLoad,
                managedFromUnity,
                managedFromUnity && !captureUseExternalMiddleware,
                captureMonitorPort,
                captureDynamicSpeciesRegex,
                captureStopWhenDynamicAgentsFound,
                capturePauseExperimentAfterPreview,
                captureStopWhenPreviewCacheStable,
                stableSec,
                UnityEngine.Debug.Log,
                captureCts.Token);

            captureRuntimeStatus = "Middleware capture ws://" + host + ":" + port + "/ (min. " + (captureTimeoutMs / 1000) +
                " s). id=\"" + id + "\". " +
                (managedFromUnity
                    ? captureUseExternalMiddleware
                        ? "Preview from GAMA: external middleware, Play-like sequence on 8080, no catalog/restart."
                        : "Unity drives monitor " + captureMonitorPort + " then listens to json_output on 8080."
                    : captureSkipRemoteLoad
                        ? "8080 Middleware only (diagnostic)."
                        : "Automatic load/play/create_player if experiment is imported.");
            UnityEngine.Debug.Log("[GAMA] Capture middleware id=\"" + id + "\" ws://" + host + ":" + port + "/");
        }

        if (captureTask == null)
        {
            captureFlowActive = false;
        }

        Repaint();
    }

    internal bool TryGetOpenPanelSelection(out string modelPath, out string experimentName)
    {
        return TryResolveRuntimeSelection(out _, out modelPath, out experimentName, out _);
    }

    private bool TryResolveRuntimeSelection(
        out string uiSelectedModelPath,
        out string runtimeModelPath,
        out string runtimeExperimentName,
        out string errorMessage)
    {
        uiSelectedModelPath = string.Empty;
        runtimeModelPath = string.Empty;
        runtimeExperimentName = string.Empty;
        errorMessage = null;

        if (selectedExperimentIndex < 0 || selectedExperimentIndex >= experimentOptions.Count)
        {
            if (analysis != null &&
                !string.IsNullOrWhiteSpace(analysis.SourcePath) &&
                !string.IsNullOrWhiteSpace(analysis.Name))
            {
                uiSelectedModelPath = SafeFullPath(analysis.SourcePath);
                runtimeModelPath = uiSelectedModelPath;
                runtimeExperimentName = analysis.Name.Trim();
                return true;
            }

            errorMessage = "No experiment selected in Unity. Select an experiment in the dropdown.";
            return false;
        }

        GamaPanelExperimentOption selectedOption = experimentOptions[selectedExperimentIndex];
        if (selectedOption == null)
        {
            errorMessage = "Invalid experiment selection. Re-explore the workspace.";
            return false;
        }

        bool needsRefresh = analysis == null ||
                            !string.Equals(NormalizePath(analysis.SourcePath), NormalizePath(selectedOption.SourcePath), StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals((analysis.Name ?? string.Empty).Trim(), (selectedOption.Name ?? string.Empty).Trim(), StringComparison.Ordinal);
        if (needsRefresh)
        {
            analysis = GamaPanelExperimentAnalyzer.Analyze(selectedOption);
        }

        uiSelectedModelPath = SafeFullPath(selectedOption.SourcePath);
        runtimeModelPath = uiSelectedModelPath;
        runtimeExperimentName = (selectedOption.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(runtimeModelPath) || string.IsNullOrWhiteSpace(runtimeExperimentName))
        {
            errorMessage = "Incomplete Unity selection (modelPath/experimentName).";
            return false;
        }

        GamaEditorRuntimeSelectionStore.Save(runtimeModelPath, runtimeExperimentName);
        return true;
    }

    private void InvalidateCaptureSelectionCache()
    {
        staticPreviewPrecisionJsonPath = string.Empty;
        staticPreviewPropertiesJsonPath = string.Empty;
        staticPreviewWorldJsonPath = string.Empty;
        staticPreviewWorldTickIndex = 0;
        availableWorldTickPaths.Clear();
        EditorPrefs.DeleteKey(StaticPreviewPrecisionJsonPrefKey);
        EditorPrefs.DeleteKey(StaticPreviewPropertiesJsonPrefKey);
        EditorPrefs.DeleteKey(StaticPreviewWorldJsonPrefKey);
        EditorPrefs.DeleteKey(StaticPreviewWorldTickPrefKey);
        captureRuntimeStatus = "Selection changed: preview cache invalidated, next capture will force current Unity selection.";
    }

    private void StartCatalogDiagnosis()
    {
        if (!TryResolveRuntimeSelection(
                out _,
                out string runtimeModelPath,
                out string runtimeExperimentName,
                out string selectionError))
        {
            catalogDiagnosisStatus = selectionError;
            captureRuntimeStatus = selectionError;
            return;
        }

        string host = string.IsNullOrWhiteSpace(captureHost) ? PlayerPrefs.GetString("IP", "localhost") : captureHost.Trim();
        if (captureManagedFromUnity && !captureUseLocalMiddleware && !captureUseExternalMiddleware)
        {
            if (!EnsureGeneratedLearningPackage(runtimeModelPath, runtimeExperimentName, out string learningRoot, out string learningError))
            {
                catalogDiagnosisStatus = learningError;
                captureRuntimeStatus = learningError;
                EditorUtility.DisplayDialog("Catalog Diagnosis", learningError, "OK");
                return;
            }

            generatedLearningPackageRoot = learningRoot;
            Debug.Log("[GAMA][SYNC] Generated middleware package root: " + generatedLearningPackageRoot);

            using (System.Threading.CancellationTokenSource restartCts =
                   new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120)))
            {
                if (!RestartMiddlewareForUnitySelection(
                        host,
                        runtimeModelPath,
                        runtimeExperimentName,
                        restartCts.Token,
                        out string restartError))
                {
                    catalogDiagnosisStatus = restartError;
                    captureRuntimeStatus = restartError;
                    EditorUtility.DisplayDialog("Catalog Diagnosis", restartError, "OK");
                    return;
                }
            }
        }
        else if (captureManagedFromUnity && !captureUseLocalMiddleware)
        {
            using (System.Threading.CancellationTokenSource verifyCts =
                   new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                if (!VerifyExistingMiddlewareReachable(host, verifyCts.Token, out string verifyError))
                {
                    catalogDiagnosisStatus = verifyError;
                    captureRuntimeStatus = verifyError;
                    EditorUtility.DisplayDialog("Catalog Diagnosis", verifyError, "OK");
                    return;
                }
            }
        }

        catalogDiagnosisStatus = "Monitor catalog diagnosis in progress...";
        captureRuntimeStatus = catalogDiagnosisStatus;
        Repaint();
        catalogDiagnosisTask = GamaEditorMiddlewareOrchestrator.DiagnoseCatalogAsync(
            host,
            captureMonitorPort,
            runtimeExperimentName,
            runtimeModelPath,
            System.Threading.CancellationToken.None,
            UnityEngine.Debug.Log);
    }

    private void SyncSelectedModelWithMiddleware()
    {
        if (!TryResolveRuntimeSelection(
                out _,
                out string runtimeModelPath,
                out string runtimeExperimentName,
                out string selectionError))
        {
            captureRuntimeStatus = selectionError;
            return;
        }

        if (!EnsureGeneratedLearningPackage(runtimeModelPath, runtimeExperimentName, out string learningRoot, out string learningError))
        {
            captureRuntimeStatus = learningError;
            Debug.LogError("[GAMA][SYNC] " + learningError);
            return;
        }

        generatedLearningPackageRoot = learningRoot;
        captureRuntimeStatus =
            "[GAMA][SYNC] Generated package: " + generatedLearningPackageRoot +
            " | Restart the middleware with LEARNING_PACKAGE_PATH including this folder.";
        Debug.Log("[GAMA][SYNC] Selected model: " + runtimeModelPath);
        Debug.Log("[GAMA][SYNC] Experiments found: " + runtimeExperimentName);
        Debug.Log("[GAMA][SYNC] Generated middleware package: " + generatedLearningPackageRoot);
        TryAssignGeneratedMiddlewareLauncherScript();
    }

    private void ConfigureAndOfferMiddlewareRestart()
    {
        if (!TryResolveRuntimeSelection(
                out _,
                out string runtimeModelPath,
                out string runtimeExperimentName,
                out string selectionError))
        {
            EditorUtility.DisplayDialog("Middleware", selectionError, "OK");
            return;
        }

        if (!EnsureGeneratedLearningPackage(runtimeModelPath, runtimeExperimentName, out string learningRoot, out string learningError))
        {
            EditorUtility.DisplayDialog("Middleware", learningError, "OK");
            return;
        }

        generatedLearningPackageRoot = learningRoot;
        Debug.Log("[GAMA][SYNC] Generated middleware package root: " + generatedLearningPackageRoot);

        using (System.Threading.CancellationTokenSource restartCts =
               new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(120)))
        {
            string host = string.IsNullOrWhiteSpace(captureHost) ? "localhost" : captureHost.Trim();
            if (!RestartMiddlewareForUnitySelection(
                    host,
                    runtimeModelPath,
                    runtimeExperimentName,
                    restartCts.Token,
                    out string restartError))
            {
                EditorUtility.DisplayDialog("Middleware", restartError, "OK");
                captureRuntimeStatus = restartError;
                return;
            }

            captureRuntimeStatus = "Middleware restarted. Catalog diagnosis in progress...";
            Repaint();
            GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult diagnosis =
                GamaEditorMiddlewareOrchestrator.DiagnoseCatalogAsync(
                        host,
                        captureMonitorPort,
                        runtimeExperimentName,
                        runtimeModelPath,
                        restartCts.Token,
                        UnityEngine.Debug.Log)
                    .GetAwaiter()
                    .GetResult();

            captureRuntimeStatus = BuildCatalogDiagnosisStatus(diagnosis);
            if (!diagnosis.Success)
            {
                EditorUtility.DisplayDialog("Middleware Catalog", captureRuntimeStatus, "OK");
            }
        }
    }

    private bool TryAssignGeneratedMiddlewareLauncherScript()
    {
        if (string.IsNullOrWhiteSpace(generatedLearningPackageRoot))
        {
            return false;
        }

        if (!GamaEditorMiddlewareLauncher.TryWriteLauncherScript(
                generatedLearningPackageRoot,
                out string launcherPath,
                out string launcherError))
        {
            EditorUtility.DisplayDialog("Middleware", launcherError, "OK");
            return false;
        }

        middlewareScriptPath = launcherPath;
        EditorPrefs.SetString(GamaMiddlewareScriptPrefKey, middlewareScriptPath);
        Debug.Log("[GAMA][MW] Middleware script registered: " + middlewareScriptPath);
        return true;
    }

    private bool EnsureGeneratedLearningPackage(
        string runtimeModelPath,
        string runtimeExperimentName,
        out string learningRoot,
        out string error)
    {
        learningRoot = string.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(runtimeModelPath) || string.IsNullOrWhiteSpace(runtimeExperimentName))
        {
            error = "Cannot generate a learning package: missing modelPath/experimentName.";
            return false;
        }

        try
        {
            string modelFullPath = Path.GetFullPath(runtimeModelPath);
            string root = Path.Combine(Application.temporaryCachePath, "GamaGeneratedLearningPackages");
            if (!TryClearGeneratedLearningPackageRoot(root, out string cleanupError))
            {
                error = cleanupError;
                return false;
            }

            string packageFolderName = BuildLearningPackageName(modelFullPath, runtimeExperimentName);
            string packageFolder = Path.Combine(root, packageFolderName);
            Directory.CreateDirectory(packageFolder);

            string settingsPath = Path.Combine(packageFolder, "settings.json");
            string displayName = Path.GetFileNameWithoutExtension(modelFullPath);
            string settingsJson = "{\n" +
                                  "  \"type\": \"json_settings\",\n" +
                                  "  \"name\": \"" + JsonEscape(displayName) + "\",\n" +
                                  "  \"splashscreen\": \"\",\n" +
                                  "  \"model_file_path\": \"" + JsonEscape(modelFullPath) + "\",\n" +
                                  "  \"experiment_name\": \"" + JsonEscape(runtimeExperimentName.Trim()) + "\",\n" +
                                  "  \"minimal_players\": \"0\",\n" +
                                  "  \"maximal_players\": \"4\",\n" +
                                  "  \"selected_monitoring\": \"gama_screen\"\n" +
                                  "}";
            File.WriteAllText(settingsPath, settingsJson);
            bool exists = File.Exists(settingsPath);
            Debug.Log("[GAMA][SYNC] settings.json path=" + settingsPath);
            Debug.Log("[GAMA][SYNC] settings.json exists=" + exists);
            if (!exists)
            {
                error = "settings.json not written: " + settingsPath;
                return false;
            }

            JObject settings = JObject.Parse(File.ReadAllText(settingsPath));
            string parsedExperiment = settings["experiment_name"]?.ToString() ?? string.Empty;
            string parsedModel = settings["model_file_path"]?.ToString() ?? string.Empty;
            Debug.Log("[GAMA][SYNC] experiment_name=" + parsedExperiment);
            Debug.Log("[GAMA][SYNC] model_file_path=" + parsedModel);
            lastGeneratedSettingsJsonPath = settingsPath;
            lastGeneratedSettingsJsonContent = settingsJson;
            Debug.Log("[GAMA][SYNC] settings.json content:\n" + settingsJson);
            LogGeneratedPackageTree(root);

            if (!string.Equals(parsedExperiment.Trim(), runtimeExperimentName.Trim(), StringComparison.Ordinal))
            {
                error = "Invalid settings.json: experiment_name=" + parsedExperiment +
                        " expected=" + runtimeExperimentName;
                return false;
            }

            if (!string.Equals(NormalizePath(parsedModel), NormalizePath(modelFullPath), StringComparison.OrdinalIgnoreCase))
            {
                error = "Invalid settings.json: model_file_path=" + parsedModel +
                        " expected=" + modelFullPath;
                return false;
            }

            learningRoot = root;
            return true;
        }
        catch (Exception ex)
        {
            error = "Middleware learning package generation impossible: " + ex.Message;
            return false;
        }
    }

    private static bool TryClearGeneratedLearningPackageRoot(string root, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "Empty learning package root folder.";
            return false;
        }

        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            return true;
        }

        List<string> failures = new List<string>();
        foreach (string dir in Directory.GetDirectories(root))
        {
            try { Directory.Delete(dir, true); }
            catch (Exception ex) { failures.Add(dir + " : " + ex.Message); }
        }

        foreach (string file in Directory.GetFiles(root))
        {
            try { File.Delete(file); }
            catch (Exception ex) { failures.Add(file + " : " + ex.Message); }
        }

        if (failures.Count > 0)
        {
            error = "Failed to clean old Unity learning packages: " + string.Join(" | ", failures);
            return false;
        }

        return true;
    }

    private static string BuildLearningPackageName(string modelFullPath, string experimentName)
    {
        string modelName = Path.GetFileNameWithoutExtension(modelFullPath);
        string safeModel = Regex.Replace(modelName ?? "model", "[^A-Za-z0-9_-]", "_");
        string safeExp = Regex.Replace(experimentName ?? "experiment", "[^A-Za-z0-9_-]", "_");
        string hash = ComputeStableHash(modelFullPath + "|" + experimentName).ToString(CultureInfo.InvariantCulture);
        return "unity_" + safeModel + "_" + safeExp + "_" + hash;
    }

    private static int ComputeStableHash(string value)
    {
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash * 31) + value[i];
            }

            return Math.Abs(hash);
        }
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    private Dictionary<string, string> BuildMiddlewareEnvironmentForSelection(string runtimeModelPath)
    {
        Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string primary = generatedLearningPackageRoot;
        if (!string.IsNullOrWhiteSpace(primary))
        {
            env["LEARNING_PACKAGE_PATH"] = Path.GetFullPath(primary);
        }

        env["EXTRA_LEARNING_PACKAGE_PATH"] = string.Empty;
        env["MONITOR_WS_PORT"] = captureMonitorPort.ToString(CultureInfo.InvariantCulture);
        env["HEADSET_WS_PORT"] = ResolveCapturePlayerPort().ToString(CultureInfo.InvariantCulture);

        return env;
    }

    private static void LogGeneratedPackageTree(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            Debug.Log("[GAMA][SYNC] Generated package tree: (missing root)");
            return;
        }

        Debug.Log("[GAMA][SYNC] Generated package tree:");
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            Debug.Log("[GAMA][SYNC] - " + file);
        }
    }

    private string BuildCatalogDiagnosisStatus(GamaEditorMiddlewareOrchestrator.CatalogDiagnosisResult diagnosis)
    {
        if (diagnosis == null)
        {
            return "Catalog diagnosis unavailable.";
        }

        string models = diagnosis.AvailableModels.Count == 0 ? "(none)" : string.Join(" | ", diagnosis.AvailableModels);
        string experiments = diagnosis.AvailableExperiments.Count == 0 ? "(none)" : string.Join(" | ", diagnosis.AvailableExperiments);
        if (diagnosis.Success)
        {
            return "[GAMA][ORCH] MATCH OK model=" + diagnosis.RequestedModelPath +
                   " experiment=" + diagnosis.RequestedExperimentName +
                   " | catalog models=" + models +
                   " experiments=" + experiments;
        }

        string statusLabel = diagnosis.Status.ToString().ToUpperInvariant();
        string settingsSummary = string.IsNullOrWhiteSpace(lastGeneratedSettingsJsonContent)
            ? "(settings.json not generated in this session)"
            : lastGeneratedSettingsJsonContent;
        string action = captureUseExternalMiddleware
            ? "1) The running middleware doesn't know this model.\n" +
              "2) Restart it manually with the correct package, or select an already catalogued experiment.\n" +
              "3) Restart the diagnosis without Unity stopping or restarting Node.\n"
            : "1) Stop the old PIDs on the monitor/player ports.\n" +
              "2) Restart from simple.webplatform with LEARNING_PACKAGE_PATH pointing to GamaGeneratedLearningPackages.\n" +
              "3) Verify that the catalog lists the exact Unity .gaml.\n";
        string conclusion = captureUseExternalMiddleware
            ? "The running middleware does not catalog the .gaml selected in Unity."
            : "The middleware ignores the generated package and still loads another learning package (often the demo).";

        return "MODELNOTFOUND\n\n" +
               "Unity asks for:\n" +
               "model=" + diagnosis.RequestedModelPath + "\n" +
               "experiment=" + diagnosis.RequestedExperimentName + "\n\n" +
               "Running middleware:\n" +
               "WorkingDirectory=" + (string.IsNullOrWhiteSpace(lastMiddlewareWorkingDirectory) ? "?" : lastMiddlewareWorkingDirectory) + "\n" +
               "PID=" + (lastMiddlewareProcessId > 0 ? lastMiddlewareProcessId.ToString(CultureInfo.InvariantCulture) : "?") + "\n" +
               "LEARNING_PACKAGE_PATH=" + (string.IsNullOrWhiteSpace(lastMiddlewareLearningPackagePath) ? "?" : lastMiddlewareLearningPackagePath) + "\n" +
               "EXTRA_LEARNING_PACKAGE_PATH=" + (lastMiddlewareExtraLearningPackagePath ?? string.Empty) + "\n\n" +
               "Generated Unity Package:\n" +
               "root=" + (string.IsNullOrWhiteSpace(generatedLearningPackageRoot) ? "?" : generatedLearningPackageRoot) + "\n" +
               "settings=" + (string.IsNullOrWhiteSpace(lastGeneratedSettingsJsonPath) ? "?" : lastGeneratedSettingsJsonPath) + "\n" +
               settingsSummary + "\n\n" +
               "Received catalog:\n" +
               "models=" + models + "\n" +
               "experiments=" + experiments + "\n\n" +
               "Conclusion:\n" +
               conclusion + "\n\n" +
               "Action:\n" +
               action +
               (string.IsNullOrWhiteSpace(diagnosis.Error) ? string.Empty : ("\nDetails: " + diagnosis.Error));
    }

    private static string SafeFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private void OnCaptureFinished(System.Threading.Tasks.Task<GamaEditorFirstTickCapture.CaptureResult> finishedTask)
    {
        captureFlowActive = false;
        string userAbortReason = pendingCaptureAbortUserMessage;
        pendingCaptureAbortUserMessage = null;

        if (captureGamaProcess != null)
        {
            try { _ = captureGamaProcess.StopAsync(1500); } catch { /* ignore */ }
            try { captureGamaProcess.Dispose(); } catch { /* ignore */ }
            captureGamaProcess = null;
        }

        if (captureMiddlewareProcess != null)
        {
            try { _ = captureMiddlewareProcess.StopAsync(2500); } catch { /* ignore */ }
            try { captureMiddlewareProcess.Dispose(); } catch { /* ignore */ }
            captureMiddlewareProcess = null;
        }

        try { captureCts?.Dispose(); } catch { /* ignore */ }
        captureCts = null;

        if (userAbortReason != null)
        {
            captureRuntimeStatus = "Capture cancelled: " + userAbortReason;
            Repaint();
            return;
        }

        GamaEditorFirstTickCapture.CaptureResult result = null;
        try
        {
            result = finishedTask.Result;
        }
        catch (Exception ex)
        {
            captureRuntimeStatus = "Capture failed: " + ex.Message;
            UnityEngine.Debug.LogError("[GAMA] Capture : " + ex);
        }

        if (result == null)
        {
            Repaint();
            return;
        }

        if (!result.Success)
        {
            captureRuntimeStatus = "Capture failed: " + (result.Error ?? "unknown reason");
            UnityEngine.Debug.LogError("[GAMA] Capture : " + result.Error + "\n" + result.LogTrail);
            Repaint();
            return;
        }

        staticPreviewPrecisionJsonPath = result.PrecisionJsonPath;
        staticPreviewPropertiesJsonPath = result.PropertiesJsonPath;
        RefreshAvailableWorldTicks();
        if (result.BestWorldTickIndex >= 0 && result.BestWorldTickIndex < availableWorldTickPaths.Count)
        {
            staticPreviewWorldTickIndex = result.BestWorldTickIndex;
        }
        else if (availableWorldTickPaths.Count > 0)
        {
            staticPreviewWorldTickIndex = availableWorldTickPaths.Count - 1;
        }

        staticPreviewWorldJsonPath = ResolvePreviewWorldJsonPath();
        if (string.IsNullOrEmpty(staticPreviewWorldJsonPath))
        {
            staticPreviewWorldJsonPath = result.WorldJsonPath;
        }

        EditorPrefs.SetString(StaticPreviewPrecisionJsonPrefKey, staticPreviewPrecisionJsonPath);
        EditorPrefs.SetString(StaticPreviewPropertiesJsonPrefKey, staticPreviewPropertiesJsonPath);
        EditorPrefs.SetString(StaticPreviewWorldJsonPrefKey, staticPreviewWorldJsonPath);
        EditorPrefs.SetInt(StaticPreviewWorldTickPrefKey, staticPreviewWorldTickIndex);

        string tickInfo = result.WorldFrameCount > 1
            ? " (" + result.WorldFrameCount + " chunks, cumulative preview tick " + result.BestWorldTickIndex + ")"
            : string.Empty;
        captureRuntimeStatus = "Capture OK: 3 JSON in " + Path.GetDirectoryName(staticPreviewPrecisionJsonPath) + tickInfo + ".";
        if (!result.DynamicAgentsFound && !string.IsNullOrEmpty(result.PreviewWarning))
        {
            captureRuntimeStatus += " " + result.PreviewWarning;
            UnityEngine.Debug.LogWarning("[GAMA] " + result.PreviewWarning);
        }

        experimentStatus = captureRuntimeStatus;
        UnityEngine.Debug.Log("[GAMA] " + captureRuntimeStatus + "\n" + result.LogTrail);

        ApplySpeciesRenderOverridesToSimulationManager();

        if (gamaHeadlessRunPreviewAfter)
        {
            GenerateStaticPreview();
        }

        Repaint();
    }

    private void DrawAgentSettings()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Agents and Species", EditorStyles.boldLabel);
        if (agentOverrides.Count == 0)
        {
            EditorGUILayout.HelpBox("No species or create statements were detected in the experiment file.", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Agent / Species", GUILayout.Width(180f));
        GUILayout.Label("Count", GUILayout.Width(80f));
        GUILayout.Label("Prefab Hint", GUILayout.Width(150f));
        GUILayout.Label("Color", GUILayout.Width(120f));
        GUILayout.Label("Scale", GUILayout.Width(70f));
        GUILayout.Label("Visible", GUILayout.Width(60f));
        EditorGUILayout.EndHorizontal();

        agentsScroll = EditorGUILayout.BeginScrollView(agentsScroll, GUILayout.MinHeight(160f), GUILayout.MaxHeight(260f));
        for (int i = 0; i < agentOverrides.Count; i++)
        {
            GamaPanelAgentOverride agent = agentOverrides[i];
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(agent.Name, GUILayout.Width(180f));
            EditorGUILayout.LabelField(agent.CountExpression, GUILayout.Width(80f));
            EditorGUILayout.LabelField(agent.PrefabHint, GUILayout.Width(150f));
            
            EditorGUI.BeginChangeCheck();

            agent.OverrideColor = EditorGUILayout.Toggle(agent.OverrideColor, GUILayout.Width(18f));
            using (new EditorGUI.DisabledScope(!agent.OverrideColor))
            {
                agent.Color = EditorGUILayout.ColorField(agent.Color, GUILayout.Width(94f));
            }

            agent.OverrideScale = EditorGUILayout.Toggle(agent.OverrideScale, GUILayout.Width(18f));
            using (new EditorGUI.DisabledScope(!agent.OverrideScale))
            {
                agent.ScaleMultiplier = Mathf.Max(0f, EditorGUILayout.FloatField(agent.ScaleMultiplier, GUILayout.Width(52f)));
            }

            agent.OverrideVisibility = EditorGUILayout.Toggle(agent.OverrideVisibility, GUILayout.Width(18f));
            using (new EditorGUI.DisabledScope(!agent.OverrideVisibility))
            {
                agent.Visible = EditorGUILayout.Toggle(agent.Visible, GUILayout.Width(42f));
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                PushAgentOverridesToAsset(agent);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void PushAgentOverridesToAsset(GamaPanelAgentOverride agent)
    {
        GamaSpeciesRenderOverrides asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        if (asset != null)
        {
            GamaSpeciesRenderOverrideEntry entry = asset.GetOrCreateEntry(agent.Name);
            
            if (agent.OverrideColor)
            {
                entry.overrideColor = true;
                entry.color = agent.Color;
            }
            else
            {
                entry.overrideColor = false;
            }

            if (agent.OverrideScale)
            {
                entry.scaleMultiplier = agent.ScaleMultiplier;
            }
            else
            {
                entry.scaleMultiplier = 1f;
            }

            if (agent.OverrideVisibility)
            {
                entry.overrideRuntimeVisibility = true;
                entry.visibleInRuntime = agent.Visible;
            }
            else
            {
                entry.overrideRuntimeVisibility = false;
            }

            EditorUtility.SetDirty(asset);
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        }
    }

    private void DrawApplyControls()
    {
        EditorGUILayout.Space();
        DrawStaticPreviewTickSection();
        using (new EditorGUI.DisabledScope(analysis == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate Static Preview", GUILayout.Height(32f)))
            {
                GenerateStaticPreview();
            }

            if (GUILayout.Button("Clear Static Preview", GUILayout.Height(32f), GUILayout.Width(170f)))
            {
                ClearStaticPreview();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Apply to Current Unity Scene", GUILayout.Height(34f)))
            {
                ApplyExperimentSettingsToScene();
            }
        }
    }

    private void DrawWorkspaceExperimentList()
    {
        if (experimentOptions.Count == 0)
        {
            return;
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Detected Experiments", EditorStyles.boldLabel);
        workspaceExperimentListScroll = EditorGUILayout.BeginScrollView(workspaceExperimentListScroll, GUILayout.MinHeight(Mathf.Clamp(28f + experimentOptions.Count * 36f, 80f, 280f)));
        for (int i = 0; i < experimentOptions.Count; i++)
        {
            GamaPanelExperimentOption option = experimentOptions[i];
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(option.DisplayName, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Import Experiment", GUILayout.Width(150f), GUILayout.Height(22f)))
            {
                GoToImportExperimentWithIndex(i);
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }

    private void GoToImportExperimentWithIndex(int index)
    {
        if (index < 0 || index >= experimentOptions.Count)
        {
            return;
        }

        selectedExperimentIndex = index;
        AnalyzeSelectedExperiment();
        InvalidateCaptureSelectionCache();
        experimentStatus = "Experiment imported: " + experimentOptions[index].DisplayName + ". Adjust agents / scene, run cumulative preview or generate static preview.";
        selectedTab = TabImportExperiment;
        Repaint();
    }

    private bool TryPopulateExperimentOptions(out string errorMessage)
    {
        errorMessage = null;
        experimentOptions.Clear();
        selectedExperimentIndex = 0;
        analysis = null;
        agentOverrides.Clear();

        string normalizedPath = NormalizePath(experimentPath);
        if (File.Exists(normalizedPath))
        {
            experimentOptions = GamaPanelExperimentAnalyzer.FindExperimentsInFile(normalizedPath);
        }
        else if (Directory.Exists(normalizedPath))
        {
            experimentOptions = GamaPanelExperimentAnalyzer.FindExperimentsInWorkspace(normalizedPath);
        }
        else
        {
            errorMessage = "Path does not exist.";
            return false;
        }

        if (experimentOptions.Count == 0)
        {
            errorMessage = "No experiment declaration was found.";
            return false;
        }

        return true;
    }

    private void ExploreExperimentPathFromWorkspace()
    {
        if (!TryPopulateExperimentOptions(out string errorMessage))
        {
            experimentStatus = errorMessage;
            return;
        }

        experimentStatus = "Found " + experimentOptions.Count.ToString(CultureInfo.InvariantCulture) +
            " experiment(s). Click 'Import Experiment' to open the Import Experiment tab.";
    }

    private void ExploreExperimentPath()
    {
        if (!TryPopulateExperimentOptions(out string errorMessage))
        {
            experimentStatus = errorMessage;
            return;
        }

        experimentStatus = "Found " + experimentOptions.Count.ToString(CultureInfo.InvariantCulture) + " experiment(s).";
        AnalyzeSelectedExperiment();
    }

    private void AnalyzeSelectedExperiment()
    {
        if (selectedExperimentIndex < 0 || selectedExperimentIndex >= experimentOptions.Count)
        {
            return;
        }

        analysis = GamaPanelExperimentAnalyzer.Analyze(experimentOptions[selectedExperimentIndex]);
        agentOverrides = new List<GamaPanelAgentOverride>();
        for (int i = 0; i < analysis.Agents.Count; i++)
        {
            agentOverrides.Add(new GamaPanelAgentOverride(analysis.Agents[i]));
        }

        sceneCharacteristicSize = Mathf.Max(1f, analysis.SuggestedSceneSize);
        renderDistance = Mathf.Max(100f, analysis.SuggestedRenderDistance);
        cameraFarClip = Mathf.Max(renderDistance, sceneCharacteristicSize * 3f);
        experimentStatus = "Experiment analyzed. Check the scene, agents and cumulative preview if needed.";
        gamaHeadlessBatchName = analysis.Name;
        InvalidateCaptureSelectionCache();
    }

    private void ApplyExperimentSettingsToScene()
    {
        if (setupSceneBeforeApply)
        {
            GAMAMenu.SetupScene();
        }

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            if (EditorUtility.DisplayDialog("GAMA scene not found", "No SimulationManager was found in the current scene. Run Setup Scene now?", "Setup Scene", "Cancel"))
            {
                GAMAMenu.SetupScene();
                manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
            }
        }

        ApplySceneObjects();

        if (manager != null)
        {
            ApplySimulationManagerSettings(manager);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        experimentStatus = "Applied experiment settings to the active Unity scene.";
        Debug.Log("[GAMA] Applied experiment import settings for " + analysis.Name + ".");
    }

    private void ApplySceneObjects()
    {
        Camera mainCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (mainCamera != null)
        {
            mainCamera.nearClipPlane = cameraNearClip;
            mainCamera.farClipPlane = cameraFarClip;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = backgroundColor;
            EditorUtility.SetDirty(mainCamera);
        }

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = backgroundColor * 0.25f;

        GameObject ground = GameObject.Find("Teleport Area/Ground") ?? GameObject.Find("Ground");
        if (ground != null)
        {
            float planeScale = Mathf.Max(0.1f, sceneCharacteristicSize / 10f);
            ground.transform.localScale = new Vector3(planeScale, 1f, planeScale);
            EditorUtility.SetDirty(ground);
        }
    }

    private void ApplySimulationManagerSettings(SimulationManager manager)
    {
        SerializedObject serializedManager = new SerializedObject(manager);
        SetBool(serializedManager, "enablePrefabRenderDistance", enablePrefabRenderDistance);
        SetFloat(serializedManager, "globalPrefabRenderDistance", renderDistance);
        SetFloat(serializedManager, "prefabViewPadding", Mathf.Max(1f, sceneCharacteristicSize * 0.05f));
        SetFloat(serializedManager, "GamaCRSCoefX", horizontalScale);
        SetFloat(serializedManager, "GamaCRSCoefY", horizontalScale);
        SetFloat(serializedManager, "GamaCRSOffsetZ", verticalOffset);
        ApplyAgentRules(serializedManager);
        serializedManager.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private void ApplySpeciesRenderOverridesToSimulationManager()
    {
        if (speciesRenderOverridesAsset == null || speciesRenderOverridesAsset.entries == null)
        {
            return;
        }

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            return;
        }

        SerializedObject serializedManager = new SerializedObject(manager);
        SerializedProperty rules = serializedManager.FindProperty("ruleSettings");
        if (rules == null || !rules.isArray)
        {
            return;
        }

        const string speciesRulePrefix = "[Species Override]";
        for (int i = rules.arraySize - 1; i >= 0; i--)
        {
            SerializedProperty rule = rules.GetArrayElementAtIndex(i);
            SerializedProperty label = rule.FindPropertyRelative("label");
            if (label != null && label.stringValue.StartsWith(speciesRulePrefix, StringComparison.Ordinal))
            {
                rules.DeleteArrayElementAtIndex(i);
            }
        }

        for (int e = 0; e < speciesRenderOverridesAsset.entries.Count; e++)
        {
            GamaSpeciesRenderOverrideEntry entry = speciesRenderOverridesAsset.entries[e];
            string species = entry != null ? entry.GetSpeciesName() : string.Empty;
            if (entry == null || string.IsNullOrWhiteSpace(species) || !entry.HasAnyOverride)
            {
                continue;
            }

            int index = rules.arraySize;
            rules.InsertArrayElementAtIndex(index);
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            SetRelativeBool(rule, "enabled", true);
            SetRelativeString(rule, "label", speciesRulePrefix + " " + species);
            SetRelativeString(rule, "propertyId", species);
            SetRelativeString(rule, "tag", species);
            SetRelativeString(rule, "prefabContains", string.Empty);
            SetRelativeString(rule, "agentNameContains", string.Empty);
            SetRelativeString(rule, "agentNameRegex", string.Empty);

            SerializedProperty manual = rule.FindPropertyRelative("manual");
            if (manual == null)
            {
                continue;
            }

            SetRelativeBool(manual, "overrideColor", entry.overrideColor);
            SetRelativeColor(manual, "color", entry.color);
            SetRelativeBool(manual, "overrideScaleMultiplier", Math.Abs(entry.scaleMultiplier - 1f) > 0.0001f);
            SetRelativeFloat(manual, "scaleMultiplier", entry.scaleMultiplier);
            SetRelativeBool(manual, "overridePositionOffset", entry.positionOffset.sqrMagnitude > 0.0001f);
            SetRelativeVector3(manual, "positionOffset", entry.positionOffset);
            SetRelativeBool(manual, "overrideRotationOffset", entry.rotationOffsetEuler.sqrMagnitude > 0.0001f);
            SetRelativeVector3(manual, "rotationOffsetEuler", entry.rotationOffsetEuler);
            SetRelativeBool(manual, "overrideVisibility", entry.overrideVisibility);
            SetRelativeBool(manual, "visible", entry.visible);
        }

        serializedManager.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private GamaSpeciesRenderOverrides CreateSpeciesRenderOverridesAsset()
    {
        GamaSpeciesRenderOverrides asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        if (asset != null)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            EditorPrefs.SetString(SpeciesOverridesAssetPrefKey, path);
            Selection.activeObject = asset;
            UnityEngine.Debug.Log("[GAMA] Overrides asset ready: " + path);
        }

        return asset;
    }

    private void ApplyAgentRules(SerializedObject serializedManager)
    {
        SerializedProperty rules = serializedManager.FindProperty("ruleSettings");
        if (rules == null || !rules.isArray)
        {
            return;
        }

        if (replaceGeneratedRules)
        {
            for (int i = rules.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                SerializedProperty label = rule.FindPropertyRelative("label");
                if (label != null && label.stringValue.StartsWith(GeneratedRulePrefix, StringComparison.Ordinal))
                {
                    rules.DeleteArrayElementAtIndex(i);
                }
            }
        }

        for (int i = 0; i < agentOverrides.Count; i++)
        {
            GamaPanelAgentOverride agent = agentOverrides[i];
            if (!agent.HasAnyOverride)
            {
                continue;
            }

            int index = rules.arraySize;
            rules.InsertArrayElementAtIndex(index);
            SerializedProperty rule = rules.GetArrayElementAtIndex(index);
            SetRelativeBool(rule, "enabled", true);
            SetRelativeString(rule, "label", GeneratedRulePrefix + " " + analysis.Name + " / " + agent.Name);
            SetRelativeString(rule, "propertyId", string.Empty);
            SetRelativeString(rule, "tag", string.Empty);
            SetRelativeString(rule, "prefabContains", string.Empty);
            SetRelativeString(rule, "agentNameContains", agent.Name);
            SetRelativeString(rule, "agentNameRegex", string.Empty);

            SerializedProperty manual = rule.FindPropertyRelative("manual");
            if (manual == null)
            {
                continue;
            }

            SetRelativeBool(manual, "overrideColor", agent.OverrideColor);
            SetRelativeColor(manual, "color", agent.Color);
            SetRelativeBool(manual, "overrideScaleMultiplier", agent.OverrideScale);
            SetRelativeFloat(manual, "scaleMultiplier", agent.ScaleMultiplier);
            SetRelativeBool(manual, "overridePositionOffset", false);
            SetRelativeVector3(manual, "positionOffset", Vector3.zero);
            SetRelativeBool(manual, "overrideRotationOffset", false);
            SetRelativeVector3(manual, "rotationOffsetEuler", Vector3.zero);
            SetRelativeBool(manual, "overrideVisibility", agent.OverrideVisibility);
            SetRelativeBool(manual, "visible", agent.Visible);
        }
    }

    private void RefreshCodeExampleScenes()
    {
        codeExampleScenes = new List<GamaCodeExampleSceneInfo>(GamaCodeExampleSceneBuilder.GetSceneInfos());
        int prev = selectedCodeExampleIndex;
        selectedCodeExampleIndex = Mathf.Clamp(selectedCodeExampleIndex, 0, Mathf.Max(0, codeExampleScenes.Count - 1));
        if (prev != selectedCodeExampleIndex)
        {
            EditorPrefs.SetInt(SelectedCodeExampleIndexPrefKey, selectedCodeExampleIndex);
        }

        codeExampleStatus = "Ready to generate " + codeExampleScenes.Count.ToString(CultureInfo.InvariantCulture) + " code example scene(s).";
    }

    private void SetupSelectedCodeExampleScene()
    {
        if (selectedCodeExampleIndex < 0 || selectedCodeExampleIndex >= codeExampleScenes.Count)
        {
            EditorUtility.DisplayDialog("Code Example Setup", "No code example scene is selected.", "OK");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        GamaCodeExampleSceneInfo sceneInfo = codeExampleScenes[selectedCodeExampleIndex];
        try
        {
            string targetSceneAssetPath = GamaCodeExampleSceneBuilder.BuildAndSave(sceneInfo);
            codeExampleStatus = "Generated " + sceneInfo.DisplayName + ".";
            Debug.Log("[GAMA] Code example scene generated: " + targetSceneAssetPath + ".");
        }
        catch (Exception exception)
        {
            Debug.LogError("[GAMA] Failed to generate code example scene: " + exception);
            EditorUtility.DisplayDialog("Code Example Setup", "Failed to generate the selected code example scene. See Console for details.\n\n" + exception.Message, "OK");
        }
    }

    private void CreateAllCodeExampleScenes()
    {
        if (codeExampleScenes.Count == 0)
        {
            return;
        }

        try
        {
            for (int i = 0; i < codeExampleScenes.Count; i++)
            {
                GamaCodeExampleSceneBuilder.BuildAndSave(codeExampleScenes[i], false);
            }

            AssetDatabase.Refresh();
            codeExampleStatus = "Generated " + codeExampleScenes.Count.ToString(CultureInfo.InvariantCulture) + " code example scene(s).";
            EditorUtility.DisplayDialog("Code Example Setup", "Generated all code example scenes into Assets/Scenes/Code Examples.", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("[GAMA] Failed to generate code example scenes: " + exception);
            EditorUtility.DisplayDialog("Code Example Setup", "Failed to generate all code example scenes. See Console for details.\n\n" + exception.Message, "OK");
        }
    }

    private static string ToFullProjectPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private bool TryApplyJsonStaticPreview(GameObject root, out bool success, out string status)
    {
        success = false;
        status = null;
        bool any = !string.IsNullOrWhiteSpace(staticPreviewPrecisionJsonPath) ||
            !string.IsNullOrWhiteSpace(staticPreviewPropertiesJsonPath) ||
            !string.IsNullOrWhiteSpace(staticPreviewWorldJsonPath);
        if (!any)
        {
            return false;
        }

        bool allPaths =
            !string.IsNullOrWhiteSpace(staticPreviewPrecisionJsonPath) &&
            !string.IsNullOrWhiteSpace(staticPreviewPropertiesJsonPath) &&
            !string.IsNullOrWhiteSpace(staticPreviewWorldJsonPath);

        if (!allPaths)
        {
            EditorUtility.DisplayDialog(
                "Static preview",
                "If you use JSON captures, all three paths (precision, properties, world / pointsLoc) must be provided. Otherwise, clear them for the grid preview.",
                "OK");
            status = "Incomplete JSON.";
            return true;
        }

        string precisionJson;
        string propertiesJson;
        string worldJson;
        string readError;
        string worldPathForPreview = ResolvePreviewWorldJsonPath();
        if (!GamaEditorStaticPreviewFromJson.TryReadFile(staticPreviewPrecisionJsonPath, out precisionJson, out readError) ||
            !GamaEditorStaticPreviewFromJson.TryReadFile(staticPreviewPropertiesJsonPath, out propertiesJson, out readError) ||
            !GamaEditorStaticPreviewFromJson.TryReadFile(worldPathForPreview, out worldJson, out readError))
        {
            EditorUtility.DisplayDialog("Static preview", "Cannot read file:\n" + readError, "OK");
            status = readError;
            return true;
        }

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning("[GAMA][PREVIEW][BUILD] No SimulationManager in the scene: building with CRS/visual defaults.");
        }

        int prefabN;
        int geomN;
        if (!GamaEditorStaticPreviewFromJson.TryBuild(
                manager,
                precisionJson,
                propertiesJson,
                worldJson,
                root.transform,
                out prefabN,
                out geomN,
                out status,
                speciesRenderOverridesAsset))
        {
            EditorUtility.DisplayDialog("Static preview", status, "OK");
            return true;
        }

        success = true;
        string tickLabel = availableWorldTickPaths.Count > 1
            ? " (tick " + staticPreviewWorldTickIndex + ")"
            : string.Empty;
        status = "Preview (JSON middleware)" + tickLabel + ": " + prefabN + " prefab(s), " + geomN + " geometry(s). CRS = SimulationManager coefficients.";
        Debug.Log("[GAMA] " + status);
        return true;
    }

    private void GenerateStaticPreview()
    {
        try
        {
            GenerateStaticPreviewInternal();
        }
        catch (Exception ex)
        {
            Debug.LogError("[GAMA][PREVIEW][BUILD] Exception: " + ex);
            captureRuntimeStatus = "Capture OK but preview build failed: " + ex.Message;
            experimentStatus = captureRuntimeStatus;
        }
    }

    private void GenerateStaticPreviewInternal()
    {
        ClearStaticPreview();

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("GAMA Static Experiment Preview");

        GameObject root = new GameObject(StaticPreviewRootName);
        Undo.RegisterCreatedObjectUndo(root, "Create GAMA static preview");

        bool jsonOk;
        string jsonStatus;
        if (TryApplyJsonStaticPreview(root, out jsonOk, out jsonStatus))
        {
            if (jsonOk)
            {
                ConfigurePreviewSession(root);
                Selection.activeGameObject = root;
                SceneView.FrameLastActiveSceneView();
                Undo.CollapseUndoOperations(undoGroup);
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                string speciesSummary = BuildPreviewSpeciesSummary(root);
                UpdateAgentOverridesFromPreview(root);
                experimentStatus = string.IsNullOrWhiteSpace(speciesSummary)
                    ? jsonStatus
                    : jsonStatus + " species=" + speciesSummary;
                captureRuntimeStatus = "Static preview built: " + experimentStatus;
                Debug.Log("[GAMA][PREVIEW][BUILD] " + captureRuntimeStatus);
                return;
            }

            Undo.DestroyObjectImmediate(root);
            Undo.CollapseUndoOperations(undoGroup);
            experimentStatus = jsonStatus ?? "JSON Error.";
            captureRuntimeStatus = "Capture successful, but preview build failed: " + experimentStatus;
            Debug.LogError("[GAMA][PREVIEW][BUILD] " + captureRuntimeStatus);
            return;
        }

        GameObject environment = GamaSceneUtility.GetOrCreateChild(root, "Environment");
        GameObject agentsRoot = GamaSceneUtility.GetOrCreateChild(root, "Agents");

        CreatePreviewEnvironment(environment);

        int visibleAgentTypes = CountVisiblePreviewAgentTypes();
        int totalPreviewInstances = CountPreviewInstances();
        int resolvedPrefabCount = 0;
        int fallbackCount = 0;
        int globalIndex = 0;

        int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, totalPreviewInstances))));
        float previewExtent = Mathf.Max(12f, sceneCharacteristicSize * 0.75f);
        float spacing = Mathf.Clamp(previewExtent / Mathf.Max(1, columns - 1), 2f, 12f);
        float originOffset = spacing * (columns - 1) * 0.5f;

        for (int i = 0; i < agentOverrides.Count; i++)
        {
            GamaPanelAgentOverride agent = agentOverrides[i];
            if (IsPreviewHidden(agent))
            {
                continue;
            }

            GameObject group = GamaSceneUtility.GetOrCreateChild(agentsRoot, SanitizeObjectName(agent.Name));
            int sampleCount = ResolvePreviewSampleCount(agent);
            AddPreviewLabel(group, agent.Name, i, visibleAgentTypes, previewExtent);

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                Vector3 position = GetPreviewGridPosition(globalIndex, columns, spacing, originOffset);
                GameObject instance = CreatePreviewInstance(agent, sampleIndex, position, out bool resolvedPrefab);
                if (instance == null)
                {
                    continue;
                }

                instance.transform.SetParent(group.transform, true);
                if (resolvedPrefab)
                {
                    resolvedPrefabCount++;
                }
                else
                {
                    fallbackCount++;
                }

                globalIndex++;
            }
        }

        Selection.activeGameObject = root;
        SceneView.FrameLastActiveSceneView();
        ConfigurePreviewSession(root);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        experimentStatus = "Static preview generated: " + resolvedPrefabCount + " prefab instance(s), " + fallbackCount + " fallback instance(s).";
        string label = analysis != null && !string.IsNullOrWhiteSpace(analysis.Name) ? analysis.Name : "GAMA active selection";
        Debug.Log("[GAMA] Static experiment preview generated for " + label + ".");
    }

    private static string BuildPreviewSpeciesSummary(GameObject root)
    {
        if (root == null)
        {
            return string.Empty;
        }

        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            GamaPreviewObject item = previewObjects[i];
            if (item == null)
            {
                continue;
            }

            string species = string.IsNullOrWhiteSpace(item.speciesName) ? "unknown" : item.speciesName.Trim();
            counts[species] = counts.TryGetValue(species, out int count) ? count + 1 : 1;
        }

        if (counts.Count == 0)
        {
            return string.Empty;
        }

        List<string> parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in counts)
        {
            parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));
        }

        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", parts.ToArray());
    }

    private void UpdateAgentOverridesFromPreview(GameObject root)
    {
        if (root == null) return;
        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            if (previewObjects[i] == null) continue;
            string species = string.IsNullOrWhiteSpace(previewObjects[i].speciesName) ? "unknown" : previewObjects[i].speciesName.Trim();
            counts[species] = counts.TryGetValue(species, out int count) ? count + 1 : 1;
        }

        foreach (KeyValuePair<string, int> pair in counts)
        {
            bool exists = false;
            for (int i = 0; i < agentOverrides.Count; i++)
            {
                if (string.Equals(agentOverrides[i].Name, pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                GamaPanelAgentInfo info = new GamaPanelAgentInfo();
                info.Name = pair.Key;
                info.CountExpression = pair.Value.ToString();
                info.PrefabHint = "-";
                agentOverrides.Add(new GamaPanelAgentOverride(info));
            }
        }
    }

    private void ConfigurePreviewSession(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        GamaPreviewSession session = root.GetComponent<GamaPreviewSession>();
        if (session == null)
        {
            session = root.AddComponent<GamaPreviewSession>();
        }

        string model = ResolvePreviewSessionModelPath();
        string experiment = analysis != null && !string.IsNullOrWhiteSpace(analysis.Name)
            ? analysis.Name
            : (!string.IsNullOrWhiteSpace(gamaHeadlessBatchName) ? gamaHeadlessBatchName : "unknown");
        bool activeGamaSelection = string.Equals(model, "GAMA_ACTIVE_SELECTION", StringComparison.Ordinal);

        session.modelPath = model;
        session.experimentName = experiment ?? string.Empty;
        session.experimentDisplayName = selectedExperimentIndex >= 0 &&
                                        selectedExperimentIndex < experimentOptions.Count &&
                                        experimentOptions[selectedExperimentIndex] != null
            ? experimentOptions[selectedExperimentIndex].DisplayName
            : session.experimentName;
        session.sourceGamlPath = analysis != null ? analysis.SourcePath : string.Empty;
        session.previewCacheReference = ResolvePreviewWorldJsonPath() ?? string.Empty;
        session.captureTimestampUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        session.monitorPort = captureMonitorPort;
        session.middlewarePort = int.TryParse(capturePort, out int middlewarePortValue) ? middlewarePortValue : 0;
        session.playerId = string.IsNullOrWhiteSpace(captureConnectionId) ? StaticInformation.getId() : captureConnectionId.Trim();
        session.selectionMode = activeGamaSelection ? "ActiveGamaSelection" : "UnitySelection";
        session.activeGamaSelection = activeGamaSelection;
        session.stale = false;
        session.useThisPreviewForPlay = false;
        session.experimentSignature = BuildPreviewSignature(session);
        if (!activeGamaSelection && File.Exists(model))
        {
            GamaEditorRuntimeSelectionStore.Save(model, experiment ?? string.Empty);
        }
        else
        {
            Debug.Log("[GAMA][PREVIEW] Runtime selection store unmodified (mode " + session.selectionMode + ", model=" + model + ").");
        }

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager != null)
        {
            SerializedObject managerSo = new SerializedObject(manager);
            session.crs = new GamaPreviewCrsSettings
            {
                coefX = ReadFloatProperty(managerSo, "GamaCRSCoefX", 1f),
                coefY = ReadFloatProperty(managerSo, "GamaCRSCoefY", 1f),
                offsetX = ReadFloatProperty(managerSo, "GamaCRSOffsetX", 0f),
                offsetY = ReadFloatProperty(managerSo, "GamaCRSOffsetY", 0f),
                offsetZ = ReadFloatProperty(managerSo, "GamaCRSOffsetZ", 0f)
            };
        }

        PopulateSessionSpeciesSnapshot(root, session);
        session.speciesOverrides = speciesRenderOverridesAsset;
        PropagateSessionToSpeciesWizards(root, session);
        EditorUtility.SetDirty(session);

        Debug.Log(
            "[GAMA][PREVIEW] session model=" + session.modelPath +
            " exp=" + session.experimentName +
            " mode=" + session.selectionMode +
            " species=" + string.Join(",", session.speciesList.ToArray()));
    }

    private string ResolvePreviewSessionModelPath()
    {
        string candidate = analysis != null && !string.IsNullOrWhiteSpace(analysis.SourcePath)
            ? analysis.SourcePath
            : experimentPath;
        candidate = NormalizePath(candidate);
        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
        {
            return candidate;
        }

        return "GAMA_ACTIVE_SELECTION";
    }

    private static string BuildPreviewSignature(GamaPreviewSession session)
    {
        if (session == null)
        {
            return string.Empty;
        }

        string text = string.Join("|",
            session.modelPath ?? string.Empty,
            session.experimentName ?? string.Empty,
            session.sourceGamlPath ?? string.Empty,
            session.previewCacheReference ?? string.Empty,
            session.captureTimestampUtc ?? string.Empty);
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }

    private static void PopulateSessionSpeciesSnapshot(GameObject root, GamaPreviewSession session)
    {
        session.speciesList.Clear();
        session.speciesCounts.Clear();

        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            GamaPreviewObject item = previewObjects[i];
            if (item == null)
            {
                continue;
            }

            string species = string.IsNullOrWhiteSpace(item.speciesName) ? "unknown" : item.speciesName.Trim();
            if (!counts.ContainsKey(species))
            {
                counts[species] = 0;
            }

            counts[species]++;
        }

        foreach (KeyValuePair<string, int> pair in counts)
        {
            session.speciesList.Add(pair.Key);
            session.speciesCounts.Add(new GamaPreviewSpeciesCount { speciesName = pair.Key, count = pair.Value });
        }
    }

    private void PropagateSessionToSpeciesWizards(GameObject root, GamaPreviewSession session)
    {
        if (root == null || session == null)
        {
            return;
        }

        GamaSpeciesWizard[] wizards = root.GetComponentsInChildren<GamaSpeciesWizard>(true);
        for (int i = 0; i < wizards.Length; i++)
        {
            GamaSpeciesWizard wizard = wizards[i];
            if (wizard == null)
            {
                continue;
            }

            wizard.modelPath = session.modelPath;
            wizard.experimentName = session.experimentName;
            if (string.IsNullOrWhiteSpace(wizard.speciesName))
            {
                wizard.speciesName = wizard.gameObject.name;
            }

            if (wizard.overridesAsset == null)
            {
                wizard.overridesAsset = speciesRenderOverridesAsset ?? GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
            }

            wizard.SaveCurrentSettingsToAsset();
            EditorUtility.SetDirty(wizard);
        }
    }

    private void CreatePreviewEnvironment(GameObject parent)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Preview Ground";
        ground.transform.SetParent(parent.transform, false);
        ground.transform.position = new Vector3(0f, verticalOffset, 0f);
        ground.transform.localScale = Vector3.one * Mathf.Max(1f, sceneCharacteristicSize / 10f);
        GamaVisualUtility.ApplyColor(ground, new Color32(72, 82, 92, 255));

        GameObject lightObject = new GameObject("Preview Directional Light");
        lightObject.transform.SetParent(parent.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;

        GameObject cameraObject = new GameObject("Preview Camera");
        cameraObject.transform.SetParent(parent.transform, false);
        float distance = Mathf.Max(16f, sceneCharacteristicSize * 0.65f);
        cameraObject.transform.position = new Vector3(0f, Mathf.Max(8f, sceneCharacteristicSize * 0.35f), -distance);
        cameraObject.transform.LookAt(new Vector3(0f, verticalOffset, 0f));

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = cameraNearClip;
        camera.farClipPlane = cameraFarClip;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = backgroundColor;
    }

    private GameObject CreatePreviewInstance(GamaPanelAgentOverride agent, int sampleIndex, Vector3 position, out bool resolvedPrefab)
    {
        GameObject prefab = ResolvePreviewPrefab(agent);
        resolvedPrefab = prefab != null;

        GameObject instance = prefab != null
            ? PrefabUtility.InstantiatePrefab(prefab) as GameObject
            : CreateFallbackPreviewObject(agent);

        if (instance == null)
        {
            instance = CreateFallbackPreviewObject(agent);
            resolvedPrefab = false;
        }

        instance.name = agent.Name + "_preview_" + (sampleIndex + 1).ToString(CultureInfo.InvariantCulture);
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, (sampleIndex * 37f) % 360f, 0f);
        GamaPreviewObject previewObject = instance.GetComponent<GamaPreviewObject>();
        if (previewObject == null)
        {
            previewObject = instance.AddComponent<GamaPreviewObject>();
        }

        previewObject.previewOnly = true;
        previewObject.speciesName = agent.Name;
        previewObject.agentId = instance.name;
        previewObject.geometryHash = string.Empty;
        previewObject.sourceTick = staticPreviewWorldTickIndex;

        float scale = agent.OverrideScale ? agent.ScaleMultiplier : Mathf.Max(0.1f, agent.ScaleMultiplier);
        instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);

        if (agent.OverrideColor || !resolvedPrefab)
        {
            GamaVisualUtility.ApplyColor(instance, agent.OverrideColor ? agent.Color : GetDefaultAgentColor(agent.Name));
        }

        if (agent.OverrideVisibility)
        {
            instance.SetActive(agent.Visible);
        }

        PlaceOnPreviewGround(instance);
        return instance;
    }

    private GameObject ResolvePreviewPrefab(GamaPanelAgentOverride agent)
    {
        foreach (string candidate in BuildPreviewPrefabCandidates(agent))
        {
            GameObject prefab = GamaVisualUtility.ResolvePrefab(candidate);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildPreviewPrefabCandidates(GamaPanelAgentOverride agent)
    {
        if (agent == null)
        {
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(agent.PrefabHint) && agent.PrefabHint != "-")
        {
            yield return agent.PrefabHint;
            yield return "Prefabs/Visual Prefabs/" + agent.PrefabHint;
            yield return "Prefabs/Visual Prefabs/Character/" + agent.PrefabHint;
        }

        if (!string.IsNullOrWhiteSpace(agent.Name))
        {
            yield return agent.Name;
            yield return "Prefabs/Visual Prefabs/" + agent.Name;
            yield return "Prefabs/Visual Prefabs/Character/" + agent.Name;
        }
    }

    private static GameObject CreateFallbackPreviewObject(GamaPanelAgentOverride agent)
    {
        PrimitiveType primitive = ResolveFallbackPrimitive(agent != null ? agent.Name : string.Empty);
        GameObject root = new GameObject((agent != null ? agent.Name : "Agent") + "_fallback");
        GameObject visual = GameObject.CreatePrimitive(primitive);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);

        if (primitive == PrimitiveType.Capsule)
        {
            visual.transform.localScale = new Vector3(0.65f, 1.2f, 0.65f);
        }
        else if (primitive == PrimitiveType.Sphere)
        {
            visual.transform.localScale = Vector3.one * 0.9f;
        }
        else
        {
            visual.transform.localScale = Vector3.one;
        }

        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyImmediate(collider);
        }

        return root;
    }

    private static PrimitiveType ResolveFallbackPrimitive(string agentName)
    {
        string normalized = (agentName ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("person") || normalized.Contains("pedestrian") || normalized.Contains("human") || normalized.Contains("agent"))
        {
            return PrimitiveType.Capsule;
        }

        if (normalized.Contains("sphere") || normalized.Contains("ball"))
        {
            return PrimitiveType.Sphere;
        }

        if (normalized.Contains("tree") || normalized.Contains("tower") || normalized.Contains("pole"))
        {
            return PrimitiveType.Cylinder;
        }

        return PrimitiveType.Cube;
    }

    private void AddPreviewLabel(GameObject parent, string label, int rowIndex, int rowCount, float previewExtent)
    {
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(parent.transform, false);
        float z = rowCount <= 1 ? 0f : Mathf.Lerp(-previewExtent * 0.42f, previewExtent * 0.42f, rowIndex / (float)(rowCount - 1));
        labelObject.transform.position = new Vector3(-previewExtent * 0.5f, verticalOffset + 0.04f, z);
        labelObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        TextMesh text = labelObject.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleLeft;
        text.alignment = TextAlignment.Left;
        text.characterSize = Mathf.Clamp(sceneCharacteristicSize * 0.025f, 0.45f, 3f);
        text.color = Color.white;
    }

    private Vector3 GetPreviewGridPosition(int globalIndex, int columns, float spacing, float originOffset)
    {
        int row = globalIndex / columns;
        int column = globalIndex % columns;
        return new Vector3(
            column * spacing - originOffset,
            verticalOffset,
            row * spacing - originOffset);
    }

    private void PlaceOnPreviewGround(GameObject instance)
    {
        Bounds bounds = CalculateBounds(instance);
        if (bounds.size == Vector3.zero)
        {
            return;
        }

        float deltaY = verticalOffset - bounds.min.y;
        instance.transform.position += new Vector3(0f, deltaY, 0f);
    }

    private static Bounds CalculateBounds(GameObject gameObject)
    {
        Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(gameObject.transform.position, Vector3.zero);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private int CountVisiblePreviewAgentTypes()
    {
        int count = 0;
        for (int i = 0; i < agentOverrides.Count; i++)
        {
            if (!IsPreviewHidden(agentOverrides[i]))
            {
                count++;
            }
        }

        return count;
    }

    private int CountPreviewInstances()
    {
        int count = 0;
        for (int i = 0; i < agentOverrides.Count; i++)
        {
            if (!IsPreviewHidden(agentOverrides[i]))
            {
                count += ResolvePreviewSampleCount(agentOverrides[i]);
            }
        }

        return Mathf.Max(1, count);
    }

    private int ResolvePreviewSampleCount(GamaPanelAgentOverride agent)
    {
        int maxSamples = Mathf.Max(1, previewSamplesPerAgent);
        if (agent != null &&
            !string.IsNullOrWhiteSpace(agent.CountExpression) &&
            int.TryParse(agent.CountExpression.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int declaredCount) &&
            declaredCount > 0)
        {
            return Mathf.Clamp(declaredCount, 1, maxSamples);
        }

        return maxSamples;
    }

    private static bool IsPreviewHidden(GamaPanelAgentOverride agent)
    {
        return agent != null && agent.OverrideVisibility && !agent.Visible;
    }

    private static string SanitizeObjectName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Agent";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            raw = raw.Replace(invalid, '_');
        }

        return raw.Trim();
    }

    private static Color32 GetDefaultAgentColor(string key)
    {
        int hash = (key ?? string.Empty).GetHashCode();
        byte r = (byte)(96 + Mathf.Abs(hash % 128));
        byte g = (byte)(96 + Mathf.Abs((hash / 17) % 128));
        byte b = (byte)(96 + Mathf.Abs((hash / 31) % 128));
        return new Color32(r, g, b, 255);
    }

    private void RefreshAvailableWorldTicks()
    {
        availableWorldTickPaths.Clear();
        string dir = ResolveWorldTicksDirectory();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            if (!string.IsNullOrWhiteSpace(staticPreviewWorldJsonPath) && File.Exists(staticPreviewWorldJsonPath))
            {
                availableWorldTickPaths.Add(staticPreviewWorldJsonPath);
            }

            ClampStaticPreviewWorldTickIndex();
            return;
        }

        string[] tickFiles = Directory.GetFiles(dir, "world_tick_*.json");
        Array.Sort(tickFiles, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < tickFiles.Length; i++)
        {
            availableWorldTickPaths.Add(tickFiles[i]);
        }

        if (availableWorldTickPaths.Count == 0)
        {
            string worldBest = Path.Combine(dir, "world_best.json");
            string worldDefault = Path.Combine(dir, "world.json");
            if (File.Exists(worldBest))
            {
                availableWorldTickPaths.Add(worldBest);
            }
            else if (File.Exists(worldDefault))
            {
                availableWorldTickPaths.Add(worldDefault);
            }
            else if (!string.IsNullOrWhiteSpace(staticPreviewWorldJsonPath) && File.Exists(staticPreviewWorldJsonPath))
            {
                availableWorldTickPaths.Add(staticPreviewWorldJsonPath);
            }
        }

        ClampStaticPreviewWorldTickIndex();
    }

    private void ClampStaticPreviewWorldTickIndex()
    {
        if (availableWorldTickPaths.Count == 0)
        {
            staticPreviewWorldTickIndex = 0;
            return;
        }

        staticPreviewWorldTickIndex = Mathf.Clamp(staticPreviewWorldTickIndex, 0, availableWorldTickPaths.Count - 1);
    }

    private string ResolveWorldTicksDirectory()
    {
        if (!string.IsNullOrWhiteSpace(staticPreviewWorldJsonPath))
        {
            try
            {
                return Path.GetDirectoryName(Path.GetFullPath(staticPreviewWorldJsonPath));
            }
            catch
            {
                // ignore
            }
        }

        if (!string.IsNullOrWhiteSpace(staticPreviewPrecisionJsonPath))
        {
            try
            {
                return Path.GetDirectoryName(Path.GetFullPath(staticPreviewPrecisionJsonPath));
            }
            catch
            {
                // ignore
            }
        }

        if (!string.IsNullOrWhiteSpace(gamaJsonExportOutputDir) && Directory.Exists(gamaJsonExportOutputDir))
        {
            return Path.GetFullPath(gamaJsonExportOutputDir);
        }

        return null;
    }

    private string ResolvePreviewWorldJsonPath()
    {
        RefreshAvailableWorldTicks();
        if (availableWorldTickPaths.Count > 0)
        {
            return availableWorldTickPaths[staticPreviewWorldTickIndex];
        }

        return staticPreviewWorldJsonPath;
    }

    private static void ClearStaticPreview()
    {
        GameObject existing = GameObject.Find(StaticPreviewRootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }

    private static void ImportPrefabsToResources(string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            EditorUtility.DisplayDialog("Import Prefabs", "Invalid source folder path.", "OK");
            return;
        }

        string targetResourcesPath = Path.Combine(Application.dataPath, "Resources");
        Directory.CreateDirectory(targetResourcesPath);

        try
        {
            int copiedFiles = CopyDirectory(sourceFolder, targetResourcesPath);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Import Prefabs", "Successfully imported " + copiedFiles + " files into Assets/Resources.", "OK");
        }
        catch (Exception exception)
        {
            Debug.LogError("[GAMA] Error importing prefabs: " + exception.Message);
            EditorUtility.DisplayDialog("Import Prefabs", "Failed to import prefabs. See console for details.\n\n" + exception.Message, "OK");
        }
    }

    private static int CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        int fileCount = 0;
        Directory.CreateDirectory(targetDirectory);

        string[] files = Directory.GetFiles(sourceDirectory);
        for (int i = 0; i < files.Length; i++)
        {
            if (string.Equals(Path.GetExtension(files[i]), ".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetFile = Path.Combine(targetDirectory, Path.GetFileName(files[i]));
            File.Copy(files[i], targetFile, true);
            fileCount++;
        }

        string[] directories = Directory.GetDirectories(sourceDirectory);
        for (int i = 0; i < directories.Length; i++)
        {
            fileCount += CopyDirectory(directories[i], Path.Combine(targetDirectory, Path.GetFileName(directories[i])));
        }

        return fileCount;
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Trim().Trim('"');
    }

    private static void SetBool(SerializedObject serializedObject, string propertyName, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }

    private static void SetFloat(SerializedObject serializedObject, string propertyName, float value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            property.floatValue = value;
        }
    }

    private static float ReadFloatProperty(SerializedObject serializedObject, string propertyName, float fallback)
    {
        if (serializedObject == null)
        {
            return fallback;
        }

        SerializedProperty property = serializedObject.FindProperty(propertyName);
        return property != null ? property.floatValue : fallback;
    }

    private static void SetRelativeBool(SerializedProperty property, string relativeName, bool value)
    {
        SerializedProperty child = property.FindPropertyRelative(relativeName);
        if (child != null)
        {
            child.boolValue = value;
        }
    }

    private static void SetRelativeFloat(SerializedProperty property, string relativeName, float value)
    {
        SerializedProperty child = property.FindPropertyRelative(relativeName);
        if (child != null)
        {
            child.floatValue = value;
        }
    }

    private static void SetRelativeString(SerializedProperty property, string relativeName, string value)
    {
        SerializedProperty child = property.FindPropertyRelative(relativeName);
        if (child != null)
        {
            child.stringValue = value ?? string.Empty;
        }
    }

    private static void SetRelativeColor(SerializedProperty property, string relativeName, Color value)
    {
        SerializedProperty child = property.FindPropertyRelative(relativeName);
        if (child != null)
        {
            child.colorValue = value;
        }
    }

    private static void SetRelativeVector3(SerializedProperty property, string relativeName, Vector3 value)
    {
        SerializedProperty child = property.FindPropertyRelative(relativeName);
        if (child != null)
        {
            child.vector3Value = value;
        }
    }
}

internal sealed class GamaPanelAgentOverride
{
    public GamaPanelAgentOverride(GamaPanelAgentInfo source)
    {
        Name = source.Name;
        CountExpression = source.CountExpression;
        PrefabHint = source.PrefabHint;
        Color = source.HasColor ? source.Color : Color.white;
        ScaleMultiplier = source.SuggestedScale > 0f ? source.SuggestedScale : 1f;
        Visible = true;
        OverrideColor = source.HasColor;
        OverrideScale = source.SuggestedScale > 0f && Math.Abs(source.SuggestedScale - 1f) > 0.001f;
        OverrideVisibility = false;
    }

    public string Name { get; private set; }
    public string CountExpression { get; private set; }
    public string PrefabHint { get; private set; }
    public bool OverrideColor;
    public Color Color;
    public bool OverrideScale;
    public float ScaleMultiplier;
    public bool OverrideVisibility;
    public bool Visible;
    public bool HasAnyOverride { get { return OverrideColor || OverrideScale || OverrideVisibility; } }
}

internal sealed class GamaPanelExperimentOption
{
    public GamaPanelExperimentOption(string name, string sourcePath, string fileContent, int declarationIndex, int nextDeclarationIndex)
    {
        Name = name;
        SourcePath = sourcePath;
        FileContent = fileContent;
        DeclarationIndex = declarationIndex;
        NextDeclarationIndex = nextDeclarationIndex;
    }

    public string Name { get; private set; }
    public string SourcePath { get; private set; }
    public string FileContent { get; private set; }
    public int DeclarationIndex { get; private set; }
    public int NextDeclarationIndex { get; private set; }
    public string DisplayName { get { return Name + " - " + Path.GetFileName(SourcePath); } }
}

internal sealed class GamaPanelExperimentAnalysis
{
    public string Name;
    public string SourcePath;
    public string CapabilityLabel;
    public float SuggestedSceneSize = 100f;
    public float SuggestedRenderDistance = 1500f;
    public List<GamaPanelAgentInfo> Agents = new List<GamaPanelAgentInfo>();
}

internal sealed class GamaPanelAgentInfo
{
    public string Name;
    public string CountExpression = "-";
    public string PrefabHint = "-";
    public bool HasColor;
    public Color Color = Color.white;
    public float SuggestedScale = 1f;
}

internal static class GamaPanelExperimentAnalyzer
{
    private static readonly Regex ExperimentRegex = new Regex(
        @"^\s*experiment\s+(?:""(?<quoted>[^""]+)""|(?<name>[A-Za-z_][\w\-]*))(?<suffix>[^\r\n]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex SpeciesRegex = new Regex(
        @"^\s*species\s+(?<name>[A-Za-z_][\w\-]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex CreateRegex = new Regex(
        @"\bcreate\s+(?<name>[A-Za-z_][\w\-]*)(?<tail>[^;\r\n}]*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NumberRegex = new Regex(
        @"\b(?:number|num|amount)\s*:\s*(?<value>[^,\r\n;}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ColorRegex = new Regex(
        @"\b(?:color|colour|rgb|rgba)\s*:\s*(?<value>#[0-9a-fA-F]{3,8}|[A-Za-z_][\w\-]*|\[[^\]]+\])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SizeRegex = new Regex(
        @"\b(?:size|scale|radius|width|height)\s*:\s*(?<value>-?\d+(?:[\.,]\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PrefabRegex = new Regex(
        @"\b(?:prefab|prefab_aspect|unity_prefab)\s*:\s*[""']?(?<value>[^,""'\r\n;}]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<GamaPanelExperimentOption> FindExperimentsInWorkspace(string workspacePath)
    {
        List<GamaPanelExperimentOption> options = new List<GamaPanelExperimentOption>();
        foreach (string file in EnumerateGamlFiles(workspacePath))
        {
            options.AddRange(FindExperimentsInFile(file));
        }

        options.Sort((left, right) => string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
        return options;
    }

    public static List<GamaPanelExperimentOption> FindExperimentsInFile(string filePath)
    {
        List<GamaPanelExperimentOption> options = new List<GamaPanelExperimentOption>();
        string content = File.ReadAllText(filePath);
        MatchCollection matches = ExperimentRegex.Matches(content);
        for (int i = 0; i < matches.Count; i++)
        {
            Match match = matches[i];
            int nextIndex = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            string name = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["name"].Value;
            options.Add(new GamaPanelExperimentOption(name.Trim(), filePath, content, match.Index, nextIndex));
        }

        return options;
    }

    public static GamaPanelExperimentAnalysis Analyze(GamaPanelExperimentOption option)
    {
        GamaPanelExperimentAnalysis analysis = new GamaPanelExperimentAnalysis
        {
            Name = option.Name,
            SourcePath = option.SourcePath,
            CapabilityLabel = InferCapability(option.FileContent, option.DeclarationIndex, option.NextDeclarationIndex)
        };

        Dictionary<string, GamaPanelAgentInfo> agents = new Dictionary<string, GamaPanelAgentInfo>(StringComparer.OrdinalIgnoreCase);
        AddSpecies(option.FileContent, agents);
        AddCreatedAgents(GetExperimentBlock(option), agents);

        foreach (GamaPanelAgentInfo agent in agents.Values)
        {
            analysis.Agents.Add(agent);
        }

        analysis.Agents.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        analysis.SuggestedSceneSize = InferSceneSize(option.FileContent);
        analysis.SuggestedRenderDistance = Mathf.Max(analysis.SuggestedSceneSize * 15f, 1500f);
        return analysis;
    }

    private static IEnumerable<string> EnumerateGamlFiles(string rootPath)
    {
        Stack<string> pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string directoryName = Path.GetFileName(current);
            if (string.Equals(directoryName, ".git", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "target", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*.gaml");
            }
            catch
            {
                files = new string[0];
            }

            for (int i = 0; i < files.Length; i++)
            {
                yield return files[i];
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                directories = new string[0];
            }

            for (int i = 0; i < directories.Length; i++)
            {
                pending.Push(directories[i]);
            }
        }
    }

    private static string GetExperimentBlock(GamaPanelExperimentOption option)
    {
        int start = Mathf.Clamp(option.DeclarationIndex, 0, option.FileContent.Length);
        int end = Mathf.Clamp(option.NextDeclarationIndex, start, option.FileContent.Length);
        return option.FileContent.Substring(start, end - start);
    }

    private static void AddSpecies(string content, Dictionary<string, GamaPanelAgentInfo> agents)
    {
        MatchCollection matches = SpeciesRegex.Matches(content);
        for (int i = 0; i < matches.Count; i++)
        {
            Match match = matches[i];
            string name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            int nextIndex = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            string block = content.Substring(match.Index, Mathf.Max(0, nextIndex - match.Index));
            GamaPanelAgentInfo info = GetOrCreateAgent(agents, name);
            FillHints(block, info);
        }
    }

    private static void AddCreatedAgents(string experimentBlock, Dictionary<string, GamaPanelAgentInfo> agents)
    {
        MatchCollection matches = CreateRegex.Matches(experimentBlock);
        for (int i = 0; i < matches.Count; i++)
        {
            string name = matches[i].Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            GamaPanelAgentInfo info = GetOrCreateAgent(agents, name);
            Match number = NumberRegex.Match(matches[i].Groups["tail"].Value);
            if (number.Success)
            {
                info.CountExpression = number.Groups["value"].Value.Trim();
            }
        }
    }

    private static GamaPanelAgentInfo GetOrCreateAgent(Dictionary<string, GamaPanelAgentInfo> agents, string name)
    {
        if (!agents.TryGetValue(name, out GamaPanelAgentInfo info))
        {
            info = new GamaPanelAgentInfo { Name = name };
            agents.Add(name, info);
        }

        return info;
    }

    private static void FillHints(string block, GamaPanelAgentInfo info)
    {
        Match colorMatch = ColorRegex.Match(block);
        if (colorMatch.Success && GamaColorUtility.TryParseString(colorMatch.Groups["value"].Value, out Color32 color))
        {
            info.HasColor = true;
            info.Color = color;
        }

        Match sizeMatch = SizeRegex.Match(block);
        if (sizeMatch.Success && TryParseFloat(sizeMatch.Groups["value"].Value, out float size))
        {
            info.SuggestedScale = Mathf.Max(0.01f, size);
        }

        Match prefabMatch = PrefabRegex.Match(block);
        if (prefabMatch.Success)
        {
            info.PrefabHint = prefabMatch.Groups["value"].Value.Trim();
        }
    }

    private static string InferCapability(string fileContent, int declarationIndex, int nextDeclarationIndex)
    {
        string block = fileContent.Substring(declarationIndex, Mathf.Max(0, nextDeclarationIndex - declarationIndex));
        string blockLower = block.ToLowerInvariant();
        bool unity = Regex.IsMatch(block, @"\btype\s*:\s*unity\b", RegexOptions.IgnoreCase);
        bool nonVr = Regex.IsMatch(blockLower, @"\bnon[\s\-]?vr\b|\bdesktop\b|\bbatch\b|\btype\s*:\s*gui\b", RegexOptions.IgnoreCase);
        bool vr = Regex.IsMatch(blockLower, @"\bvr\b|\bopenxr\b|\bheadset\b|\boculus\b|\bteleport", RegexOptions.IgnoreCase);

        if (unity)
        {
            return vr ? "Unity VR" : "Unity";
        }

        if (vr && nonVr)
        {
            return "VR + Non-VR (without Unity)";
        }

        if (vr)
        {
            return "VR (without Unity)";
        }

        if (nonVr)
        {
            return "Non-VR / batch";
        }

        return "Unknown";
    }

    private static float InferSceneSize(string content)
    {
        float maxValue = 100f;
        MatchCollection matches = Regex.Matches(content, @"\b(?:width|height|size|world_size|worldSize)\s*:\s*(?<value>\d+(?:[\.,]\d+)?)", RegexOptions.IgnoreCase);
        for (int i = 0; i < matches.Count; i++)
        {
            if (TryParseFloat(matches[i].Groups["value"].Value, out float value))
            {
                maxValue = Mathf.Max(maxValue, value);
            }
        }

        return maxValue;
    }

    private static bool TryParseFloat(string raw, out float value)
    {
        return float.TryParse((raw ?? string.Empty).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
