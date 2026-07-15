using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Species parent in the static preview; applies overrides to every child instance.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class GamaSpeciesWizard : MonoBehaviour
{
    [Header("Identity")]
    public string modelPath = string.Empty;
    public string experimentName = string.Empty;
    public string speciesName = string.Empty;

    [Header("Visual binding")]
    public GameObject prefabOverride;
    public Material materialOverride;
    public bool colorOverrideEnabled;
    public Color colorOverride = Color.white;
    public bool positionOffsetOverrideEnabled;
    public Vector3 positionOffset;
    public bool rotationOffsetOverrideEnabled;
    public Vector3 rotationOffset;
    public bool scaleOverrideEnabled;
    public float scaleMultiplier = 1f;
    public bool previewVisibilityOverrideEnabled;
    public bool visibleInPreview = true;
    public bool runtimeVisibilityOverrideEnabled;
    public bool visibleInRuntime = true;
    public GamaSpeciesRenderMode renderMode = GamaSpeciesRenderMode.Default;
    [TextArea(1, 4)] public string notesDebug = string.Empty;

    [Header("Storage")]
    public GamaSpeciesRenderOverrides overridesAsset;

    [SerializeField, HideInInspector] private string prefabResourcePath = string.Empty;
    [SerializeField, HideInInspector] private GameObject prefabPathSource;

    [ContextMenu("Apply Overrides To Children")]
    public void ApplyOverridesToChildren()
    {
        if (overridesAsset == null || string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GamaSpeciesAppearanceContext context = new GamaSpeciesAppearanceContext(
            overridesAsset,
            modelPath,
            experimentName);
        if (!GamaSpeciesAppearanceStateStore.TryGetEntry(
                context,
                speciesName,
                Application.isPlaying,
                out GamaSpeciesRenderOverrideEntry entry) ||
            entry == null)
        {
            return;
        }

        PopulateFromEntry(entry);
        ApplyEntryToChildren(entry);
    }

    public int ApplyEntryToChildren(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return 0;
        }

        int rendererCount = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            rendererCount += ApplyEntryToInstance(child, entry);
        }

        return rendererCount;
    }

    public void NormalizeSpeciesContainerScale()
    {
        if ((transform.localScale - Vector3.one).sqrMagnitude <= 0.000001f)
        {
            return;
        }

        transform.localScale = Vector3.one;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(transform);
        }
#endif
    }

#if UNITY_EDITOR
    public static Action OnWizardSettingsChanged;
    public static Func<GamaSpeciesRenderOverrides> GetDefaultOverridesAsset;
    private static int suppressAssetWriteDepth;

    public static IDisposable SuppressAssetWrites()
    {
        suppressAssetWriteDepth++;
        return new SuppressAssetWriteScope();
    }

    private static bool AssetWritesSuppressed
    {
        get { return suppressAssetWriteDepth > 0; }
    }

    private sealed class SuppressAssetWriteScope : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            suppressAssetWriteDepth = Math.Max(0, suppressAssetWriteDepth - 1);
            disposed = true;
        }
    }

    private void OnValidate()
    {
        scaleMultiplier = Mathf.Max(0f, scaleMultiplier);
        if (prefabPathSource != prefabOverride)
        {
            prefabPathSource = prefabOverride;
            prefabResourcePath = ResolveResourcesPath(prefabOverride);
        }
        if (overridesAsset == null && GetDefaultOverridesAsset != null)
        {
            overridesAsset = GetDefaultOverridesAsset.Invoke();
        }

        if (OnWizardSettingsChanged != null)
        {
            OnWizardSettingsChanged.Invoke();
        }
    }

    [ContextMenu("Save Parent Transform As Species Override")]
    public void SaveParentTransformAsSpeciesOverride()
    {
        if (overridesAsset == null || string.IsNullOrWhiteSpace(speciesName))
        {
            GamaLog.Warning("[GAMA] No overrides asset is assigned, or speciesName is empty, for " + name + ".");
            return;
        }

        SaveCurrentSettingsToAsset();
        EditorUtility.SetDirty(overridesAsset);
        AssetDatabase.SaveAssets();
        GamaLog.Dev("[GAMA][WIZARD] species=" + speciesName + " scale=" + scaleMultiplier + " color=" + colorOverride + " saved override");
    }
