using NUnit.Framework;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

public class GamaSpeciesAppearanceStateStoreTests
{
    private GamaSpeciesRenderOverridesAsset asset;

    [SetUp]
    public void SetUp()
    {
        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
        asset = ScriptableObject.CreateInstance<GamaSpeciesRenderOverridesAsset>();
    }

    [TearDown]
    public void TearDown()
    {
        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
        if (asset != null)
        {
            Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void ExactContext_DoesNotLeakBetweenExperiments()
    {
        GamaSpeciesAppearanceContext first = Context("model.gaml", "first");
        GamaSpeciesAppearanceContext second = Context("model.gaml", "second");

        GamaSpeciesRenderOverrideEntry firstEntry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(first, "wolf", false);
        firstEntry.overrideScaleMultiplier = true;
        firstEntry.scaleMultiplier = 2f;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(first, "wolf", false);

        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(second, "wolf", false, out _),
            Is.False);
        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(first, "wolf", false, out var resolved),
            Is.True);
        Assert.That(resolved.GetEffectiveScaleMultiplier(), Is.EqualTo(2f));
    }

    [Test]
    public void ExactContext_NormalizesWindowsSeparatorsAndRelativeSegments()
    {
        GamaSpeciesAppearanceContext first = Context("C:\\models\\nested\\..\\demo.gaml", "MAIN");
        GamaSpeciesAppearanceContext equivalent = Context("c:/models/demo.gaml", "main");

        GamaSpeciesRenderOverrideEntry entry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(first, "wolf", false);
        entry.overrideColor = true;
        entry.color = Color.cyan;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(first, "wolf", false);

        Assert.That(first, Is.EqualTo(equivalent));
        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(equivalent, "wolf", false, out var resolved),
            Is.True);
        Assert.That(resolved, Is.SameAs(entry));

