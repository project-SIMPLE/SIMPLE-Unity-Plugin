using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GamaSpeciesAppearanceEditorCoordinator
{
    private const string PreviewRootName = "[GAMA] Static Experiment Preview";

    static GamaSpeciesAppearanceEditorCoordinator()
    {
        GamaSpeciesAppearanceStateStore.Changed += OnAppearanceChanged;
        Undo.undoRedoPerformed += OnUndoRedoPerformed;
        AssemblyReloadEvents.beforeAssemblyReload += ClearTransientStateBeforeReload;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneClosing += OnSceneClosing;
        EditorApplication.delayCall += SynchronizeFromScene;
    }

    public static bool TryResolveActiveContext(out GamaSpeciesAppearanceContext context)
    {
        GamaPreviewSession session = FindCurrentPreviewSession();
        if (session != null)
        {
            GamaSpeciesRenderOverrides asset = session.speciesOverrides;
            if (asset != null)
            {
                context = new GamaSpeciesAppearanceContext(
                    asset,
                    session.modelPath,
                    session.experimentName);
                SetActiveContext(context, false);
                return true;
            }
        }

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager != null &&
            manager.TryGetSpeciesRenderOverridesContext(
                out GamaSpeciesRenderOverrides managerAsset,
                out string modelPath,
                out string experimentName) &&
            managerAsset != null)
        {
            context = new GamaSpeciesAppearanceContext(managerAsset, modelPath, experimentName);
            SetActiveContext(context, false);
            return true;
        }

        context = default;
        return false;
    }

    public static void SetActiveContext(
        GamaSpeciesAppearanceContext context,
        bool propagateToScene = true)
    {
        if (!context.IsValid)
        {
            return;
        }

        GamaSpeciesAppearanceStateStore.SetActiveContext(context);
        if (!propagateToScene)
        {
            return;
        }

        bool sceneChanged = false;
        GamaPreviewSession session = FindCurrentPreviewSession();
        if (session != null &&
            (session.speciesOverrides != context.Asset ||
             session.modelPath != context.ModelPath ||
             session.experimentName != context.ExperimentName))
        {
            if (!EditorApplication.isPlaying)
            {
                Undo.RecordObject(session, "Set GAMA species appearance context");
            }
            session.speciesOverrides = context.Asset;
            session.modelPath = context.ModelPath;
            session.experimentName = context.ExperimentName;
            EditorUtility.SetDirty(session);
            sceneChanged = true;
        }

        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            SimulationManager manager = managers[i];
            if (manager == null)
            {
                continue;
            }

            bool alreadyAssigned = manager.TryGetSpeciesRenderOverridesContext(
                out GamaSpeciesRenderOverrides currentAsset,
                out string currentModel,
                out string currentExperiment) &&
                currentAsset == context.Asset &&
                GamaSpeciesRenderOverrides.NormalizeModelPath(currentModel) ==
                    GamaSpeciesRenderOverrides.NormalizeModelPath(context.ModelPath) &&
                GamaSpeciesRenderOverrides.NormalizeKey(currentExperiment) ==
                    GamaSpeciesRenderOverrides.NormalizeKey(context.ExperimentName);
            if (alreadyAssigned)
            {
                continue;
            }

            if (!EditorApplication.isPlaying)
            {
                Undo.RecordObject(manager, "Set GAMA species appearance context");
            }
            if (manager.SetSpeciesRenderOverridesContext(
                    context.Asset,
                    context.ModelPath,
                    context.ExperimentName))
            {
                EditorUtility.SetDirty(manager);
                sceneChanged = true;
            }
        }

        if (sceneChanged &&
            !EditorApplication.isPlaying &&
            SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }

    public static void ClearActiveContext(bool removePersistedEntries)
    {
        GamaSpeciesAppearanceContext context = GamaSpeciesAppearanceStateStore.ActiveContext;
        if (!context.IsValid)
        {
            GamaPreviewSession session = FindCurrentPreviewSession();
            if (session != null && session.speciesOverrides != null)
            {
                context = new GamaSpeciesAppearanceContext(
                    session.speciesOverrides,
                    session.modelPath,
                    session.experimentName);
            }
            else
            {
                SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(
                    FindObjectsInactive.Include);
                if (manager != null && manager.TryGetSpeciesRenderOverridesContext(
                        out GamaSpeciesRenderOverrides managerAsset,
                        out string modelPath,
                        out string experimentName) &&
                    managerAsset != null)
                {
                    context = new GamaSpeciesAppearanceContext(
                        managerAsset,
                        modelPath,
                        experimentName);
                }
            }
        }

        if (context.IsValid)
        {
            GamaSpeciesAppearanceStateStore.ClearContext(context, removePersistedEntries);
            if (removePersistedEntries && !EditorApplication.isPlaying)
            {
                AssetDatabase.SaveAssets();
            }
        }
        else
        {
            GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
        }

        bool sceneChanged = false;
        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] == null)
            {
                continue;
            }

            if (!managers[i].TryGetSpeciesRenderOverridesContext(
                    out GamaSpeciesRenderOverrides currentAsset,
                    out _,
                    out _) ||
                currentAsset == null)
            {
                continue;
            }

            if (!EditorApplication.isPlaying)
            {
                Undo.RecordObject(managers[i], "Clear GAMA species appearance context");
            }
            managers[i].SetSpeciesRenderOverridesContext(null, string.Empty, string.Empty);
            EditorUtility.SetDirty(managers[i]);
            sceneChanged = true;
        }

        if (sceneChanged &&
            !EditorApplication.isPlaying &&
            SceneManager.GetActiveScene().IsValid())
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
    }

    public static GamaPreviewSession FindCurrentPreviewSession()
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
            if (!session.stale && session.gameObject != null && session.gameObject.name == PreviewRootName)
            {
                fallback = session;
            }
            else if (fallback == null)
            {
                fallback = session;
            }
        }
        return fallback;
    }

    private static void SynchronizeFromScene()
    {
        if (TryResolveActiveContext(out GamaSpeciesAppearanceContext context))
        {
            SetActiveContext(context, false);
        }
    }

    private static void OnAppearanceChanged(GamaSpeciesAppearanceChange change)
    {
        if (change.Kind == GamaSpeciesAppearanceChangeKind.EntryChanged)
        {
            SynchronizeWizardViews(change);
            if (EditorApplication.isPlaying)
            {
                EditorApplication.delayCall += () => ApplyRuntimeChange(change.SpeciesName);
            }
        }

        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        }

        GamaPanelWindow[] panels = Resources.FindObjectsOfTypeAll<GamaPanelWindow>();
        for (int i = 0; i < panels.Length; i++)
        {
            panels[i]?.Repaint();
        }
        if (ActiveEditorTracker.sharedTracker != null)
        {
            ActiveEditorTracker.sharedTracker.ForceRebuild();
        }
        SceneView.RepaintAll();
    }

    private static void SynchronizeWizardViews(GamaSpeciesAppearanceChange change)
    {
        if (!change.Context.IsValid || string.IsNullOrWhiteSpace(change.SpeciesName))
        {
            return;
        }

        if (!GamaSpeciesAppearanceStateStore.TryGetEntry(
                change.Context,
                change.SpeciesName,
                change.RuntimeOnly,
                out GamaSpeciesRenderOverrideEntry entry) ||
            entry == null)
        {
            return;
        }

        GamaSpeciesWizard[] wizards = UnityEngine.Object.FindObjectsByType<GamaSpeciesWizard>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < wizards.Length; i++)
        {
            GamaSpeciesWizard wizard = wizards[i];
            if (wizard == null || wizard.overridesAsset != change.Context.Asset ||
                !string.Equals(
                    GamaSpeciesRenderOverrides.NormalizeModelPath(wizard.modelPath),
                    GamaSpeciesRenderOverrides.NormalizeModelPath(change.Context.ModelPath),
                    System.StringComparison.Ordinal) ||
                !string.Equals(
                    GamaSpeciesRenderOverrides.NormalizeKey(wizard.experimentName),
                    GamaSpeciesRenderOverrides.NormalizeKey(change.Context.ExperimentName),
                    System.StringComparison.Ordinal) ||
                !string.Equals(
                    GamaSpeciesRenderOverrides.NormalizeKey(wizard.speciesName),
                    GamaSpeciesRenderOverrides.NormalizeKey(change.SpeciesName),
                    System.StringComparison.Ordinal))
            {
                continue;
            }

            using (GamaSpeciesWizard.SuppressAssetWrites())
            {
                wizard.PopulateFromEntry(entry);
            }
            EditorUtility.SetDirty(wizard);
        }
    }

    private static void ApplyRuntimeChange(string speciesName)
    {
        if (!EditorApplication.isPlaying || string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GamaRuntimePreviewOverrideApplier.RefreshNow();
        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            managers[i]?.ApplyRuntimeSpeciesOverrideNow(speciesName);
        }
    }

    private static void ClearTransientStateBeforeReload()
    {
        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += () =>
        {
            SynchronizeFromScene();
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        };
    }

    private static void OnSceneClosing(Scene scene, bool removingScene)
    {
        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
    }

    private static void OnUndoRedoPerformed()
    {
        SynchronizeFromScene();
        GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        SceneView.RepaintAll();
    }
}
