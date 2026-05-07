using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public abstract partial class SimulationManager
{
    [Header("Imported GAMA prefab references")]
    [SerializeField] private bool createPropertyEntries = true;
    [SerializeField] private bool keepManualPrefabEntriesWhenMissing = true;
    [SerializeField] private List<GamaPrefabPropertyBinding> propertyBindings = new List<GamaPrefabPropertyBinding>();

    [Header("Ordered key translations")]
    [SerializeField] private bool applyKeyTranslations = true;
    [SerializeField] private List<GamaPrefabKeyTranslation> keyTranslations = new List<GamaPrefabKeyTranslation>();

    [Header("Fallback lookup")]
    [SerializeField] private bool allowResourcesLookup = true;
    [SerializeField] private bool allowFileNameFallback = true;
    [SerializeField] private bool logMissingPrefabOnce = true;

    private readonly Dictionary<string, GamaPrefabPropertyBinding> prefabPropertyLookup =
        new Dictionary<string, GamaPrefabPropertyBinding>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, GameObject> resourcesCache =
        new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> resourcesMissingCache =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> missingPrefabLogCache =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> sharedCandidateKeys = new List<string>(16);
    private readonly HashSet<string> sharedKeyDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> sharedResourceCandidates = new List<string>(16);
    private readonly HashSet<string> sharedResourceDedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void ImportPrefabProperties(IEnumerable<PropertiesGAMA> properties)
    {
        RebuildPrefabLookups();
        HashSet<string> importedPropertyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (properties == null)
        {
            return;
        }

        foreach (PropertiesGAMA property in properties)
        {
            if (property == null || string.IsNullOrWhiteSpace(property.id))
            {
                continue;
            }

            importedPropertyIds.Add(property.id);

            GamaPrefabPropertyBinding binding;
            bool hasEntry = prefabPropertyLookup.TryGetValue(property.id, out binding);
            if (!hasEntry && !createPropertyEntries)
            {
                continue;
            }

            if (!hasEntry)
            {
                binding = new GamaPrefabPropertyBinding();
                propertyBindings.Add(binding);
                prefabPropertyLookup[property.id] = binding;
            }

            binding.ImportFrom(property);
        }

        if (!keepManualPrefabEntriesWhenMissing)
        {
            propertyBindings.RemoveAll(b => b == null || !importedPropertyIds.Contains(b.PropertyId));
        }
        else
        {
            propertyBindings.RemoveAll(b =>
                b == null ||
                string.IsNullOrWhiteSpace(b.PropertyId) ||
                (!importedPropertyIds.Contains(b.PropertyId) && !b.HasManualOverride()));
        }

        RebuildPrefabLookups();
    }

    public bool TryResolvePrefab(
        PropertiesGAMA property,
        Attributes attributes,
        out GameObject prefab,
        out string signature)
    {
        prefab = null;
        signature = string.Empty;

        if (property == null || !property.hasPrefab)
        {
            return false;
        }

        GamaPrefabPropertyBinding propertyBinding;
        if (!string.IsNullOrWhiteSpace(property.id) &&
            prefabPropertyLookup.TryGetValue(property.id, out propertyBinding))
        {
            if (propertyBinding.TryResolveManualPrefab(this, out prefab, out signature))
            {
                return true;
            }
        }

        CollectCandidateKeys(property, attributes, sharedCandidateKeys, sharedKeyDedup);

        if (applyKeyTranslations && TryResolveByTranslations(sharedCandidateKeys, out prefab, out signature))
        {
            return true;
        }

        if (allowResourcesLookup && TryResolveByResources(sharedCandidateKeys, out prefab, out signature))
        {
            return true;
        }

        if (property.prefabObj != null)
        {
            prefab = property.prefabObj;
            signature = "legacy:" + NormalizeKey(property.prefab);
            return true;
        }

        LogMissingPrefab(property, sharedCandidateKeys);
        signature = "placeholder:" + NormalizeKey(property.prefab);
        return false;
    }

    public static string NormalizeKey(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string trimmed = StripOuterQuotes(raw.Trim());
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        trimmed = trimmed.Replace('\\', '/');
        while (trimmed.Contains("//"))
        {
            trimmed = trimmed.Replace("//", "/");
        }

        trimmed = trimmed.Trim();
        string noExtension = RemoveKnownExtension(trimmed);
        if (!string.IsNullOrEmpty(noExtension))
        {
            trimmed = noExtension;
        }

        return trimmed.Trim().ToLowerInvariant();
    }

    private bool TryResolveByTranslations(List<string> candidateKeys, out GameObject prefab, out string signature)
    {
        prefab = null;
        signature = string.Empty;

        for (int i = 0; i < keyTranslations.Count; i++)
        {
            GamaPrefabKeyTranslation translation = keyTranslations[i];
            if (translation == null || !translation.Enabled)
            {
                continue;
            }

            for (int k = 0; k < candidateKeys.Count; k++)
            {
                string key = candidateKeys[k];
                if (!translation.Matches(key))
                {
                    continue;
                }

                if (translation.TryResolveManualPrefab(this, out prefab))
                {
                    signature = "rule:" + translation.Label + ":" + i;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryResolveByResources(List<string> candidateKeys, out GameObject prefab, out string signature)
    {
        prefab = null;
        signature = string.Empty;

        for (int i = 0; i < candidateKeys.Count; i++)
        {
            sharedResourceCandidates.Clear();
            sharedResourceDedup.Clear();
            BuildResourceCandidates(candidateKeys[i], sharedResourceCandidates, sharedResourceDedup);

            for (int c = 0; c < sharedResourceCandidates.Count; c++)
            {
                string candidate = sharedResourceCandidates[c];
                if (TryLoadResource(candidate, out prefab))
                {
                    signature = "resources:" + NormalizeKey(candidate);
                    return true;
                }
            }
        }

        return false;
    }

    internal bool TryResolveResourcesPath(string resourcePath, out GameObject prefab)
    {
        prefab = null;
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return false;
        }

        sharedResourceCandidates.Clear();
        sharedResourceDedup.Clear();
        BuildResourceCandidates(resourcePath, sharedResourceCandidates, sharedResourceDedup);
        if (sharedResourceCandidates.Count == 0)
        {
            AddResourceCandidate(resourcePath, sharedResourceCandidates, sharedResourceDedup);
        }

        for (int i = 0; i < sharedResourceCandidates.Count; i++)
        {
            if (TryLoadResource(sharedResourceCandidates[i], out prefab))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryLoadResource(string resourcePath, out GameObject prefab)
    {
        prefab = null;
        if (string.IsNullOrWhiteSpace(resourcePath))
        {
            return false;
        }

        if (resourcesCache.TryGetValue(resourcePath, out prefab))
        {
            return prefab != null;
        }

        if (resourcesMissingCache.Contains(resourcePath))
        {
            return false;
        }

        prefab = Resources.Load<GameObject>(resourcePath);
        if (prefab != null)
        {
            resourcesCache[resourcePath] = prefab;
            return true;
        }

        resourcesMissingCache.Add(resourcePath);
        return false;
    }

    private void CollectCandidateKeys(
        PropertiesGAMA property,
        Attributes attributes,
        List<string> output,
        HashSet<string> dedup)
    {
        output.Clear();
        dedup.Clear();

        AddKeyCandidate(property != null ? property.prefab : string.Empty, output, dedup);

        string value;
        if (attributes != null &&
            attributes.TryGetString(
                out value,
                "prefab",
                "prefab3D",
                "prefab_3d",
                "unityPrefab",
                "unity_prefab",
                "model",
                "mesh",
                "shape",
                "asset",
                "displayPrefab",
                "display_prefab",
                "gamaPrefab",
                "gama_prefab"))
        {
            AddKeyCandidate(value, output, dedup);
        }

        AddKeyCandidate(property != null ? property.id : string.Empty, output, dedup);
        AddKeyCandidate(property != null ? property.tag : string.Empty, output, dedup);
    }

    private static void AddKeyCandidate(string raw, List<string> output, HashSet<string> dedup)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string trimmed = StripOuterQuotes(raw.Trim());
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (dedup.Add(trimmed))
        {
            output.Add(trimmed);
        }

        string normalized = NormalizeKey(trimmed);
        if (!string.IsNullOrWhiteSpace(normalized) && dedup.Add(normalized))
        {
            output.Add(normalized);
        }
    }

    private void BuildResourceCandidates(
        string key,
        List<string> output,
        HashSet<string> dedup)
    {
        string raw = StripOuterQuotes((key ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string slashPath = raw.Replace('\\', '/').Trim();
        AddResourceCandidate(slashPath, output, dedup);

        string withoutResourcesPrefix = StripResourcesPrefix(slashPath);
        AddResourceCandidate(withoutResourcesPrefix, output, dedup);

        string withoutAssetsPrefix = StripAssetsPrefix(withoutResourcesPrefix);
        AddResourceCandidate(withoutAssetsPrefix, output, dedup);

        string noExtension = RemoveKnownExtension(withoutAssetsPrefix);
        AddResourceCandidate(noExtension, output, dedup);

        string noExtensionWithoutResources = RemoveKnownExtension(withoutResourcesPrefix);
        AddResourceCandidate(noExtensionWithoutResources, output, dedup);

        if (allowFileNameFallback)
        {
            string fileName = Path.GetFileName(withoutAssetsPrefix);
            AddResourceCandidate(fileName, output, dedup);
            AddResourceCandidate(RemoveKnownExtension(fileName), output, dedup);
        }
    }

    private static void AddResourceCandidate(string value, List<string> output, HashSet<string> dedup)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string trimmed = value.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (dedup.Add(trimmed))
        {
            output.Add(trimmed);
        }
    }

    private void LogMissingPrefab(PropertiesGAMA property, List<string> candidateKeys)
    {
        if (!logMissingPrefabOnce)
        {
            return;
        }

        string key = (property != null ? property.id : string.Empty) + "|" +
                     (property != null ? property.prefab : string.Empty);

        if (!missingPrefabLogCache.Add(key))
        {
            return;
        }

        string candidates = candidateKeys.Count > 0
            ? string.Join(", ", candidateKeys.ToArray())
            : "(none)";

        Debug.LogWarning(
            "[GAMA] Could not resolve prefab for property '" +
            (property != null ? property.id : "(null)") +
            "' (source='" +
            (property != null ? property.prefab : string.Empty) +
            "'). Tried keys: " +
            candidates +
            ". Configure Prefab Resolution Settings on SimulationManager.");
    }

    private static string StripResourcesPrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = path.Replace('\\', '/');
        int idx = normalized.IndexOf("resources/", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            return normalized.Substring(idx + "resources/".Length);
        }

        return normalized;
    }

    private static string StripAssetsPrefix(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalized = path.Replace('\\', '/');
        return normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring("assets/".Length)
            : normalized;
    }

    private static string StripOuterQuotes(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
            (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal)))
        {
            return value.Substring(1, value.Length - 2);
        }

        return value;
    }

    private static string RemoveKnownExtension(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return path;
        }

        switch (extension.ToLowerInvariant())
        {
            case ".prefab":
            case ".fbx":
            case ".obj":
            case ".gltf":
            case ".glb":
            case ".dae":
            case ".3ds":
            case ".blend":
                return path.Substring(0, path.Length - extension.Length);
            default:
                return path;
        }
    }

    private void RebuildPrefabLookups()
    {
        prefabPropertyLookup.Clear();

        for (int i = 0; i < propertyBindings.Count; i++)
        {
            GamaPrefabPropertyBinding binding = propertyBindings[i];
            if (binding == null || string.IsNullOrWhiteSpace(binding.PropertyId))
            {
                continue;
            }

            if (!prefabPropertyLookup.ContainsKey(binding.PropertyId))
            {
                prefabPropertyLookup.Add(binding.PropertyId, binding);
            }
        }
    }
}

[Serializable]
public class GamaPrefabPropertyBinding
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private string propertyId;
    [SerializeField] private string importedTag;
    [SerializeField] private string importedPrefabReference;
    [SerializeField] private string importedPrefabNormalized;
    [SerializeField] private GameObject unityPrefab;
    [SerializeField] private string unityResourcesPath;

    public string PropertyId { get { return propertyId; } }

    public void ImportFrom(PropertiesGAMA property)
    {
        propertyId = property.id;
        importedTag = property.tag;
        importedPrefabReference = property.prefab;
        importedPrefabNormalized = SimulationManager.NormalizeKey(property.prefab);
    }

    public bool TryResolveManualPrefab(
        SimulationManager settings,
        out GameObject prefab,
        out string signature)
    {
        prefab = null;
        signature = string.Empty;
        if (!enabled)
        {
            return false;
        }

        if (unityPrefab != null)
        {
            prefab = unityPrefab;
            signature = "property:" + propertyId;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(unityResourcesPath) &&
            settings != null &&
            settings.TryResolveResourcesPath(unityResourcesPath, out prefab))
        {
            signature = "property-resources:" + propertyId;
            return true;
        }

        return false;
    }

    public bool HasManualOverride()
    {
        return unityPrefab != null || !string.IsNullOrWhiteSpace(unityResourcesPath);
    }
}

[Serializable]
public class GamaPrefabKeyTranslation
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private string label = "Translation";
    [SerializeField] private string gamaKey;
    [SerializeField] private bool containsMatch;
    [SerializeField] private GameObject unityPrefab;
    [SerializeField] private string unityResourcesPath;

    public bool Enabled { get { return enabled; } }
    public string Label { get { return string.IsNullOrWhiteSpace(label) ? "Translation" : label; } }

    public bool Matches(string candidate)
    {
        if (!enabled || string.IsNullOrWhiteSpace(gamaKey) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        string expected = SimulationManager.NormalizeKey(gamaKey);
        string current = SimulationManager.NormalizeKey(candidate);
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(current))
        {
            return false;
        }

        if (containsMatch)
        {
            return current.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryResolveManualPrefab(SimulationManager settings, out GameObject prefab)
    {
        prefab = null;
        if (!enabled)
        {
            return false;
        }

        if (unityPrefab != null)
        {
            prefab = unityPrefab;
            return true;
        }

        return !string.IsNullOrWhiteSpace(unityResourcesPath) &&
               settings != null &&
               settings.TryResolveResourcesPath(unityResourcesPath, out prefab);
    }
}
