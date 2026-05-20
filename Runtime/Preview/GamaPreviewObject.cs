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

    public void ApplySpeciesOverride(GamaSpeciesRenderOverrideEntry entry)
    {
        if (!hasBaseState) return;

        if (entry.positionOffset.sqrMagnitude > 0.0001f)
            transform.localPosition = baseLocalPosition + entry.positionOffset;
        else
            transform.localPosition = baseLocalPosition;

        if (entry.rotationOffsetEuler.sqrMagnitude > 0.0001f)
            transform.localRotation = baseLocalRotation * Quaternion.Euler(entry.rotationOffsetEuler);
        else
            transform.localRotation = baseLocalRotation;

        if (Mathf.Abs(entry.scaleMultiplier - 1f) > 0.0001f)
            transform.localScale = baseLocalScale * Mathf.Max(0f, entry.scaleMultiplier);
        else
            transform.localScale = baseLocalScale;

        bool previewVisible = entry.visibleInPreview;
        if (entry.overridePreviewVisibility == false && entry.overrideVisibility)
        {
            previewVisible = entry.visible;
        }

        if (!previewVisible && (entry.overridePreviewVisibility || entry.overrideVisibility))
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
