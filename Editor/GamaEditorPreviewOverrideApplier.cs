using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class GamaEditorPreviewOverrideApplier
{
    private const string PreviewRootName = "[GAMA] Static Experiment Preview";
    private const string VisualChildName = "Visual";
    private static readonly HashSet<string> MissingAnchorWarnings = new HashSet<string>();

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

        int totalUpdated = 0;
        foreach (GamaSpeciesRenderOverrideEntry entry in asset.entries)
        {
            if (entry == null)
            {
                continue;
            }

            string speciesName = entry.GetSpeciesName();
            if (string.IsNullOrWhiteSpace(speciesName))
            {
                continue;
            }

            if (!objectsBySpecies.TryGetValue(speciesName, out List<GamaPreviewObject> list) || list.Count == 0)
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

        if (entry == null)
        {
            GamaSpeciesRenderOverrides asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
            if (asset == null || !asset.TryGetOverride(speciesName, out entry))
            {
                return;
            }
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

            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
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
        visual.position = worldAnchor;
        visual.rotation = previewObj.transform.rotation;
        visual.localScale = Vector3.one * Mathf.Max(0.0001f, entry.scaleMultiplier);
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

        if (entry.overridePreviewVisibility)
        {
            return entry.visibleInPreview;
        }

        if (entry.overrideVisibility)
        {
            return entry.visible;
        }

        return true;
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
