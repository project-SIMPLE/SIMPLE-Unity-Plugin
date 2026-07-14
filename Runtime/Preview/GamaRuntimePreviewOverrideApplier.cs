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

    static GamaRuntimePreviewOverrideApplier()
    {
        GamaSpeciesAppearanceStateStore.Changed += OnAppearanceStateChanged;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        overridesBySpecies = null;
        initialized = false;
        runtimeContextAvailable = false;
        logCount = 0;
    }

    public static GamaSpeciesRenderOverrideEntry GetOrCreateRuntimeSessionOverride(
        GamaSpeciesRenderOverrides sourceAsset,
        string modelPath,
        string experimentName,
        string speciesName)
    {
        GamaSpeciesAppearanceContext context = new GamaSpeciesAppearanceContext(
            sourceAsset,
            modelPath,
            experimentName);
        GamaSpeciesAppearanceStateStore.SetActiveContext(context);
        GamaSpeciesRenderOverrideEntry entry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, speciesName, true);
        initialized = false;
        return entry;
    }

    public static void ClearRuntimeSessionOverrides()
    {
        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
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
        if (found && GamaLog.VerboseEnabled && logCount < MaxLogs)
        {
            logCount++;
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] Applied species=" + speciesKey + " to an object.");
        }
        else if (!runtimeContextAvailable && GamaLog.VerboseEnabled && logCount < MaxLogs)
        {
            logCount++;
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] No species-only override for species=" + speciesKey);
        }
        else if (!found && GamaLog.VerboseEnabled && logCount < MaxLogs)
        {
            logCount++;
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] No override for species=" + speciesKey);
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
            if (GamaLog.VerboseEnabled && logCount < MaxLogs)
            {
                logCount++;
                GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] Applied property=" + propertyId +
                          " tag=" + tag +
                          " overrideSpecies=" + entry.GetSpeciesName() +
                          " scale=" + entry.GetEffectiveScaleMultiplier());
            }

            return true;
        }

        if (GamaLog.VerboseEnabled && logCount < MaxLogs)
        {
            logCount++;
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] No override for property=" + propertyId + " tag=" + tag);
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
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] No overrides asset found on the SimulationManager or preview session.");
            return;
        }

        Dictionary<string, int> bestScoresBySpecies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        runtimeContextAvailable = !string.IsNullOrWhiteSpace(modelPath) || !string.IsNullOrWhiteSpace(experimentName);
        GamaLog.Dev("[GAMA][RUNTIME][CONTEXT] model=" + (modelPath ?? string.Empty) +
                  " experiment=" + (experimentName ?? string.Empty));
        if (!runtimeContextAvailable)
        {
            GamaLog.DevWarning("[GAMA][RUNTIME][OVERRIDE_WARN] missing context; using species-only runtime overrides from the active SimulationManager.");
        }

        string wantedModel = GamaSpeciesRenderOverrides.NormalizeModelPath(modelPath);
        string wantedExperiment = GamaSpeciesRenderOverrides.NormalizeKey(experimentName);
        GamaSpeciesAppearanceContext context = new GamaSpeciesAppearanceContext(
            asset,
            modelPath,
            experimentName);
        GamaSpeciesAppearanceStateStore.SetActiveContext(context);

        bool applyPersistedPreviewSettings = true;
#if UNITY_EDITOR
        applyPersistedPreviewSettings = UnityEditor.EditorPrefs.GetBool(
            "ProjectSimple.GamaUnity.Panel.ApplyPreviewSettingsToPlay",
            true);
