using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared workspace scan UI used by <see cref="GamaWorkspaceExplorerWindow"/> and <see cref="GamaPanelWindow"/>.
/// </summary>
internal sealed class GamaWorkspaceExplorerPanel
{
    internal const string WorkspacePathPrefKey = "ProjectSimple.GamaUnity.WorkspaceExplorer.Path";
    internal const string WorkspacePortsPrefKey = "ProjectSimple.GamaUnity.WorkspaceExplorer.Ports";
    internal const string AdvancedSectionPrefKey = "ProjectSimple.GamaUnity.WorkspaceExplorer.AdvancedOpen";
    private const string DefaultPortsValue = "1000,8000,8080";
    private const float ResultsScrollMinHeight = 280f;

    private string workspacePath = string.Empty;
    private string workspacePorts = DefaultPortsValue;
    private string statusMessage = "Choose a GAMA workspace folder, then click Scan.";
    private Vector2 scrollPosition;
    private List<GamaWorkspaceExperimentInfo> experiments = new List<GamaWorkspaceExperimentInfo>();
    private Task<GamaWorkspaceAutoDetectResult> autoDetectTask;
    private bool advancedSectionExpanded;

    public void OnHostEnable()
    {
        workspacePath = EditorPrefs.GetString(WorkspacePathPrefKey, workspacePath);
        workspacePorts = EditorPrefs.GetString(WorkspacePortsPrefKey, DefaultPortsValue);
        advancedSectionExpanded = EditorPrefs.GetBool(AdvancedSectionPrefKey, false);
        if (string.IsNullOrWhiteSpace(workspacePorts))
        {
            workspacePorts = DefaultPortsValue;
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            StartAutoDetect(isAutoTryOnLoad: true);
        }
    }

    public void Tick(EditorWindow repaintHost)
    {
        if (autoDetectTask == null || !autoDetectTask.IsCompleted)
        {
            return;
        }

        CompleteAutoDetect();
        if (repaintHost != null)
        {
            repaintHost.Repaint();
        }
    }

    public void OnGUI()
    {
        DrawCompactWorkspaceUi();
        EditorGUILayout.Space(4f);
        DrawAdvancedFoldout();
    }

    /// <summary>
    /// Chemin, scan, statut et liste — utilisé aussi depuis <see cref="GamaPanelWindow"/> au-dessus du repliable global.
    /// </summary>
    public void DrawCompactWorkspaceUi()
    {
        DrawPrimaryPathAndScan();

        EditorGUILayout.Space(6f);
        DrawStatusLine();
        DrawResults();
    }

