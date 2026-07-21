using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal readonly struct GamaEditorPlayPreviewIdentity
{
    public GamaEditorPlayPreviewIdentity(
        bool activeGamaSelection,
        string modelPath,
        string experimentName,
        string monitorExperimentId,
        string playerId,
        int monitorPort,
        int middlewarePort)
    {
        ActiveGamaSelection = activeGamaSelection;
        ModelPath = modelPath ?? string.Empty;
        ExperimentName = experimentName ?? string.Empty;
        MonitorExperimentId = monitorExperimentId ?? string.Empty;
        PlayerId = playerId ?? string.Empty;
        MonitorPort = monitorPort;
        MiddlewarePort = middlewarePort;
    }

    public bool ActiveGamaSelection { get; }
    public string ModelPath { get; }
    public string ExperimentName { get; }
    public string MonitorExperimentId { get; }
    public string PlayerId { get; }
    public int MonitorPort { get; }
    public int MiddlewarePort { get; }
}

internal static class GamaEditorPlayExitPreviewCapture
{
    [Serializable]
    private sealed class PendingMetadata
    {
        public int schemaVersion = 1;
        public bool activeGamaSelection;
        public string modelPath = string.Empty;
        public string experimentName = string.Empty;
        public string monitorExperimentId = string.Empty;
        public string playerId = string.Empty;
        public int monitorPort;
        public int middlewarePort;
        public string capturedAtUtc = string.Empty;
    }

    private const string PendingStateKey = "ProjectSimple.GamaUnity.PreviewSafety.PendingPlayPreview";
    private const string PrecisionFileName = "precision.json";
    private const string PropertiesFileName = "properties.json";
    private const string WorldFileName = "world.json";
    private const string MetadataFileName = "metadata.json";

    public static bool HasPendingSnapshot
    {
        get
        {
            return SessionState.GetBool(PendingStateKey, false) &&
                   File.Exists(PendingFile(PrecisionFileName)) &&
                   File.Exists(PendingFile(PropertiesFileName)) &&
                   File.Exists(PendingFile(WorldFileName)) &&
                   File.Exists(PendingFile(MetadataFileName));
        }
    }

