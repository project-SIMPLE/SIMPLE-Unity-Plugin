using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class GamaEditorPreviewOverrideApplier
{
    private const string PreviewRootName = "[GAMA] Static Experiment Preview";
    private const string VisualChildName = "Visual";
    private const float ActiveSpreadReferenceOverflowWarnRatio = 0.12f;
    private const float ActiveSpreadEpsilon = 0.0001f;
    private static readonly HashSet<string> MissingAnchorWarnings = new HashSet<string>();
    private static readonly HashSet<string> OverridePickLogKeys = new HashSet<string>();

    [InitializeOnLoadMethod]
    private static void Init()
    {
        GamaSpeciesWizard.OnWizardSettingsChanged += HandleWizardSettingsChanged;
        GamaSpeciesWizard.GetDefaultOverridesAsset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset;
    }

    private static void HandleWizardSettingsChanged()
    {
        ScheduleApplyOverridesToCurrentPreview();
    }

    private static bool isUpdateQueued = false;

    public static void ScheduleApplyOverridesToCurrentPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        if (!isUpdateQueued)
        {
            isUpdateQueued = true;
            EditorApplication.delayCall += () =>
            {
                isUpdateQueued = false;
                ApplyOverridesToCurrentPreview();
            };
        }
    }

    public static void ApplyOverridesToCurrentPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        GameObject root = GameObject.Find(PreviewRootName);
        if (root == null)
        {
            return;
        }

        GamaPreviewSession session = root.GetComponent<GamaPreviewSession>();
        GamaSpeciesRenderOverrides asset = session != null ? session.speciesOverrides : null;
        if (asset == null)
        {
            asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        }

        if (asset == null || asset.entries == null)
        {
            return;
        }

        NormalizePreviewContainerScales(root.transform);

        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        Dictionary<string, List<GamaPreviewObject>> objectsBySpecies =
            new Dictionary<string, List<GamaPreviewObject>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (GamaPreviewObject obj in previewObjects)
        {
            if (obj == null || string.IsNullOrWhiteSpace(obj.speciesName))
            {
                continue;
            }

            if (!objectsBySpecies.TryGetValue(obj.speciesName, out List<GamaPreviewObject> list))
            {
                list = new List<GamaPreviewObject>();
                objectsBySpecies[obj.speciesName] = list;
            }

            list.Add(obj);
        }

        string modelPath = session != null ? session.modelPath ?? string.Empty : string.Empty;
        string experimentName = session != null ? session.experimentName ?? string.Empty : string.Empty;

        int totalUpdated = 0;
        foreach (KeyValuePair<string, List<GamaPreviewObject>> pair in objectsBySpecies)
        {
            string speciesName = pair.Key;
            if (string.IsNullOrWhiteSpace(speciesName))
            {
                continue;
            }

            bool exactContext = session != null;
            if (!asset.TryGetOverride(modelPath, experimentName, speciesName, out GamaSpeciesRenderOverrideEntry entry, exactContext) ||
                entry == null)
            {
                continue;
            }

            string source = exactContext ? "context" : "contextless-fallback";
            if (!exactContext)
            {
                Debug.LogWarning("[GAMA][OVERRIDE][WARN] contextless fallback used species=" + speciesName);
            }

            LogEditorOverridePickOnce(speciesName, modelPath, experimentName, entry, source);

            List<GamaPreviewObject> list = pair.Value;
            if (list == null || list.Count == 0)
            {
                continue;
            }

            int updatedRenderers = 0;
            foreach (GamaPreviewObject obj in list)
            {
                if (obj == null)
                {
                    continue;
                }

                updatedRenderers += ApplySpeciesVisualState(obj, entry, rebuildVisual: true);
            }

            if (updatedRenderers > 0)
            {
                Debug.Log("[GAMA][PREVIEW] Applied prefab visuals species=" + speciesName +
                          " objects=" + list.Count +
                          " renderers=" + updatedRenderers);
                totalUpdated += list.Count;
            }
        }

        RunActivePreviewSpreadDiagnostics(root.transform, "all-overrides");

        if (totalUpdated > 0)
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    public static void ApplyPrefabVisualToPreviewSpecies(string speciesName, GamaSpeciesRenderOverrideEntry entry)
    {
        ApplySpeciesOverrideToCurrentPreview(speciesName, entry, rebuildVisual: true, logAction: "Applied prefab visuals");
    }

    public static void ApplyScaleToPreviewSpecies(string speciesName, GamaSpeciesRenderOverrideEntry entry)
    {
        ApplySpeciesOverrideToCurrentPreview(speciesName, entry, rebuildVisual: false, logAction: null);
    }

    public static void ApplyColorToPreviewSpecies(string speciesName, GamaSpeciesRenderOverrideEntry entry)
    {
        ApplySpeciesOverrideToCurrentPreview(speciesName, entry, rebuildVisual: false, logAction: null);
    }

    public static void ApplyVisibilityToPreviewSpecies(string speciesName, GamaSpeciesRenderOverrideEntry entry)
    {
        ApplySpeciesOverrideToCurrentPreview(speciesName, entry, rebuildVisual: false, logAction: null);
    }

    private static void ApplySpeciesOverrideToCurrentPreview(
        string speciesName,
        GamaSpeciesRenderOverrideEntry entry,
        bool rebuildVisual,
        string logAction)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GameObject root = GameObject.Find(PreviewRootName);
        if (root == null)
        {
            return;
        }

        NormalizePreviewContainerScales(root.transform);

        if (entry == null)
        {
            GamaPreviewSession session = root.GetComponent<GamaPreviewSession>();
            GamaSpeciesRenderOverrides asset = session != null && session.speciesOverrides != null
                ? session.speciesOverrides
                : GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
            string modelPath = session != null ? session.modelPath ?? string.Empty : string.Empty;
            string experimentName = session != null ? session.experimentName ?? string.Empty : string.Empty;
            bool exactContext = session != null;
            if (asset == null ||
                !asset.TryGetOverride(modelPath, experimentName, speciesName, out entry, exactContext) ||
                entry == null)
            {
                return;
            }

            if (!exactContext)
            {
                Debug.LogWarning("[GAMA][OVERRIDE][WARN] contextless fallback used species=" + speciesName);
            }

            LogEditorOverridePickOnce(
                speciesName,
                modelPath,
                experimentName,
                entry,
                exactContext ? "context" : "contextless-fallback");
        }
        else
        {
            LogEditorOverridePickOnce(
                speciesName,
                entry.modelPath ?? string.Empty,
                entry.experimentName ?? string.Empty,
                entry,
                "provided");
        }

        GamaPreviewObject[] all = root.GetComponentsInChildren<GamaPreviewObject>(true);
        int updatedObjects = 0;
        int updatedRenderers = 0;
        for (int i = 0; i < all.Length; i++)
        {
            GamaPreviewObject obj = all[i];
            if (obj == null || !string.Equals(obj.speciesName, speciesName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            updatedRenderers += ApplySpeciesVisualState(obj, entry, rebuildVisual);
            updatedObjects++;
        }

        if (updatedObjects > 0)
        {
            if (!string.IsNullOrWhiteSpace(logAction))
            {
                Debug.Log("[GAMA][PREVIEW] " + logAction + " species=" + speciesName +
                          " objects=" + updatedObjects +
                          " renderers=" + updatedRenderers);
            }

            RunActivePreviewSpreadDiagnostics(root.transform, "species=" + speciesName);
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }

    private static void LogEditorOverridePickOnce(
        string speciesName,
        string modelPath,
        string experimentName,
        GamaSpeciesRenderOverrideEntry entry,
        string source)
    {
        string logKey = GamaSpeciesRenderOverrides.NormalizeKey(modelPath) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(experimentName) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(speciesName) + "|" +
            (source ?? string.Empty);
        if (!OverridePickLogKeys.Add(logKey))
        {
            return;
        }

        string prefab = entry != null && !string.IsNullOrWhiteSpace(entry.prefabResourcePath)
            ? entry.prefabResourcePath
            : (entry != null && entry.prefabOverride != null ? entry.prefabOverride.name : "none");
        Debug.Log("[GAMA][EDITOR][OVERRIDE_PICK] species=" + speciesName +
                  " model=" + (modelPath ?? string.Empty) +
                  " experiment=" + (experimentName ?? string.Empty) +
                  " prefab=" + prefab +
                  " scale=" + (entry != null ? entry.GetEffectiveScaleMultiplier() : 1f) +
                  " source=" + source);
    }

    private static int ApplySpeciesVisualState(
        GamaPreviewObject previewObj,
        GamaSpeciesRenderOverrideEntry entry,
        bool rebuildVisual)
    {
        if (previewObj == null || entry == null)
        {
            return 0;
        }

        Transform parent = previewObj.transform;
        bool visible = ResolvePreviewVisible(entry);
        bool hasPrefabOverride = entry.prefabOverride != null;
        previewObj.gameObject.SetActive(true);
        EnsureStableScalePivot(previewObj);

        if (hasPrefabOverride)
        {
            previewObj.RestoreBaseLocalScaleIfCaptured();
            Transform visual = parent.Find(VisualChildName);
            if (rebuildVisual || visual == null || PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject) != entry.prefabOverride)
            {
                visual = EnsurePrefabVisual(parent, entry.prefabOverride);
            }

            if (visual == null)
            {
                return 0;
            }

            ApplyVisualTransform(previewObj, visual, entry);
            int updated = SetOriginalGeometryRenderersEnabled(parent, visual, false);
            updated += SetVisualRenderersState(visual, visible, entry);
            return updated;
        }

        Transform existingVisual = parent.Find(VisualChildName);
        bool meshMissing = IsMeshMissing(previewObj);
        if (meshMissing)
        {
            Transform fallbackVisual = existingVisual;
            bool existingIsPrefab = fallbackVisual != null &&
                                    PrefabUtility.GetCorrespondingObjectFromSource(fallbackVisual.gameObject) != null;
            if (fallbackVisual == null || existingIsPrefab)
            {
                if (fallbackVisual != null)
                {
                    DestroyImmediateSafe(fallbackVisual.gameObject);
                }

                fallbackVisual = CreateFallbackPrimitive(parent, previewObj.speciesName).transform;
            }

            ApplyVisualTransform(previewObj, fallbackVisual, entry);
            int updated = SetOriginalGeometryRenderersEnabled(parent, fallbackVisual, false);
            updated += SetVisualRenderersState(fallbackVisual, visible, entry);
            return updated;
        }

        if (existingVisual != null)
        {
            DestroyImmediateSafe(existingVisual.gameObject);
        }

        previewObj.ApplySpeciesOverride(entry);
        return SetOriginalGeometryRenderersEnabled(parent, null, visible);
    }

    private static void NormalizePreviewContainerScales(Transform root)
    {
        if (root == null)
        {
            return;
        }

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null)
            {
                continue;
            }

            if (t.GetComponent<GamaPreviewObject>() != null)
            {
                continue;
            }

            bool isPreviewRoot = t == root;
            bool isSpeciesParent = t.GetComponent<GamaSpeciesWizard>() != null;
            bool isGamaContainer = string.Equals(t.name, "GAMA", System.StringComparison.Ordinal);
            if (!isPreviewRoot && !isSpeciesParent && !isGamaContainer)
            {
                continue;
            }

            if ((t.localScale - Vector3.one).sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            t.localScale = Vector3.one;
            EditorUtility.SetDirty(t);
            Debug.Log("[GAMA][PREVIEW][SCALE] Reset preview container scale path=" + GetTransformPath(t));
        }
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
        {
            return string.Empty;
        }

        string path = t.name;
        Transform current = t.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static void RunActivePreviewSpreadDiagnostics(Transform root, string reason)
    {
        if (root == null)
        {
            return;
        }

        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        if (previewObjects == null || previewObjects.Length == 0)
        {
            return;
        }

        Dictionary<string, ActiveSpreadProbe> probes =
            new Dictionary<string, ActiveSpreadProbe>(System.StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            GamaPreviewObject previewObject = previewObjects[i];
            if (previewObject == null)
            {
                continue;
            }

            string species = string.IsNullOrWhiteSpace(previewObject.speciesName)
                ? "unknown"
                : previewObject.speciesName.Trim();
            if (!probes.TryGetValue(species, out ActiveSpreadProbe probe) || probe == null)
            {
                probe = new ActiveSpreadProbe(species);
                probes[species] = probe;
            }

            probe.Add(previewObject.transform.position, previewObject.transform.localScale);
            if (HasScaledContainerBetween(previewObject.transform, root))
            {
                probe.ScaledContainerObjectCount++;
            }
        }

        ActiveSpreadProbe reference = ResolveActiveReferenceProbe(probes);
        string referenceName = reference != null ? reference.SpeciesKey : "none";
        foreach (KeyValuePair<string, ActiveSpreadProbe> pair in probes)
        {
            ActiveSpreadProbe probe = pair.Value;
            if (probe == null || !probe.HasBounds)
            {
                continue;
            }

            float diagonal = probe.DiagonalXZ;
            float referenceOverflow = reference != null && reference != probe && reference.HasBounds
                ? ComputeReferenceOverflowRatio(probe.Bounds, reference.Bounds)
                : 0f;
            bool parentScaled = probe.ScaledContainerObjectCount > 0;
            bool outsideReference = reference != null &&
                reference != probe &&
                referenceOverflow > ActiveSpreadReferenceOverflowWarnRatio &&
                probe.Count > 1;

            string line = "[GAMA][PREVIEW][SPREAD][ACTIVE] reason=" + (reason ?? string.Empty) +
                          " species=" + probe.SpeciesKey +
                          " count=" + probe.Count +
                          " actualXZ=" + FormatFloat(diagonal) +
                          " reference=" + referenceName +
                          " referenceOverflow=" + FormatFloat(referenceOverflow) +
                          " scaleRange=" + FormatFloat(probe.MinObservedScale) + ".." + FormatFloat(probe.MaxObservedScale) +
                          " scaledContainerObjects=" + probe.ScaledContainerObjectCount;
            Debug.Log(line);

            if (outsideReference || parentScaled)
            {
                Debug.LogWarning("[GAMA][PREVIEW][SPREAD][ACTIVE][WARN] species=" + probe.SpeciesKey +
                                 " outsideReference=" + outsideReference +
                                 " parentScaled=" + parentScaled +
                                 " details={" + line + "}");
            }
        }
    }

    private static bool HasScaledContainerBetween(Transform leaf, Transform root)
    {
        Transform current = leaf != null ? leaf.parent : null;
        while (current != null)
        {
            if ((current.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                return true;
            }

            if (current == root)
            {
                return false;
            }

            current = current.parent;
        }

        return false;
    }

    private static ActiveSpreadProbe ResolveActiveReferenceProbe(Dictionary<string, ActiveSpreadProbe> probes)
    {
        ActiveSpreadProbe bestNamed = null;
        ActiveSpreadProbe bestCount = null;
        foreach (KeyValuePair<string, ActiveSpreadProbe> pair in probes)
        {
            ActiveSpreadProbe probe = pair.Value;
            if (probe == null || !probe.HasBounds || probe.Count <= 0)
            {
                continue;
            }

            if (IsReferenceSpeciesName(probe.SpeciesKey) &&
                (bestNamed == null || probe.Count > bestNamed.Count))
            {
                bestNamed = probe;
            }

            if (bestCount == null || probe.Count > bestCount.Count)
            {
                bestCount = probe;
            }
        }

        return bestNamed ?? bestCount;
    }

    private static bool IsReferenceSpeciesName(string speciesKey)
    {
        if (string.IsNullOrWhiteSpace(speciesKey))
        {
            return false;
        }

        string lower = speciesKey.ToLowerInvariant();
        return lower.Contains("vegetation") ||
               lower.Contains("cell") ||
               lower.Contains("terrain") ||
               lower.Contains("ground") ||
               lower.Contains("grid") ||
               lower.Contains("patch") ||
               lower.Contains("field") ||
               lower.Contains("zone");
    }

    private static float ComputeReferenceOverflowRatio(Bounds candidate, Bounds reference)
    {
        float overflow = 0f;
        overflow = Mathf.Max(overflow, reference.min.x - candidate.min.x);
        overflow = Mathf.Max(overflow, candidate.max.x - reference.max.x);
        overflow = Mathf.Max(overflow, reference.min.z - candidate.min.z);
        overflow = Mathf.Max(overflow, candidate.max.z - reference.max.z);
        float referenceDiag = BoundsDiagonalXZ(reference);
        return referenceDiag > ActiveSpreadEpsilon ? Mathf.Max(0f, overflow) / referenceDiag : 0f;
    }

    private static float BoundsDiagonalXZ(Bounds bounds)
    {
        Vector3 size = bounds.size;
        return Mathf.Sqrt(size.x * size.x + size.z * size.z);
    }

    private static string FormatFloat(float value)
    {
        if (float.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        if (float.IsNaN(value))
        {
            return "nan";
        }

        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class ActiveSpreadProbe
    {
        public readonly string SpeciesKey;
        public int Count;
        public Bounds Bounds;
        public bool HasBounds;
        public float MinObservedScale = float.PositiveInfinity;
        public float MaxObservedScale = 0f;
        public int ScaledContainerObjectCount;

        public ActiveSpreadProbe(string speciesKey)
        {
            SpeciesKey = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey;
        }

        public float DiagonalXZ
        {
            get { return HasBounds ? BoundsDiagonalXZ(Bounds) : 0f; }
        }

        public void Add(Vector3 point, Vector3 localScale)
        {
            if (!HasBounds)
            {
                Bounds = new Bounds(point, Vector3.zero);
                HasBounds = true;
            }
            else
            {
                Bounds.Encapsulate(point);
            }

            Count++;
            float scale = Mathf.Max(Mathf.Abs(localScale.x), Mathf.Abs(localScale.y), Mathf.Abs(localScale.z));
            MinObservedScale = Mathf.Min(MinObservedScale, scale);
            MaxObservedScale = Mathf.Max(MaxObservedScale, scale);
        }
    }

    private static void EnsureStableScalePivot(GamaPreviewObject previewObj)
    {
        if (previewObj == null)
        {
            return;
        }

        if (previewObj.NormalizePivotToVisualAnchorForStableScale())
        {
            EditorUtility.SetDirty(previewObj);
            EditorUtility.SetDirty(previewObj.gameObject);
            Debug.Log("[GAMA][PREVIEW][SCALE] Recentered preview pivot for species=" +
                      (string.IsNullOrWhiteSpace(previewObj.speciesName) ? "unknown" : previewObj.speciesName));
        }
    }

    private static Transform EnsurePrefabVisual(Transform parent, GameObject prefab)
    {
        if (parent == null || prefab == null)
        {
            return null;
        }

        Transform existingVisual = parent.Find(VisualChildName);
        if (existingVisual != null)
        {
            GameObject sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(existingVisual.gameObject);
            if (sourcePrefab == prefab)
            {
                return existingVisual;
            }

            DestroyImmediateSafe(existingVisual.gameObject);
        }

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (visual == null)
        {
            visual = UnityEngine.Object.Instantiate(prefab);
        }

        visual.name = VisualChildName;
        visual.transform.SetParent(parent, true);
        return visual.transform;
    }

    private static void ApplyVisualTransform(
        GamaPreviewObject previewObj,
        Transform visual,
        GamaSpeciesRenderOverrideEntry entry)
    {
        if (previewObj == null || visual == null || entry == null)
        {
            return;
        }

        Vector3 worldAnchor = GetPreviewObjectWorldAnchor(previewObj);
        visual.position = worldAnchor + entry.GetEffectivePositionOffset();
        visual.rotation = previewObj.transform.rotation * Quaternion.Euler(entry.GetEffectiveRotationOffsetEuler());
        visual.localScale = Vector3.one * Mathf.Max(0.0001f, entry.GetEffectiveScaleMultiplier());
        EditorUtility.SetDirty(visual);
    }

    private static Vector3 GetPreviewObjectWorldAnchor(GamaPreviewObject previewObj)
    {
        if (previewObj == null)
        {
            return Vector3.zero;
        }

        Vector3 localAnchor;
        if (previewObj.TryGetVisualAnchorLocal(out localAnchor))
        {
            return previewObj.transform.TransformPoint(localAnchor);
        }

        Vector3 worldAnchor;
        if (TryGetRendererWorldAnchor(previewObj.transform, out worldAnchor))
        {
            return worldAnchor;
        }

        if (TryGetMeshWorldAnchor(previewObj.transform, out worldAnchor))
        {
            return worldAnchor;
        }

        if (previewObj.transform.position.sqrMagnitude > 0.000001f)
        {
            return previewObj.transform.position;
        }

        string species = string.IsNullOrWhiteSpace(previewObj.speciesName) ? "unknown" : previewObj.speciesName;
        if (MissingAnchorWarnings.Add(species))
        {
            Debug.LogWarning("[GAMA][PREVIEW] No valid visual anchor found for species=" + species +
                             ". Prefab visuals for that species may be stacked until the preview builder stores coordinates.");
        }

        return previewObj.transform.position;
    }

    private static bool TryGetRendererWorldAnchor(Transform root, out Vector3 anchor)
    {
        anchor = Vector3.zero;
        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || IsUnderVisual(renderer.transform) || renderer.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!hasBounds)
            {
                combined = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        anchor = combined.center;
        return true;
    }

    private static bool TryGetMeshWorldAnchor(Transform root, out Vector3 anchor)
    {
        anchor = Vector3.zero;
        if (root == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null || IsUnderVisual(meshFilter.transform) || mesh.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Vector3 worldCenter = meshFilter.transform.TransformPoint(mesh.bounds.center);
            if (!hasBounds)
            {
                combined = new Bounds(worldCenter, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(worldCenter);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        anchor = combined.center;
        return true;
    }

    private static bool IsUnderVisual(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == VisualChildName)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool ResolvePreviewVisible(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return true;
        }

        return entry.GetEffectivePreviewVisible();
    }

    private static bool IsMeshMissing(GamaPreviewObject previewObj)
    {
        if (previewObj == null)
        {
            return true;
        }

        MeshFilter[] meshFilters = previewObj.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            if (meshFilter != null && !IsUnderVisual(meshFilter.transform) && meshFilter.sharedMesh != null)
            {
                return false;
            }
        }

        return true;
    }

    private static int SetOriginalGeometryRenderersEnabled(Transform parent, Transform visualRoot, bool enabled)
    {
        if (parent == null)
        {
            return 0;
        }

        int count = 0;
        Renderer[] renderers = parent.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (visualRoot != null && (renderer.transform == visualRoot || renderer.transform.IsChildOf(visualRoot)))
            {
                continue;
            }

            renderer.enabled = enabled;
            EditorUtility.SetDirty(renderer);
            count++;
        }

        return count;
    }

    private static int SetVisualRenderersState(
        Transform visualRoot,
        bool visible,
        GamaSpeciesRenderOverrideEntry entry)
    {
        if (visualRoot == null)
        {
            return 0;
        }

        int count = 0;
        Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = visible;
            ApplyRendererColorOverride(renderer, entry != null, entry != null ? entry.color : Color.white);
            EditorUtility.SetDirty(renderer);
            count++;
        }

        return count;
    }

    private static void ApplyRendererColorOverride(Renderer renderer, bool overrideColor, Color color)
    {
        if (renderer == null)
        {
            return;
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        if (!overrideColor)
        {
            block.Clear();
            renderer.SetPropertyBlock(block);
            return;
        }

        block.SetColor("_BaseColor", color);
        block.SetColor("_Color", color);
        renderer.SetPropertyBlock(block);
    }

    private static GameObject CreateFallbackPrimitive(Transform parent, string speciesName)
    {
        PrimitiveType primitiveType = PrimitiveType.Cube;
        if (!string.IsNullOrEmpty(speciesName))
        {
            string lower = speciesName.ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"predator|prey|people|pedestrian|person|walker|car|vehicle|voiture|human|agent"))
            {
                primitiveType = PrimitiveType.Capsule;
            }
        }

        GameObject fallback = GameObject.CreatePrimitive(primitiveType);
        fallback.name = VisualChildName;
        fallback.transform.SetParent(parent, true);
        fallback.transform.localScale = Vector3.one * 0.5f;
        Collider col = fallback.GetComponent<Collider>();
        if (col != null)
        {
            UnityEngine.Object.DestroyImmediate(col);
        }
        return fallback;
    }

    private static void DestroyImmediateSafe(GameObject obj)
    {
        if (obj != null)
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
