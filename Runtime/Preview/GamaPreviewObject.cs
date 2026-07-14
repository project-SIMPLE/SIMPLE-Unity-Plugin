using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;


[DisallowMultipleComponent]
public class GamaPreviewObject : MonoBehaviour
{
    private const int CurrentBaseStateVersion = 2;

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
    [SerializeField, HideInInspector] private bool baseActiveSelf = true;
    [SerializeField, HideInInspector] private int baseStateVersion = 0;
    [SerializeField, HideInInspector] private bool hasVisualAnchor = false;
    [SerializeField, HideInInspector] private Vector3 visualAnchorLocal = Vector3.zero;

    [System.Serializable]
    private class RendererBaseState
    {
        public Renderer renderer;
        public Material[] sharedMaterials;
        public bool rendererEnabled;
        public ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
        public int stateVersion;
        public bool hasSerializedColorBaseline;
        public bool rendererSupportsColor;
        public bool rendererSupportsBaseColor;
        public Color rendererColor = Color.white;
        public Color rendererBaseColor = Color.white;
        public bool[] materialSupportsColor;
        public bool[] materialSupportsBaseColor;
        public Color[] materialColors;
        public Color[] materialBaseColors;

        // Unity does not serialize MaterialPropertyBlock. Keep an in-memory copy of
        // every block so applying a color never discards unrelated shader values.
        [System.NonSerialized] public bool hasCapturedPropertyBlocks;
        [System.NonSerialized] public bool hadRendererPropertyBlock;
        [System.NonSerialized] public MaterialPropertyBlock rendererPropertyBlock;
        [System.NonSerialized] public bool[] hadMaterialPropertyBlocks;
        [System.NonSerialized] public MaterialPropertyBlock[] materialPropertyBlocks;
        [System.NonSerialized] public bool allowFullPropertyBlockCapture;
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
            baseActiveSelf = gameObject.activeSelf;
            baseRenderers.Clear();
            baseStateVersion = CurrentBaseStateVersion;
            hasBaseState = true;
        }

        // Existing serialized previews predate the complete renderer baseline.
        // Upgrade their missing fields once without replacing the transform or
        // material baselines that were already captured.
        bool upgradeExistingState = baseStateVersion < CurrentBaseStateVersion;
        if (upgradeExistingState)
        {
            baseActiveSelf = gameObject.activeSelf;
            baseStateVersion = CurrentBaseStateVersion;
        }

