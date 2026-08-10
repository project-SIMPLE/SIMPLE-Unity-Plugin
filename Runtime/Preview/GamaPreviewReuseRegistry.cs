using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Claims preview instances for a single authorized Play session and restores
/// their editor-preview hierarchy and rendering state when they are released.
/// </summary>
public sealed class GamaPreviewReuseRegistry : IDisposable
{
    private const string PreviewRootName = "[GAMA] Static Experiment Preview";

    private sealed class ClaimRecord
    {
        public WeakReference Owner;
        public WeakReference Instance;
    }

    private sealed class RendererSnapshot
    {
        public Renderer Renderer;
        public Material[] SharedMaterials;
        public bool Enabled;
        public ShadowCastingMode ShadowCastingMode;
        public bool ReceiveShadows;
        public bool HadRendererPropertyBlock;
        public MaterialPropertyBlock RendererPropertyBlock;
        public bool[] HadMaterialPropertyBlocks;
        public MaterialPropertyBlock[] MaterialPropertyBlocks;

        public static RendererSnapshot Capture(Renderer renderer)
        {
            RendererSnapshot snapshot = new RendererSnapshot
            {
                Renderer = renderer,
                SharedMaterials = renderer.sharedMaterials != null
                    ? (Material[])renderer.sharedMaterials.Clone()
                    : new Material[0],
                Enabled = renderer.enabled,
                ShadowCastingMode = renderer.shadowCastingMode,
                ReceiveShadows = renderer.receiveShadows,
                RendererPropertyBlock = new MaterialPropertyBlock()
            };

            renderer.GetPropertyBlock(snapshot.RendererPropertyBlock);
            snapshot.HadRendererPropertyBlock = !snapshot.RendererPropertyBlock.isEmpty;

            int materialCount = snapshot.SharedMaterials.Length;
            snapshot.HadMaterialPropertyBlocks = new bool[materialCount];
            snapshot.MaterialPropertyBlocks = new MaterialPropertyBlock[materialCount];
            for (int i = 0; i < materialCount; i++)
            {
                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, i);
                snapshot.HadMaterialPropertyBlocks[i] = !block.isEmpty;
                snapshot.MaterialPropertyBlocks[i] = block;
            }

            return snapshot;
        }

