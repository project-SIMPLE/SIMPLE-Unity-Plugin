using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Overrides visuels persistants par espèce avec clé contextuelle model/experiment/species.
/// </summary>
public class GamaSpeciesRenderOverrides : ScriptableObject
{
    public List<GamaSpeciesRenderOverrideEntry> entries = new List<GamaSpeciesRenderOverrideEntry>();

    public bool TryGetOverride(
        string modelPath,
        string experimentName,
        string speciesName,
        out GamaSpeciesRenderOverrideEntry entry)
    {
        return TryGetOverride(modelPath, experimentName, speciesName, out entry, out _);
    }

    public bool TryGetOverride(
        string modelPath,
        string experimentName,
        string speciesName,
        out GamaSpeciesRenderOverrideEntry entry,
        bool exactContextOnly)
    {
        if (!exactContextOnly)
        {
            return TryGetOverride(modelPath, experimentName, speciesName, out entry);
        }

        entry = null;
        if (entries == null || entries.Count == 0 || string.IsNullOrWhiteSpace(speciesName))
        {
            return false;
        }

        string wantedModel = NormalizeKey(modelPath);
        string wantedExperiment = NormalizeKey(experimentName);
        string wantedSpecies = NormalizeKey(speciesName);
        for (int i = 0; i < entries.Count; i++)
        {
            GamaSpeciesRenderOverrideEntry candidate = entries[i];
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(NormalizeKey(candidate.modelPath), wantedModel, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(candidate.experimentName), wantedExperiment, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(candidate.GetSpeciesName()), wantedSpecies, StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryGetOverride(
        string modelPath,
        string experimentName,
        string speciesName,
        out GamaSpeciesRenderOverrideEntry entry,
        out int bestScore)
    {
        entry = null;
        bestScore = int.MinValue;
        if (entries == null || entries.Count == 0 || string.IsNullOrWhiteSpace(speciesName))
        {
            return false;
        }

        string wantedModel = NormalizeKey(modelPath);
        string wantedExperiment = NormalizeKey(experimentName);
        string wantedSpecies = NormalizeKey(speciesName);

        for (int i = 0; i < entries.Count; i++)
        {
            GamaSpeciesRenderOverrideEntry candidate = entries[i];
            if (candidate == null)
            {
                continue;
            }

            int score = candidate.GetSelectionScore(wantedModel, wantedExperiment, wantedSpecies);
            if (score > bestScore)
            {
                bestScore = score;
                entry = candidate;
            }
        }

        return entry != null && bestScore >= 0;
    }

    public bool TryGetOverride(string speciesName, out GamaSpeciesRenderOverrideEntry entry)
    {
        // Legacy/global lookup only. Active preview and runtime contexts should use the model/experiment overload.
        return TryGetOverride(string.Empty, string.Empty, speciesName, out entry);
    }

    public GamaSpeciesRenderOverrideEntry GetOrCreateEntry(string speciesName)
    {
        if (TryGetOverride(speciesName, out GamaSpeciesRenderOverrideEntry entry))
        {
            return entry;
        }

        entry = new GamaSpeciesRenderOverrideEntry();
        entry.speciesName = speciesName;
        entry.speciesKey = speciesName;
        entries.Add(entry);
        return entry;
    }

    public GamaSpeciesRenderOverrideEntry GetOrCreateEntry(
        string modelPath,
        string experimentName,
        string speciesName)
    {
        string wantedModel = NormalizeKey(modelPath);
        string wantedExperiment = NormalizeKey(experimentName);
        string wantedSpecies = NormalizeKey(speciesName);

        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                GamaSpeciesRenderOverrideEntry candidate = entries[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(NormalizeKey(candidate.modelPath), wantedModel, StringComparison.Ordinal) &&
                    string.Equals(NormalizeKey(candidate.experimentName), wantedExperiment, StringComparison.Ordinal) &&
                    string.Equals(NormalizeKey(candidate.GetSpeciesName()), wantedSpecies, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        GamaSpeciesRenderOverrideEntry entry = new GamaSpeciesRenderOverrideEntry();
        entry.modelPath = modelPath ?? string.Empty;
        entry.experimentName = experimentName ?? string.Empty;
        entry.speciesName = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        entry.speciesKey = entry.speciesName;
        entries.Add(entry);
        return entry;
    }

    public void SetOrReplaceEntry(GamaSpeciesRenderOverrideEntry newEntry)
    {
        if (newEntry == null || string.IsNullOrWhiteSpace(newEntry.GetSpeciesName()))
        {
            return;
        }

        string newModel = NormalizeKey(newEntry.modelPath);
        string newExperiment = NormalizeKey(newEntry.experimentName);
        string newSpecies = NormalizeKey(newEntry.GetSpeciesName());

        for (int i = 0; i < entries.Count; i++)
        {
            GamaSpeciesRenderOverrideEntry existing = entries[i];
            if (existing == null)
            {
                continue;
            }

            if (string.Equals(NormalizeKey(existing.modelPath), newModel, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(existing.experimentName), newExperiment, StringComparison.Ordinal) &&
                string.Equals(NormalizeKey(existing.GetSpeciesName()), newSpecies, StringComparison.Ordinal))
            {
                entries[i] = newEntry;
                return;
            }
        }

        entries.Add(newEntry);
    }

    public static string NormalizeKey(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }
}

[CreateAssetMenu(fileName = "GamaSpeciesRenderOverrides", menuName = "GAMA/Species Render Overrides")]
public class GamaSpeciesRenderOverridesAsset : GamaSpeciesRenderOverrides
{
}

public enum GamaSpeciesRenderMode
{
    Default = 0,
    Standard = 1,
    Wireframe = 2,
    Unlit = 3,
    Hidden = 4
}

public enum GamaDynamicColorMode
{
    None = 0,
    Discrete = 1,
    Continuous = 2
}

[Serializable]
public class GamaDiscreteColorRule
{
    public string value;
    public Color color = Color.white;
}

[Serializable]
public class GamaSpeciesRenderOverrideEntry
{
    [Header("Override key")]
    public string modelPath = string.Empty;
    public string experimentName = string.Empty;
    public string speciesName = "vehicle";

    [Header("Legacy compatibility")]
    public string speciesKey = string.Empty;
    public string prefabResourcePath = string.Empty;

    [Header("Visual overrides")]
    public GameObject prefabOverride;
    public Material materialOverride;
    public bool overrideColor;
    public Color color = Color.white;
    public bool overrideScaleMultiplier;
    public bool overridePositionOffset;
    public bool overrideRotationOffset;
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;
    public float scaleMultiplier = 1f;
    public bool overridePreviewVisibility;
    public bool visibleInPreview = true;
    public bool overrideRuntimeVisibility;
    public bool visibleInRuntime = true;
    public GamaSpeciesRenderMode renderMode = GamaSpeciesRenderMode.Default;
    public string notesDebug = string.Empty;

    [Header("Dynamic color")]
    public bool overrideDynamicColor;
    public GamaDynamicColorMode dynamicColorMode = GamaDynamicColorMode.None;
    public string dynamicColorAttribute = string.Empty;
    public List<GamaDiscreteColorRule> discreteColorRules = new List<GamaDiscreteColorRule>();
    public Color continuousBaseColor = Color.green;
    public float continuousMinValue = 0f;
    public float continuousMaxValue = 1f;
    public bool continuousInvert;
    [Range(0f, 1f)] public float continuousLightAmount = 0.45f;
    [Range(0f, 1f)] public float continuousDarkAmount = 0.45f;
    public bool fallbackToStaticColor = true;

    [Header("Legacy runtime visibility")]
    public bool overrideVisibility;
    public bool visible = true;

    public string GetSpeciesName()
    {
        if (!string.IsNullOrWhiteSpace(speciesName))
        {
            return speciesName.Trim();
        }

        return string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();
    }

    public int GetMatchScore(string wantedModel, string wantedExperiment, string wantedSpecies)
    {
        if (!string.Equals(GamaSpeciesRenderOverrides.NormalizeKey(GetSpeciesName()), wantedSpecies, StringComparison.Ordinal))
        {
            return -1;
        }

        string currentModel = GamaSpeciesRenderOverrides.NormalizeKey(modelPath);
        string currentExperiment = GamaSpeciesRenderOverrides.NormalizeKey(experimentName);

        bool modelMatches = string.IsNullOrEmpty(currentModel) || string.Equals(currentModel, wantedModel, StringComparison.Ordinal);
        bool experimentMatches = string.IsNullOrEmpty(currentExperiment) || string.Equals(currentExperiment, wantedExperiment, StringComparison.Ordinal);
        if (!modelMatches || !experimentMatches)
        {
            return -1;
        }

        int score = 0;
        if (!string.IsNullOrEmpty(currentModel))
        {
            score += 4;
        }

        if (!string.IsNullOrEmpty(currentExperiment))
        {
            score += 2;
        }

        return score;
    }

    public int GetSelectionScore(string wantedModel, string wantedExperiment, string wantedSpecies)
    {
        int matchScore = GetMatchScore(wantedModel, wantedExperiment, wantedSpecies);
        if (matchScore < 0)
        {
            return -1;
        }

        int score = matchScore * 100 + GetOverrideMeaningScore();
        if (HasAnyOverride)
        {
            score += 1000;
        }

        if (HasStrongRuntimeOverride())
        {
            score += 10000;
        }

        return score;
    }

    public int GetOverrideMeaningScore()
    {
        int score = 0;
        if (prefabOverride != null || !string.IsNullOrWhiteSpace(prefabResourcePath))
        {
            score += 100;
        }

        if (materialOverride != null)
        {
            score += 70;
        }

        if (overrideColor)
        {
            score += 60;
        }

        if (UsesDynamicColorOverride())
        {
            score += 65;
        }

        if (UsesScaleOverride())
        {
            score += 80;
        }

        if (UsesPositionOffsetOverride() || UsesRotationOffsetOverride())
        {
            score += 40;
        }

        if (UsesPreviewVisibilityOverride() || UsesRuntimeVisibilityOverride())
        {
            score += 20;
        }

        return score;
    }

    public bool HasStrongRuntimeOverride()
    {
        return prefabOverride != null ||
               materialOverride != null ||
               !string.IsNullOrWhiteSpace(prefabResourcePath) ||
               UsesDynamicColorOverride() ||
               UsesScaleOverride() ||
               UsesPositionOffsetOverride() ||
               UsesRotationOffsetOverride() ||
               UsesRuntimeVisibilityOverride();
    }

    public bool HasAnyOverride =>
        prefabOverride != null ||
        materialOverride != null ||
        !string.IsNullOrWhiteSpace(prefabResourcePath) ||
        overrideColor ||
        UsesDynamicColorOverride() ||
        overrideScaleMultiplier ||
        overridePositionOffset ||
        overrideRotationOffset ||
        overrideVisibility ||
        overridePreviewVisibility ||
        overrideRuntimeVisibility ||
        positionOffset.sqrMagnitude > 0.0001f ||
        rotationOffsetEuler.sqrMagnitude > 0.0001f ||
        Math.Abs(scaleMultiplier - 1f) > 0.0001f;

    public bool UsesScaleOverride()
    {
        return overrideScaleMultiplier || Math.Abs(scaleMultiplier - 1f) > 0.0001f;
    }

    public bool UsesDynamicColorOverride()
    {
        return overrideDynamicColor &&
               dynamicColorMode != GamaDynamicColorMode.None &&
               !string.IsNullOrWhiteSpace(dynamicColorAttribute);
    }

    public bool TryResolveDynamicColor(Attributes attributes, out Color color)
    {
        color = Color.white;
        try
        {
            if (!UsesDynamicColorOverride() || attributes == null)
            {
                return false;
            }

            switch (dynamicColorMode)
            {
                case GamaDynamicColorMode.Discrete:
                    return TryResolveDiscreteDynamicColor(attributes, out color);
                case GamaDynamicColorMode.Continuous:
                    return TryResolveContinuousDynamicColor(attributes, out color);
                default:
                    return false;
            }
        }
        catch
        {
            color = Color.white;
            return false;
        }
    }

    private bool TryResolveDiscreteDynamicColor(Attributes attributes, out Color color)
    {
        color = Color.white;
        if (discreteColorRules == null || discreteColorRules.Count == 0)
        {
            return false;
        }

        string attributeValue;
        if (!TryReadDiscreteAttribute(attributes, out attributeValue))
        {
            return false;
        }

        string normalizedAttribute = NormalizeDiscreteValue(attributeValue);
        for (int i = 0; i < discreteColorRules.Count; i++)
        {
            GamaDiscreteColorRule rule = discreteColorRules[i];
            if (rule == null || string.IsNullOrWhiteSpace(rule.value))
            {
                continue;
            }

            if (string.Equals(
                    NormalizeDiscreteValue(rule.value),
                    normalizedAttribute,
                    StringComparison.OrdinalIgnoreCase))
            {
                color = rule.color;
                return true;
            }
        }

        return false;
    }

    private bool TryResolveContinuousDynamicColor(Attributes attributes, out Color color)
    {
        color = Color.white;
        float value;
        if (!attributes.TryGetFloat(out value, dynamicColorAttribute))
        {
            return false;
        }

        if (Mathf.Abs(continuousMaxValue - continuousMinValue) < 0.000001f)
        {
            return false;
        }

        float t = Mathf.Clamp01(Mathf.InverseLerp(continuousMinValue, continuousMaxValue, value));
        if (continuousInvert)
        {
            t = 1f - t;
        }

        Color low = MakeLighter(continuousBaseColor, continuousLightAmount);
        Color high = MakeDarker(continuousBaseColor, continuousDarkAmount);
        color = Color.Lerp(low, high, t);
        color.a = continuousBaseColor.a;
        return true;
    }

    private bool TryReadDiscreteAttribute(Attributes attributes, out string value)
    {
        value = string.Empty;
        if (attributes == null || string.IsNullOrWhiteSpace(dynamicColorAttribute))
        {
            return false;
        }

        if (attributes.TryGetString(out value, dynamicColorAttribute))
        {
            return true;
        }

        bool boolValue;
        if (attributes.TryGetBool(out boolValue, dynamicColorAttribute))
        {
            value = boolValue ? "true" : "false";
            return true;
        }

        float floatValue;
        if (attributes.TryGetFloat(out floatValue, dynamicColorAttribute))
        {
            value = floatValue.ToString("G9", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string NormalizeDiscreteValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().Trim('"', '\'').ToLowerInvariant();
        bool boolValue;
        if (bool.TryParse(normalized, out boolValue))
        {
            return boolValue ? "true" : "false";
        }

        string numeric = normalized.IndexOf(',') >= 0 && normalized.IndexOf('.') < 0
            ? normalized.Replace(',', '.')
            : normalized;
        double number;
        if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            if (Math.Abs(number) < 0.000001d)
            {
                return "false";
            }

            if (Math.Abs(number - 1d) < 0.000001d)
            {
                return "true";
            }

            double rounded = Math.Round(number);
            if (Math.Abs(number - rounded) < 0.000001d)
            {
                return ((long)rounded).ToString(CultureInfo.InvariantCulture);
            }

            return number.ToString("G9", CultureInfo.InvariantCulture);
        }

        return normalized;
    }

    private static Color MakeLighter(Color baseColor, float amount)
    {
        Color color = Color.Lerp(baseColor, Color.white, Mathf.Clamp01(amount));
        color.a = baseColor.a;
        return color;
    }

    private static Color MakeDarker(Color baseColor, float amount)
    {
        Color color = Color.Lerp(baseColor, Color.black, Mathf.Clamp01(amount));
        color.a = baseColor.a;
        return color;
    }

    public bool UsesPositionOffsetOverride()
    {
        return overridePositionOffset || positionOffset.sqrMagnitude > 0.0001f;
    }

    public bool UsesRotationOffsetOverride()
    {
        return overrideRotationOffset || rotationOffsetEuler.sqrMagnitude > 0.0001f;
    }

    public float GetEffectiveScaleMultiplier()
    {
        return UsesScaleOverride() ? Mathf.Max(0f, scaleMultiplier) : 1f;
    }

    public Vector3 GetEffectivePositionOffset()
    {
        return UsesPositionOffsetOverride() ? positionOffset : Vector3.zero;
    }

    public Vector3 GetEffectiveRotationOffsetEuler()
    {
        return UsesRotationOffsetOverride() ? rotationOffsetEuler : Vector3.zero;
    }

    public bool UsesPreviewVisibilityOverride()
    {
        return overridePreviewVisibility || overrideVisibility;
    }

    public bool UsesRuntimeVisibilityOverride()
    {
        return overrideRuntimeVisibility || overrideVisibility;
    }

    public bool GetEffectivePreviewVisible()
    {
        if (overridePreviewVisibility)
        {
            return visibleInPreview;
        }

        return overrideVisibility ? visible : true;
    }

    public bool GetEffectiveRuntimeVisible()
    {
        if (overrideRuntimeVisibility)
        {
            return visibleInRuntime;
        }

        return overrideVisibility ? visible : true;
    }
}