    /// <summary>
    /// Options secondaires du scan (sans repliable). Le repliable est géré par l’hôte ou par <see cref="DrawAdvancedFoldout"/>.
    /// </summary>
    public void DrawAdvancedScannerOptions()
    {
        EditorGUILayout.HelpBox(
            "Local parsing of .gaml files: detected experiments, VR / non-VR launch hints. No middleware connection needed. The ports below are only for auto-detection (local services, GAMA preferences).",
            MessageType.Info);

        DrawDetectionPortsField();

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(autoDetectTask != null && !autoDetectTask.IsCompleted))
        {
            if (GUILayout.Button("Auto-detect workspace", GUILayout.Width(200f)))
            {
                StartAutoDetect(isAutoTryOnLoad: false);
            }
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)))
        {
            if (GUILayout.Button("Open folder", GUILayout.Width(130f)))
            {
                EditorUtility.RevealInFinder(workspacePath);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawPrimaryPathAndScan()
    {
        EditorGUILayout.LabelField("GAMA Workspace — offline scan", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        string updatedPath = EditorGUILayout.TextField(workspacePath);
        if (EditorGUI.EndChangeCheck())
        {
            workspacePath = updatedPath.Trim();
            EditorPrefs.SetString(WorkspacePathPrefKey, workspacePath);
        }

        if (GUILayout.Button("Browse...", GUILayout.Width(100f)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("GAMA workspace folder", workspacePath, string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                workspacePath = selectedPath;
                EditorPrefs.SetString(WorkspacePathPrefKey, workspacePath);
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("Scan workspace", GUILayout.Height(30f)))
        {
            ScanWorkspace();
        }
    }

    private void DrawStatusLine()
    {
        EditorGUILayout.HelpBox(statusMessage, MessageType.None);
    }

    private void DrawAdvancedFoldout()
    {
        EditorGUI.BeginChangeCheck();
        advancedSectionExpanded = EditorGUILayout.Foldout(advancedSectionExpanded, "Advanced settings (auto-detect, ports, folder...)", true);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(AdvancedSectionPrefKey, advancedSectionExpanded);
        }

        if (!advancedSectionExpanded)
        {
            return;
        }

        EditorGUI.indentLevel++;
        DrawAdvancedScannerOptions();
        EditorGUI.indentLevel--;
    }

    private void DrawDetectionPortsField()
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Detection ports (comma-separated)", EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        string updatedPorts = EditorGUILayout.TextField(workspacePorts);
        if (EditorGUI.EndChangeCheck())
        {
            workspacePorts = string.IsNullOrWhiteSpace(updatedPorts) ? DefaultPortsValue : updatedPorts.Trim();
            EditorPrefs.SetString(WorkspacePortsPrefKey, workspacePorts);
        }
    }

    private void StartAutoDetect(bool isAutoTryOnLoad)
    {
        if (autoDetectTask != null && !autoDetectTask.IsCompleted)
        {
            return;
        }

        statusMessage = isAutoTryOnLoad
            ? "Auto-detection in progress (workspace field empty)..."
            : "Auto-detection in progress...";

        string configuredPorts = workspacePorts;
        autoDetectTask = Task.Run(() => GamaWorkspaceAutoDetector.Detect(configuredPorts));
    }

    private void CompleteAutoDetect()
    {
        Task<GamaWorkspaceAutoDetectResult> completedTask = autoDetectTask;
        autoDetectTask = null;

        GamaWorkspaceAutoDetectResult result;
        try
        {
            result = completedTask.Result;
        }
        catch (System.Exception ex)
        {
            statusMessage = $"Unexpected auto-detection failure: {ex.GetType().Name}. You can still enter a path or use Browse.";
            Debug.LogWarning($"[GAMA] Workspace auto-detection failed: {ex.Message}");
            return;
        }

        string portLabel = result.UsedPort > 0 ? result.UsedPort.ToString() : "n/a";
        if (result.Success)
        {
            workspacePath = result.WorkspacePath;
            EditorPrefs.SetString(WorkspacePathPrefKey, workspacePath);
            statusMessage = $"Workspace detected. Method: {result.Method}. Port: {portLabel}. Confidence: {result.Confidence}. Path: {workspacePath}";
            Debug.Log($"[GAMA] Workspace auto-detected via {result.Method} (port {portLabel}, {result.Confidence}).");
            return;
        }

        statusMessage = $"Cannot auto-detect workspace. Method: {result.Method}. Port: {portLabel}. Confidence: {result.Confidence}. {result.Message}";
        Debug.LogWarning("[GAMA] Auto-detection failed. Manual path entry is still possible.");
    }

    private void ScanWorkspace()
    {
        experiments = GamaWorkspaceScanner.Scan(workspacePath, out _, out int scannedFileCount, out int errorCount);
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            statusMessage = "The workspace path is empty.";
        }
        else if (!Directory.Exists(workspacePath))
        {
            statusMessage = "The workspace path does not exist or is not accessible.";
        }
        else if (experiments.Count == 0)
        {
            statusMessage =
                $"Scan completed. No experiment detected. Scanned .gaml files: {scannedFileCount}. Errors: {errorCount}.";
        }
        else
        {
            statusMessage =
                $"Scan completed. {experiments.Count} experiment(s) found. Scanned .gaml files: {scannedFileCount}. Errors: {errorCount}.";
        }

        if (errorCount > 0)
        {
            Debug.LogWarning($"[GAMA] Workspace Explorer completed with {errorCount} scanning issue(s).");
        }
        else
        {
            Debug.Log($"[GAMA] Workspace Explorer found {experiments.Count} experiment(s).");
        }
    }

    private void DrawResults()
    {
        EditorGUILayout.LabelField($"Experiments ({experiments.Count})", EditorStyles.boldLabel);
        if (experiments.Count == 0)
        {
            EditorGUILayout.HelpBox("No experiments yet. Check the workspace path and run a scan.", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Experiment", GUILayout.Width(220f));
        GUILayout.Label("Capability", GUILayout.Width(120f));
        GUILayout.Label("Confidence", GUILayout.Width(180f));
        GUILayout.Label("Source File");
        EditorGUILayout.EndHorizontal();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUI.skin.box, GUILayout.MinHeight(ResultsScrollMinHeight));
        for (int i = 0; i < experiments.Count; i++)
        {
            GamaWorkspaceExperimentInfo entry = experiments[i];
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label(entry.Name, GUILayout.Width(220f));
            GUILayout.Label(entry.CapabilityLabel, GUILayout.Width(120f));
            GUILayout.Label(entry.ConfidenceNote, GUILayout.Width(180f));
            GUILayout.Label(entry.RelativeFilePath);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }
}
