using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

internal readonly struct GamaEditorPlayRuntimeSnapshot
{
    public GamaEditorPlayRuntimeSnapshot(
        string precisionJson,
        string propertiesJson,
        string worldJson)
    {
        PrecisionJson = precisionJson ?? string.Empty;
        PropertiesJson = propertiesJson ?? string.Empty;
        WorldJson = worldJson ?? string.Empty;
    }

    public string PrecisionJson { get; }
    public string PropertiesJson { get; }
    public string WorldJson { get; }
}

[InitializeOnLoad]
internal static class GamaEditorPlayRuntimeRecorder
{
    private static readonly object Sync = new object();

    private static bool collecting;
    private static string precisionJson = string.Empty;
    private static string propertiesJson = string.Empty;
    private static Dictionary<string, PropertiesGAMA> propertyMap =
        new Dictionary<string, PropertiesGAMA>(StringComparer.OrdinalIgnoreCase);
    private static GamaEditorPreviewWorldAccumulator worldAccumulator =
        new GamaEditorPreviewWorldAccumulator();
    private static int worldTickIndex;

    static GamaEditorPlayRuntimeRecorder()
    {
        ConnectionManager.OnAnyServerMessageReceived -= RecordServerMessage;
        ConnectionManager.OnAnyServerMessageReceived += RecordServerMessage;
        collecting = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
    }

    public static void BeginPlaySession()
    {
        lock (Sync)
        {
            collecting = true;
            precisionJson = string.Empty;
            propertiesJson = string.Empty;
            propertyMap = new Dictionary<string, PropertiesGAMA>(StringComparer.OrdinalIgnoreCase);
            worldAccumulator = new GamaEditorPreviewWorldAccumulator();
            worldTickIndex = 0;
        }
    }

    public static void EndPlaySession()
    {
        lock (Sync)
        {
            collecting = false;
        }
    }

    public static bool HasCompleteSnapshot
    {
        get
        {
            lock (Sync)
            {
                return HasCompleteSnapshotUnsafe();
            }
        }
    }

    public static bool TryGetSnapshot(out GamaEditorPlayRuntimeSnapshot snapshot)
    {
        lock (Sync)
        {
            if (!HasCompleteSnapshotUnsafe())
            {
                snapshot = default;
                return false;
            }

            snapshot = new GamaEditorPlayRuntimeSnapshot(
                precisionJson,
                propertiesJson,
                worldAccumulator.ToWorldJson());
            return true;
        }
    }

    internal static void RecordServerMessage(string firstKey, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        lock (Sync)
        {
            if (!collecting)
            {
                return;
            }

            try
            {
                JObject contents = JObject.Parse(content);
                if (contents["precision"] != null ||
                    string.Equals(firstKey, "precision", StringComparison.OrdinalIgnoreCase))
                {
                    precisionJson = contents.ToString(Formatting.None);
                }

                if (contents["properties"] != null ||
                    string.Equals(firstKey, "properties", StringComparison.OrdinalIgnoreCase))
                {
                    propertiesJson = contents.ToString(Formatting.None);
                    AllProperties parsedProperties = AllProperties.CreateFromJSON(propertiesJson);
                    propertyMap = GamaEditorPreviewCapture.BuildPropertyMap(parsedProperties);
                }

                JObject world = contents["world"] as JObject ?? contents;
                if (ContainsWorldData(world, firstKey))
                {
                    worldAccumulator.Merge(
                        world,
                        worldTickIndex++,
                        propertyMap,
                        null);
                }
            }
            catch (Exception ex)
            {
                GamaLog.DevWarning(
                    "[GAMA][PREVIEW][PLAY-SNAPSHOT] Could not record a runtime JSON message: " +
                    ex.GetBaseException().Message);
            }
        }
    }

    internal static void ResetForTests()
    {
        BeginPlaySession();
    }

    private static bool HasCompleteSnapshotUnsafe()
    {
        return !string.IsNullOrWhiteSpace(precisionJson) &&
               !string.IsNullOrWhiteSpace(propertiesJson) &&
               worldAccumulator != null &&
               worldAccumulator.Count > 0;
    }

    private static bool ContainsWorldData(JObject world, string firstKey)
    {
        if (world == null)
        {
            return false;
        }

        if (world["pointsLoc"] != null ||
            world["pointsGeom"] != null ||
            world["names"] != null ||
            world["propertyID"] != null)
        {
            return true;
        }

        return string.Equals(firstKey, "pointsLoc", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstKey, "pointsGeom", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstKey, "names", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(firstKey, "world", StringComparison.OrdinalIgnoreCase);
    }
}
