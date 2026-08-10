using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct GamaSpeciesAppearanceContext : IEquatable<GamaSpeciesAppearanceContext>
{
    public readonly GamaSpeciesRenderOverrides Asset;
    public readonly string ModelPath;
    public readonly string ExperimentName;

    public GamaSpeciesAppearanceContext(
        GamaSpeciesRenderOverrides asset,
        string modelPath,
        string experimentName)
    {
        Asset = asset;
        ModelPath = modelPath ?? string.Empty;
        ExperimentName = experimentName ?? string.Empty;
    }

    public bool IsValid => Asset != null;

    public bool Equals(GamaSpeciesAppearanceContext other)
    {
        return Asset == other.Asset &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeModelPath(ModelPath),
                   GamaSpeciesRenderOverrides.NormalizeModelPath(other.ModelPath),
                   StringComparison.Ordinal) &&
               string.Equals(
                   GamaSpeciesRenderOverrides.NormalizeKey(ExperimentName),
                   GamaSpeciesRenderOverrides.NormalizeKey(other.ExperimentName),
                   StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is GamaSpeciesAppearanceContext other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Asset != null ? Asset.GetGamaObjectId().GetHashCode() : 0;
            hash = hash * 397 ^ GamaSpeciesRenderOverrides.NormalizeModelPath(ModelPath).GetHashCode();
            hash = hash * 397 ^ GamaSpeciesRenderOverrides.NormalizeKey(ExperimentName).GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(GamaSpeciesAppearanceContext left, GamaSpeciesAppearanceContext right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(GamaSpeciesAppearanceContext left, GamaSpeciesAppearanceContext right)
    {
        return !left.Equals(right);
    }
}

public enum GamaSpeciesAppearanceChangeKind
{
    EntryChanged,
    ContextChanged,
    ContextCleared,
    RuntimeOverlayCleared
}

public readonly struct GamaSpeciesAppearanceChange
{
    public readonly GamaSpeciesAppearanceChangeKind Kind;
    public readonly GamaSpeciesAppearanceContext Context;
    public readonly string SpeciesName;
    public readonly bool RuntimeOnly;

    public GamaSpeciesAppearanceChange(
        GamaSpeciesAppearanceChangeKind kind,
        GamaSpeciesAppearanceContext context,
        string speciesName,
        bool runtimeOnly)
    {
        Kind = kind;
        Context = context;
        SpeciesName = speciesName ?? string.Empty;
        RuntimeOnly = runtimeOnly;
    }
}

/// <summary>
/// Canonical access point for persisted species appearance and the temporary Play overlay.
/// Every lookup is exact for asset/model/experiment/species.
/// </summary>
public static class GamaSpeciesAppearanceStateStore
{
    private static readonly Dictionary<string, GamaSpeciesRenderOverrideEntry> runtimeOverlay =
        new Dictionary<string, GamaSpeciesRenderOverrideEntry>(StringComparer.OrdinalIgnoreCase);

    private static GamaSpeciesAppearanceContext activeContext;

    public static event Action<GamaSpeciesAppearanceChange> Changed;

    public static GamaSpeciesAppearanceContext ActiveContext => activeContext;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        runtimeOverlay.Clear();
        activeContext = default;
    }

    public static void SetActiveContext(GamaSpeciesAppearanceContext context)
    {
        if (activeContext == context)
        {
            return;
        }

        if (activeContext.IsValid)
        {
            RemoveRuntimeOverlayEntries(activeContext);
        }

        activeContext = context;
        RaiseChanged(new GamaSpeciesAppearanceChange(
            GamaSpeciesAppearanceChangeKind.ContextChanged,
            context,
            string.Empty,
            false));
    }

    public static bool TryGetEntry(
        GamaSpeciesAppearanceContext context,
        string speciesName,
        bool includeRuntimeOverlay,
        out GamaSpeciesRenderOverrideEntry entry)
    {
        entry = null;
        if (!context.IsValid || string.IsNullOrWhiteSpace(speciesName))
        {
            return false;
        }

        if (includeRuntimeOverlay &&
            runtimeOverlay.TryGetValue(BuildKey(context, speciesName), out entry) &&
            entry != null)
        {
            return true;
        }

        return context.Asset.TryGetOverride(
            context.ModelPath,
            context.ExperimentName,
            speciesName,
            out entry,
            true);
    }

    public static GamaSpeciesRenderOverrideEntry GetOrCreateEditableEntry(
        GamaSpeciesAppearanceContext context,
        string speciesName,
        bool runtimeOnly)
    {
        if (!context.IsValid || string.IsNullOrWhiteSpace(speciesName))
        {
            return null;
        }

        string normalizedSpecies = speciesName.Trim();
        if (!runtimeOnly)
        {
            return context.Asset.GetOrCreateEntry(
                context.ModelPath,
                context.ExperimentName,
                normalizedSpecies);
        }

        string key = BuildKey(context, normalizedSpecies);
        if (runtimeOverlay.TryGetValue(key, out GamaSpeciesRenderOverrideEntry overlayEntry) &&
            overlayEntry != null)
        {
            return overlayEntry;
        }

        context.Asset.TryGetOverride(
            context.ModelPath,
            context.ExperimentName,
            normalizedSpecies,
            out GamaSpeciesRenderOverrideEntry persisted,
            true);

        overlayEntry = CloneEntry(persisted) ?? new GamaSpeciesRenderOverrideEntry();
        SetEntryIdentity(overlayEntry, context, normalizedSpecies);
        NormalizeEntry(overlayEntry);
        runtimeOverlay[key] = overlayEntry;
        return overlayEntry;
    }

    public static void NotifyEntryChanged(
        GamaSpeciesAppearanceContext context,
        string speciesName,
        bool runtimeOnly)
    {
        if (!context.IsValid || string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GamaSpeciesRenderOverrideEntry entry = GetOrCreateEditableEntry(context, speciesName, runtimeOnly);
        if (entry == null)
        {
            return;
        }

        SetEntryIdentity(entry, context, speciesName.Trim());
        NormalizeEntry(entry);

#if UNITY_EDITOR
        if (!runtimeOnly)
        {
            UnityEditor.EditorUtility.SetDirty(context.Asset);
        }
#endif

        RaiseChanged(new GamaSpeciesAppearanceChange(
            GamaSpeciesAppearanceChangeKind.EntryChanged,
            context,
            speciesName.Trim(),
            runtimeOnly));
    }

    public static IReadOnlyList<GamaSpeciesRenderOverrideEntry> GetRuntimeOverlayEntries(
        GamaSpeciesAppearanceContext context)
    {
        List<GamaSpeciesRenderOverrideEntry> result = new List<GamaSpeciesRenderOverrideEntry>();
        if (!context.IsValid)
        {
            return result;
        }

        string prefix = BuildContextKey(context) + "|";
        foreach (KeyValuePair<string, GamaSpeciesRenderOverrideEntry> pair in runtimeOverlay)
        {
            if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && pair.Value != null)
            {
                result.Add(pair.Value);
            }
        }

        return result;
    }

    public static void ClearRuntimeOverlay()
    {
        if (runtimeOverlay.Count == 0)
        {
            return;
        }

        runtimeOverlay.Clear();
        RaiseChanged(new GamaSpeciesAppearanceChange(
            GamaSpeciesAppearanceChangeKind.RuntimeOverlayCleared,
            activeContext,
            string.Empty,
            true));
    }

    public static void ClearContext(
        GamaSpeciesAppearanceContext context,
        bool removePersistedEntries)
    {
        if (!context.IsValid)
        {
            return;
        }

        RemoveRuntimeOverlayEntries(context);

        if (removePersistedEntries && context.Asset.entries != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.Undo.RecordObject(context.Asset, "Clear GAMA species appearance");
            }
#endif
            string wantedModel = GamaSpeciesRenderOverrides.NormalizeModelPath(context.ModelPath);
            string wantedExperiment = GamaSpeciesRenderOverrides.NormalizeKey(context.ExperimentName);
            context.Asset.entries.RemoveAll(entry =>
                entry != null &&
                string.Equals(
                    GamaSpeciesRenderOverrides.NormalizeModelPath(entry.modelPath),
                    wantedModel,
                    StringComparison.Ordinal) &&
                string.Equals(
                    GamaSpeciesRenderOverrides.NormalizeKey(entry.experimentName),
                    wantedExperiment,
                    StringComparison.Ordinal));
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(context.Asset);
#endif
        }

        if (activeContext == context)
        {
            activeContext = default;
        }

        RaiseChanged(new GamaSpeciesAppearanceChange(
            GamaSpeciesAppearanceChangeKind.ContextCleared,
            context,
            string.Empty,
            false));
    }

    public static void NormalizeEntry(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        // Migrate the legacy single visibility override before normalizing new
        // split preview/runtime fields. Otherwise a saved hidden entry would be
        // silently reset to visible the first time it is edited.
        if (entry.overrideVisibility)
        {
            if (!entry.overridePreviewVisibility)
            {
                entry.overridePreviewVisibility = true;
                entry.visibleInPreview = entry.visible;
            }
            if (!entry.overrideRuntimeVisibility)
            {
                entry.overrideRuntimeVisibility = true;
                entry.visibleInRuntime = entry.visible;
            }
        }

        entry.scaleMultiplier = entry.overrideScaleMultiplier
            ? Mathf.Max(0.0001f, entry.scaleMultiplier)
            : 1f;
        if (!entry.overridePositionOffset)
        {
            entry.positionOffset = Vector3.zero;
        }
        if (!entry.overrideRotationOffset)
        {
            entry.rotationOffsetEuler = Vector3.zero;
        }

        if (!entry.overridePreviewVisibility)
        {
            entry.visibleInPreview = true;
        }
        if (!entry.overrideRuntimeVisibility)
        {
            entry.visibleInRuntime = true;
        }

        // New writes use the split visibility fields only.
        entry.overrideVisibility = false;
        entry.visible = true;
    }

    public static void CopyEntryValues(
        GamaSpeciesRenderOverrideEntry destination,
        GamaSpeciesRenderOverrideEntry source)
    {
        if (destination == null || source == null)
        {
            return;
        }

        destination.modelPath = source.modelPath ?? string.Empty;
        destination.experimentName = source.experimentName ?? string.Empty;
        destination.speciesName = source.speciesName ?? string.Empty;
        destination.speciesKey = source.speciesKey ?? string.Empty;
        destination.prefabResourcePath = source.prefabResourcePath ?? string.Empty;
        destination.prefabOverride = source.prefabOverride;
        destination.materialOverride = source.materialOverride;
        destination.overrideColor = source.overrideColor;
        destination.color = source.color;
        destination.overrideScaleMultiplier = source.overrideScaleMultiplier;
        destination.overridePositionOffset = source.overridePositionOffset;
        destination.overrideRotationOffset = source.overrideRotationOffset;
        destination.positionOffset = source.positionOffset;
        destination.rotationOffsetEuler = source.rotationOffsetEuler;
        destination.scaleMultiplier = source.scaleMultiplier;
        destination.overridePreviewVisibility = source.overridePreviewVisibility;
        destination.visibleInPreview = source.visibleInPreview;
        destination.overrideRuntimeVisibility = source.overrideRuntimeVisibility;
        destination.visibleInRuntime = source.visibleInRuntime;
        destination.renderMode = source.renderMode;
        destination.notesDebug = source.notesDebug ?? string.Empty;
        destination.overrideDynamicColor = source.overrideDynamicColor;
        destination.dynamicColorMode = source.dynamicColorMode;
        destination.dynamicColorAttribute = source.dynamicColorAttribute ?? string.Empty;
        destination.continuousBaseColor = source.continuousBaseColor;
        destination.continuousMinValue = source.continuousMinValue;
        destination.continuousMaxValue = source.continuousMaxValue;
        destination.continuousInvert = source.continuousInvert;
        destination.continuousLightAmount = source.continuousLightAmount;
        destination.continuousDarkAmount = source.continuousDarkAmount;
        destination.fallbackToStaticColor = source.fallbackToStaticColor;
        destination.overrideVisibility = source.overrideVisibility;
        destination.visible = source.visible;
        destination.discreteColorRules = new List<GamaDiscreteColorRule>();
        if (source.discreteColorRules != null)
        {
            for (int i = 0; i < source.discreteColorRules.Count; i++)
            {
                GamaDiscreteColorRule rule = source.discreteColorRules[i];
                if (rule != null)
                {
                    destination.discreteColorRules.Add(new GamaDiscreteColorRule
                    {
                        value = rule.value,
                        color = rule.color
                    });
                }
            }
        }
    }

    private static void SetEntryIdentity(
        GamaSpeciesRenderOverrideEntry entry,
        GamaSpeciesAppearanceContext context,
        string speciesName)
    {
        entry.modelPath = context.ModelPath;
        entry.experimentName = context.ExperimentName;
        entry.speciesName = speciesName;
        entry.speciesKey = speciesName;
    }

    private static int RemoveRuntimeOverlayEntries(GamaSpeciesAppearanceContext context)
    {
        string prefix = BuildContextKey(context) + "|";
        List<string> keys = new List<string>();
        foreach (string key in runtimeOverlay.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                keys.Add(key);
            }
        }

        for (int i = 0; i < keys.Count; i++)
        {
            runtimeOverlay.Remove(keys[i]);
        }

        return keys.Count;
    }

    private static string BuildKey(GamaSpeciesAppearanceContext context, string speciesName)
    {
        return BuildContextKey(context) + "|" + GamaSpeciesRenderOverrides.NormalizeKey(speciesName);
    }

    private static string BuildContextKey(GamaSpeciesAppearanceContext context)
    {
        GamaUnityObjectId assetId = context.Asset != null ? context.Asset.GetGamaObjectId() : default;
        return assetId + "|" +
               GamaSpeciesRenderOverrides.NormalizeModelPath(context.ModelPath) + "|" +
               GamaSpeciesRenderOverrides.NormalizeKey(context.ExperimentName);
    }

    private static GamaSpeciesRenderOverrideEntry CloneEntry(GamaSpeciesRenderOverrideEntry source)
    {
        if (source == null)
        {
            return null;
        }

        GamaSpeciesRenderOverrideEntry clone = new GamaSpeciesRenderOverrideEntry();
        CopyEntryValues(clone, source);
        return clone;
    }

    private static void RaiseChanged(GamaSpeciesAppearanceChange change)
    {
        Changed?.Invoke(change);
    }
}
