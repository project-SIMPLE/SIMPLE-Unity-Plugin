using System;
using NUnit.Framework;
using UnityEngine;

public class GamaPreviewReuseIdentityRegistryTests
{
    private const string PreviewRootName = "[GAMA] Static Experiment Preview";

    private GameObject previewRoot;
    private GamaPreviewSession session;
    private string experimentKey;

    [SetUp]
    public void SetUp()
    {
        previewRoot = new GameObject(PreviewRootName);
        session = previewRoot.AddComponent<GamaPreviewSession>();
        session.modelPath = "C:/gama/models/reuse-test.gaml";
        session.experimentName = "Main";
        session.activeGamaSelection = false;
        Assert.That(session.RefreshStableExperimentKey(), Is.True);
        experimentKey = session.stableExperimentKey;
        session.reuseAuthorizedForPlay = true;
        session.authorizedStableExperimentKey = experimentKey;
        previewRoot.SetActive(false);
    }

    [TearDown]
    public void TearDown()
    {
        if (previewRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(previewRoot);
        }

        GamaPreviewSession[] remainingSessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < remainingSessions.Length; i++)
        {
            GamaPreviewSession remaining = remainingSessions[i];
            if (remaining != null && remaining.gameObject.name == PreviewRootName)
            {
                UnityEngine.Object.DestroyImmediate(remaining.gameObject);
            }
        }
    }

    [Test]
    public void ExperimentIdentity_NormalizesSemanticContextOnly()
    {
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "C:\\gama\\models\\nested\\..\\reuse-test.gaml",
                " MAIN ",
                false,
                "ignored-monitor",
                out string first),
            Is.True);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "c:/gama/models/reuse-test.gaml",
                "main",
                false,
                string.Empty,
                out string second),
            Is.True);

        Assert.That(first, Is.EqualTo(second));
        Assert.That(first, Does.Not.Contain("ignored-monitor"));
        Assert.That(first, Does.Not.Contain("timestamp"));
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "c:/gama/models/reuse-test.gaml",
                "other",
                false,
                string.Empty,
                out string otherExperiment),
            Is.True);
        Assert.That(otherExperiment, Is.Not.EqualTo(first));
    }

    [Test]
    public void ActiveSelection_RequiresMonitorExperimentIdentity()
    {
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "GAMA_ACTIVE_SELECTION",
                "main",
                true,
                string.Empty,
                out _),
            Is.False);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "GAMA_ACTIVE_SELECTION",
                "main",
                true,
                "experiment-42",
                out string first),
            Is.True);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableExperimentKey(
                "GAMA_ACTIVE_SELECTION",
                "main",
                true,
                "experiment-43",
                out string second),
            Is.True);
        Assert.That(second, Is.Not.EqualTo(first));
    }

    [Test]
    public void AgentIdentity_PrefersStableAttributesAndRejectsSyntheticFallbacks()
    {
        WorldJSONInfo world = WorldJSONInfo.CreateFromJSON(
            "{\"attributes\":[{\"id\":\"agent_7\",\"gama_id\":\"G-17\",\"uuid\":\"U-99\"}]}");
        Attributes attributes = world.GetAttributesAt(0);

        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "Wolf",
                "agent_4",
                attributes,
                out string key,
                out string sourceId),
            Is.True);
        Assert.That(sourceId, Is.EqualTo("G-17"));
        Assert.That(key, Is.EqualTo("wolf::G-17"));

        Assert.That(GamaPreviewReuseIdentity.IsSyntheticAgentName("agent_0"), Is.True);
        Assert.That(GamaPreviewReuseIdentity.IsSyntheticAgentName("unknown_agent_i"), Is.True);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "wolf",
                "unknown_agent_12",
                null,
                out _,
                out _),
            Is.False);

        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "wolf",
                "Luna",
                null,
                out string wolfKey,
                out _),
            Is.True);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "fox",
                "Luna",
                null,
                out string foxKey,
                out _),
            Is.True);
        Assert.That(foxKey, Is.Not.EqualTo(wolfKey));

        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "wolf",
                "A",
                null,
                out string upperCaseId,
                out _),
            Is.True);
        Assert.That(
            GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                "WOLF",
                "a",
                null,
                out string lowerCaseId,
                out _),
            Is.True);
        Assert.That(upperCaseId, Is.EqualTo("wolf::A"));
        Assert.That(lowerCaseId, Is.EqualTo("wolf::a"));
        Assert.That(lowerCaseId, Is.Not.EqualTo(upperCaseId));
    }

    [Test]
    public void TryCreate_FindsInactiveRootAndRequiresExactAuthorization()
    {
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);
        Assert.That(registry.PreviewRoot, Is.SameAs(previewRoot));
        registry.Dispose();

        session.authorizedStableExperimentKey = experimentKey + "-mismatch";
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out _), Is.False);

        session.authorizedStableExperimentKey = experimentKey;
        session.stale = true;
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out _), Is.False);
    }

    [Test]
    public void ActiveSession_RequiresExactAuthorizedMonitorId()
    {
        session.modelPath = "GAMA_ACTIVE_SELECTION";
        session.experimentName = "main";
        session.activeGamaSelection = true;
        session.monitorExperimentId = "Monitor-A";
        Assert.That(session.RefreshStableExperimentKey(), Is.True);
        experimentKey = session.stableExperimentKey;
        session.authorizedStableExperimentKey = experimentKey;
        session.authorizedMonitorExperimentId = "monitor-a";

        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out _), Is.False);

        session.authorizedMonitorExperimentId = "Monitor-A";
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);
        registry.Dispose();
    }

    [Test]
    public void DuplicateStableAgentKeys_AreAllRefused()
    {
        AddReusableMarker("wolf::17", "wolf-shape", GamaPreviewRepresentationKind.Geometry);
        AddReusableMarker("wolf::17", "wolf-shape-copy", GamaPreviewRepresentationKind.Geometry);

        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);
        Assert.That(registry.AvailableCount, Is.Zero);
        Assert.That(
            registry.TryTake(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Geometry,
                "geometry:wolf-shape",
                out _),
            Is.False);
        registry.Dispose();
    }

    [Test]
    public void TryTake_RequiresExactPropertyRepresentationAndSignature()
    {
        AddReusableMarker("wolf::17", "wolf-shape", GamaPreviewRepresentationKind.Geometry);
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);

        Assert.That(
            registry.TryTake(
                "wolf::17",
                "other-shape",
                GamaPreviewRepresentationKind.Geometry,
                "geometry:wolf-shape",
                out _),
            Is.False);
        Assert.That(
            registry.TryTake(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Prefab,
                "geometry:wolf-shape",
                out _),
            Is.False);
        Assert.That(
            registry.TryTake(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Geometry,
                "geometry:other-shape",
                out _),
            Is.False);
        Assert.That(
            registry.TryTake(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Geometry,
                "geometry:wolf-shape",
                out GameObject instance),
            Is.True);
        Assert.That(instance, Is.Not.Null);
        registry.Dispose();
    }

    [Test]
    public void TryTake_PrefabRequiresExactResolvedUnityAsset()
    {
        GameObject prefabA = new GameObject("wolf-prefab-a");
        GameObject prefabB = new GameObject("wolf-prefab-b");
        GamaPreviewReuseRegistry registry = null;
        try
        {
            AddReusableMarker(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Prefab,
                prefabA);
            Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out registry), Is.True);

            Assert.That(
                registry.TryTake(
                    "wolf::17",
                    "wolf-shape",
                    GamaPreviewRepresentationKind.Prefab,
                    "resources:wolf-prefab-b",
                    prefabB,
                    out _),
                Is.False);
            Assert.That(
                registry.TryTake(
                    "wolf::17",
                    "wolf-shape",
                    GamaPreviewRepresentationKind.Prefab,
                    "resources:wolf-prefab-a",
                    prefabA,
                    out GameObject instance),
                Is.True);
            Assert.That(instance, Is.Not.Null);
        }
        finally
        {
            registry?.Dispose();
            UnityEngine.Object.DestroyImmediate(prefabA);
            UnityEngine.Object.DestroyImmediate(prefabB);
        }
    }

    [Test]
    public void TryTake_RequiresOrdinalExactAgentIdCase()
    {
        AddReusableMarker("wolf::A", "wolf-shape", GamaPreviewRepresentationKind.Geometry);
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);

        try
        {
            Assert.That(
                registry.TryTake(
                    "wolf::a",
                    "wolf-shape",
                    GamaPreviewRepresentationKind.Geometry,
                    "geometry:wolf-shape",
                    out _),
                Is.False);
            Assert.That(
                registry.TryTake(
                    "wolf::A",
                    "wolf-shape",
                    GamaPreviewRepresentationKind.Geometry,
                    "geometry:wolf-shape",
                    out GameObject instance),
                Is.True);
            Assert.That(instance, Is.Not.Null);
        }
        finally
        {
            registry.Dispose();
        }
    }

    [Test]
    public void Release_RestoresHierarchyTransformActiveAndRendererState_ThenRetakesSameInstance()
    {
        GameObject before = new GameObject("before");
        before.transform.SetParent(previewRoot.transform, false);
        GamaPreviewObject marker = AddReusableMarker(
            "wolf::17",
            "wolf-shape",
            GamaPreviewRepresentationKind.Geometry);
        GameObject after = new GameObject("after");
        after.transform.SetParent(previewRoot.transform, false);

        GameObject instance = marker.gameObject;
        Transform originalParent = instance.transform.parent;
        int originalSibling = instance.transform.GetSiblingIndex();
        Vector3 originalPosition = new Vector3(1f, 2f, 3f);
        Quaternion originalRotation = Quaternion.Euler(10f, 20f, 30f);
        Vector3 originalScale = new Vector3(2f, 3f, 4f);
        instance.transform.localPosition = originalPosition;
        instance.transform.localRotation = originalRotation;
        instance.transform.localScale = originalScale;

        Renderer renderer = instance.GetComponent<Renderer>();
        renderer.enabled = false;
        int propertyId = Shader.PropertyToID("_GamaReuseBaseline");
        MaterialPropertyBlock baselineBlock = new MaterialPropertyBlock();
        baselineBlock.SetFloat(propertyId, 7f);
        renderer.SetPropertyBlock(baselineBlock);
        MeshFilter meshFilter = instance.GetComponent<MeshFilter>();
        Mesh originalMesh = meshFilter.sharedMesh;

        GameObject nested = new GameObject("nested");
        nested.transform.SetParent(instance.transform, false);
        nested.SetActive(false);

        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var registry), Is.True);
        Assert.That(
            registry.TryTake(
                "wolf::17",
                "wolf-shape",
                GamaPreviewRepresentationKind.Geometry,
                "geometry:wolf-shape",
                out GameObject firstTake),
            Is.True);
        int originalInstanceId = firstTake.GetInstanceID();

        GameObject runtimeRoot = new GameObject("runtime-root");
        Mesh runtimeMesh = new Mesh { name = "runtime-only-mesh" };
        try
        {
            instance.name = "runtime-name";
            firstTake.transform.SetParent(runtimeRoot.transform, false);
            firstTake.transform.localPosition = Vector3.one * 99f;
            firstTake.transform.localRotation = Quaternion.identity;
            firstTake.transform.localScale = Vector3.one * 9f;
            firstTake.SetActive(false);
            nested.SetActive(true);
            renderer.enabled = true;
            MaterialPropertyBlock runtimeBlock = new MaterialPropertyBlock();
            runtimeBlock.SetFloat(propertyId, 99f);
            renderer.SetPropertyBlock(runtimeBlock);
            meshFilter.sharedMesh = runtimeMesh;
            firstTake.AddComponent<GamaRuntimePrefabSignature>();
            GameObject runtimeVisual = new GameObject("VisualOverride");
            runtimeVisual.transform.SetParent(firstTake.transform, false);

            Assert.That(registry.Release("wolf::17"), Is.True);
            Assert.That(instance.name, Is.EqualTo("wolf::17"));
            Assert.That(instance.transform.parent, Is.SameAs(originalParent));
            Assert.That(instance.transform.GetSiblingIndex(), Is.EqualTo(originalSibling));
            Assert.That(instance.transform.localPosition, Is.EqualTo(originalPosition));
            Assert.That(Quaternion.Angle(instance.transform.localRotation, originalRotation), Is.LessThan(0.001f));
            Assert.That(instance.transform.localScale, Is.EqualTo(originalScale));
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(nested.activeSelf, Is.False);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(meshFilter.sharedMesh, Is.SameAs(originalMesh));
            Assert.That(firstTake.GetComponent<GamaRuntimePrefabSignature>(), Is.Null);
            Assert.That(firstTake.transform.Find("VisualOverride"), Is.Null);
            MaterialPropertyBlock restoredBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(restoredBlock);
            Assert.That(restoredBlock.GetFloat(propertyId), Is.EqualTo(7f));

            Assert.That(
                registry.TryTake(
                    "wolf::17",
                    "wolf-shape",
                    GamaPreviewRepresentationKind.Geometry,
                    "geometry:wolf-shape",
                    out GameObject secondTake),
                Is.True);
            Assert.That(secondTake.GetInstanceID(), Is.EqualTo(originalInstanceId));
            registry.RestoreAll();
            registry.RestoreAll();
            Assert.That(registry.ClaimedCount, Is.Zero);
            Assert.That(instance.transform.parent, Is.SameAs(originalParent));
        }
        finally
        {
            registry.Dispose();
            UnityEngine.Object.DestroyImmediate(runtimeMesh);
            UnityEngine.Object.DestroyImmediate(runtimeRoot);
        }
    }

    [Test]
    public void GlobalClaim_PreventsTwoRegistriesFromTakingSameMarker()
    {
        AddReusableMarker("wolf::17", "wolf-shape", GamaPreviewRepresentationKind.Geometry);
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var first), Is.True);
        Assert.That(GamaPreviewReuseRegistry.TryCreate(experimentKey, out var second), Is.True);

        try
        {
            Assert.That(TryTakeWolf(first, out GameObject firstInstance), Is.True);
            Assert.That(TryTakeWolf(second, out _), Is.False);
            Assert.That(first.Release("wolf::17"), Is.True);
            Assert.That(TryTakeWolf(second, out GameObject secondInstance), Is.True);
            Assert.That(secondInstance, Is.SameAs(firstInstance));
        }
        finally
        {
            first.Dispose();
            second.Dispose();
        }
    }

    private GamaPreviewObject AddReusableMarker(
        string stableAgentKey,
        string propertyId,
        GamaPreviewRepresentationKind kind,
        GameObject sourcePrefabAsset = null)
    {
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        instance.name = stableAgentKey;
        instance.transform.SetParent(previewRoot.transform, false);
        GamaPreviewObject marker = instance.AddComponent<GamaPreviewObject>();
        marker.previewOnly = true;
        marker.canBeReusedAtRuntime = true;
        marker.provenance = GamaPreviewProvenance.CapturedJson;
        marker.representationKind = kind;
        marker.stableAgentKey = stableAgentKey;
        marker.sourcePropertyId = propertyId;
        marker.sourcePrefabSignature = kind == GamaPreviewRepresentationKind.Prefab
            ? "prefab:" + propertyId + ":wolf-prefab"
            : "geometry:" + propertyId;
        marker.sourcePrefabAsset = sourcePrefabAsset;
        return marker;
    }

    private static bool TryTakeWolf(GamaPreviewReuseRegistry registry, out GameObject instance)
    {
        return registry.TryTake(
            "wolf::17",
            "wolf-shape",
            GamaPreviewRepresentationKind.Geometry,
            "geometry:wolf-shape",
            out instance);
    }
}