        GamaSpeciesAppearanceContext relative = Context("relative-model.gaml", "main");
        GamaSpeciesAppearanceContext absolute = Context(Path.GetFullPath("relative-model.gaml"), "MAIN");
        Assert.That(relative, Is.EqualTo(absolute));
    }

    [Test]
    public void RuntimeOverlay_IsSharedAndDoesNotMutatePersistentEntry()
    {
        GamaSpeciesAppearanceContext context = Context("model.gaml", "main");
        GamaSpeciesRenderOverrideEntry persisted =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", false);
        persisted.overrideScaleMultiplier = true;
        persisted.scaleMultiplier = 2f;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, "wolf", false);

        GamaSpeciesRenderOverrideEntry firstOverlay =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", true);
        GamaSpeciesRenderOverrideEntry secondOverlay =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", true);
        Assert.That(secondOverlay, Is.SameAs(firstOverlay));

        firstOverlay.scaleMultiplier = 3f;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, "wolf", true);

        Assert.That(persisted.scaleMultiplier, Is.EqualTo(2f));
        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(context, "wolf", true, out var effective),
            Is.True);
        Assert.That(effective.scaleMultiplier, Is.EqualTo(3f));

        GamaSpeciesAppearanceStateStore.ClearRuntimeOverlay();
        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(context, "wolf", true, out effective),
            Is.True);
        Assert.That(effective, Is.SameAs(persisted));
    }

    [Test]
    public void ScaleNormalization_IsAbsoluteAcrossOneTwoOne()
    {
        GamaSpeciesAppearanceContext context = Context("model.gaml", "main");
        GamaSpeciesRenderOverrideEntry entry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", false);

        entry.overrideScaleMultiplier = true;
        entry.scaleMultiplier = 2f;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, "wolf", false);
        Assert.That(entry.GetEffectiveScaleMultiplier(), Is.EqualTo(2f));

        entry.overrideScaleMultiplier = false;
        entry.scaleMultiplier = 2f;
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, "wolf", false);
        Assert.That(entry.scaleMultiplier, Is.EqualTo(1f));
        Assert.That(entry.UsesScaleOverride(), Is.False);
        Assert.That(entry.GetEffectiveScaleMultiplier(), Is.EqualTo(1f));
    }

    [Test]
    public void ClearContext_RemovesOnlyExactPersistentAndTemporaryEntries()
    {
        GamaSpeciesAppearanceContext first = Context("model.gaml", "first");
        GamaSpeciesAppearanceContext second = Context("model.gaml", "second");
        GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(first, "wolf", false);
        GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(first, "wolf", true);
        GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(second, "wolf", false);

        GamaSpeciesAppearanceStateStore.ClearContext(first, true);

        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(first, "wolf", true, out _),
            Is.False);
        Assert.That(
            GamaSpeciesAppearanceStateStore.TryGetEntry(second, "wolf", false, out _),
            Is.True);
    }

    [Test]
    public void NormalizeEntry_MigratesLegacyVisibilityBeforeClearingLegacyFields()
    {
        GamaSpeciesRenderOverrideEntry entry = new GamaSpeciesRenderOverrideEntry
        {
            overrideVisibility = true,
            visible = false,
            overridePreviewVisibility = false,
            overrideRuntimeVisibility = false
        };

        GamaSpeciesAppearanceStateStore.NormalizeEntry(entry);

        Assert.That(entry.overridePreviewVisibility, Is.True);
        Assert.That(entry.visibleInPreview, Is.False);
        Assert.That(entry.overrideRuntimeVisibility, Is.True);
        Assert.That(entry.visibleInRuntime, Is.False);
        Assert.That(entry.overrideVisibility, Is.False);
    }

    [Test]
    public void WizardEdit_PreservesDynamicResourceAndExplicitVisibleOverrides()
    {
        GamaSpeciesAppearanceContext context = Context("model.gaml", "main");
        GamaSpeciesRenderOverrideEntry entry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", false);
        entry.prefabResourcePath = "Animals/Wolf";
        entry.overrideDynamicColor = true;
        entry.dynamicColorMode = GamaDynamicColorMode.Discrete;
        entry.dynamicColorAttribute = "mood";
        entry.discreteColorRules.Add(new GamaDiscreteColorRule { value = "calm", color = Color.blue });
        entry.overridePreviewVisibility = true;
        entry.visibleInPreview = true;
        entry.overrideRuntimeVisibility = true;
        entry.visibleInRuntime = true;

        GameObject go = new GameObject("wizard");
        try
        {
            GamaSpeciesWizard wizard = go.AddComponent<GamaSpeciesWizard>();
            wizard.overridesAsset = asset;
            wizard.PopulateFromEntry(entry);
            wizard.scaleOverrideEnabled = true;
            wizard.scaleMultiplier = 2f;
            wizard.SaveCurrentSettingsToAsset();

            Assert.That(entry.prefabResourcePath, Is.EqualTo("Animals/Wolf"));
            Assert.That(entry.overrideDynamicColor, Is.True);
            Assert.That(entry.dynamicColorMode, Is.EqualTo(GamaDynamicColorMode.Discrete));
            Assert.That(entry.dynamicColorAttribute, Is.EqualTo("mood"));
            Assert.That(entry.discreteColorRules.Count, Is.EqualTo(1));
            Assert.That(entry.overridePreviewVisibility, Is.True);
            Assert.That(entry.visibleInPreview, Is.True);
            Assert.That(entry.overrideScaleMultiplier, Is.True);
            Assert.That(entry.scaleMultiplier, Is.EqualTo(2f));

            wizard.scaleOverrideEnabled = false;
            wizard.previewVisibilityOverrideEnabled = false;
            wizard.visibleInPreview = false;
            wizard.SaveCurrentSettingsToAsset();

            Assert.That(entry.overrideScaleMultiplier, Is.False);
            Assert.That(entry.scaleMultiplier, Is.EqualTo(1f));
            Assert.That(entry.overridePreviewVisibility, Is.False);
            Assert.That(entry.visibleInPreview, Is.True);
            Assert.That(entry.overrideRuntimeVisibility, Is.True);
            Assert.That(entry.visibleInRuntime, Is.True);
            Assert.That(entry.GetEffectiveScaleMultiplier(), Is.EqualTo(1f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void InactivePreview_ContextClearRestoresNeutralBaseline()
    {
        GameObject root = new GameObject("[GAMA] Static Experiment Preview");
        GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            GamaSpeciesAppearanceContext context = Context("model.gaml", "main");
            GamaPreviewSession session = root.AddComponent<GamaPreviewSession>();
            session.speciesOverrides = asset;
            session.modelPath = context.ModelPath;
            session.experimentName = context.ExperimentName;
            session.stale = false;
            child.transform.SetParent(root.transform, false);
            GamaPreviewObject preview = child.AddComponent<GamaPreviewObject>();
            preview.speciesName = "wolf";
            preview.CaptureBaseTransformIfNeeded();

            GamaSpeciesRenderOverrideEntry entry =
                GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(context, "wolf", false);
            entry.overrideScaleMultiplier = true;
            entry.scaleMultiplier = 2f;
            GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, "wolf", false);
            root.SetActive(false);

            GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
            Assert.That(child.transform.localScale, Is.EqualTo(Vector3.one * 2f));

            GamaSpeciesAppearanceStateStore.ClearContext(context, true);
            GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
            Assert.That(child.transform.localScale, Is.EqualTo(Vector3.one));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void PreviewObject_ApplyIsIdempotentAndNeutralRestoresBaseline()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            go.transform.localScale = new Vector3(2f, 3f, 4f);
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.enabled = false;
            int unrelatedProperty = Shader.PropertyToID("_GamaBaselineTest");
            MaterialPropertyBlock baselineBlock = new MaterialPropertyBlock();
            baselineBlock.SetFloat(unrelatedProperty, 17f);
            renderer.SetPropertyBlock(baselineBlock);
            int colorProperty = Shader.PropertyToID("_Color");
            MaterialPropertyBlock indexedBaseline = new MaterialPropertyBlock();
            indexedBaseline.SetFloat(unrelatedProperty, 23f);
            indexedBaseline.SetColor(colorProperty, Color.green);
            renderer.SetPropertyBlock(indexedBaseline, 0);

            GamaPreviewObject preview = go.AddComponent<GamaPreviewObject>();
            preview.CaptureBaseTransformIfNeeded();

            GamaSpeciesRenderOverrideEntry entry = new GamaSpeciesRenderOverrideEntry
            {
                overrideScaleMultiplier = true,
                scaleMultiplier = 2f,
                overridePreviewVisibility = true,
                visibleInPreview = true,
                overrideColor = true,
                color = Color.red
            };

            preview.ApplySpeciesOverride(entry);
            preview.ApplySpeciesOverride(entry);
            Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(4f, 6f, 8f)));
            Assert.That(renderer.enabled, Is.True);

            // Simulate the non-serialized half of a domain reload. Serialized
            // color baselines must still restore indexed MPBs without adopting red.
            FieldInfo statesField = typeof(GamaPreviewObject).GetField(
                "baseRenderers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            IList states = (IList)statesField.GetValue(preview);
            object state = states[0];
            state.GetType().GetField("hasCapturedPropertyBlocks").SetValue(state, false);
            state.GetType().GetField("allowFullPropertyBlockCapture").SetValue(state, false);
            state.GetType().GetField("rendererPropertyBlock").SetValue(state, null);
            state.GetType().GetField("materialPropertyBlocks").SetValue(state, null);

            entry.overrideScaleMultiplier = false;
            entry.overridePreviewVisibility = false;
            entry.overrideColor = false;
            GamaSpeciesAppearanceStateStore.NormalizeEntry(entry);
            preview.ApplySpeciesOverride(entry);

            Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
            Assert.That(renderer.enabled, Is.False);
            MaterialPropertyBlock restored = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(restored);
            Assert.That(restored.GetFloat(unrelatedProperty), Is.EqualTo(17f));
            MaterialPropertyBlock restoredIndexed = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(restoredIndexed, 0);
            Assert.That(restoredIndexed.GetFloat(unrelatedProperty), Is.EqualTo(23f));
            Assert.That(restoredIndexed.GetColor(colorProperty), Is.EqualTo(Color.green));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void RuntimeColorBaseline_RestoresIndexedPropertyBlock()
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            Renderer renderer = go.GetComponent<Renderer>();
            Material material = renderer.sharedMaterial;
            int colorProperty = material != null && material.HasProperty("_BaseColor")
                ? Shader.PropertyToID("_BaseColor")
                : Shader.PropertyToID("_Color");
            int unrelatedProperty = Shader.PropertyToID("_GamaRuntimeBaselineTest");
            MaterialPropertyBlock baseline = new MaterialPropertyBlock();
            baseline.SetColor(colorProperty, Color.green);
            baseline.SetFloat(unrelatedProperty, 31f);
            renderer.SetPropertyBlock(baseline, 0);

            SimulationManager.ChangeColor(go, Color.red);
            MaterialPropertyBlock changed = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(changed, 0);
            Assert.That(changed.GetColor(colorProperty), Is.EqualTo(Color.red));

            GamaRuntimeRendererAppearanceBaseline appearanceBaseline =
                renderer.GetComponent<GamaRuntimeRendererAppearanceBaseline>();
            Assert.That(appearanceBaseline, Is.Not.Null);
            appearanceBaseline.Restore(renderer);

            MaterialPropertyBlock restored = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(restored, 0);
            Assert.That(restored.GetColor(colorProperty), Is.EqualTo(Color.green));
            Assert.That(restored.GetFloat(unrelatedProperty), Is.EqualTo(31f));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    private GamaSpeciesAppearanceContext Context(string modelPath, string experimentName)
    {
        return new GamaSpeciesAppearanceContext(asset, modelPath, experimentName);
    }
}