        public void Restore()
        {
            if (Renderer == null)
            {
                return;
            }

            Renderer.sharedMaterials = SharedMaterials != null
                ? (Material[])SharedMaterials.Clone()
                : new Material[0];
            Renderer.SetPropertyBlock(HadRendererPropertyBlock ? RendererPropertyBlock : null);

            int materialCount = Renderer.sharedMaterials != null
                ? Renderer.sharedMaterials.Length
                : 0;
            for (int i = 0; i < materialCount; i++)
            {
                bool hasBlock = HadMaterialPropertyBlocks != null &&
                                i < HadMaterialPropertyBlocks.Length &&
                                HadMaterialPropertyBlocks[i];
                MaterialPropertyBlock block = hasBlock &&
                                              MaterialPropertyBlocks != null &&
                                              i < MaterialPropertyBlocks.Length
                    ? MaterialPropertyBlocks[i]
                    : null;
                Renderer.SetPropertyBlock(block, i);
            }

            Renderer.enabled = Enabled;
            Renderer.shadowCastingMode = ShadowCastingMode;
            Renderer.receiveShadows = ReceiveShadows;
        }
    }

    private sealed class ActiveStateSnapshot
    {
        public GameObject GameObject;
        public bool ActiveSelf;
    }

    private sealed class MeshFilterSnapshot
    {
        public MeshFilter MeshFilter;
        public Mesh SharedMesh;
    }

    private sealed class SkinnedMeshSnapshot
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh SharedMesh;
    }

    private sealed class Candidate
    {
        public string AgentKey;
        public string PropertyId;
        public string PrefabSignature;
        public GameObject PrefabAsset;
        public GamaPreviewRepresentationKind Kind;
        public GamaPreviewObject Marker;
        public Transform OriginalParent;
        public int OriginalSiblingIndex;
        public Vector3 OriginalLocalPosition;
        public Quaternion OriginalLocalRotation;
        public Vector3 OriginalLocalScale;
        public bool OriginalActiveSelf;
        public string OriginalName;
        public string OriginalTag;
        public int OriginalLayer;
        public ActiveStateSnapshot[] ActiveStates;
        public RendererSnapshot[] Renderers;
        public MeshFilterSnapshot[] MeshFilters;
        public SkinnedMeshSnapshot[] SkinnedMeshes;
        public HashSet<GamaUnityObjectId> OriginalGameObjectIds;
        public HashSet<GamaUnityObjectId> OriginalComponentIds;
        public bool Claimed;

        public GameObject Instance
        {
            get { return Marker != null ? Marker.gameObject : null; }
        }

        public static Candidate Capture(GamaPreviewObject marker)
        {
            Transform markerTransform = marker.transform;
            Transform[] transforms = marker.GetComponentsInChildren<Transform>(true);
            ActiveStateSnapshot[] activeStates = new ActiveStateSnapshot[transforms.Length];
            HashSet<GamaUnityObjectId> originalGameObjectIds = new HashSet<GamaUnityObjectId>();
            HashSet<GamaUnityObjectId> originalComponentIds = new HashSet<GamaUnityObjectId>();
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject gameObject = transforms[i].gameObject;
                originalGameObjectIds.Add(gameObject.GetGamaObjectId());
                Component[] components = gameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component != null)
                    {
                        originalComponentIds.Add(component.GetGamaObjectId());
                    }
                }
                activeStates[i] = new ActiveStateSnapshot
                {
                    GameObject = gameObject,
                    ActiveSelf = gameObject.activeSelf
                };
            }

            Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
            RendererSnapshot[] rendererSnapshots = new RendererSnapshot[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                rendererSnapshots[i] = RendererSnapshot.Capture(renderers[i]);
            }

            MeshFilter[] meshFilters = marker.GetComponentsInChildren<MeshFilter>(true);
            MeshFilterSnapshot[] meshFilterSnapshots = new MeshFilterSnapshot[meshFilters.Length];
            for (int i = 0; i < meshFilters.Length; i++)
            {
                meshFilterSnapshots[i] = new MeshFilterSnapshot
                {
                    MeshFilter = meshFilters[i],
                    SharedMesh = meshFilters[i].sharedMesh
                };
            }

            SkinnedMeshRenderer[] skinnedRenderers =
                marker.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshSnapshot[] skinnedMeshSnapshots =
                new SkinnedMeshSnapshot[skinnedRenderers.Length];
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                skinnedMeshSnapshots[i] = new SkinnedMeshSnapshot
                {
                    Renderer = skinnedRenderers[i],
                    SharedMesh = skinnedRenderers[i].sharedMesh
                };
            }

            return new Candidate
            {
                AgentKey = NormalizeAgentKey(marker.stableAgentKey),
                PropertyId = NormalizeCompatibilityValue(marker.sourcePropertyId),
                PrefabSignature = NormalizeCompatibilityValue(marker.sourcePrefabSignature),
                PrefabAsset = marker.sourcePrefabAsset,
                Kind = marker.representationKind,
                Marker = marker,
                OriginalParent = markerTransform.parent,
                OriginalSiblingIndex = markerTransform.GetSiblingIndex(),
                OriginalLocalPosition = markerTransform.localPosition,
                OriginalLocalRotation = markerTransform.localRotation,
                OriginalLocalScale = markerTransform.localScale,
                OriginalActiveSelf = marker.gameObject.activeSelf,
                OriginalName = marker.gameObject.name,
                OriginalTag = marker.gameObject.tag,
                OriginalLayer = marker.gameObject.layer,
                ActiveStates = activeStates,
                Renderers = rendererSnapshots,
                MeshFilters = meshFilterSnapshots,
                SkinnedMeshes = skinnedMeshSnapshots,
                OriginalGameObjectIds = originalGameObjectIds,
                OriginalComponentIds = originalComponentIds
            };
        }

        public bool IsCompatible(
            string propertyId,
            GamaPreviewRepresentationKind kind,
            string prefabSignature,
            GameObject prefabAsset)
        {
            if (kind == GamaPreviewRepresentationKind.Unknown ||
                Kind != kind ||
                string.IsNullOrEmpty(PropertyId) ||
                !string.Equals(
                    PropertyId,
                    NormalizeCompatibilityValue(propertyId),
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (kind == GamaPreviewRepresentationKind.Prefab)
            {
                // Compare the actual resolved Unity asset, not a property/path
                // hint. The same property can resolve to different prefabs via
                // runtime bindings, translations, or agent attributes.
                return PrefabAsset != null &&
                       prefabAsset != null &&
                       PrefabAsset == prefabAsset &&
                       !string.IsNullOrWhiteSpace(prefabSignature);
            }

            return !string.IsNullOrEmpty(PrefabSignature) &&
                   string.Equals(
                       PrefabSignature,
                       NormalizeCompatibilityValue(prefabSignature),
                       StringComparison.Ordinal);
        }

        public void Restore()
        {
            GameObject instance = Instance;
            if (instance == null)
            {
                return;
            }

            Transform instanceTransform = instance.transform;
            instanceTransform.SetParent(OriginalParent, false);
            instanceTransform.SetSiblingIndex(OriginalSiblingIndex);
            instanceTransform.localPosition = OriginalLocalPosition;
            instanceTransform.localRotation = OriginalLocalRotation;
            instanceTransform.localScale = OriginalLocalScale;
            instance.name = OriginalName;
            instance.tag = OriginalTag;
            instance.layer = OriginalLayer;

            if (MeshFilters != null)
            {
                for (int i = 0; i < MeshFilters.Length; i++)
                {
                    MeshFilterSnapshot mesh = MeshFilters[i];
                    if (mesh != null && mesh.MeshFilter != null)
                    {
                        mesh.MeshFilter.sharedMesh = mesh.SharedMesh;
                    }
                }
            }

            if (SkinnedMeshes != null)
            {
                for (int i = 0; i < SkinnedMeshes.Length; i++)
                {
                    SkinnedMeshSnapshot mesh = SkinnedMeshes[i];
                    if (mesh != null && mesh.Renderer != null)
                    {
                        mesh.Renderer.sharedMesh = mesh.SharedMesh;
                    }
                }
            }

            if (Renderers != null)
            {
                for (int i = 0; i < Renderers.Length; i++)
                {
                    RendererSnapshot renderer = Renderers[i];
                    if (renderer != null)
                    {
                        renderer.Restore();
                    }
                }
            }

            RemoveRuntimeOnlyComponents(instance, OriginalGameObjectIds, OriginalComponentIds);
            RemoveRuntimeOnlyChildren(instance, OriginalGameObjectIds);

            if (ActiveStates != null)
            {
                for (int i = 0; i < ActiveStates.Length; i++)
                {
                    ActiveStateSnapshot state = ActiveStates[i];
                    if (state == null || state.GameObject == null || state.GameObject == instance)
                    {
                        continue;
                    }

                    if (state.GameObject.activeSelf != state.ActiveSelf)
                    {
                        state.GameObject.SetActive(state.ActiveSelf);
                    }
                }
            }

            if (instance.activeSelf != OriginalActiveSelf)
            {
                instance.SetActive(OriginalActiveSelf);
            }
        }

        private static void RemoveRuntimeOnlyComponents(
            GameObject instance,
            HashSet<GamaUnityObjectId> originalGameObjectIds,
            HashSet<GamaUnityObjectId> originalComponentIds)
        {
            if (instance == null || originalGameObjectIds == null || originalComponentIds == null)
            {
                return;
            }

            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                GameObject gameObject = transforms[i].gameObject;
                if (!originalGameObjectIds.Contains(gameObject.GetGamaObjectId()))
                {
                    continue;
                }

                Component[] components = gameObject.GetComponents<Component>();
                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null ||
                        component is Transform ||
                        originalComponentIds.Contains(component.GetGamaObjectId()))
                    {
                        continue;
                    }

                    DisableRuntimeComponent(component);
                    DestroyRuntimeMutation(component);
                }
            }
        }

        private static void RemoveRuntimeOnlyChildren(
            GameObject instance,
            HashSet<GamaUnityObjectId> originalGameObjectIds)
        {
            if (instance == null || originalGameObjectIds == null)
            {
                return;
            }

            Transform[] transforms = instance.GetComponentsInChildren<Transform>(true);
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                Transform transform = transforms[i];
                if (transform == null ||
                    transform == instance.transform ||
                    originalGameObjectIds.Contains(transform.gameObject.GetGamaObjectId()))
                {
                    continue;
                }

                Transform parent = transform.parent;
                if (parent != null &&
                    !originalGameObjectIds.Contains(parent.gameObject.GetGamaObjectId()))
                {
                    // The highest runtime-only ancestor owns this whole subtree.
                    continue;
                }

                GameObject runtimeOnly = transform.gameObject;
                runtimeOnly.SetActive(false);
                transform.SetParent(null, false);
                DestroyRuntimeMutation(runtimeOnly);
            }
        }

        private static void DisableRuntimeComponent(Component component)
        {
            Behaviour behaviour = component as Behaviour;
            if (behaviour != null)
            {
                behaviour.enabled = false;
                return;
            }

            Collider collider = component as Collider;
            if (collider != null)
            {
                collider.enabled = false;
                return;
            }

            Renderer renderer = component as Renderer;
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }

        private static void DestroyRuntimeMutation(UnityEngine.Object mutation)
        {
            if (mutation == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mutation);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mutation);
            }
        }
    }

    private static readonly object ClaimsLock = new object();
    private static readonly Dictionary<GamaUnityObjectId, ClaimRecord> GlobalClaims =
        new Dictionary<GamaUnityObjectId, ClaimRecord>();

    private readonly Dictionary<string, Candidate> candidatesByAgentKey =
        new Dictionary<string, Candidate>(StringComparer.Ordinal);
    private readonly Dictionary<string, Candidate> claimedByAgentKey =
        new Dictionary<string, Candidate>(StringComparer.Ordinal);
    private readonly GameObject previewRoot;
    private bool disposed;

    private GamaPreviewReuseRegistry(GameObject previewRoot)
    {
        this.previewRoot = previewRoot;
        BuildCandidateIndex(previewRoot);
    }

    public GameObject PreviewRoot
    {
        get { return previewRoot; }
    }

    public int AvailableCount
    {
        get { return candidatesByAgentKey.Count - claimedByAgentKey.Count; }
    }

    public int ClaimedCount
    {
        get { return claimedByAgentKey.Count; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetGlobalClaims()
    {
        lock (ClaimsLock)
        {
            GlobalClaims.Clear();
        }
    }

    public static bool TryCreate(
        string expectedExperimentKey,
        out GamaPreviewReuseRegistry registry)
    {
        registry = null;
        if (string.IsNullOrWhiteSpace(expectedExperimentKey))
        {
            return false;
        }

        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        GamaPreviewSession matchingSession = null;
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (!IsAuthorizedSession(session, expectedExperimentKey))
            {
                continue;
            }

            // Ambiguous roots must never share a live-data stream.
            if (matchingSession != null)
            {
                return false;
            }

            matchingSession = session;
        }

        if (matchingSession == null)
        {
            return false;
        }

        registry = new GamaPreviewReuseRegistry(matchingSession.gameObject);
        return true;
    }

    public bool TryTake(
        string agentKey,
        string propertyId,
        GamaPreviewRepresentationKind kind,
        string prefabSignature,
        out GameObject instance)
    {
        return TryTake(
            agentKey,
            propertyId,
            kind,
            prefabSignature,
            null,
            out instance);
    }

    public bool TryTake(
        string agentKey,
        string propertyId,
        GamaPreviewRepresentationKind kind,
        string prefabSignature,
        GameObject prefabAsset,
        out GameObject instance)
    {
        instance = null;
        if (disposed)
        {
            return false;
        }

        string normalizedAgentKey = NormalizeAgentKey(agentKey);
        if (string.IsNullOrEmpty(normalizedAgentKey) ||
            !candidatesByAgentKey.TryGetValue(normalizedAgentKey, out Candidate candidate) ||
            candidate == null ||
            candidate.Claimed ||
            candidate.Instance == null ||
            !candidate.IsCompatible(propertyId, kind, prefabSignature, prefabAsset) ||
            !TryAcquireGlobalClaim(candidate.Instance))
        {
            return false;
        }

        candidate.Claimed = true;
        claimedByAgentKey[normalizedAgentKey] = candidate;
        instance = candidate.Instance;
        return instance != null;
    }

    public bool Release(string agentKey)
    {
        if (disposed)
        {
            return false;
        }

        string normalizedAgentKey = NormalizeAgentKey(agentKey);
        if (!claimedByAgentKey.TryGetValue(normalizedAgentKey, out Candidate candidate) ||
            candidate == null)
        {
            return false;
        }

        try
        {
            RestoreCandidate(candidate);
        }
        finally
        {
            claimedByAgentKey.Remove(normalizedAgentKey);
        }
        return true;
    }

    public void RestoreAll()
    {
        if (claimedByAgentKey.Count == 0)
        {
            return;
        }

        Candidate[] claimed = new Candidate[claimedByAgentKey.Count];
        claimedByAgentKey.Values.CopyTo(claimed, 0);
        for (int i = 0; i < claimed.Length; i++)
        {
            RestoreCandidate(claimed[i]);
        }

        claimedByAgentKey.Clear();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        RestoreAll();
        disposed = true;
    }

    private static bool IsAuthorizedSession(
        GamaPreviewSession session,
        string expectedExperimentKey)
    {
        if (session == null ||
            session.gameObject == null ||
            session.gameObject.name != PreviewRootName ||
            !session.gameObject.scene.IsValid() ||
            session.stale ||
            !session.reuseAuthorizedForPlay ||
            string.IsNullOrEmpty(session.stableExperimentKey) ||
            !string.Equals(session.stableExperimentKey, expectedExperimentKey, StringComparison.Ordinal) ||
            !string.Equals(session.authorizedStableExperimentKey, expectedExperimentKey, StringComparison.Ordinal) ||
            !session.TryGetStableExperimentKey(out string computedKey) ||
            !string.Equals(computedKey, expectedExperimentKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (session.activeGamaSelection)
        {
            if (string.IsNullOrWhiteSpace(session.monitorExperimentId) ||
                !string.Equals(
                    session.monitorExperimentId,
                    session.authorizedMonitorExperimentId,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void BuildCandidateIndex(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        HashSet<string> duplicateKeys = new HashSet<string>(StringComparer.Ordinal);
        GamaPreviewObject[] markers = root.GetComponentsInChildren<GamaPreviewObject>(true);
        for (int i = 0; i < markers.Length; i++)
        {
            GamaPreviewObject marker = markers[i];
            if (marker == null || !marker.IsEligibleForRuntimeReuse)
            {
                continue;
            }

            Candidate candidate = Candidate.Capture(marker);
            if (string.IsNullOrEmpty(candidate.AgentKey) || duplicateKeys.Contains(candidate.AgentKey))
            {
                continue;
            }

            if (candidatesByAgentKey.ContainsKey(candidate.AgentKey))
            {
                candidatesByAgentKey.Remove(candidate.AgentKey);
                duplicateKeys.Add(candidate.AgentKey);
                continue;
            }

            candidatesByAgentKey.Add(candidate.AgentKey, candidate);
        }
    }

    private bool TryAcquireGlobalClaim(GameObject instance)
    {
        GamaUnityObjectId instanceId = instance.GetGamaObjectId();
        lock (ClaimsLock)
        {
            if (GlobalClaims.TryGetValue(instanceId, out ClaimRecord existing))
            {
                object existingInstance = existing.Instance != null ? existing.Instance.Target : null;
                object existingOwner = existing.Owner != null ? existing.Owner.Target : null;
                if (!ReferenceEquals(existingInstance, instance) || existingOwner == null)
                {
                    GlobalClaims.Remove(instanceId);
                }
                else if (!ReferenceEquals(existingOwner, this))
                {
                    return false;
                }
            }

            GlobalClaims[instanceId] = new ClaimRecord
            {
                Owner = new WeakReference(this),
                Instance = new WeakReference(instance)
            };
            return true;
        }
    }

    private void RestoreCandidate(Candidate candidate)
    {
        if (candidate == null)
        {
            return;
        }

        GameObject instance = candidate.Instance;
        try
        {
            candidate.Restore();
        }
        finally
        {
            candidate.Claimed = false;
            ReleaseGlobalClaim(instance);
        }
    }

    private void ReleaseGlobalClaim(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        GamaUnityObjectId instanceId = instance.GetGamaObjectId();
        lock (ClaimsLock)
        {
            if (!GlobalClaims.TryGetValue(instanceId, out ClaimRecord existing))
            {
                return;
            }

            object existingOwner = existing.Owner != null ? existing.Owner.Target : null;
            object existingInstance = existing.Instance != null ? existing.Instance.Target : null;
            if (ReferenceEquals(existingOwner, this) && ReferenceEquals(existingInstance, instance))
            {
                GlobalClaims.Remove(instanceId);
            }
        }
    }

    private static string NormalizeAgentKey(string value)
    {
        // The species portion is already canonicalized by the identity builder;
        // the encoded agent ID remains an opaque, case-sensitive value.
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string NormalizeCompatibilityValue(string value)
    {
        return GamaPreviewReuseIdentity.NormalizeIdentityPart(value);
    }
}
