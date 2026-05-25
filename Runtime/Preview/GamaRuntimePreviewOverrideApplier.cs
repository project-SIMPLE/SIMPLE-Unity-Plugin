using UnityEngine;
using System;
using System.Collections.Generic;

public static class GamaRuntimePreviewOverrideApplier
{
    private static Dictionary<string, GamaSpeciesRenderOverrideEntry> overridesBySpecies;
    private static bool initialized;
    private static int logCount;
    private const int MaxLogs = 5;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        overridesBySpecies = null;
        initialized = false;
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
        else if (!found && logCount < MaxLogs)
        {
            logCount++;
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No override for species=" + speciesKey);
        }
        return found;
    }

    public static void RefreshNow()
    {
        initialized = true;
        Initialize();
    }

    private static void Initialize()
    {
        overridesBySpecies = new Dictionary<string, GamaSpeciesRenderOverrideEntry>(StringComparer.OrdinalIgnoreCase);

        GamaPreviewSession session = null;
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        if (sessions.Length > 0)
        {
            session = sessions[0];
        }

        if (session == null)
        {
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No preview session found, running without overrides.");
            return;
        }

        GamaSpeciesRenderOverrides asset = session.speciesOverrides;
        if (asset == null)
        {
            Debug.Log("[GAMA][RUNTIME][OVERRIDE] No overrides asset found on the preview session.");
            return;
        }

        foreach (var e in asset.entries)
        {
            if (e == null) continue;
            if (string.IsNullOrWhiteSpace(e.speciesName) && string.IsNullOrWhiteSpace(e.speciesKey)) continue;
            
            string key = !string.IsNullOrWhiteSpace(e.speciesKey) ? e.speciesKey : e.speciesName;
            overridesBySpecies[key.Trim()] = e;
        }

        Debug.Log("[GAMA][RUNTIME][OVERRIDE] Loaded preview overrides: " + string.Join(",", overridesBySpecies.Keys));
    }
}
