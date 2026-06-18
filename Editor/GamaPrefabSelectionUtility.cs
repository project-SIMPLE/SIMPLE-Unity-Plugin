using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class GamaPrefabSelectionUtility
{
    private const float ClearButtonWidth = 44f;
    private const float BrowseButtonWidth = 28f;
    private const double CacheLifetimeSeconds = 8.0;
    private static readonly List<GameObject> CachedVisualPrefabs = new List<GameObject>();
    private static readonly Dictionary<string, GameObject> PendingSelections = new Dictionary<string, GameObject>();
    private static double lastCacheRefreshTime = -1000.0;

    private sealed class PrefabChoice
    {
        public string Label;
        public GameObject Prefab;
    }

    public static GameObject DrawPrefabSelector(
        string label,
        GameObject current,
        string speciesName,
        string prefabHint)
    {
        return (GameObject)EditorGUILayout.ObjectField(
            new GUIContent(label),
            current,
            typeof(GameObject),
            false);
    }

    public static GameObject DrawCompactPrefabSelector(
        GameObject current,
        string speciesName,
        string prefabHint,
        float width)
    {
        EditorGUILayout.BeginHorizontal(GUILayout.Width(width));
        GameObject selected = DrawPrefabSelectorControl(
            "compact|" + speciesName + "|" + prefabHint,
            current,
            speciesName,
            prefabHint,
            GUILayout.Width(Mathf.Max(80f, width - ClearButtonWidth - BrowseButtonWidth - 8f)));
        EditorGUILayout.EndHorizontal();
        return selected;
    }

    private static GameObject DrawPrefabSelectorControl(
        string key,
        GameObject current,
        string speciesName,
        string prefabHint,
        params GUILayoutOption[] buttonOptions)
    {
        GameObject pending;
        if (PendingSelections.TryGetValue(key, out pending))
        {
            PendingSelections.Remove(key);
            current = pending;
            GUI.changed = true;
        }

        List<PrefabChoice> choices = BuildPrefabChoices(current, speciesName, prefabHint);
        GameObject selected = current;

        if (GUILayout.Button(BuildCurrentButtonLabel(current), EditorStyles.popup, buttonOptions))
        {
            ShowPrefabMenu(key, choices, current);
        }

        EditorGUI.BeginDisabledGroup(current == null);
        if (GUILayout.Button("Clear", GUILayout.Width(ClearButtonWidth)))
        {
            selected = null;
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("...", GUILayout.Width(BrowseButtonWidth)))
        {
            GameObject browsed = BrowsePrefab(current);
            if (browsed != null || current != null)
            {
                selected = browsed;
            }
        }

        return selected;
    }

    private static string BuildCurrentButtonLabel(GameObject current)
    {
        return current != null ? current.name : "None";
    }

    private static void ShowPrefabMenu(string key, List<PrefabChoice> choices, GameObject current)
    {
        GenericMenu menu = new GenericMenu();
        for (int i = 0; i < choices.Count; i++)
        {
            PrefabChoice choice = choices[i];
            GameObject prefab = choice.Prefab;
            bool selected = prefab == current;
            menu.AddItem(
                new GUIContent(SanitizeMenuLabel(choice.Label)),
                selected,
                () => QueueSelection(key, prefab));
        }

        menu.AddSeparator(string.Empty);
        menu.AddItem(new GUIContent("Refresh prefab list"), false, () => RefreshPrefabCache());
        menu.ShowAsContext();
    }

    private static string SanitizeMenuLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Unnamed";
        }

        return label.Replace("/", " > ").Replace("\\", " > ");
    }

    private static void QueueSelection(string key, GameObject prefab)
    {
        PendingSelections[key] = prefab;
        GUI.changed = true;
        EditorApplication.delayCall += RepaintProjectWindows;
    }

    private static void RefreshPrefabCache()
    {
        CachedVisualPrefabs.Clear();
        lastCacheRefreshTime = -1000.0;
        EditorApplication.delayCall += RepaintProjectWindows;
    }

    private static void RepaintProjectWindows()
    {
        EditorApplication.RepaintProjectWindow();
        SceneView.RepaintAll();
    }

    private static List<PrefabChoice> BuildPrefabChoices(
        GameObject current,
        string speciesName,
        string prefabHint)
    {
        List<PrefabChoice> choices = new List<PrefabChoice>
        {
            new PrefabChoice { Label = "None", Prefab = null }
        };

        HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddChoice(choices, seenPaths, current, "Current");

        GameObject hintedPrefab;
        if (TryResolvePrefabHint(prefabHint, out hintedPrefab))
        {
            AddChoice(choices, seenPaths, hintedPrefab, "Suggested");
        }

        foreach (GameObject prefab in FindVisualPrefabs(speciesName, prefabHint))
        {
            AddChoice(choices, seenPaths, prefab, null);
        }

        return choices;
    }

    private static void AddChoice(
        List<PrefabChoice> choices,
        HashSet<string> seenPaths,
        GameObject prefab,
        string prefix)
    {
        if (prefab == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(prefab);
        string key = string.IsNullOrWhiteSpace(path) ? prefab.GetInstanceID().ToString() : path;
        if (!seenPaths.Add(key))
        {
            return;
        }

        string label = BuildChoiceLabel(prefab, path);
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            label = prefix + ": " + label;
        }

        choices.Add(new PrefabChoice { Label = label, Prefab = prefab });
    }

    private static string BuildChoiceLabel(GameObject prefab, string path)
    {
        string name = prefab != null ? prefab.name : "None";
        string folder = string.Empty;
        if (!string.IsNullOrWhiteSpace(path))
        {
            string normalized = path.Replace('\\', '/');
            int resourcesIndex = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
            if (resourcesIndex >= 0)
            {
                folder = normalized.Substring(resourcesIndex + "/Resources/".Length);
            }
            else
            {
                folder = normalized;
            }

            int slash = folder.LastIndexOf('/');
            if (slash >= 0)
            {
                folder = folder.Substring(0, slash);
            }
        }

        return string.IsNullOrWhiteSpace(folder) ? name : name + "  [" + folder + "]";
    }

    private static IEnumerable<GameObject> FindVisualPrefabs(string speciesName, string prefabHint)
    {
        string species = (speciesName ?? string.Empty).Trim();
        string hint = (prefabHint ?? string.Empty).Trim();
        List<GameObject> broad = new List<GameObject>();
        List<GameObject> exact = new List<GameObject>();

        List<GameObject> prefabs = GetCachedVisualPrefabs();
        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            if (prefab == null)
            {
                continue;
            }

            if (IsNameMatch(prefab.name, species) || IsNameMatch(prefab.name, hint))
            {
                exact.Add(prefab);
            }
            else
            {
                broad.Add(prefab);
            }
        }

        exact.Sort(ComparePrefabNames);
        broad.Sort(ComparePrefabNames);

        foreach (GameObject prefab in exact)
        {
            yield return prefab;
        }

        foreach (GameObject prefab in broad)
        {
            yield return prefab;
        }
    }

    private static List<GameObject> GetCachedVisualPrefabs()
    {
        double now = EditorApplication.timeSinceStartup;
        if (CachedVisualPrefabs.Count > 0 && now - lastCacheRefreshTime < CacheLifetimeSeconds)
        {
            return CachedVisualPrefabs;
        }

        CachedVisualPrefabs.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!IsRelevantPrefabPath(path))
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                CachedVisualPrefabs.Add(prefab);
            }
        }

        lastCacheRefreshTime = now;
        return CachedVisualPrefabs;
    }

    private static bool IsRelevantPrefabPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string normalized = path.Replace('\\', '/');
        string lower = normalized.ToLowerInvariant();
        if (!lower.EndsWith(".prefab", StringComparison.Ordinal))
        {
            return false;
        }

        if (lower.Contains("/debug") ||
            lower.Contains("/samples") ||
            lower.Contains("/manager") ||
            lower.Contains("/ui/") ||
            lower.Contains("/runtime/resources/prefabs/debug") ||
            lower.Contains("/runtime/resources/prefabs/managers"))
        {
            return false;
        }

        return lower.Contains("/visual prefabs/") ||
               lower.Contains("/resources/prefabs/visual") ||
               (lower.StartsWith("assets/") && lower.Contains("/resources/") && lower.Contains("/prefabs/"));
    }

    private static bool IsNameMatch(string prefabName, string token)
    {
        if (string.IsNullOrWhiteSpace(prefabName) || string.IsNullOrWhiteSpace(token) || token == "-")
        {
            return false;
        }

        string prefab = NormalizeToken(prefabName);
        string wanted = NormalizeToken(token);
        return prefab.Contains(wanted) || wanted.Contains(prefab);
    }

    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static int ComparePrefabNames(GameObject left, GameObject right)
    {
        return string.Compare(
            left != null ? left.name : string.Empty,
            right != null ? right.name : string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolvePrefabHint(string prefabHint, out GameObject prefab)
    {
        prefab = null;
        if (string.IsNullOrWhiteSpace(prefabHint) || prefabHint.Trim() == "-")
        {
            return false;
        }

        string hint = prefabHint.Trim().Replace("\\", "/");
        foreach (string candidate in BuildHintCandidates(hint))
        {
            GameObject resolved = Resources.Load<GameObject>(candidate);
            if (resolved != null)
            {
                prefab = resolved;
                return true;
            }
        }

        string fileName = Path.GetFileNameWithoutExtension(hint);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        string[] guids = AssetDatabase.FindAssets(fileName + " t:Prefab");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (candidate != null)
            {
                prefab = candidate;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildHintCandidates(string hint)
    {
        string noExtension = hint;
        if (noExtension.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            noExtension = noExtension.Substring(0, noExtension.Length - ".prefab".Length);
        }

        int resourcesIndex = noExtension.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
        {
            yield return noExtension.Substring(resourcesIndex + "/Resources/".Length);
        }

        yield return noExtension;
        yield return "Prefabs/Visual Prefabs/" + noExtension;
        yield return "Prefabs/Visual Prefabs/Character/" + noExtension;
    }

    private static GameObject BrowsePrefab(GameObject current)
    {
        string startFolder = Application.dataPath;
        string currentPath = current != null ? AssetDatabase.GetAssetPath(current) : string.Empty;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            string directory = Path.GetDirectoryName(ToAbsolutePath(currentPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                startFolder = directory;
            }
        }

        string selected = EditorUtility.OpenFilePanel("Select Prefab", startFolder, "prefab");
        if (string.IsNullOrWhiteSpace(selected))
        {
            return current;
        }

        string assetPath = ToAssetPath(selected);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog(
                "Select Prefab",
                "The selected file is not a Unity prefab inside this project:\n" + selected,
                "OK");
            return current;
        }

        return prefab;
    }

    private static string ToAbsolutePath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return Application.dataPath;
        }

        string normalized = assetPath.Replace('\\', '/');
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        return Path.GetFullPath(Path.Combine(projectRoot, normalized));
    }

    private static string ToAssetPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(projectRoot.Length + 1);
        }

        return normalized;
    }
}
