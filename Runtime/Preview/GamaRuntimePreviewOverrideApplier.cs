using UnityEngine;
using System;
using System.Collections.Generic;

public static class GamaRuntimePreviewOverrideApplier
{
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";
    private static Dictionary<string, GamaSpeciesRenderOverrideEntry> overridesBySpecies;
    private static bool initialized;
    private static bool runtimeContextAvailable;
    private static int logCount;
    private const int MaxLogs = 5;
    private const int MaxStartupOverrideLogs = 20;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        overridesBySpecies = null;
        initialized = false;
        runtimeContextAvailable = false;
        logCount = 0;
    }

    public static bool TryGetOverride(string speciesKey, out GamaSpeciesRenderOverrideEntry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(speciesKey))
        {
            return false;
        }

        if (!initialized)
        {
            initialized = true;
            Initialize();
        }

        if (overridesBySpecies == null)
        {
            return false;
        }

        bool found = overridesBySpecies.TryGetValue(speciesKey, out entry);
        if (found && logCount < MaxLogs)
        {
            logCount++;
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] Applied species=" + speciesKey + " to an object.");
        }
        else if (!runtimeContextAvailable && logCount < MaxLogs)
        {
            logCount++;
            Debug.LogWarning("[GAMA][RUNTIME][OVERRIDE_WARN] missing context, refusing global fallback species=" + speciesKey);
        }
        else if (!found && logCount < MaxLogs)
        {
            logCount++;
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No override for species=" + speciesKey);
        }
        return found;
    }

    public static bool TryGetOverrideForProperty(
        string propertyId,
        string tag,
        string prefabPath,
        out GamaSpeciesRenderOverrideEntry entry)
    {
        entry = null;
        int bestWeight = int.MinValue;
        TrySelectPropertyOverrideCandidate(tag, 30, ref entry, ref bestWeight);
        TrySelectPropertyOverrideCandidate(propertyId, 20, ref entry, ref bestWeight);
        TrySelectPropertyOverrideCandidate(prefabPath, 10, ref entry, ref bestWeight);

        if (entry != null)
        {
            if (logCount < MaxLogs)
            {
                logCount++;
                Debug.Log("[GAMA][RUNTIME][OVERRIDE] Applied property=" + propertyId +
                          " tag=" + tag +
                          " overrideSpecies=" + entry.GetSpeciesName() +
                          " scale=" + entry.GetEffectiveScaleMultiplier());
            }

            return true;
        }

        if (logCount < MaxLogs)
        {
            logCount++;
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No override for property=" + propertyId + " tag=" + tag);
        }

        return false;
    }

    public static void RefreshNow()
    {
        initialized = true;
        Initialize();
    }

    private static void Initialize()
    {
        overridesBySpecies = new Dictionary<string, GamaSpeciesRenderOverrideEntry>(StringComparer.OrdinalIgnoreCase);

        GamaPreviewSession session = FindCurrentPreviewSession();
        GamaSpeciesRenderOverrides asset = null;
        string modelPath = string.Empty;
        string experimentName = string.Empty;

        if (TryResolveManagerOverridesContext(out GamaSpeciesRenderOverrides managerAsset, out string managerModel, out string managerExperiment) &&
            (session == null ||
             !string.IsNullOrWhiteSpace(managerModel) ||
             !string.IsNullOrWhiteSpace(managerExperiment)))
        {
            asset = managerAsset;
            modelPath = managerModel;
            experimentName = managerExperiment;
        }
        else if (session != null)
        {
            asset = session.speciesOverrides;
            modelPath = session.modelPath ?? string.Empty;
            experimentName = session.experimentName ?? string.Empty;
        }

        if (asset == null)
        {
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No overrides asset found on the SimulationManager or preview session.");
            return;
        }

        Dictionary<string, int> bestScoresBySpecies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        runtimeContextAvailable = !string.IsNullOrWhiteSpace(modelPath) || !string.IsNullOrWhiteSpace(experimentName);
        Debug.Log("[GAMA][RUNTIME][CONTEXT] model=" + (modelPath ?? string.Empty) +
                  " experiment=" + (experimentName ?? string.Empty));
        if (!runtimeContextAvailable)
        {
            Debug.LogWarning("[GAMA][RUNTIME][OVERRIDE_WARN] missing context, refusing global fallback for runtime overrides.");
            return;
        }

        string wantedModel = GamaSpeciesRenderOverrides.NormalizeKey(modelPath);
        string wantedExperiment = GamaSpeciesRenderOverrides.NormalizeKey(experimentName);

        foreach (var e in asset.entries)
        {
            if (e == null) continue;
            if (string.IsNullOrWhiteSpace(e.speciesName) && string.IsNullOrWhiteSpace(e.speciesKey)) continue;
            if (!IsExactRuntimeContextEntry(e, wantedModel, wantedExperiment)) continue;
            
            string key = !string.IsNullOrWhiteSpace(e.speciesKey) ? e.speciesKey : e.speciesName;
            key = key.Trim();
            int score = e.GetSelectionScore(
                wantedModel,
                wantedExperiment,
                GamaSpeciesRenderOverrides.NormalizeKey(key));
            if (score < 0)
            {
                continue;
            }

            if (!bestScoresBySpecies.TryGetValue(key, out int bestScore) || score > bestScore)
            {
                bestScoresBySpecies[key] = score;
                overridesBySpecies[key] = e;
            }
        }

        Debug.Log("[GAMA][RUNTIME][OVERRIDE] Loaded preview overrides: " + string.Join(",", overridesBySpecies.Keys));
        LogLoadedOverrides(bestScoresBySpecies, modelPath, experimentName);
    }

    private static bool IsExactRuntimeContextEntry(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedModel,
        string wantedExperiment)
    {
        if (entry == null)
        {
            return false;
        }

        return string.Equals(GamaSpeciesRenderOverrides.NormalizeKey(entry.modelPath), wantedModel, StringComparison.Ordinal) &&
               string.Equals(GamaSpeciesRenderOverrides.NormalizeKey(entry.experimentName), wantedExperiment, StringComparison.Ordinal);
    }

    private static void LogLoadedOverrides(Dictionary<string, int> scoresBySpecies, string requestedModel, string requestedExperiment)
    {
        if (overridesBySpecies == null || overridesBySpecies.Count == 0)
        {
            return;
        }

        int count = 0;
        foreach (KeyValuePair<string, GamaSpeciesRenderOverrideEntry> pair in overridesBySpecies)
        {
            if (pair.Value == null)
            {
                continue;
            }

            GamaSpeciesRenderOverrideEntry entry = pair.Value;
            string prefab = !string.IsNullOrWhiteSpace(entry.prefabResourcePath)
                ? entry.prefabResourcePath
                : (entry.prefabOverride != null ? entry.prefabOverride.name : "none");
            int score = scoresBySpecies != null && scoresBySpecies.TryGetValue(pair.Key, out int pickedScore)
                ? pickedScore
                : -1;

            Debug.Log("[GAMA][RUNTIME][OVERRIDE_PICK] species=" + pair.Key +
                      " pickedModel=" + (entry.modelPath ?? string.Empty) +
                      " pickedExperiment=" + (entry.experimentName ?? string.Empty) +
                      " requestedModel=" + (requestedModel ?? string.Empty) +
                      " requestedExperiment=" + (requestedExperiment ?? string.Empty) +
                      " prefab=" + prefab +
                      " scale=" + entry.GetEffectiveScaleMultiplier() +
                      " score=" + score);

            Debug.Log("[GAMA][RUNTIME][OVERRIDES] species=" + pair.Key +
                      " prefab=" + prefab +
                      " colorOverride=" + entry.overrideColor +
                      " scale=" + entry.GetEffectiveScaleMultiplier() +
                      " visible=" + entry.GetEffectiveRuntimeVisible());

            count++;
            if (count >= MaxStartupOverrideLogs)
            {
                break;
            }
        }
    }

    private static bool TryGetOverrideNoLog(string speciesKey, out GamaSpeciesRenderOverrideEntry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(speciesKey))
        {
            return false;
        }

        if (!initialized)
        {
            initialized = true;
            Initialize();
        }

        if (overridesBySpecies != null && overridesBySpecies.TryGetValue(speciesKey.Trim(), out entry))
        {
            return true;
        }

        if (!runtimeContextAvailable && logCount < MaxLogs)
        {
            logCount++;
            Debug.LogWarning("[GAMA][RUNTIME][OVERRIDE_WARN] missing context, refusing global fallback species=" + speciesKey);
        }

        return false;
    }

    private static void TrySelectPropertyOverrideCandidate(
        string speciesKey,
        int keyPriority,
        ref GamaSpeciesRenderOverrideEntry bestEntry,
        ref int bestWeight)
    {
        if (!TryGetOverrideNoLog(speciesKey, out GamaSpeciesRenderOverrideEntry candidate) ||
            candidate == null)
        {
            return;
        }

        int weight = keyPriority + GetRuntimeOverrideWeight(candidate);
        if (weight > bestWeight)
        {
            bestWeight = weight;
            bestEntry = candidate;
        }
    }

    private static int GetRuntimeOverrideWeight(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return 0;
        }

        int weight = 1;
        if (entry.prefabOverride != null || !string.IsNullOrWhiteSpace(entry.prefabResourcePath))
        {
            weight += 100;
        }

        if (entry.UsesScaleOverride())
        {
            weight += 80;
        }

        if (entry.overrideColor)
        {
            weight += 60;
        }

        if (entry.UsesPositionOffsetOverride() || entry.UsesRotationOffsetOverride())
        {
            weight += 40;
        }

        if (entry.UsesRuntimeVisibilityOverride())
        {
            weight += 20;
        }

        return weight;
    }

    private static bool TryResolveManagerOverridesContext(
        out GamaSpeciesRenderOverrides asset,
        out string modelPath,
        out string experimentName)
    {
        asset = null;
        modelPath = string.Empty;
        experimentName = string.Empty;

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        return manager != null &&
               manager.TryGetSpeciesRenderOverridesContext(out asset, out modelPath, out experimentName) &&
               asset != null;
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
}