    public static bool TryStorePendingSnapshot(
        GamaEditorPlayRuntimeSnapshot snapshot,
        GamaEditorPlayPreviewIdentity identity,
        out string error)
    {
        error = null;
        ClearPendingSnapshot();

        if (string.IsNullOrWhiteSpace(snapshot.PrecisionJson) ||
            string.IsNullOrWhiteSpace(snapshot.PropertiesJson) ||
            string.IsNullOrWhiteSpace(snapshot.WorldJson))
        {
            error = "The runtime preview snapshot is incomplete.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(PendingDirectory);
            WriteUtf8(PendingFile(PrecisionFileName), snapshot.PrecisionJson);
            WriteUtf8(PendingFile(PropertiesFileName), snapshot.PropertiesJson);
            WriteUtf8(PendingFile(WorldFileName), snapshot.WorldJson);

            PendingMetadata metadata = new PendingMetadata
            {
                activeGamaSelection = identity.ActiveGamaSelection,
                modelPath = identity.ModelPath,
                experimentName = identity.ExperimentName,
                monitorExperimentId = identity.MonitorExperimentId,
                playerId = identity.PlayerId,
                monitorPort = identity.MonitorPort,
                middlewarePort = identity.MiddlewarePort,
                capturedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            WriteUtf8(PendingFile(MetadataFileName), JsonUtility.ToJson(metadata, true));
            SessionState.SetBool(PendingStateKey, true);
            return true;
        }
        catch (Exception ex)
        {
            ClearPendingSnapshot();
            error = "The Play Mode preview could not be saved temporarily: " +
                    ex.GetBaseException().Message;
            return false;
        }
    }

    public static void ScheduleRestoreAfterPlayModeExit()
    {
        EditorApplication.delayCall -= RestorePendingPreview;
        if (HasPendingSnapshot)
        {
            EditorApplication.delayCall += RestorePendingPreview;
        }
    }

    public static bool TryRestorePendingSnapshot(out string error)
    {
        error = null;
        if (!HasPendingSnapshot)
        {
            return false;
        }

        GameObject buildingRoot = null;
        int undoGroup = -1;
        try
        {
            string precisionJson = File.ReadAllText(PendingFile(PrecisionFileName));
            string propertiesJson = File.ReadAllText(PendingFile(PropertiesFileName));
            string worldJson = File.ReadAllText(PendingFile(WorldFileName));
            PendingMetadata metadata = JsonUtility.FromJson<PendingMetadata>(
                File.ReadAllText(PendingFile(MetadataFileName)));
            if (metadata == null || metadata.schemaVersion != 1)
            {
                error = "The pending Play Mode preview metadata is invalid.";
                return false;
            }

            if (!GamaEditorPreviewSafety.TryApproveCurrentReplacement(
                    null,
                    out GameObject existingRoot))
            {
                GamaLog.Info(
                    "[GAMA][PREVIEW] Saving the Play Mode view was cancelled. " +
                    "The existing Edit Mode preview was kept.");
                return false;
            }

            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Save GAMA Play Mode preview");
            GamaEditorPreviewSafety.ClearBuildingPreview();

            buildingRoot = new GameObject(GamaEditorPreviewSafety.BuildingPreviewRootName);
            Undo.RegisterCreatedObjectUndo(buildingRoot, "Save GAMA Play Mode preview");
            SimulationManager manager = FindSimulationManagerInActiveScene();
            GamaSpeciesRenderOverrides overridesAsset = ResolveOverridesAsset(manager);
            string modelPath = metadata.activeGamaSelection
                ? "GAMA_ACTIVE_SELECTION"
                : metadata.modelPath;
            string experimentName = metadata.activeGamaSelection ||
                                    string.IsNullOrWhiteSpace(metadata.experimentName)
                ? "unknown"
                : metadata.experimentName;

            if (!GamaEditorStaticPreviewFromJson.TryBuild(
                    manager,
                    precisionJson,
                    propertiesJson,
                    worldJson,
                    buildingRoot.transform,
                    out int prefabCount,
                    out int geometryCount,
                    out string buildError,
                    overridesAsset,
                    modelPath,
                    experimentName))
            {
                error = "The Play Mode view could not be rebuilt as an editable preview: " + buildError;
                Undo.DestroyObjectImmediate(buildingRoot);
                buildingRoot = null;
                return false;
            }

            ConfigureSession(buildingRoot, manager, overridesAsset, metadata);
            GamaEditorPreviewSafety.CommitReplacement(existingRoot, buildingRoot);
            buildingRoot = null;
            FinalizeRestoredPreview(prefabCount, geometryCount);
            return true;
        }
        catch (Exception ex)
        {
            if (buildingRoot != null)
            {
                Undo.DestroyObjectImmediate(buildingRoot);
            }

            error = "The Play Mode preview could not be restored: " +
                    ex.GetBaseException().Message;
            return false;
        }
        finally
        {
            if (undoGroup >= 0)
            {
                try
                {
                    Undo.CollapseUndoOperations(undoGroup);
                }
                catch
                {
                    // The restore result has already been handled.
                }
            }

            ClearPendingSnapshot();
        }
    }

    public static void ClearPendingSnapshot()
    {
        EditorApplication.delayCall -= RestorePendingPreview;
        SessionState.EraseBool(PendingStateKey);

        string[] fileNames =
        {
            PrecisionFileName,
            PropertiesFileName,
            WorldFileName,
            MetadataFileName
        };
        for (int i = 0; i < fileNames.Length; i++)
        {
            try
            {
                string path = PendingFile(fileNames[i]);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // A later capture will overwrite the same project-local files.
            }
        }

        try
        {
            if (Directory.Exists(PendingDirectory) &&
                Directory.GetFileSystemEntries(PendingDirectory).Length == 0)
            {
                Directory.Delete(PendingDirectory, false);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private static void RestorePendingPreview()
    {
        EditorApplication.delayCall -= RestorePendingPreview;
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (!TryRestorePendingSnapshot(out string error) &&
            !string.IsNullOrWhiteSpace(error))
        {
            GamaLog.Warning("[GAMA][PREVIEW] " + error + " The previous Edit Mode preview was kept.");
        }
    }

    private static void FinalizeRestoredPreview(int prefabCount, int geometryCount)
    {
        GamaEditorPreviewSafety.TryFindEditablePreview(
            out GameObject completedRoot,
            out GamaPreviewSession completedSession);

        try
        {
            if (completedSession != null && completedSession.speciesOverrides != null)
            {
                GamaSpeciesAppearanceEditorCoordinator.SetActiveContext(
                    new GamaSpeciesAppearanceContext(
                        completedSession.speciesOverrides,
                        completedSession.modelPath,
                        completedSession.experimentName));
            }

            GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        catch (Exception ex)
        {
            GamaLog.DevWarning(
                "[GAMA][PREVIEW] The Play Mode preview was saved, but its Editor context refresh was incomplete: " +
                ex.GetBaseException().Message);
        }

        try
        {
            if (completedRoot != null)
            {
                Selection.activeGameObject = completedRoot;
                SceneView.FrameLastActiveSceneView();
            }
        }
        catch (Exception ex)
        {
            GamaLog.DevWarning(
                "[GAMA][PREVIEW] The Play Mode preview was saved, but the Scene view could not be framed: " +
                ex.GetBaseException().Message);
        }

        GamaLog.Info(
            "[GAMA][PREVIEW] Saved the last Play Mode view as an editable preview (" +
            prefabCount + " prefab(s), " + geometryCount + " geometry item(s)).");
    }

    private static void ConfigureSession(
        GameObject root,
        SimulationManager manager,
        GamaSpeciesRenderOverrides overridesAsset,
        PendingMetadata metadata)
    {
        GamaPreviewSession session = root.AddComponent<GamaPreviewSession>();
        session.modelPath = metadata.activeGamaSelection
            ? "GAMA_ACTIVE_SELECTION"
            : metadata.modelPath ?? string.Empty;
        session.experimentName = metadata.activeGamaSelection ||
                                 string.IsNullOrWhiteSpace(metadata.experimentName)
            ? "unknown"
            : metadata.experimentName;
        session.experimentDisplayName = session.experimentName;
        session.sourceGamlPath = metadata.activeGamaSelection
            ? string.Empty
            : metadata.modelPath ?? string.Empty;
        session.experimentSignature = BuildSnapshotSignature(metadata);
        session.previewCacheReference = string.Empty;
        session.selectionMode = "PlayModeSnapshot";
        session.activeGamaSelection = metadata.activeGamaSelection;
        session.stableExperimentKey = string.Empty;
        session.monitorExperimentId = metadata.monitorExperimentId ?? string.Empty;
        session.captureTimestampUtc = metadata.capturedAtUtc ?? string.Empty;
        session.monitorPort = metadata.monitorPort;
        session.middlewarePort = metadata.middlewarePort;
        session.playerId = metadata.playerId ?? string.Empty;
        session.stale = false;
        session.useThisPreviewForPlay = false;
        session.speciesOverrides = overridesAsset;

        PopulateSpeciesSnapshot(root, session);
        PropagateSessionToSpeciesWizards(root, session);
        EditorUtility.SetDirty(session);

    }

    private static void PopulateSpeciesSnapshot(GameObject root, GamaPreviewSession session)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            GamaPreviewObject item = previewObjects[i];
            if (item == null)
            {
                continue;
            }

            string species = string.IsNullOrWhiteSpace(item.speciesName)
                ? "unknown"
                : item.speciesName.Trim();
            counts[species] = counts.TryGetValue(species, out int count) ? count + 1 : 1;
        }

        List<string> speciesNames = new List<string>(counts.Keys);
        speciesNames.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < speciesNames.Count; i++)
        {
            string species = speciesNames[i];
            session.speciesList.Add(species);
            session.speciesCounts.Add(new GamaPreviewSpeciesCount
            {
                speciesName = species,
                count = counts[species]
            });
        }
    }

    private static void PropagateSessionToSpeciesWizards(
        GameObject root,
        GamaPreviewSession session)
    {
        GamaSpeciesWizard[] wizards = root.GetComponentsInChildren<GamaSpeciesWizard>(true);
        for (int i = 0; i < wizards.Length; i++)
        {
            GamaSpeciesWizard wizard = wizards[i];
            if (wizard == null)
            {
                continue;
            }

            using (GamaSpeciesWizard.SuppressAssetWrites())
            {
                wizard.modelPath = session.modelPath;
                wizard.experimentName = session.experimentName;
                wizard.speciesName = string.IsNullOrWhiteSpace(wizard.speciesName)
                    ? wizard.gameObject.name
                    : wizard.speciesName;
                if (wizard.overridesAsset == null)
                {
                    wizard.overridesAsset = session.speciesOverrides;
                }
            }

            EditorUtility.SetDirty(wizard);
        }
    }

    private static SimulationManager FindSimulationManagerInActiveScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.InstanceID);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null && managers[i].gameObject.scene == activeScene)
            {
                return managers[i];
            }
        }

        return null;
    }

    private static GamaSpeciesRenderOverrides ResolveOverridesAsset(SimulationManager manager)
    {
        if (manager != null &&
            manager.TryGetSpeciesRenderOverridesContext(
                out GamaSpeciesRenderOverrides asset,
                out _,
                out _) &&
            asset != null)
        {
            return asset;
        }

        return GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
    }

    private static string BuildSnapshotSignature(PendingMetadata metadata)
    {
        string input = string.Join("|",
            metadata.modelPath ?? string.Empty,
            metadata.experimentName ?? string.Empty,
            metadata.monitorExperimentId ?? string.Empty,
            metadata.capturedAtUtc ?? string.Empty);
        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < input.Length; i++)
            {
                hash ^= input[i];
                hash *= 16777619;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    private static void WriteUtf8(string path, string contents)
    {
        File.WriteAllText(path, contents ?? string.Empty, new UTF8Encoding(false));
    }

    private static string PendingFile(string fileName)
    {
        return Path.Combine(PendingDirectory, fileName);
    }

    private static string PendingDirectory
    {
        get
        {
            return Path.Combine(
                Application.temporaryCachePath,
                "ProjectSimple",
                "PendingPlayPreview");
        }
    }
}
