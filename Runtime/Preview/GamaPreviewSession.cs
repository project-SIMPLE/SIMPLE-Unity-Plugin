using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct GamaPreviewCrsSettings
{
    public float coefX;
    public float coefY;
    public float offsetX;
    public float offsetY;
    public float offsetZ;
}

[Serializable]
public class GamaPreviewSpeciesCount
{
    public string speciesName;
    public int count;
}

[DisallowMultipleComponent]
public class GamaPreviewSession : MonoBehaviour
{
    [Header("Experiment identity")]
    public string modelPath = string.Empty;
    public string experimentName = string.Empty;
    public string experimentDisplayName = string.Empty;
    public string sourceGamlPath = string.Empty;
    public string experimentSignature = string.Empty;
    public string previewCacheReference = string.Empty;
    public string selectionMode = string.Empty;
    public bool activeGamaSelection;
    public string stableExperimentKey = string.Empty;
    public string monitorExperimentId = string.Empty;

    [Header("Play reuse authorization")]
    [HideInInspector]
    public bool reuseAuthorizedForPlay;
    [HideInInspector]
    public string authorizedStableExperimentKey = string.Empty;
    [HideInInspector]
    public string authorizedMonitorExperimentId = string.Empty;

    [Header("Capture metadata")]
    public string captureTimestampUtc = string.Empty;
    public GamaPreviewCrsSettings crs;
    public int monitorPort;
    public int middlewarePort;
    public string playerId = string.Empty;
    public bool stale;
    public bool useThisPreviewForPlay;

    [Header("Species snapshot")]
    public List<string> speciesList = new List<string>();
    public List<GamaPreviewSpeciesCount> speciesCounts = new List<GamaPreviewSpeciesCount>();
    public GamaSpeciesRenderOverrides speciesOverrides;

    public bool TryGetStableExperimentKey(out string key)
    {
        if (!GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                modelPath,
                experimentName,
                activeGamaSelection,
                monitorExperimentId,
                out key))
        {
            key = string.Empty;
            return false;
        }

        return string.IsNullOrEmpty(stableExperimentKey) ||
               string.Equals(stableExperimentKey, key, StringComparison.Ordinal);
    }

    public bool RefreshStableExperimentKey()
    {
        if (!GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                modelPath,
                experimentName,
                activeGamaSelection,
                monitorExperimentId,
                out string key))
        {
            stableExperimentKey = string.Empty;
            return false;
        }

        stableExperimentKey = key;
        return true;
    }

    public void ClearRuntimeReuseAuthorization()
    {
        reuseAuthorizedForPlay = false;
        authorizedStableExperimentKey = string.Empty;
        authorizedMonitorExperimentId = string.Empty;
    }
}