#endif

    [ContextMenu("Apply Current Settings To Children")]
    public void ApplyCurrentSettingsToChildren()
    {
        int rendererCount = ApplyEntryToChildren(BuildEntryFromCurrentSettings());
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(speciesName))
        {
            GamaLog.Dev("[GAMA][WIZARD] Applied editor color override species=" + speciesName + " color=" + colorOverride + " count=" + rendererCount);
        }
#endif
    }

    public void PopulateFromEntry(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        speciesName = entry.GetSpeciesName();
        modelPath = entry.modelPath;
        experimentName = entry.experimentName;
        prefabOverride = entry.prefabOverride;
        prefabPathSource = entry.prefabOverride;
        prefabResourcePath = entry.prefabResourcePath ?? string.Empty;
        materialOverride = entry.materialOverride;
        colorOverrideEnabled = entry.overrideColor;
        colorOverride = entry.color;
        positionOffsetOverrideEnabled = entry.overridePositionOffset;
        positionOffset = entry.GetEffectivePositionOffset();
        rotationOffsetOverrideEnabled = entry.overrideRotationOffset;
        rotationOffset = entry.GetEffectiveRotationOffsetEuler();
        scaleOverrideEnabled = entry.overrideScaleMultiplier;
        scaleMultiplier = entry.GetEffectiveScaleMultiplier();
        previewVisibilityOverrideEnabled = entry.UsesPreviewVisibilityOverride();
        visibleInPreview = entry.UsesPreviewVisibilityOverride() ? entry.GetEffectivePreviewVisible() : true;
        runtimeVisibilityOverrideEnabled = entry.UsesRuntimeVisibilityOverride();
        visibleInRuntime = entry.UsesRuntimeVisibilityOverride() ? entry.GetEffectiveRuntimeVisible() : true;
        renderMode = entry.renderMode;
        notesDebug = entry.notesDebug;
    }

    public GamaSpeciesRenderOverrideEntry BuildEntryFromCurrentSettings()
    {
        return new GamaSpeciesRenderOverrideEntry
        {
            modelPath = modelPath,
            experimentName = experimentName,
            speciesName = speciesName,
            speciesKey = speciesName,
            prefabOverride = prefabOverride,
            prefabResourcePath = prefabResourcePath ?? string.Empty,
            materialOverride = materialOverride,
            overrideColor = colorOverrideEnabled,
            color = colorOverride,
            overrideScaleMultiplier = scaleOverrideEnabled,
            overridePositionOffset = positionOffsetOverrideEnabled,
            overrideRotationOffset = rotationOffsetOverrideEnabled,
            positionOffset = positionOffset,
            rotationOffsetEuler = rotationOffset,
            scaleMultiplier = Mathf.Max(0f, scaleMultiplier),
            overridePreviewVisibility = previewVisibilityOverrideEnabled,
            visibleInPreview = visibleInPreview,
            overrideRuntimeVisibility = runtimeVisibilityOverrideEnabled,
            visibleInRuntime = visibleInRuntime,
            overrideVisibility = false,
            visible = true,
            renderMode = renderMode,
            notesDebug = notesDebug
        };
    }

    public void SaveCurrentSettingsToAsset()
    {
#if UNITY_EDITOR
        if (AssetWritesSuppressed)
        {
            return;
        }
#endif

        if (overridesAsset == null || string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GamaSpeciesAppearanceContext context = new GamaSpeciesAppearanceContext(
            overridesAsset,
            modelPath,
            experimentName);
        GamaSpeciesAppearanceStateStore.SetActiveContext(context);
        bool runtimeOnly = Application.isPlaying;
#if UNITY_EDITOR
        if (!runtimeOnly)
        {
            Undo.RecordObject(overridesAsset, "Edit GAMA species appearance");
        }
#endif
        GamaSpeciesRenderOverrideEntry entry =
            GamaSpeciesAppearanceStateStore.GetOrCreateEditableEntry(
                context,
                speciesName,
                runtimeOnly);
        CopyCurrentSettingsToEntry(entry);
        GamaSpeciesAppearanceStateStore.NotifyEntryChanged(context, speciesName, runtimeOnly);
#if UNITY_EDITOR
        if (!runtimeOnly)
        {
            EditorUtility.SetDirty(overridesAsset);
        }
#endif
        GamaLog.Dev("[GAMA][WIZARD] species=" + speciesName + " scale=" + scaleMultiplier + " color=" + colorOverride + " saved override");
    }

    private void CopyCurrentSettingsToEntry(GamaSpeciesRenderOverrideEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        GamaSpeciesRenderOverrideEntry wizardValues = BuildEntryFromCurrentSettings();
        entry.modelPath = wizardValues.modelPath;
        entry.experimentName = wizardValues.experimentName;
        entry.speciesName = wizardValues.speciesName;
        entry.speciesKey = wizardValues.speciesKey;
        entry.prefabOverride = wizardValues.prefabOverride;
        entry.prefabResourcePath = wizardValues.prefabResourcePath;
        entry.materialOverride = wizardValues.materialOverride;
        entry.overrideColor = wizardValues.overrideColor;
        entry.color = wizardValues.color;
        entry.overrideScaleMultiplier = wizardValues.overrideScaleMultiplier;
        entry.scaleMultiplier = wizardValues.scaleMultiplier;
        entry.overridePositionOffset = wizardValues.overridePositionOffset;
        entry.positionOffset = wizardValues.positionOffset;
        entry.overrideRotationOffset = wizardValues.overrideRotationOffset;
        entry.rotationOffsetEuler = wizardValues.rotationOffsetEuler;
        entry.overridePreviewVisibility = wizardValues.overridePreviewVisibility;
        entry.visibleInPreview = wizardValues.visibleInPreview;
        entry.overrideRuntimeVisibility = wizardValues.overrideRuntimeVisibility;
        entry.visibleInRuntime = wizardValues.visibleInRuntime;
        entry.overrideVisibility = false;
        entry.visible = true;
        entry.renderMode = wizardValues.renderMode;
        entry.notesDebug = wizardValues.notesDebug;
    }

#if UNITY_EDITOR
    private static string ResolveResourcesPath(GameObject prefab)
    {
        if (prefab == null)
        {
            return string.Empty;
        }

        string assetPath = AssetDatabase.GetAssetPath(prefab).Replace('\\', '/');
        int resourcesIndex = assetPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex < 0)
        {
            return string.Empty;
        }

        string resourcePath = assetPath.Substring(resourcesIndex + "/Resources/".Length);
        if (resourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            resourcePath = resourcePath.Substring(0, resourcePath.Length - ".prefab".Length);
        }
        return resourcePath.Trim('/');
    }
