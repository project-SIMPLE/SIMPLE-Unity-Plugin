using UnityEngine;
using System.Collections.Generic;


[DisallowMultipleComponent]
public class GamaPreviewObject : MonoBehaviour
{
    public bool previewOnly = true;
    public bool canBeReusedAtRuntime = false;
    public string speciesName = string.Empty;
    public string agentId = string.Empty;
    public string geometryHash = string.Empty;
    public int sourceTick = -1;

    [SerializeField, HideInInspector] private bool hasBaseState = false;
    [SerializeField, HideInInspector] private Vector3 baseLocalPosition;
    [SerializeField, HideInInspector] private Quaternion baseLocalRotation;
    [SerializeField, HideInInspector] private Vector3 baseLocalScale;
    [SerializeField, HideInInspector] private bool hasVisualAnchor = false;
    [SerializeField, HideInInspector] private Vector3 visualAnchorLocal = Vector3.zero;

    [System.Serializable]
    private class RendererBaseState
    {
        public Renderer renderer;
        public Material[] sharedMaterials;
    }

    [SerializeField, HideInInspector]
    private List<RendererBaseState> baseRenderers = new List<RendererBaseState>();

    public void CaptureBaseTransformIfNeeded()
    {
        if (!hasBaseState)
        {
            baseLocalPosition = transform.localPosition;
            baseLocalRotation = transform.localRotation;
            baseLocalScale = transform.localScale;

            baseRenderers.Clear();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    RendererBaseState rs = new RendererBaseState();
                    rs.renderer = r;
                    rs.sharedMaterials = r.sharedMaterials != null ? (Material[])r.sharedMaterials.Clone() : new Material[0];
                    baseRenderers.Add(rs);
                }
            }

            hasBaseState = true;
        }
    }

    public void SetVisualAnchorLocal(Vector3 localAnchor)
    {
        visualAnchorLocal = localAnchor;
        hasVisualAnchor = true;
    }

    public bool TryGetVisualAnchorLocal(out Vector3 localAnchor)
    {
        localAnchor = visualAnchorLocal;
        return hasVisualAnchor;
    }

    public bool NormalizePivotToVisualAnchorForStableScale()
    {
        if (!hasBaseState || !hasVisualAnchor || visualAnchorLocal.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>(true);
        List<MeshFilter> editableFilters = new List<MeshFilter>();
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            if (filter == null || IsUnderGeneratedVisual(filter.transform))
            {
                continue;
            }

            Mesh mesh = filter.sharedMesh;
            if (mesh != null && mesh.vertexCount > 0)
            {
                editableFilters.Add(filter);
            }
        }

        if (editableFilters.Count == 0)
        {
            return false;
        }

        transform.localPosition = baseLocalPosition;
        transform.localRotation = baseLocalRotation;
        transform.localScale = baseLocalScale;

        Vector3 worldAnchor = transform.TransformPoint(visualAnchorLocal);
        List<Vector3[]> worldVerticesByFilter = new List<Vector3[]>(editableFilters.Count);
        for (int i = 0; i < editableFilters.Count; i++)
        {
            MeshFilter filter = editableFilters[i];
            Vector3[] vertices = filter.sharedMesh.vertices;
            Vector3[] worldVertices = new Vector3[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                worldVertices[v] = filter.transform.TransformPoint(vertices[v]);
            }

            worldVerticesByFilter.Add(worldVertices);
        }

        transform.position = worldAnchor;

        for (int i = 0; i < editableFilters.Count; i++)
        {
            MeshFilter filter = editableFilters[i];
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] worldVertices = worldVerticesByFilter[i];
            for (int v = 0; v < vertices.Length && v < worldVertices.Length; v++)
            {
                vertices[v] = filter.transform.InverseTransformPoint(worldVertices[v]);
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        baseLocalPosition = transform.localPosition;
        visualAnchorLocal = Vector3.zero;
        return true;
    }

    private static bool IsUnderGeneratedVisual(Transform t)
    {
        Transform current = t;
        while (current != null)
        {
            string name = current.name;
            if (name == "Visual" || name == "VisualOverride" || name == "InvalidGeometryFallback")
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    public void RestoreBaseLocalScaleIfCaptured()
    {
        if (hasBaseState)
        {
            transform.localScale = baseLocalScale;
        }
    }

    public void ApplySpeciesOverride(GamaSpeciesRenderOverrideEntry entry)
    {
        if (!hasBaseState) return;

        if (entry.UsesPositionOffsetOverride())
            transform.localPosition = baseLocalPosition + entry.GetEffectivePositionOffset();
        else
            transform.localPosition = baseLocalPosition;

        if (entry.UsesRotationOffsetOverride())
            transform.localRotation = baseLocalRotation * Quaternion.Euler(entry.GetEffectiveRotationOffsetEuler());
        else
            transform.localRotation = baseLocalRotation;

        if (entry.UsesScaleOverride())
            transform.localScale = baseLocalScale * entry.GetEffectiveScaleMultiplier();
        else
            transform.localScale = baseLocalScale;

        bool previewVisible = entry.GetEffectivePreviewVisible();
        if (!previewVisible && entry.UsesPreviewVisibilityOverride())
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        foreach (RendererBaseState state in baseRenderers)
        {
            Renderer r = state.renderer;
            if (r == null) continue;

            Material[] mats = state.sharedMaterials != null ? (Material[])state.sharedMaterials.Clone() : new Material[0];
            if (entry.materialOverride != null)
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = entry.materialOverride;
                }
            }
            r.sharedMaterials = mats;

            ApplyRendererColorOverride(r, entry.overrideColor, entry.color);

            if (entry.renderMode == GamaSpeciesRenderMode.Hidden)
            {
                r.enabled = false;
            }
            else if (entry.renderMode == GamaSpeciesRenderMode.Wireframe)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = true;
            }
            else
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                r.enabled = true;
            }
        }
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static void ApplyRendererColorOverride(Renderer renderer, bool overrideColor, Color color)
    {
        if (renderer == null) return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);

        if (!overrideColor)
        {
            block.Clear();
            renderer.SetPropertyBlock(block);
            return;
        }

        bool supportsBaseColor = false;
        bool supportsColor = false;
        if (renderer.sharedMaterials != null)
        {
            foreach (Material m in renderer.sharedMaterials)
            {
                if (m != null)
                {
                    if (m.HasProperty(BaseColorId)) supportsBaseColor = true;
                    if (m.HasProperty(ColorId)) supportsColor = true;
                }
            }
        }

        if (supportsBaseColor) block.SetColor(BaseColorId, color);
        if (supportsColor || !supportsBaseColor) block.SetColor(ColorId, color);

        renderer.SetPropertyBlock(block);
    }
}