#endif
        if (applyPersistedPreviewSettings && asset != null && asset.entries != null)
        {
            foreach (var e in asset.entries)
            {
                TryAddExactRuntimeOverrideEntry(e, wantedModel, wantedExperiment, bestScoresBySpecies, 0);
            }
        }

        IReadOnlyList<GamaSpeciesRenderOverrideEntry> overlayEntries =
            GamaSpeciesAppearanceStateStore.GetRuntimeOverlayEntries(context);
        for (int i = 0; i < overlayEntries.Count; i++)
        {
            TryAddExactRuntimeOverrideEntry(
                overlayEntries[i],
                wantedModel,
                wantedExperiment,
                bestScoresBySpecies,
                1000000);
        }

        if (GamaLog.VerboseEnabled)
        {
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] Loaded preview overrides: " + string.Join(",", overridesBySpecies.Keys));
            LogLoadedOverrides(bestScoresBySpecies, modelPath, experimentName);
        }
        initialized = true;
    }

    private static void TryAddExactRuntimeOverrideEntry(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedModel,
        string wantedExperiment,
        Dictionary<string, int> bestScoresBySpecies,
        int scoreBonus)
    {
        if (entry == null)
        {
            return;
        }

        string species = entry.GetSpeciesName();
        if (string.IsNullOrWhiteSpace(species) ||
            !IsExactRuntimeContextEntry(
                entry,
                wantedModel,
                wantedExperiment,
                GamaSpeciesRenderOverrides.NormalizeKey(species)))
        {
            return;
        }

        int score = entry.GetOverrideMeaningScore() + scoreBonus;
        bestScoresBySpecies[species] = score;
        overridesBySpecies[species] = entry;
    }

    private static void OnAppearanceStateChanged(GamaSpeciesAppearanceChange change)
    {
        overridesBySpecies = null;
        initialized = false;
        runtimeContextAvailable = false;
        logCount = 0;
    }

    private static int GetRuntimeSelectionScore(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedModel,
        string wantedExperiment,
        string wantedSpecies)
    {
        if (entry == null)
        {
            return -1;
        }

        if (IsExactRuntimeContextEntry(entry, wantedModel, wantedExperiment, wantedSpecies))
        {
            int exactScore = entry.GetSelectionScore(wantedModel, wantedExperiment, wantedSpecies);
            return exactScore;
        }

        if (!IsActiveSelectionFallbackEntry(entry, wantedExperiment, wantedSpecies))
        {
            return -1;
        }

        int fallbackScore = 250 + entry.GetOverrideMeaningScore();
        if (entry.HasAnyOverride)
        {
            fallbackScore += 1000;
        }

        if (entry.HasStrongRuntimeOverride())
        {
            fallbackScore += 10000;
        }

        return fallbackScore;
    }

    private static void TryAddRuntimeOverrideEntry(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedModel,
        string wantedExperiment,
        Dictionary<string, int> bestScoresBySpecies,
        bool allowContextMismatch,
        int scoreBonus)
    {
        if (entry == null || (string.IsNullOrWhiteSpace(entry.speciesName) && string.IsNullOrWhiteSpace(entry.speciesKey)))
        {
            return;
        }

        string key = !string.IsNullOrWhiteSpace(entry.speciesKey) ? entry.speciesKey : entry.speciesName;
        key = key.Trim();
        string wantedSpecies = GamaSpeciesRenderOverrides.NormalizeKey(key);
        int score = GetRuntimeSelectionScore(entry, wantedModel, wantedExperiment, wantedSpecies);
        if (score < 0 && allowContextMismatch)
        {
            score = entry.GetOverrideMeaningScore();
            if (entry.HasAnyOverride)
            {
                score += 1000;
            }

            if (entry.HasStrongRuntimeOverride())
            {
                score += 10000;
            }
        }

        if (score < 0)
        {
            return;
        }

        score += scoreBonus;
        if (!bestScoresBySpecies.TryGetValue(key, out int bestScore) || score > bestScore)
        {
            bestScoresBySpecies[key] = score;
            overridesBySpecies[key] = entry;
        }
    }

    private static string BuildRuntimeSessionOverrideKey(string modelPath, string experimentName, string speciesName)
    {
        return GamaSpeciesRenderOverrides.NormalizeModelPath(modelPath) + "|" +
               GamaSpeciesRenderOverrides.NormalizeKey(experimentName) + "|" +
               GamaSpeciesRenderOverrides.NormalizeKey(speciesName);
    }

    private static GamaSpeciesRenderOverrideEntry CloneOverrideEntry(GamaSpeciesRenderOverrideEntry source)
    {
        if (source == null)
        {
            return null;
        }

        GamaSpeciesRenderOverrideEntry clone = new GamaSpeciesRenderOverrideEntry();
        clone.modelPath = source.modelPath ?? string.Empty;
        clone.experimentName = source.experimentName ?? string.Empty;
        clone.speciesName = source.speciesName ?? string.Empty;
        clone.speciesKey = source.speciesKey ?? string.Empty;
        clone.prefabResourcePath = source.prefabResourcePath ?? string.Empty;
        clone.prefabOverride = source.prefabOverride;
        clone.materialOverride = source.materialOverride;
        clone.overrideColor = source.overrideColor;
        clone.color = source.color;
        clone.overrideScaleMultiplier = source.overrideScaleMultiplier;
        clone.overridePositionOffset = source.overridePositionOffset;
        clone.overrideRotationOffset = source.overrideRotationOffset;
        clone.positionOffset = source.positionOffset;
        clone.rotationOffsetEuler = source.rotationOffsetEuler;
        clone.scaleMultiplier = source.scaleMultiplier;
        clone.overridePreviewVisibility = source.overridePreviewVisibility;
        clone.visibleInPreview = source.visibleInPreview;
        clone.overrideRuntimeVisibility = source.overrideRuntimeVisibility;
        clone.visibleInRuntime = source.visibleInRuntime;
        clone.renderMode = source.renderMode;
        clone.notesDebug = source.notesDebug ?? string.Empty;
        clone.overrideDynamicColor = source.overrideDynamicColor;
        clone.dynamicColorMode = source.dynamicColorMode;
        clone.dynamicColorAttribute = source.dynamicColorAttribute ?? string.Empty;
        clone.discreteColorRules = CloneDiscreteColorRules(source.discreteColorRules);
        clone.continuousBaseColor = source.continuousBaseColor;
        clone.continuousMinValue = source.continuousMinValue;
        clone.continuousMaxValue = source.continuousMaxValue;
        clone.continuousInvert = source.continuousInvert;
        clone.continuousLightAmount = source.continuousLightAmount;
        clone.continuousDarkAmount = source.continuousDarkAmount;
        clone.fallbackToStaticColor = source.fallbackToStaticColor;
        clone.overrideVisibility = source.overrideVisibility;
        clone.visible = source.visible;
        return clone;
    }

    private static List<GamaDiscreteColorRule> CloneDiscreteColorRules(List<GamaDiscreteColorRule> sourceRules)
    {
        List<GamaDiscreteColorRule> cloneRules = new List<GamaDiscreteColorRule>();
        if (sourceRules == null)
        {
            return cloneRules;
        }

        for (int i = 0; i < sourceRules.Count; i++)
        {
            GamaDiscreteColorRule sourceRule = sourceRules[i];
            if (sourceRule == null)
            {
                continue;
            }

            cloneRules.Add(new GamaDiscreteColorRule
            {
                value = sourceRule.value,
                color = sourceRule.color
            });
        }

        return cloneRules;
    }

    private static bool IsExactRuntimeContextEntry(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedModel,
        string wantedExperiment,
        string wantedSpecies)
    {
        if (entry == null)
        {
            return false;
        }

        return string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeModelPath(entry.modelPath),
                   wantedModel,
                   StringComparison.Ordinal) &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeKey(entry.experimentName),
                   wantedExperiment,
                   StringComparison.Ordinal) &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeKey(entry.GetSpeciesName()),
                   wantedSpecies,
                   StringComparison.Ordinal);
    }

    private static bool IsActiveSelectionFallbackEntry(
        GamaSpeciesRenderOverrideEntry entry,
        string wantedExperiment,
        string wantedSpecies)
    {
        if (entry == null || string.IsNullOrWhiteSpace(wantedExperiment))
        {
            return false;
        }

        return string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeModelPath(entry.modelPath),
                   "gama_active_selection",
                   StringComparison.Ordinal) &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeKey(entry.experimentName),
                   wantedExperiment,
                   StringComparison.Ordinal) &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeKey(entry.GetSpeciesName()),
                   wantedSpecies,
                   StringComparison.Ordinal);
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

            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE_PICK] species=" + pair.Key +
                      " pickedModel=" + (entry.modelPath ?? string.Empty) +
                      " pickedExperiment=" + (entry.experimentName ?? string.Empty) +
                      " requestedModel=" + (requestedModel ?? string.Empty) +
                      " requestedExperiment=" + (requestedExperiment ?? string.Empty) +
                      " prefab=" + prefab +
                      " scale=" + entry.GetEffectiveScaleMultiplier() +
                      " score=" + score);

            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDES] species=" + pair.Key +
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

        if (!runtimeContextAvailable && GamaLog.VerboseEnabled && logCount < MaxLogs)
        {
            logCount++;
            GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] No species-only override for species=" + speciesKey);
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