        CaptureMissingRendererBaseStates(upgradeExistingState);
    }

    private void CaptureMissingRendererBaseStates(bool upgradeExistingState)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            // A generated custom-prefab replacement owns its own native-scale
            // baseline. It is recreated by the editor applier and must not become
            // part of the imported GAMA preview baseline, otherwise each refresh
            // would retain a dead renderer entry from the previous replacement.
            GamaPreviewBaseline generatedVisualBaseline =
                renderer.GetComponentInParent<GamaPreviewBaseline>(true);
            if (generatedVisualBaseline != null &&
                generatedVisualBaseline.transform != transform &&
                generatedVisualBaseline.transform.IsChildOf(transform) &&
                generatedVisualBaseline.gameObject.name == "Visual")
            {
                continue;
            }

            RendererBaseState state = FindRendererBaseState(renderer);
            if (state == null)
            {
                state = new RendererBaseState
                {
                    renderer = renderer,
                    sharedMaterials = CloneSharedMaterials(renderer),
                    rendererEnabled = renderer.enabled,
                    shadowCastingMode = renderer.shadowCastingMode,
                    receiveShadows = renderer.receiveShadows,
                    stateVersion = CurrentBaseStateVersion
                };
                state.allowFullPropertyBlockCapture = true;
                baseRenderers.Add(state);
            }
            else if (upgradeExistingState || state.stateVersion < CurrentBaseStateVersion)
            {
                if (state.sharedMaterials == null)
                {
                    state.sharedMaterials = CloneSharedMaterials(renderer);
                }

                state.rendererEnabled = renderer.enabled;
                state.shadowCastingMode = renderer.shadowCastingMode;
                state.receiveShadows = renderer.receiveShadows;
                state.stateVersion = CurrentBaseStateVersion;
            }

            CaptureSerializedColorBaselineIfNeeded(
                state,
                state.allowFullPropertyBlockCapture);
            CapturePropertyBlockBaselineIfNeeded(state);
        }
    }

    private RendererBaseState FindRendererBaseState(Renderer renderer)
    {
        for (int i = 0; i < baseRenderers.Count; i++)
        {
            RendererBaseState state = baseRenderers[i];
            if (state != null && state.renderer == renderer)
            {
                return state;
            }
        }

        return null;
    }

    private static Material[] CloneSharedMaterials(Renderer renderer)
    {
        Material[] materials = renderer != null ? renderer.sharedMaterials : null;
        return materials != null ? (Material[])materials.Clone() : new Material[0];
    }

    private static void CapturePropertyBlockBaselineIfNeeded(RendererBaseState state)
    {
        if (state == null ||
            state.renderer == null ||
            state.hasCapturedPropertyBlocks ||
            !state.allowFullPropertyBlockCapture)
        {
            return;
        }

        Renderer renderer = state.renderer;
        state.rendererPropertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(state.rendererPropertyBlock);
        state.hadRendererPropertyBlock = !state.rendererPropertyBlock.isEmpty;

        int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
        state.hadMaterialPropertyBlocks = new bool[materialCount];
        state.materialPropertyBlocks = new MaterialPropertyBlock[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, i);
            state.hadMaterialPropertyBlocks[i] = !block.isEmpty;
            state.materialPropertyBlocks[i] = block;
        }

        state.hasCapturedPropertyBlocks = true;
    }

    private static void CaptureSerializedColorBaselineIfNeeded(
        RendererBaseState state,
        bool includeCurrentPropertyBlocks)
    {
        if (state == null || state.renderer == null || state.hasSerializedColorBaseline)
        {
            return;
        }

        Renderer renderer = state.renderer;
        Material[] materials = state.sharedMaterials ?? renderer.sharedMaterials ?? new Material[0];
        MaterialPropertyBlock rendererBlock = new MaterialPropertyBlock();
        if (includeCurrentPropertyBlocks)
        {
            renderer.GetPropertyBlock(rendererBlock);
        }

        state.rendererSupportsColor = MaterialsSupportProperty(materials, ColorId);
        state.rendererSupportsBaseColor = MaterialsSupportProperty(materials, BaseColorId);
        state.rendererColor = ResolveBaselineColor(
            includeCurrentPropertyBlocks ? rendererBlock : null,
            materials,
            ColorId);
        state.rendererBaseColor = ResolveBaselineColor(
            includeCurrentPropertyBlocks ? rendererBlock : null,
            materials,
            BaseColorId);

        int materialCount = materials.Length;
        state.materialSupportsColor = new bool[materialCount];
        state.materialSupportsBaseColor = new bool[materialCount];
        state.materialColors = new Color[materialCount];
        state.materialBaseColors = new Color[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            Material material = materials[i];
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            if (includeCurrentPropertyBlocks)
            {
                renderer.GetPropertyBlock(block, i);
            }

            Material[] oneMaterial = { material };
            state.materialSupportsColor[i] = material != null && material.HasProperty(ColorId);
            state.materialSupportsBaseColor[i] = material != null && material.HasProperty(BaseColorId);
            state.materialColors[i] = ResolveBaselineColor(
                includeCurrentPropertyBlocks ? block : null,
                oneMaterial,
                ColorId);
            state.materialBaseColors[i] = ResolveBaselineColor(
                includeCurrentPropertyBlocks ? block : null,
                oneMaterial,
                BaseColorId);
        }

        state.hasSerializedColorBaseline = true;
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

    public void RestoreBaseLocalScaleIfCaptured()
    {
        if (hasBaseState)
        {
            transform.localScale = baseLocalScale;
        }
    }

    public void ApplySpeciesOverride(GamaSpeciesRenderOverrideEntry entry)
    {
        CaptureBaseTransformIfNeeded();

        if (entry != null && entry.UsesPositionOffsetOverride())
            transform.localPosition = baseLocalPosition + entry.GetEffectivePositionOffset();
        else
            transform.localPosition = baseLocalPosition;

        if (entry != null && entry.UsesRotationOffsetOverride())
            transform.localRotation = baseLocalRotation * Quaternion.Euler(entry.GetEffectiveRotationOffsetEuler());
        else
            transform.localRotation = baseLocalRotation;

        if (entry != null && entry.UsesScaleOverride())
            transform.localScale = baseLocalScale * entry.GetEffectiveScaleMultiplier();
        else
            transform.localScale = baseLocalScale;

        bool overridesPreviewVisibility = entry != null && entry.UsesPreviewVisibilityOverride();
        bool previewVisible = !overridesPreviewVisibility || entry.GetEffectivePreviewVisible();
        bool targetActiveSelf = overridesPreviewVisibility ? previewVisible : baseActiveSelf;

        foreach (RendererBaseState state in baseRenderers)
        {
            if (state == null) continue;

            Renderer r = state.renderer;
            if (r == null) continue;

            Material[] mats = state.sharedMaterials != null ? (Material[])state.sharedMaterials.Clone() : new Material[0];
            if (entry != null && entry.materialOverride != null)
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = entry.materialOverride;
                }
            }
            r.sharedMaterials = mats;

            RestorePropertyBlockBaseline(state);
            if (entry != null && entry.overrideColor)
            {
                ApplyRendererColorOverride(r, entry.color);
            }

            r.enabled = state.rendererEnabled;
            r.shadowCastingMode = state.shadowCastingMode;
            r.receiveShadows = state.receiveShadows;

            GamaSpeciesRenderMode renderMode = entry != null
                ? entry.renderMode
                : GamaSpeciesRenderMode.Default;

            if (renderMode == GamaSpeciesRenderMode.Hidden)
            {
                r.enabled = false;
            }
            else if (renderMode == GamaSpeciesRenderMode.Wireframe)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            else if (renderMode != GamaSpeciesRenderMode.Default)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }

            if (overridesPreviewVisibility)
            {
                r.enabled = previewVisible && renderMode != GamaSpeciesRenderMode.Hidden;
            }
        }

        if (gameObject.activeSelf != targetActiveSelf)
        {
            gameObject.SetActive(targetActiveSelf);
        }
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private static void RestorePropertyBlockBaseline(RendererBaseState state)
    {
        Renderer renderer = state.renderer;
        if (renderer == null)
        {
            return;
        }

        if (!state.hasCapturedPropertyBlocks)
        {
            RestoreSerializedColorBaseline(state);
            return;
        }

        renderer.SetPropertyBlock(state.hadRendererPropertyBlock ? state.rendererPropertyBlock : null);

        int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
        for (int i = 0; i < materialCount; i++)
        {
            bool hasBaseline = state.hadMaterialPropertyBlocks != null &&
                               i < state.hadMaterialPropertyBlocks.Length &&
                               state.hadMaterialPropertyBlocks[i];
            MaterialPropertyBlock block = hasBaseline && state.materialPropertyBlocks != null &&
                                          i < state.materialPropertyBlocks.Length
                ? state.materialPropertyBlocks[i]
                : null;
            renderer.SetPropertyBlock(block, i);
        }
    }

    private static void RestoreSerializedColorBaseline(RendererBaseState state)
    {
        if (state == null || state.renderer == null || !state.hasSerializedColorBaseline)
        {
            return;
        }

        Renderer renderer = state.renderer;
        MaterialPropertyBlock rendererBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(rendererBlock);
        if (state.rendererSupportsColor)
        {
            rendererBlock.SetColor(ColorId, state.rendererColor);
        }
        if (state.rendererSupportsBaseColor)
        {
            rendererBlock.SetColor(BaseColorId, state.rendererBaseColor);
        }
        renderer.SetPropertyBlock(rendererBlock);

        int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
        for (int i = 0; i < materialCount; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, i);
            if (state.materialSupportsColor != null &&
                i < state.materialSupportsColor.Length &&
                state.materialSupportsColor[i] &&
                state.materialColors != null &&
                i < state.materialColors.Length)
            {
                block.SetColor(ColorId, state.materialColors[i]);
            }
            if (state.materialSupportsBaseColor != null &&
                i < state.materialSupportsBaseColor.Length &&
                state.materialSupportsBaseColor[i] &&
                state.materialBaseColors != null &&
                i < state.materialBaseColors.Length)
            {
                block.SetColor(BaseColorId, state.materialBaseColors[i]);
            }
            renderer.SetPropertyBlock(block, i);
        }
    }

    private static bool MaterialsSupportProperty(Material[] materials, int propertyId)
    {
        if (materials == null)
        {
            return false;
        }
        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null && materials[i].HasProperty(propertyId))
            {
                return true;
            }
        }
        return false;
    }

    private static Color ResolveBaselineColor(
        MaterialPropertyBlock block,
        Material[] materials,
        int propertyId)
    {
        if (block != null && block.HasColor(propertyId))
        {
            return block.GetColor(propertyId);
        }

        if (materials != null)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null && material.HasProperty(propertyId))
                {
                    return material.GetColor(propertyId);
                }
            }
        }
        return Color.white;
    }

    private static void ApplyRendererColorOverride(Renderer renderer, Color color)
    {
        if (renderer == null) return;

        Material[] materials = renderer.sharedMaterials;
        bool supportsBaseColor = false;
        bool supportsColor = false;
        if (materials != null)
        {
            foreach (Material material in materials)
            {
                if (material == null) continue;
                if (material.HasProperty(BaseColorId)) supportsBaseColor = true;
                if (material.HasProperty(ColorId)) supportsColor = true;
            }
        }

        // Start from the restored renderer-level block and change only color keys.
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        SetColorProperties(block, supportsBaseColor, supportsColor, color);
        renderer.SetPropertyBlock(block);

        // A per-material block takes precedence over the renderer-level block.
        // Overlay the color there as well while retaining every baseline value.
        if (materials != null)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                bool materialSupportsBaseColor = material != null && material.HasProperty(BaseColorId);
                bool materialSupportsColor = material != null && material.HasProperty(ColorId);

                MaterialPropertyBlock materialBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(materialBlock, i);
                SetColorProperties(
                    materialBlock,
                    materialSupportsBaseColor,
                    materialSupportsColor,
                    color);
                renderer.SetPropertyBlock(materialBlock, i);
            }
        }
    }

    private static void SetColorProperties(
        MaterialPropertyBlock block,
        bool supportsBaseColor,
        bool supportsColor,
        Color color)
    {
        if (supportsBaseColor) block.SetColor(BaseColorId, color);
        if (supportsColor || !supportsBaseColor) block.SetColor(ColorId, color);
    }
}
