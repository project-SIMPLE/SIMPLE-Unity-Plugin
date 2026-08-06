using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GamaDirectPlaySpeciesOverridesTests
{
    private const string ApplyPreviewSettingsToPlayPrefKey =
        "ProjectSimple.GamaUnity.Panel.ApplyPreviewSettingsToPlay";

    private Scene originalActiveScene;
    private Scene testScene;
    private bool ownsIsolatedTestScene;
    private bool hadApplyPreviewSettingsPreference;
    private bool originalApplyPreviewSettingsPreference;
    private GamaSpeciesAppearanceContext originalAppearanceContext;
    private GamaSpeciesRenderOverridesAsset asset;
    private GamaSpeciesRenderOverridesAsset unusedFallbackAsset;
    private GameObject createdPreviewObject;
    private GameObject createdManagerObject;

    [SetUp]
    public void SetUp()
    {
        originalAppearanceContext = GamaSpeciesAppearanceStateStore.ActiveContext;
        if (originalAppearanceContext.IsValid)
        {
            GamaSpeciesAppearanceStateStore.ClearContext(originalAppearanceContext, false);
        }

        GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();
        originalActiveScene = SceneManager.GetActiveScene();
        if (Application.isBatchMode)
        {
            testScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            ownsIsolatedTestScene = false;
        }
        else
        {
            try
            {
                testScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                ownsIsolatedTestScene = true;
                SceneManager.SetActiveScene(testScene);
            }
            catch (System.InvalidOperationException)
            {
                Assert.Ignore("Could not create an isolated scene without touching the open scene.");
            }
        }

        if (Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include) != null ||
            Object.FindFirstObjectByType<GamaPreviewSession>(FindObjectsInactive.Include) != null)
        {
            Assert.Ignore(
                "The direct-Play context tests require a scene without an existing GAMA manager or preview.");
        }

        hadApplyPreviewSettingsPreference = EditorPrefs.HasKey(ApplyPreviewSettingsToPlayPrefKey);
        originalApplyPreviewSettingsPreference = EditorPrefs.GetBool(
            ApplyPreviewSettingsToPlayPrefKey,
            true);
        EditorPrefs.SetBool(ApplyPreviewSettingsToPlayPrefKey, true);

        asset = ScriptableObject.CreateInstance<GamaSpeciesRenderOverridesAsset>();
        unusedFallbackAsset = ScriptableObject.CreateInstance<GamaSpeciesRenderOverridesAsset>();
    }

    [TearDown]
    public void TearDown()
    {
        GamaSpeciesAppearanceContext activeContext = GamaSpeciesAppearanceStateStore.ActiveContext;
        if (activeContext.IsValid)
        {
            GamaSpeciesAppearanceStateStore.ClearContext(activeContext, false);
        }
        GamaRuntimePreviewOverrideApplier.ClearRuntimeSessionOverrides();

        if (createdManagerObject != null)
        {
            Object.DestroyImmediate(createdManagerObject);
        }
        if (createdPreviewObject != null)
        {
            Object.DestroyImmediate(createdPreviewObject);
        }

        if (asset != null)
        {
            Object.DestroyImmediate(asset);
        }
        if (unusedFallbackAsset != null)
        {
            Object.DestroyImmediate(unusedFallbackAsset);
        }

        if (ownsIsolatedTestScene &&
            originalActiveScene.IsValid() &&
            originalActiveScene.isLoaded)
        {
            SceneManager.SetActiveScene(originalActiveScene);
        }
        if (ownsIsolatedTestScene && testScene.IsValid() && testScene.isLoaded)
        {
            EditorSceneManager.CloseScene(testScene, true);
        }

        if (hadApplyPreviewSettingsPreference)
        {
            EditorPrefs.SetBool(
                ApplyPreviewSettingsToPlayPrefKey,
                originalApplyPreviewSettingsPreference);
        }
        else
        {
            EditorPrefs.DeleteKey(ApplyPreviewSettingsToPlayPrefKey);
        }

        if (originalAppearanceContext.IsValid)
        {
            GamaSpeciesAppearanceStateStore.SetActiveContext(originalAppearanceContext);
        }
    }

    [Test]
    public void ResolveForPlay_WithoutPreviewOrManager_UsesSpeciesOnlyFallback()
    {
        int fallbackCalls = 0;

        bool resolved = GamaPreviewPlayModeGuard.TryResolveSpeciesOverrideContextForPlay(
            () =>
            {
                fallbackCalls++;
                return asset;
            },
            out GamaSpeciesAppearanceContext context);

        Assert.That(resolved, Is.True);
        Assert.That(fallbackCalls, Is.EqualTo(1));
        Assert.That(context.Asset, Is.SameAs(asset));
        Assert.That(context.ModelPath, Is.Empty);
        Assert.That(context.ExperimentName, Is.Empty);
    }

    [Test]
    public void ResolveForPlay_WithPreview_PreservesContextWithoutCreatingFallback()
    {
        createdPreviewObject = new GameObject("[GAMA] Static Experiment Preview");
        GamaPreviewSession preview = createdPreviewObject.AddComponent<GamaPreviewSession>();
        preview.speciesOverrides = asset;
        preview.modelPath = "C:/models/demo.gaml";
        preview.experimentName = "vr_xp";
        preview.useThisPreviewForPlay = true;

        int fallbackCalls = 0;
        bool resolved = GamaPreviewPlayModeGuard.TryResolveSpeciesOverrideContextForPlay(
            () =>
            {
                fallbackCalls++;
                return unusedFallbackAsset;
            },
            out GamaSpeciesAppearanceContext context);

        Assert.That(resolved, Is.True);
        Assert.That(fallbackCalls, Is.Zero);
        Assert.That(context.Asset, Is.SameAs(asset));
        Assert.That(context.ModelPath, Is.EqualTo("C:/models/demo.gaml"));
        Assert.That(context.ExperimentName, Is.EqualTo("vr_xp"));
    }

    [Test]
    public void AssignPreparedContext_AfterEarlyRuntimeMiss_AssignsManagerAndRefreshesCache()
    {
        GamaSpeciesRenderOverrideEntry wolfOverride = new GamaSpeciesRenderOverrideEntry
        {
            speciesName = "wolf",
            speciesKey = "wolf",
            modelPath = string.Empty,
            experimentName = string.Empty,
            overrideScaleMultiplier = true,
            scaleMultiplier = 2f
        };
        asset.entries.Add(wolfOverride);

        Assert.That(
            GamaRuntimePreviewOverrideApplier.TryGetOverride("wolf", out _),
            Is.False,
            "The first lookup must reproduce the cached no-context path.");

        createdManagerObject = new GameObject("Game Manager");
        SimulationManagerSolo manager = createdManagerObject.AddComponent<SimulationManagerSolo>();

        bool assigned = GamaPreviewPlayModeGuard.TryAssignPreparedSpeciesOverrideContext(
            asset,
            string.Empty,
            string.Empty);

        Assert.That(assigned, Is.True);
        Assert.That(
            manager.TryGetSpeciesRenderOverridesContext(
                out GamaSpeciesRenderOverrides assignedAsset,
                out string modelPath,
                out string experimentName),
            Is.True);
        Assert.That(assignedAsset, Is.SameAs(asset));
        Assert.That(modelPath, Is.Empty);
        Assert.That(experimentName, Is.Empty);
        Assert.That(
            GamaRuntimePreviewOverrideApplier.TryGetOverride("wolf", out GamaSpeciesRenderOverrideEntry resolved),
            Is.True);
        Assert.That(resolved, Is.SameAs(wolfOverride));
    }

    [Test]
    public void AssignPreparedContext_WithoutManager_ReturnsFalse()
    {
        bool assigned = GamaPreviewPlayModeGuard.TryAssignPreparedSpeciesOverrideContext(
            asset,
            string.Empty,
            string.Empty);

        Assert.That(assigned, Is.False);
        Assert.That(GamaSpeciesAppearanceStateStore.ActiveContext.IsValid, Is.False);
    }
}