#endif

    public static int ApplyEntryToInstance(Transform instance, GamaSpeciesRenderOverrideEntry entry)
    {
        if (instance == null || entry == null)
        {
            return 0;
        }

        GamaPreviewObject previewObject = instance.GetComponent<GamaPreviewObject>();
        if (previewObject != null)
        {
            previewObject.ApplySpeciesOverride(entry);
            Renderer[] previewRenderers = instance.GetComponentsInChildren<Renderer>(true);
            return previewRenderers != null ? previewRenderers.Length : 0;
        }

        GamaPreviewBaseline baseline = instance.GetComponent<GamaPreviewBaseline>();
        if (baseline == null)
        {
            baseline = instance.gameObject.AddComponent<GamaPreviewBaseline>();
            baseline.localPosition = instance.localPosition;
            baseline.localRotation = instance.localRotation;
            baseline.localScale = instance.localScale;
            baseline.activeSelf = instance.gameObject.activeSelf;
        }

        bool previewVisible = entry.GetEffectivePreviewVisible();
        bool overridesVisibility = entry.UsesPreviewVisibilityOverride();
        instance.gameObject.SetActive(overridesVisibility ? previewVisible : baseline.activeSelf);
        if (!instance.gameObject.activeSelf)
        {
            return 0;
        }

        instance.localPosition = baseline.localPosition + entry.GetEffectivePositionOffset();
        instance.localRotation = baseline.localRotation * Quaternion.Euler(entry.GetEffectiveRotationOffsetEuler());
        instance.localScale = baseline.localScale * entry.GetEffectiveScaleMultiplier();

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        int touchedRendererCount = 0;
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            GamaPreviewRendererBaseline rendererBaseline = renderer.GetComponent<GamaPreviewRendererBaseline>();
            if (rendererBaseline == null)
            {
                rendererBaseline = renderer.gameObject.AddComponent<GamaPreviewRendererBaseline>();
            }
            rendererBaseline.Capture(renderer);
            rendererBaseline.Restore(renderer);

            Material[] mats = rendererBaseline.CloneSharedMaterials();
            if (entry.materialOverride != null)
            {
                for (int m = 0; m < mats.Length; m++)
                {
                    mats[m] = entry.materialOverride;
                }
            }
            renderer.sharedMaterials = mats;
            ApplyRendererColorOverride(renderer, entry.overrideColor, entry.color);
            touchedRendererCount++;

            if (entry.renderMode == GamaSpeciesRenderMode.Hidden)
            {
                renderer.enabled = false;
            }
            else if (entry.renderMode == GamaSpeciesRenderMode.Wireframe)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        return touchedRendererCount;
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
            return;
        }

        bool supportsBaseColor = MaterialArraySupportsProperty(renderer.sharedMaterials, BaseColorId);
        bool supportsColor = MaterialArraySupportsProperty(renderer.sharedMaterials, ColorId);
        if (supportsBaseColor)
        {
            block.SetColor(BaseColorId, color);
        }

        if (supportsColor || !supportsBaseColor)
        {
            block.SetColor(ColorId, color);
        }

        renderer.SetPropertyBlock(block);

        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            MaterialPropertyBlock indexedBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(indexedBlock, i);
            if (material != null && material.HasProperty(BaseColorId))
            {
                indexedBlock.SetColor(BaseColorId, color);
            }
            if (material == null || material.HasProperty(ColorId) || !material.HasProperty(BaseColorId))
            {
                indexedBlock.SetColor(ColorId, color);
            }
            renderer.SetPropertyBlock(indexedBlock, i);
        }
    }

    private static bool MaterialArraySupportsProperty(Material[] materials, int propertyId)
    {
        if (materials == null || materials.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material != null && material.HasProperty(propertyId))
            {
                return true;
            }
        }

        return false;
    }
}

