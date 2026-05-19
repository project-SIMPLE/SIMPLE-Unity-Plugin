using System;
using System.Collections.Generic;
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
        entry = null;
        if (entries == null || entries.Count == 0 || string.IsNullOrWhiteSpace(speciesName))
        {
            return false;
        }

        string wantedModel = NormalizeKey(modelPath);
        string wantedExperiment = NormalizeKey(experimentName);
        string wantedSpecies = NormalizeKey(speciesName);

        int bestScore = int.MinValue;
        for (int i = 0; i < entries.Count; i++)
        {
            GamaSpeciesRenderOverrideEntry candidate = entries[i];
            if (candidate == null)
            {
                continue;
            }

            int score = candidate.GetMatchScore(wantedModel, wantedExperiment, wantedSpecies);
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
        return TryGetOverride(string.Empty, string.Empty, speciesName, out entry);
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
    public Vector3 positionOffset;
    public Vector3 rotationOffsetEuler;
    public float scaleMultiplier = 1f;
    public bool overridePreviewVisibility = true;
    public bool visibleInPreview = true;
    public bool overrideRuntimeVisibility = true;
    public bool visibleInRuntime = true;
    public GamaSpeciesRenderMode renderMode = GamaSpeciesRenderMode.Default;
    public string notesDebug = string.Empty;

    [Header("Legacy runtime visibility")]
    public bool overrideVisibility = true;
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

    public bool HasAnyOverride =>
        prefabOverride != null ||
        materialOverride != null ||
        !string.IsNullOrWhiteSpace(prefabResourcePath) ||
        overrideColor ||
        overrideVisibility ||
        overridePreviewVisibility ||
        overrideRuntimeVisibility ||
        positionOffset.sqrMagnitude > 0.0001f ||
        rotationOffsetEuler.sqrMagnitude > 0.0001f ||
        Math.Abs(scaleMultiplier - 1f) > 0.0001f;
}
