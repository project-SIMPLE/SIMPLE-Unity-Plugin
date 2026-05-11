using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public sealed class GamaWorkspaceExplorerWindow : EditorWindow
{
    private const string WorkspacePathPrefKey = "ProjectSimple.GamaUnity.WorkspaceExplorer.Path";
    private const string WorkspacePortsPrefKey = "ProjectSimple.GamaUnity.WorkspaceExplorer.Ports";
    private const string DefaultPortsValue = "1000,8000,8080";

    private string workspacePath = string.Empty;
    private string workspacePorts = DefaultPortsValue;
    private string statusMessage = "Select a GAMA workspace folder, then click Scan.";
    private Vector2 scrollPosition;
    private List<GamaWorkspaceExperimentInfo> experiments = new List<GamaWorkspaceExperimentInfo>();
    private Task<GamaWorkspaceAutoDetectResult> autoDetectTask;

    [MenuItem("GAMA/Workspace Explorer")]
    private static void OpenWindow()
    {
        GamaWorkspaceExplorerWindow window = GetWindow<GamaWorkspaceExplorerWindow>("Workspace Explorer");
        window.minSize = new Vector2(720f, 360f);
        window.Show();
    }

    private void OnEnable()
    {
        workspacePath = EditorPrefs.GetString(WorkspacePathPrefKey, workspacePath);
        workspacePorts = EditorPrefs.GetString(WorkspacePortsPrefKey, DefaultPortsValue);
        if (string.IsNullOrWhiteSpace(workspacePorts))
        {
            workspacePorts = DefaultPortsValue;
        }

        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            StartAutoDetect(isAutoTryOnLoad: true);
        }
    }

    private void Update()
    {
        if (autoDetectTask == null || !autoDetectTask.IsCompleted)
        {
            return;
        }

        CompleteAutoDetect();
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Offline scan: no middleware connection is required. The scanner reads workspace files to discover experiments and infer VR/non-VR launch capabilities.",
            MessageType.Info);

        EditorGUILayout.Space();
        DrawWorkspacePath();

        EditorGUILayout.Space();
        DrawToolbar();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        DrawResults();
    }

    private void DrawWorkspacePath()
    {
        EditorGUILayout.LabelField("GAMA Workspace Path", EditorStyles.boldLabel);
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
            string selectedPath = EditorUtility.OpenFolderPanel("Select GAMA Workspace", workspacePath, string.Empty);
            if (!string.IsNullOrEmpty(selectedPath))
            {
                workspacePath = selectedPath;
                EditorPrefs.SetString(WorkspacePathPrefKey, workspacePath);
            }
        }

        using (new EditorGUI.DisabledScope(autoDetectTask != null && !autoDetectTask.IsCompleted))
        {
            if (GUILayout.Button("Auto-Detect", GUILayout.Width(100f)))
            {
                StartAutoDetect(isAutoTryOnLoad: false);
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2f);
        EditorGUILayout.LabelField("Detection Ports (comma-separated)", EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        string updatedPorts = EditorGUILayout.TextField(workspacePorts);
        if (EditorGUI.EndChangeCheck())
        {
            workspacePorts = string.IsNullOrWhiteSpace(updatedPorts) ? DefaultPortsValue : updatedPorts.Trim();
            EditorPrefs.SetString(WorkspacePortsPrefKey, workspacePorts);
        }
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Workspace", GUILayout.Width(160f)))
        {
            ScanWorkspace();
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)))
        {
            if (GUILayout.Button("Open Folder", GUILayout.Width(120f)))
            {
                EditorUtility.RevealInFinder(workspacePath);
            }
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private void StartAutoDetect(bool isAutoTryOnLoad)
    {
        if (autoDetectTask != null && !autoDetectTask.IsCompleted)
        {
            return;
        }

        statusMessage = isAutoTryOnLoad
            ? "Auto-detection in progress (workspace field is empty)..."
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
        catch (Exception ex)
        {
            statusMessage = $"Auto-detection failed unexpectedly: {ex.GetType().Name}. You can still use Browse or enter a path manually.";
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

        statusMessage = $"Workspace auto-detection failed. Method: {result.Method}. Port: {portLabel}. Confidence: {result.Confidence}. {result.Message}";
        Debug.LogWarning("[GAMA] Workspace auto-detection failed. Manual path entry remains available.");
    }

    private void ScanWorkspace()
    {
        experiments = GamaWorkspaceScanner.Scan(workspacePath, out string scanStatus, out int scannedFileCount, out int errorCount);
        statusMessage = $"{scanStatus} Files scanned: {scannedFileCount}. Errors: {errorCount}.";

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
            EditorGUILayout.HelpBox("No experiments found. Check that the workspace path is correct and contains .gaml files.", MessageType.Warning);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Experiment", GUILayout.Width(220f));
        GUILayout.Label("Capability", GUILayout.Width(120f));
        GUILayout.Label("Confidence", GUILayout.Width(180f));
        GUILayout.Label("Source File");
        EditorGUILayout.EndHorizontal();

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
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

internal sealed class GamaWorkspaceAutoDetectResult
{
    public GamaWorkspaceAutoDetectResult(bool success, string workspacePath, string method, int usedPort, string confidence, string message)
    {
        Success = success;
        WorkspacePath = workspacePath ?? string.Empty;
        Method = method ?? "unknown";
        UsedPort = usedPort;
        Confidence = confidence ?? "low";
        Message = message ?? string.Empty;
    }

    public bool Success { get; }
    public string WorkspacePath { get; }
    public string Method { get; }
    public int UsedPort { get; }
    public string Confidence { get; }
    public string Message { get; }
}

internal static class GamaWorkspaceAutoDetector
{
    private const int HttpTimeoutMs = 450;
    private const int HeuristicDirectoryBudget = 300;
    private const int PreferenceFileBudget = 120;
    private const int PreferenceDirectoryDepthBudget = 7;
    private const int MinimumScoreFromPreferences = 4;
    private const int MinimumScoreFromService = 4;
    private const int MinimumScoreFromProcess = 3;
    private const int MinimumScoreFromHeuristic = 5;

    private static readonly int[] DefaultCandidatePorts =
    {
        1000,
        8000,
        8080
    };

    private static readonly string[] BlacklistedPathTokens =
    {
        "\\.git\\",
        "\\node_modules\\",
        "\\library\\packagecache\\",
        "\\temp\\",
        "\\obj\\",
        "\\bin\\"
    };

    private static readonly string[] ServiceEndpoints =
    {
        "/",
        "/workspace",
        "/workspaces",
        "/status",
        "/api",
        "/api/status",
        "/api/workspace",
        "/experiment",
        "/experiments"
    };

    public static GamaWorkspaceAutoDetectResult Detect(string configuredPortsRaw)
    {
        int[] candidatePorts = ParseCandidatePorts(configuredPortsRaw);
        GamaWorkspaceAutoDetectResult viaPreferences = TryDetectFromGamaPreferences();
        if (viaPreferences.Success)
        {
            return viaPreferences;
        }

        GamaWorkspaceAutoDetectResult viaService = TryDetectFromLocalService(candidatePorts);
        if (viaService.Success)
        {
            return viaService;
        }

        GamaWorkspaceAutoDetectResult viaPortProcess = TryDetectFromPortOwnerProcess(candidatePorts);
        if (viaPortProcess.Success)
        {
            return viaPortProcess;
        }

        GamaWorkspaceAutoDetectResult viaLocalHeuristic = TryDetectFromLocalHeuristics();
        if (viaLocalHeuristic.Success)
        {
            return viaLocalHeuristic;
        }

        return new GamaWorkspaceAutoDetectResult(
            success: false,
            workspacePath: string.Empty,
            method: "none",
            usedPort: -1,
            confidence: "low",
            message: $"No solid workspace found. Ports tested: {string.Join(", ", candidatePorts)}. Heuristic fallback rejected low-confidence candidates. Use Browse to set the workspace manually.");
    }

    private static GamaWorkspaceAutoDetectResult TryDetectFromGamaPreferences()
    {
        List<string> preferenceFiles = CollectPreferenceCandidateFiles();
        if (preferenceFiles.Count == 0)
        {
            return new GamaWorkspaceAutoDetectResult(
                success: false,
                workspacePath: string.Empty,
                method: "gama-preferences",
                usedPort: -1,
                confidence: "low",
                message: "No local GAMA/Eclipse preference file was found.");
        }

        preferenceFiles.Sort((left, right) =>
        {
            DateTime leftTime = SafeGetLastWriteTimeUtc(left);
            DateTime rightTime = SafeGetLastWriteTimeUtc(right);
            int byDate = rightTime.CompareTo(leftTime);
            if (byDate != 0)
            {
                return byDate;
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        });

        string bestWorkspace = string.Empty;
        string bestSource = string.Empty;
        int bestScore = int.MinValue;
        DateTime bestSourceWriteTimeUtc = DateTime.MinValue;

        for (int fileIndex = 0; fileIndex < preferenceFiles.Count; fileIndex++)
        {
            string preferenceFile = preferenceFiles[fileIndex];
            string content;
            try
            {
                content = File.ReadAllText(preferenceFile);
            }
            catch
            {
                continue;
            }

            List<string> pathCandidates = ExtractPreferencePathCandidates(content);
            for (int candidateIndex = 0; candidateIndex < pathCandidates.Count; candidateIndex++)
            {
                if (!TryNormalizeWorkspacePath(pathCandidates[candidateIndex], MinimumScoreFromPreferences, out string detectedPath, out int score))
                {
                    continue;
                }

                DateTime sourceWriteTimeUtc = SafeGetLastWriteTimeUtc(preferenceFile);
                bool isRecentPreference = sourceWriteTimeUtc > DateTime.UtcNow.AddDays(-14);
                int finalScore = score + (isRecentPreference ? 1 : 0);
                if (finalScore > bestScore ||
                    (finalScore == bestScore && sourceWriteTimeUtc > bestSourceWriteTimeUtc))
                {
                    bestWorkspace = detectedPath;
                    bestSource = preferenceFile;
                    bestScore = finalScore;
                    bestSourceWriteTimeUtc = sourceWriteTimeUtc;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(bestWorkspace))
        {
            string sourceLabel = Path.GetFileName(bestSource);
            string confidence = bestScore >= 7 ? "high" : "medium";
            return new GamaWorkspaceAutoDetectResult(
                success: true,
                workspacePath: bestWorkspace,
                method: $"gama-preferences ({sourceLabel})",
                usedPort: -1,
                confidence: confidence,
                message: "Workspace extracted from local GAMA/Eclipse preferences (MRU/history).");
        }

        return new GamaWorkspaceAutoDetectResult(
            success: false,
            workspacePath: string.Empty,
            method: "gama-preferences",
            usedPort: -1,
            confidence: "low",
            message: "Preference files were found but no trustworthy workspace path was extracted.");
    }

    private static GamaWorkspaceAutoDetectResult TryDetectFromLocalService(IReadOnlyList<int> candidatePorts)
    {
        for (int portIndex = 0; portIndex < candidatePorts.Count; portIndex++)
        {
            int port = candidatePorts[portIndex];
            for (int endpointIndex = 0; endpointIndex < ServiceEndpoints.Length; endpointIndex++)
            {
                string endpoint = ServiceEndpoints[endpointIndex];
                string url = $"http://localhost:{port}{endpoint}";
                if (!TryGetHttpContent(url, HttpTimeoutMs, out string responseBody))
                {
                    continue;
                }

                List<string> candidates = ExtractPathCandidates(responseBody);
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
                {
                    if (TryNormalizeWorkspacePath(candidates[candidateIndex], MinimumScoreFromService, out string detectedPath, out int score))
                    {
                        return new GamaWorkspaceAutoDetectResult(
                            success: true,
                            workspacePath: detectedPath,
                            method: $"localhost:{port} ({endpoint})",
                            usedPort: port,
                            confidence: score >= 6 ? "high" : "medium",
                            message: "Workspace extracted from local GAMA service response.");
                    }
                }
            }
        }

        return new GamaWorkspaceAutoDetectResult(
            success: false,
            workspacePath: string.Empty,
            method: "localhost service",
            usedPort: -1,
            confidence: "low",
            message: "No solid workspace path exposed by tested localhost endpoints.");
    }

    private static GamaWorkspaceAutoDetectResult TryDetectFromPortOwnerProcess(IReadOnlyList<int> candidatePorts)
    {
        if (!TryRunCommand("cmd.exe", "/c netstat -ano -p tcp", 1200, out string netstatOutput))
        {
            return new GamaWorkspaceAutoDetectResult(false, string.Empty, "port-owner", -1, "low", "Unable to query netstat.");
        }

        for (int portIndex = 0; portIndex < candidatePorts.Count; portIndex++)
        {
            int port = candidatePorts[portIndex];
            List<int> pids = ParsePidsListeningOnPort(netstatOutput, port);
            for (int i = 0; i < pids.Count; i++)
            {
                int pid = pids[i];
                string wmiArgs = $"/c wmic process where processid={pid} get CommandLine,ExecutablePath /format:list";
                if (!TryRunCommand("cmd.exe", wmiArgs, 1200, out string processDetails))
                {
                    continue;
                }

                List<string> candidates = ExtractPathCandidates(processDetails);
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (TryNormalizeWorkspacePath(candidates[j], MinimumScoreFromProcess, out string detectedPath, out int score))
                    {
                        return new GamaWorkspaceAutoDetectResult(
                            success: true,
                            workspacePath: detectedPath,
                            method: $"port owner process (PID {pid})",
                            usedPort: port,
                            confidence: score >= 5 ? "medium" : "low",
                            message: "Workspace inferred from process command line/path.");
                    }
                }
            }
        }

        return new GamaWorkspaceAutoDetectResult(
            success: false,
            workspacePath: string.Empty,
            method: "port owner process",
            usedPort: -1,
            confidence: "low",
            message: "Port owner process found but no solid workspace path extracted.");
    }

    private static GamaWorkspaceAutoDetectResult TryDetectFromLocalHeuristics()
    {
        List<string> roots = new List<string>();
        AddRootIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddRootIfPresent(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        int bestScore = int.MinValue;
        string bestPath = string.Empty;

        for (int i = 0; i < roots.Count; i++)
        {
            if (TryFindWorkspaceUnderRoot(roots[i], out string detectedPath, out int score) && score > bestScore)
            {
                bestPath = detectedPath;
                bestScore = score;
            }
        }

        if (!string.IsNullOrWhiteSpace(bestPath) && bestScore >= MinimumScoreFromHeuristic)
        {
            return new GamaWorkspaceAutoDetectResult(
                success: true,
                workspacePath: bestPath,
                method: "local heuristic (Desktop/Documents)",
                usedPort: -1,
                confidence: bestScore >= 7 ? "medium" : "low",
                message: "Workspace inferred from local directory structure.");
        }

        return new GamaWorkspaceAutoDetectResult(
            success: false,
            workspacePath: string.Empty,
            method: "local heuristic",
            usedPort: -1,
            confidence: "low",
            message: "No trustworthy workspace discovered in Desktop/Documents scan.");
    }

    private static void AddRootIfPresent(List<string> roots, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            roots.Add(path);
        }
    }

    private static List<string> CollectPreferenceCandidateFiles()
    {
        HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        TryAddPreferenceFile(files, Path.Combine(roamingAppData, "GAMA", "gama.ini"));
        TryAddPreferenceFile(files, Path.Combine(localAppData, "GAMA", "gama.ini"));
        TryAddPreferenceFile(files, Path.Combine(userProfile, "gama.ini"));
        TryAddPreferenceFile(files, Path.Combine(userProfile, "eclipse.ini"));

        // Typical Eclipse/GAMA global preference locations.
        TryAddPreferenceFile(files, Path.Combine(roamingAppData, "GAMA", "configuration", ".settings", "org.eclipse.ui.ide.prefs"));
        TryAddPreferenceFile(files, Path.Combine(localAppData, "GAMA", "configuration", ".settings", "org.eclipse.ui.ide.prefs"));
        TryAddPreferenceFile(files, Path.Combine(userProfile, ".eclipse", "org.eclipse.platform", "configuration", ".settings", "org.eclipse.ui.ide.prefs"));

        // If users use the default folder name shown by GAMA, this file usually stores recent workspaces.
        TryAddPreferenceFile(files, Path.Combine(userProfile, "Gama_Workspace", ".metadata", ".plugins", "org.eclipse.core.runtime", ".settings", "org.eclipse.ui.ide.prefs"));
        TryAddPreferenceFile(files, Path.Combine(desktop, "Gama_Workspace", ".metadata", ".plugins", "org.eclipse.core.runtime", ".settings", "org.eclipse.ui.ide.prefs"));
        TryAddPreferenceFile(files, Path.Combine(documents, "Gama_Workspace", ".metadata", ".plugins", "org.eclipse.core.runtime", ".settings", "org.eclipse.ui.ide.prefs"));

        CollectPreferenceFilesFromRoot(Path.Combine(roamingAppData, "GAMA"), files);
        CollectPreferenceFilesFromRoot(Path.Combine(localAppData, "GAMA"), files);
        CollectPreferenceFilesFromRoot(Path.Combine(userProfile, ".eclipse"), files);

        return new List<string>(files);
    }

    private static void TryAddPreferenceFile(HashSet<string> files, string path)
    {
        if (files == null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
        }
        catch
        {
            // Ignore inaccessible paths.
        }
    }

    private static void CollectPreferenceFilesFromRoot(string root, HashSet<string> files)
    {
        if (files == null || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        Queue<KeyValuePair<string, int>> pending = new Queue<KeyValuePair<string, int>>();
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(new KeyValuePair<string, int>(root, 0));
        visited.Add(root);

        while (pending.Count > 0 && files.Count < PreferenceFileBudget)
        {
            KeyValuePair<string, int> item = pending.Dequeue();
            string current = item.Key;
            int depth = item.Value;

            try
            {
                string[] fileList = Directory.GetFiles(current);
                for (int i = 0; i < fileList.Length && files.Count < PreferenceFileBudget; i++)
                {
                    string filePath = fileList[i];
                    string fileName = Path.GetFileName(filePath);
                    if (IsPreferenceFileName(fileName))
                    {
                        files.Add(filePath);
                    }
                }
            }
            catch
            {
                // Ignore inaccessible directories.
            }

            if (depth >= PreferenceDirectoryDepthBudget)
            {
                continue;
            }

            try
            {
                string[] subDirectories = Directory.GetDirectories(current);
                for (int i = 0; i < subDirectories.Length; i++)
                {
                    string subDirectory = subDirectories[i];
                    if (visited.Contains(subDirectory))
                    {
                        continue;
                    }

                    string directoryName = Path.GetFileName(subDirectory);
                    if (ShouldSkipPreferenceDirectory(directoryName))
                    {
                        continue;
                    }

                    visited.Add(subDirectory);
                    pending.Enqueue(new KeyValuePair<string, int>(subDirectory, depth + 1));
                }
            }
            catch
            {
                // Ignore inaccessible directories.
            }
        }
    }

    private static bool IsPreferenceFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.Equals("org.eclipse.ui.ide.prefs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Equals("dialog_settings.xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Equals("gama.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Equals("eclipse.ini", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.EndsWith(".prefs", StringComparison.OrdinalIgnoreCase) &&
            fileName.IndexOf("workspace", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static bool ShouldSkipPreferenceDirectory(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return true;
        }

        return directoryName.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("tmp", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("temp", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("packages", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ExtractPreferencePathCandidates(string text)
    {
        HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        AddCandidates(candidates, ExtractPathCandidates(text));

        string unescaped = UnescapePreferenceText(text);
        AddCandidates(candidates, ExtractPathCandidates(unescaped));

        MatchCollection dataMatches = Regex.Matches(unescaped, @"-data\s+(?:""(?<quoted>[A-Za-z]:\\[^""]+)""|(?<path>[A-Za-z]:\\[^\s]+))", RegexOptions.IgnoreCase);
        for (int i = 0; i < dataMatches.Count; i++)
        {
            Match match = dataMatches[i];
            string raw = match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["path"].Value;
            if (!string.IsNullOrWhiteSpace(raw))
            {
                candidates.Add(NormalizeRawCandidate(raw));
            }
        }

        // Key/value preferences (RECENT_WORKSPACES, LAST_USED_WORKSPACE, etc.).
        MatchCollection kvMatches = Regex.Matches(unescaped, @"(?im)^\s*[\w\.\-]*(workspace|recent|data)[\w\.\-]*\s*=\s*(?<value>.+)$");
        for (int i = 0; i < kvMatches.Count; i++)
        {
            string value = kvMatches[i].Groups["value"].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string[] parts = value.Split(new[] { ';', '|', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            for (int j = 0; j < parts.Length; j++)
            {
                AddCandidates(candidates, ExtractPathCandidates(parts[j]));
            }
        }

        return new List<string>(candidates);
    }

    private static string UnescapePreferenceText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Eclipse often escapes ':' and '\' in workspace history fields.
        return text
            .Replace("\\:", ":")
            .Replace("\\\\", "\\")
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("%20", " ");
    }

    private static void AddCandidates(HashSet<string> destination, IReadOnlyList<string> source)
    {
        if (destination == null || source == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(source[i]))
            {
                continue;
            }

            destination.Add(source[i]);
        }
    }

    private static DateTime SafeGetLastWriteTimeUtc(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path);
            }
        }
        catch
        {
            // Best effort only.
        }

        return DateTime.MinValue;
    }

    private static bool TryGetHttpContent(string url, int timeoutMs, out string body)
    {
        body = string.Empty;
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = timeoutMs;
            request.ReadWriteTimeout = timeoutMs;

            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                body = reader.ReadToEnd();
                return !string.IsNullOrWhiteSpace(body);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRunCommand(string fileName, string arguments, int timeoutMs, out string output)
    {
        output = string.Empty;
        try
        {
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(info))
            {
                if (process == null)
                {
                    return false;
                }

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Best effort timeout handling.
                    }
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                output = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}";
                return !string.IsNullOrWhiteSpace(output);
            }
        }
        catch
        {
            return false;
        }
    }

    private static List<int> ParsePidsListeningOnPort(string netstatOutput, int port)
    {
        HashSet<int> unique = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(netstatOutput))
        {
            return new List<int>();
        }

        string[] lines = netstatOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (!line.Contains("LISTENING", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Regex.IsMatch(line, $@":{port}\s"))
            {
                continue;
            }

            string[] tokens = Regex.Split(line, @"\s+");
            if (tokens.Length < 5)
            {
                continue;
            }

            if (int.TryParse(tokens[tokens.Length - 1], out int pid))
            {
                unique.Add(pid);
            }
        }

        return new List<int>(unique);
    }

    private static List<string> ExtractPathCandidates(string text)
    {
        HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<string>();
        }

        MatchCollection matches = Regex.Matches(text, @"[A-Za-z]:\\[^""'\r\n]+", RegexOptions.IgnoreCase);
        for (int i = 0; i < matches.Count; i++)
        {
            string raw = matches[i].Value.Trim();
            string normalized = NormalizeRawCandidate(raw);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        return new List<string>(candidates);
    }

    private static int[] ParseCandidatePorts(string configuredPortsRaw)
    {
        if (string.IsNullOrWhiteSpace(configuredPortsRaw))
        {
            return DefaultCandidatePorts;
        }

        string[] chunks = configuredPortsRaw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        HashSet<int> ports = new HashSet<int>();
        for (int i = 0; i < chunks.Length; i++)
        {
            if (!int.TryParse(chunks[i], out int port))
            {
                continue;
            }

            if (port < 1 || port > 65535)
            {
                continue;
            }

            ports.Add(port);
        }

        if (ports.Count == 0)
        {
            return DefaultCandidatePorts;
        }

        int[] parsed = new int[ports.Count];
        ports.CopyTo(parsed);
        Array.Sort(parsed);
        return parsed;
    }

    private static string NormalizeRawCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        string cleaned = candidate
            .Trim()
            .Trim('"', '\'', '`')
            .TrimEnd(';', ',', ')', ']', '}')
            .Replace("\\\\", "\\")
            .Replace('/', '\\');

        int marker = cleaned.IndexOf(".gaml", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            cleaned = cleaned.Substring(0, marker + 5);
        }

        return cleaned;
    }

    private static bool TryNormalizeWorkspacePath(string candidate, out string workspacePath)
    {
        return TryNormalizeWorkspacePath(candidate, minimumScore: 1, out workspacePath, out _);
    }

    private static bool TryNormalizeWorkspacePath(string candidate, int minimumScore, out string workspacePath, out int score)
    {
        workspacePath = string.Empty;
        score = 0;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string normalized = NormalizeRawCandidate(candidate);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (IsBlacklistedWorkspacePath(normalized))
        {
            return false;
        }

        if (normalized.EndsWith(".gaml", StringComparison.OrdinalIgnoreCase) && File.Exists(normalized))
        {
            normalized = Path.GetDirectoryName(normalized);
        }

        if (!Directory.Exists(normalized))
        {
            return false;
        }

        string bestRoot = FindBestWorkspaceRoot(normalized);
        if (string.IsNullOrWhiteSpace(bestRoot) || !Directory.Exists(bestRoot))
        {
            return false;
        }

        if (!EvaluateWorkspaceConfidence(bestRoot, out score))
        {
            return false;
        }

        if (score < minimumScore)
        {
            return false;
        }

        workspacePath = bestRoot;
        return true;
    }

    private static string FindBestWorkspaceRoot(string startingDirectory)
    {
        if (string.IsNullOrWhiteSpace(startingDirectory) || !Directory.Exists(startingDirectory))
        {
            return string.Empty;
        }

        string current = startingDirectory;
        int guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard < 8)
        {
            if (IsBlacklistedWorkspacePath(current))
            {
                return string.Empty;
            }

            if (HasWorkspaceIndicators(current))
            {
                return current;
            }

            string parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
            guard++;
        }

        return startingDirectory;
    }

    private static bool HasWorkspaceIndicators(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return false;
            }

            if (Directory.Exists(Path.Combine(directory, ".metadata")))
            {
                return true;
            }

            if (Directory.GetFiles(directory, "*.gaml", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return true;
            }

            return CountGamlFiles(directory, 2, 8) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFindWorkspaceUnderRoot(string root, out string workspacePath)
    {
        return TryFindWorkspaceUnderRoot(root, out workspacePath, out _);
    }

    private static bool TryFindWorkspaceUnderRoot(string root, out string workspacePath, out int workspaceScore)
    {
        workspacePath = string.Empty;
        workspaceScore = 0;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return false;
        }

        Queue<string> pending = new Queue<string>();
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        pending.Enqueue(root);
        visited.Add(root);

        int explored = 0;
        int bestScore = int.MinValue;
        string bestCandidate = string.Empty;
        while (pending.Count > 0 && explored < HeuristicDirectoryBudget)
        {
            string current = pending.Dequeue();
            explored++;

            try
            {
                if (IsBlacklistedWorkspacePath(current))
                {
                    continue;
                }

                if (EvaluateWorkspaceConfidence(current, out int score) && score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = current;
                }

                string[] subDirs = Directory.GetDirectories(current);
                for (int i = 0; i < subDirs.Length; i++)
                {
                    string subDir = subDirs[i];
                    string name = Path.GetFileName(subDir);
                    if (ShouldSkipHeuristicDirectory(name) || visited.Contains(subDir))
                    {
                        continue;
                    }

                    visited.Add(subDir);
                    pending.Enqueue(subDir);
                }
            }
            catch
            {
                // Ignore inaccessible directories.
            }
        }

        if (string.IsNullOrWhiteSpace(bestCandidate))
        {
            return false;
        }

        string resolved = FindBestWorkspaceRoot(bestCandidate);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return false;
        }

        workspacePath = resolved;
        workspaceScore = bestScore;
        return true;
    }

    private static bool ShouldSkipHeuristicDirectory(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return true;
        }

        return directoryName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("git", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals(".idea", StringComparison.OrdinalIgnoreCase) ||
               directoryName.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlacklistedWorkspacePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        string normalized = path.Replace('/', '\\');
        for (int i = 0; i < BlacklistedPathTokens.Length; i++)
        {
            if (normalized.IndexOf(BlacklistedPathTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        string leaf = Path.GetFileName(normalized.TrimEnd('\\'));
        return ShouldSkipHeuristicDirectory(leaf);
    }

    private static bool EvaluateWorkspaceConfidence(string directory, out int score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || IsBlacklistedWorkspacePath(directory))
        {
            return false;
        }

        try
        {
            int topLevelGaml = Directory.GetFiles(directory, "*.gaml", SearchOption.TopDirectoryOnly).Length;
            int nestedGaml = CountGamlFiles(directory, 2, 8);
            bool hasMetadata = Directory.Exists(Path.Combine(directory, ".metadata"));
            bool hasProject = File.Exists(Path.Combine(directory, ".project"));
            bool hasModels = Directory.Exists(Path.Combine(directory, "models"));
            bool hasExperiments = Directory.Exists(Path.Combine(directory, "experiments"));

            if (hasMetadata)
            {
                score += 3;
            }

            if (hasProject)
            {
                score += 2;
            }

            if (hasModels)
            {
                score += 1;
            }

            if (hasExperiments)
            {
                score += 1;
            }

            if (topLevelGaml >= 1)
            {
                score += 2;
            }

            if (nestedGaml >= 3)
            {
                score += 2;
            }
            else if (nestedGaml >= 1)
            {
                score += 1;
            }

            bool strongEnough = nestedGaml >= 2 || (topLevelGaml >= 1 && (hasMetadata || hasProject || hasModels || hasExperiments));
            return strongEnough;
        }
        catch
        {
            return false;
        }
    }

    private static int CountGamlFiles(string root, int maxDepth, int maxHits)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return 0;
        }

        int hits = 0;
        Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
        queue.Enqueue(new KeyValuePair<string, int>(root, 0));

        while (queue.Count > 0)
        {
            KeyValuePair<string, int> item = queue.Dequeue();
            string current = item.Key;
            int depth = item.Value;

            try
            {
                string[] files = Directory.GetFiles(current, "*.gaml", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                {
                    hits += files.Length;
                    if (hits >= maxHits)
                    {
                        return hits;
                    }
                }

                if (depth >= maxDepth)
                {
                    continue;
                }

                string[] dirs = Directory.GetDirectories(current);
                for (int i = 0; i < dirs.Length; i++)
                {
                    if (ShouldSkipHeuristicDirectory(Path.GetFileName(dirs[i])))
                    {
                        continue;
                    }

                    queue.Enqueue(new KeyValuePair<string, int>(dirs[i], depth + 1));
                }
            }
            catch
            {
                // Ignore inaccessible content and continue best effort.
            }
        }

        return hits;
    }
}

internal static class GamaWorkspaceScanner
{
    private static readonly Regex ExperimentHeaderRegex = new Regex(
        @"^\s*experiment\s+(?:""(?<quoted>[^""]+)""|(?<name>[A-Za-z_][\w\-]*))(?<suffix>[^\r\n]*)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly string[] VrKeywords =
    {
        " vr",
        "vr_",
        "_vr",
        "openxr",
        "xr ",
        "xr_",
        "_xr",
        "headset",
        "oculus",
        "steamvr",
        "teleport"
    };

    private static readonly string[] NonVrKeywords =
    {
        "non-vr",
        "non vr",
        "desktop",
        "2d",
        "batch",
        "headless"
    };

    public static List<GamaWorkspaceExperimentInfo> Scan(string rootPath, out string status, out int scannedFileCount, out int errorCount)
    {
        scannedFileCount = 0;
        errorCount = 0;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            status = "Workspace path is empty.";
            return new List<GamaWorkspaceExperimentInfo>();
        }

        if (!Directory.Exists(rootPath))
        {
            status = "Workspace path does not exist or is not accessible.";
            return new List<GamaWorkspaceExperimentInfo>();
        }

        List<GamaWorkspaceExperimentInfo> results = new List<GamaWorkspaceExperimentInfo>();
        foreach (string gamlFile in EnumerateFilesSafely(rootPath, "*.gaml", ref errorCount))
        {
            scannedFileCount++;
            try
            {
                string content = File.ReadAllText(gamlFile);
                ParseExperiments(rootPath, gamlFile, content, results);
            }
            catch (UnauthorizedAccessException)
            {
                errorCount++;
            }
            catch (IOException)
            {
                errorCount++;
            }
            catch (Exception)
            {
                errorCount++;
            }
        }

        results.Sort((left, right) =>
        {
            int byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }

            return string.Compare(left.RelativeFilePath, right.RelativeFilePath, StringComparison.OrdinalIgnoreCase);
        });

        status = results.Count == 0
            ? "Scan complete. No experiments detected."
            : $"Scan complete. Found {results.Count} experiment(s).";

        return results;
    }

    private static List<string> EnumerateFilesSafely(string rootPath, string pattern, ref int errorCount)
    {
        Stack<string> pending = new Stack<string>();
        List<string> collectedFiles = new List<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            string currentDirectory = pending.Pop();
            string[] subDirectories;
            try
            {
                subDirectories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception)
            {
                errorCount++;
                continue;
            }

            for (int i = 0; i < subDirectories.Length; i++)
            {
                string directoryName = Path.GetFileName(subDirectories[i]);
                if (string.Equals(directoryName, ".git", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(directoryName, ".svn", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(subDirectories[i]);
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory, pattern);
            }
            catch (Exception)
            {
                errorCount++;
                continue;
            }

            for (int i = 0; i < files.Length; i++)
            {
                collectedFiles.Add(files[i]);
            }
        }

        return collectedFiles;
    }

    private static void ParseExperiments(string rootPath, string filePath, string content, List<GamaWorkspaceExperimentInfo> results)
    {
        MatchCollection matches = ExperimentHeaderRegex.Matches(content);
        if (matches.Count == 0)
        {
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            Match current = matches[i];
            string experimentName = ExtractExperimentName(current);
            if (string.IsNullOrWhiteSpace(experimentName))
            {
                continue;
            }

            int start = current.Index;
            int end = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            int length = Mathf.Max(0, end - start);
            string experimentBlock = content.Substring(start, length);

            LaunchCapability capability = InferCapability(current.Groups["suffix"].Value, experimentBlock);
            results.Add(new GamaWorkspaceExperimentInfo(
                experimentName,
                capability,
                GetConfidenceNote(capability),
                MakeRelativePath(rootPath, filePath)));
        }
    }

    private static string ExtractExperimentName(Match match)
    {
        if (match.Groups["quoted"].Success)
        {
            return match.Groups["quoted"].Value.Trim();
        }

        if (match.Groups["name"].Success)
        {
            return match.Groups["name"].Value.Trim();
        }

        return string.Empty;
    }

    private static LaunchCapability InferCapability(string declarationSuffix, string experimentBlock)
    {
        string combined = $"{declarationSuffix} {experimentBlock}".ToLowerInvariant();

        bool hasVrSignal = ContainsAnyKeyword(combined, VrKeywords);
        bool hasNonVrSignal = ContainsAnyKeyword(combined, NonVrKeywords);

        if (combined.Contains("type:") && combined.Contains("batch"))
        {
            hasNonVrSignal = true;
        }

        if (combined.Contains("type:") && combined.Contains("gui"))
        {
            hasNonVrSignal = true;
        }

        if (combined.Contains("type:") && combined.Contains("vr"))
        {
            hasVrSignal = true;
        }

        if (hasVrSignal && hasNonVrSignal)
        {
            return LaunchCapability.VrAndNonVr;
        }

        if (hasVrSignal)
        {
            return LaunchCapability.VrOnly;
        }

        if (hasNonVrSignal)
        {
            return LaunchCapability.NonVrOnly;
        }

        return LaunchCapability.Unknown;
    }

    private static bool ContainsAnyKeyword(string source, IReadOnlyList<string> keywords)
    {
        for (int i = 0; i < keywords.Count; i++)
        {
            if (source.Contains(keywords[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetConfidenceNote(LaunchCapability capability)
    {
        return capability == LaunchCapability.Unknown ? "Low (metadata missing)" : "Heuristic";
    }

    private static string MakeRelativePath(string rootPath, string absoluteFilePath)
    {
        if (absoluteFilePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return absoluteFilePath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return absoluteFilePath;
    }
}

internal sealed class GamaWorkspaceExperimentInfo
{
    public GamaWorkspaceExperimentInfo(string name, LaunchCapability capability, string confidenceNote, string relativeFilePath)
    {
        Name = name;
        Capability = capability;
        ConfidenceNote = confidenceNote;
        RelativeFilePath = relativeFilePath;
    }

    public string Name { get; }
    public LaunchCapability Capability { get; }
    public string ConfidenceNote { get; }
    public string RelativeFilePath { get; }

    public string CapabilityLabel
    {
        get
        {
            switch (Capability)
            {
                case LaunchCapability.VrOnly:
                    return "VR";
                case LaunchCapability.NonVrOnly:
                    return "Non-VR";
                case LaunchCapability.VrAndNonVr:
                    return "VR + Non-VR";
                default:
                    return "Unknown";
            }
        }
    }
}

internal enum LaunchCapability
{
    Unknown = 0,
    VrOnly = 1,
    NonVrOnly = 2,
    VrAndNonVr = 3
}
