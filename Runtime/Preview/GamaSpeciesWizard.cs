using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Parent d'espèce dans l'aperçu statique : applique les overrides à toutes les instances enfants.
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
    public Vector3 positionOffset;
    public Vector3 rotationOffset;
    public float scaleMultiplier = 1f;
    public bool visibleInPreview = true;
    public bool visibleInRuntime = true;
    public GamaSpeciesRenderMode renderMode = GamaSpeciesRenderMode.Default;
    [TextArea(1, 4)] public string notesDebug = string.Empty;

    [Header("Storage")]
    public GamaSpeciesRenderOverrides overridesAsset;

    [ContextMenu("Apply Overrides To Children")]
    public void ApplyOverridesToChildren()
    {
        if (overridesAsset == null || string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        if (!overridesAsset.TryGetOverride(modelPath, experimentName, speciesName, out GamaSpeciesRenderOverrideEntry entry) ||
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
        if (overridesAsset == null && GetDefaultOverridesAsset != null)
        {
            overridesAsset = GetDefaultOverridesAsset.Invoke();
        }

        if (!AssetWritesSuppressed)
        {
            SaveCurrentSettingsToAsset();
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
            Debug.LogWarning("[GAMA] Aucun asset d'overrides ou speciesName vide pour " + name + ".");
            return;
        }

        SaveCurrentSettingsToAsset();
        EditorUtility.SetDirty(overridesAsset);
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA][WIZARD] species=" + speciesName + " scale=" + scaleMultiplier + " color=" + colorOverride + " saved override");
    }
#endif

    [ContextMenu("Apply Current Settings To Children")]
    public void ApplyCurrentSettingsToChildren()
    {
        int rendererCount = ApplyEntryToChildren(BuildEntryFromCurrentSettings());
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(speciesName))
        {
            Debug.Log("[GAMA][WIZARD] Applied editor color override species=" + speciesName + " color=" + colorOverride + " count=" + rendererCount);
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
        materialOverride = entry.materialOverride;
        colorOverrideEnabled = entry.overrideColor;
        colorOverride = entry.color;
        positionOffset = entry.positionOffset;
        rotationOffset = entry.rotationOffsetEuler;
        scaleMultiplier = Mathf.Max(0f, entry.scaleMultiplier);
        visibleInPreview = entry.visibleInPreview;
        visibleInRuntime = entry.visibleInRuntime;
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
            materialOverride = materialOverride,
            overrideColor = colorOverrideEnabled,
            color = colorOverride,
            positionOffset = positionOffset,
            rotationOffsetEuler = rotationOffset,
            scaleMultiplier = Mathf.Max(0f, scaleMultiplier),
            overridePreviewVisibility = true,
            visibleInPreview = visibleInPreview,
            overrideRuntimeVisibility = true,
            visibleInRuntime = visibleInRuntime,
            overrideVisibility = true,
            visible = visibleInRuntime,
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

        GamaSpeciesRenderOverrideEntry entry = BuildEntryFromCurrentSettings();
        overridesAsset.SetOrReplaceEntry(entry);
#if UNITY_EDITOR
        EditorUtility.SetDirty(overridesAsset);
#endif
        Debug.Log("[GAMA][WIZARD] species=" + speciesName + " scale=" + scaleMultiplier + " color=" + colorOverride + " saved override");
    }

    public static int ApplyEntryToInstance(Transform instance, GamaSpeciesRenderOverrideEntry entry)
    {
        if (instance == null || entry == null)
        {
            return 0;
        }

        GamaPreviewBaseline baseline = instance.GetComponent<GamaPreviewBaseline>();
        if (baseline == null)
        {
            baseline = instance.gameObject.AddComponent<GamaPreviewBaseline>();
            baseline.localPosition = instance.localPosition;
            baseline.localRotation = instance.localRotation;
            baseline.localScale = instance.localScale;
        }

        bool previewVisible = entry.visibleInPreview;
        if (entry.overridePreviewVisibility == false && entry.overrideVisibility)
        {
            previewVisible = entry.visible;
        }

        if (!previewVisible && (entry.overridePreviewVisibility || entry.overrideVisibility))
        {
            instance.gameObject.SetActive(false);
            return 0;
        }

        instance.gameObject.SetActive(true);
        instance.localPosition = baseline.localPosition + entry.positionOffset;
        instance.localRotation = baseline.localRotation * Quaternion.Euler(entry.rotationOffsetEuler);
        instance.localScale = baseline.localScale * entry.scaleMultiplier;

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
                rendererBaseline.Capture(renderer.sharedMaterials);
            }

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
            block.Clear();
            renderer.SetPropertyBlock(block);
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
}

[DisallowMultipleComponent]
public sealed class GamaPreviewRendererBaseline : MonoBehaviour
{
    [SerializeField] private Material[] sharedMaterials;

    public void Capture(Material[] currentSharedMaterials)
    {
        if (currentSharedMaterials == null)
        {
            sharedMaterials = Array.Empty<Material>();
            return;
        }

        sharedMaterials = (Material[])currentSharedMaterials.Clone();
    }

    public Material[] CloneSharedMaterials()
    {
        if (sharedMaterials == null)
        {
            return Array.Empty<Material>();
        }

        return (Material[])sharedMaterials.Clone();
    }
}