[DisallowMultipleComponent]
public sealed class GamaPreviewBaseline : MonoBehaviour
{
    public Vector3 localPosition;
    public Quaternion localRotation = Quaternion.identity;
    public Vector3 localScale = Vector3.one;
    public bool activeSelf = true;
    public GameObject sourcePrefab;
}

[DisallowMultipleComponent]
public sealed class GamaPreviewRendererBaseline : MonoBehaviour
{
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [SerializeField] private bool captured;
    [SerializeField] private Material[] sharedMaterials;
    [SerializeField] private bool rendererEnabled;
    [SerializeField] private UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
    [SerializeField] private bool receiveShadows;
    [SerializeField] private Color rendererColor = Color.white;
    [SerializeField] private Color rendererBaseColor = Color.white;
    [SerializeField] private Color[] materialColors = Array.Empty<Color>();
    [SerializeField] private Color[] materialBaseColors = Array.Empty<Color>();

    [NonSerialized] private bool hasInMemoryPropertyBlocks;
    [NonSerialized] private bool hadRendererPropertyBlock;
    [NonSerialized] private MaterialPropertyBlock rendererPropertyBlock;
    [NonSerialized] private bool[] hadMaterialPropertyBlocks;
    [NonSerialized] private MaterialPropertyBlock[] materialPropertyBlocks;

    public void Capture(Renderer renderer)
    {
        if (captured || renderer == null)
        {
            return;
        }

        Material[] currentSharedMaterials = renderer.sharedMaterials;
        sharedMaterials = currentSharedMaterials != null
            ? (Material[])currentSharedMaterials.Clone()
            : Array.Empty<Material>();
        rendererEnabled = renderer.enabled;
        shadowCastingMode = renderer.shadowCastingMode;
        receiveShadows = renderer.receiveShadows;

        rendererPropertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(rendererPropertyBlock);
        hadRendererPropertyBlock = !rendererPropertyBlock.isEmpty;
        rendererColor = ResolveBaselineColor(rendererPropertyBlock, sharedMaterials, ColorId);
        rendererBaseColor = ResolveBaselineColor(rendererPropertyBlock, sharedMaterials, BaseColorId);

        materialColors = new Color[sharedMaterials.Length];
        materialBaseColors = new Color[sharedMaterials.Length];
        hadMaterialPropertyBlocks = new bool[sharedMaterials.Length];
        materialPropertyBlocks = new MaterialPropertyBlock[sharedMaterials.Length];
        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, i);
            hadMaterialPropertyBlocks[i] = !block.isEmpty;
            materialPropertyBlocks[i] = block;
            Material[] oneMaterial = { sharedMaterials[i] };
            materialColors[i] = ResolveBaselineColor(block, oneMaterial, ColorId);
            materialBaseColors[i] = ResolveBaselineColor(block, oneMaterial, BaseColorId);
        }

        hasInMemoryPropertyBlocks = true;
        captured = true;
    }

    public void Restore(Renderer renderer)
    {
        if (!captured || renderer == null)
        {
            return;
        }

        renderer.sharedMaterials = sharedMaterials != null
            ? (Material[])sharedMaterials.Clone()
            : Array.Empty<Material>();
        renderer.enabled = rendererEnabled;
        renderer.shadowCastingMode = shadowCastingMode;
        renderer.receiveShadows = receiveShadows;

        if (hasInMemoryPropertyBlocks)
        {
            renderer.SetPropertyBlock(hadRendererPropertyBlock ? rendererPropertyBlock : null);
            for (int i = 0; i < renderer.sharedMaterials.Length; i++)
            {
                bool hadBlock = hadMaterialPropertyBlocks != null &&
                                i < hadMaterialPropertyBlocks.Length &&
                                hadMaterialPropertyBlocks[i];
                MaterialPropertyBlock block = materialPropertyBlocks != null &&
                                              i < materialPropertyBlocks.Length
                    ? materialPropertyBlocks[i]
                    : null;
                renderer.SetPropertyBlock(hadBlock ? block : null, i);
            }
            return;
        }

        // MaterialPropertyBlock is native/non-serializable. After a domain reload,
        // retain all current unrelated values and restore only the color keys that
        // this feature can modify, including per-material-index blocks.
        MaterialPropertyBlock rendererBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(rendererBlock);
        rendererBlock.SetColor(ColorId, rendererColor);
        rendererBlock.SetColor(BaseColorId, rendererBaseColor);
        renderer.SetPropertyBlock(rendererBlock);
        for (int i = 0; i < renderer.sharedMaterials.Length; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, i);
            block.SetColor(ColorId, i < materialColors.Length ? materialColors[i] : Color.white);
            block.SetColor(BaseColorId, i < materialBaseColors.Length ? materialBaseColors[i] : Color.white);
            renderer.SetPropertyBlock(block, i);
        }
    }

    public Material[] CloneSharedMaterials()
    {
        return sharedMaterials != null ? (Material[])sharedMaterials.Clone() : Array.Empty<Material>();
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
}
