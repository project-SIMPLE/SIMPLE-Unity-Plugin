using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Couche de "réglage runtime" pour les agents reçus de GAMA.
/// Permet d'ajuster côté Unity (sans modifier le middleware) :
///  - l'échelle (par type ou globalement),
///  - la rotation (heading GAMA strict, déplacement, identité, offset),
///  - la couleur (override, GAMA, aléatoire),
///  - un overlay debug pour inspecter les valeurs envoyées par le middleware.
///
/// À ajouter sur le même GameObject que <see cref="SimulationManager"/>.
/// Si absent, le SimulationManager fonctionne comme avant.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
[AddComponentMenu("GAMA/Gama Agent Tuner")]
public class GamaAgentTuner : MonoBehaviour
{
    public enum RotationMode
    {
        FromGama,
        FromMovement,
        Auto,
        Identity
    }

    public enum ColorMode
    {
        FromGama,
        Override,
        RandomPerAgent,
        RandomPerProperty
    }

    [Serializable]
    public class AgentOverride
    {
        public string label = "Override";
        public bool enabled = true;

        [Header("Match (laisser vide = ignore)")]
        public string propertyIdEquals;
        public string propertyIdContains;
        public string prefabPathContains;
        public string tagContains;
        public string nameRegex;

        [Header("Échelle / Position")]
        [Range(0.01f, 20f)] public float scaleMultiplier = 1f;
        [Range(-5f, 5f)] public float yOffset = 0f;

        [Header("Rotation")]
        public RotationMode rotationMode = RotationMode.FromGama;
        [Range(-180f, 180f)] public float rotationOffsetDegrees = 0f;
        [Range(-2f, 2f)] public float headingCoeffMultiplier = 1f;
        public bool invertHeadingSign = false;

        [Header("Couleur")]
        public ColorMode colorMode = ColorMode.FromGama;
        public Color overrideColor = Color.white;

        [Header("Visuel (optionnel)")]
        public Material materialOverride;
        public bool hideRenderers = false;

        public bool Matches(PropertiesGAMA prop, string objectName)
        {
            if (!enabled || prop == null)
            {
                return false;
            }

            string propId = prop.id ?? string.Empty;
            string prefabPath = prop.prefab ?? string.Empty;
            string tag = prop.tag ?? string.Empty;

            if (!string.IsNullOrEmpty(propertyIdEquals) &&
                !propId.Equals(propertyIdEquals, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(propertyIdContains) &&
                propId.IndexOf(propertyIdContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(prefabPathContains) &&
                prefabPath.IndexOf(prefabPathContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(tagContains) &&
                tag.IndexOf(tagContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(nameRegex))
            {
                try
                {
                    if (!Regex.IsMatch(objectName ?? string.Empty, nameRegex))
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return HasAtLeastOneCriterion();
        }

        bool HasAtLeastOneCriterion()
        {
            return !string.IsNullOrEmpty(propertyIdEquals) ||
                   !string.IsNullOrEmpty(propertyIdContains) ||
                   !string.IsNullOrEmpty(prefabPathContains) ||
                   !string.IsNullOrEmpty(tagContains) ||
                   !string.IsNullOrEmpty(nameRegex);
        }
    }

    [Header("Master (s'applique à tous les agents)")]
    [Range(0.05f, 5f)] public float globalScaleAll = 1f;
    [Range(0.05f, 5f)] public float globalVehicleScale = 1f;
    [Range(0.05f, 5f)] public float globalPedestrianScale = 1f;
    [Range(-180f, 180f)] public float globalRotationOffset = 0f;
    [Tooltip("Force l'orientation depuis le heading GAMA pur (désactive l'inférence par déplacement). Utile si vous voulez exactement la rotation envoyée par le middleware.")]
    public bool forceHeadingFromGamaStrict = false;
    [Tooltip("Désactive le fallback teinte inspector quand GAMA n'envoie pas de RGB.")]
    public bool disableInspectorTintFallback = false;

    [Header("Overrides ciblés (1er match gagne)")]
    public List<AgentOverride> overrides = new List<AgentOverride>();

    [Header("Overlay debug (toggle clavier)")]
    public bool overlayEnabled = false;
    public KeyCode overlayToggleKey = KeyCode.F8;

    private struct TrackedInstance
    {
        public GameObject obj;
        public PropertiesGAMA prop;
        public Vector3 originalLocalScale;
        public AgentOverride lastOverride;
    }

    private readonly List<TrackedInstance> tracked = new List<TrackedInstance>();
    private readonly Dictionary<int, MaterialPropertyBlock> propertyBlocks = new Dictionary<int, MaterialPropertyBlock>();

    void Awake()
    {
        EnsureHiddenWhenManagerPresent();
    }

    void OnEnable()
    {
        EnsureHiddenWhenManagerPresent();
    }

    void EnsureHiddenWhenManagerPresent()
    {
        if (GetComponent<SimulationManager>() != null)
        {
            hideFlags = HideFlags.HideInInspector;
        }
        else if ((hideFlags & HideFlags.HideInInspector) != 0)
        {
            hideFlags &= ~HideFlags.HideInInspector;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(overlayToggleKey))
        {
            overlayEnabled = !overlayEnabled;
        }
    }

    public AgentOverride FindOverride(PropertiesGAMA prop, string objectName)
    {
        if (prop == null || overrides == null)
        {
            return null;
        }

        for (int i = 0; i < overrides.Count; i++)
        {
            AgentOverride o = overrides[i];
            if (o != null && o.Matches(prop, objectName))
            {
                return o;
            }
        }

        return null;
    }

    public void ApplyOnInstantiate(GameObject obj, PropertiesGAMA prop)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        AgentOverride o = FindOverride(prop, obj.name);
        Vector3 originalScale = obj.transform.localScale;

        ApplyScale(obj, prop, o);
        ApplyMaterial(obj, o);
        ApplyVisibility(obj, o);
        ApplyColor(obj, prop, o);

        Track(obj, prop, originalScale, o);
    }

    public Quaternion ResolveOrientation(
        Quaternion gamaHeadingRotation,
        Quaternion movementRotation,
        bool movementValid,
        bool initGame,
        PropertiesGAMA prop,
        GameObject obj)
    {
        AgentOverride o = FindOverride(prop, obj != null ? obj.name : null);
        RotationMode mode = forceHeadingFromGamaStrict
            ? RotationMode.FromGama
            : (o != null ? o.rotationMode : RotationMode.Auto);

        Quaternion globalOffset = Quaternion.Euler(0f, globalRotationOffset, 0f);
        Quaternion localOffset = o != null
            ? Quaternion.Euler(0f, o.rotationOffsetDegrees, 0f)
            : Quaternion.identity;

        switch (mode)
        {
            case RotationMode.Identity:
                return globalOffset * localOffset;

            case RotationMode.FromGama:
                return globalOffset * localOffset * gamaHeadingRotation;

            case RotationMode.FromMovement:
                return movementValid
                    ? globalOffset * localOffset * movementRotation
                    : globalOffset * localOffset * gamaHeadingRotation;

            case RotationMode.Auto:
            default:
                if (initGame || !movementValid)
                {
                    return globalOffset * localOffset * gamaHeadingRotation;
                }
                return globalOffset * localOffset * movementRotation;
        }
    }

    public bool TryGetHeadingCoeffMultiplier(PropertiesGAMA prop, string objectName, out float multiplier, out bool invert)
    {
        multiplier = 1f;
        invert = false;

        AgentOverride o = FindOverride(prop, objectName);
        if (o == null)
        {
            return false;
        }

        multiplier = o.headingCoeffMultiplier;
        invert = o.invertHeadingSign;
        return true;
    }

    public bool ShouldSkipInspectorTint(PropertiesGAMA prop, string objectName)
    {
        if (disableInspectorTintFallback)
        {
            return true;
        }

        AgentOverride o = FindOverride(prop, objectName);
        return o != null && o.colorMode != ColorMode.FromGama;
    }

    public bool TryApplyColorOverride(GameObject obj, PropertiesGAMA prop)
    {
        if (obj == null || prop == null)
        {
            return false;
        }

        AgentOverride o = FindOverride(prop, obj.name);
        return ApplyColor(obj, prop, o);
    }

    void ApplyScale(GameObject obj, PropertiesGAMA prop, AgentOverride o)
    {
        float master = Mathf.Max(0.0001f, globalScaleAll);
        float categoryScale = ResolveCategoryScale(prop);
        float overrideScale = o != null ? Mathf.Max(0.0001f, o.scaleMultiplier) : 1f;
        float yOff = o != null ? o.yOffset : 0f;

        float total = master * categoryScale * overrideScale;

        if (Mathf.Abs(total - 1f) > 1e-4f)
        {
            obj.transform.localScale = obj.transform.localScale * total;
        }

        if (Mathf.Abs(yOff) > 1e-4f)
        {
            Vector3 p = obj.transform.position;
            obj.transform.position = new Vector3(p.x, p.y + yOff, p.z);
        }
    }

    float ResolveCategoryScale(PropertiesGAMA prop)
    {
        string descriptor =
            ((prop.prefab ?? string.Empty) + " " +
             (prop.tag ?? string.Empty) + " " +
             (prop.id ?? string.Empty)).ToLowerInvariant();

        if (descriptor.Contains("car") || descriptor.Contains("vehicle") ||
            descriptor.Contains("truck") || descriptor.Contains("bus") ||
            descriptor.Contains("bike") || descriptor.Contains("scooter") ||
            descriptor.Contains("moto"))
        {
            return Mathf.Max(0.0001f, globalVehicleScale);
        }

        if (descriptor.Contains("pedestrian") || descriptor.Contains("character") ||
            descriptor.Contains("people") || descriptor.Contains("person") ||
            descriptor.Contains("ghost"))
        {
            return Mathf.Max(0.0001f, globalPedestrianScale);
        }

        return 1f;
    }

    void ApplyMaterial(GameObject obj, AgentOverride o)
    {
        if (o == null || o.materialOverride == null)
        {
            return;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].sharedMaterial = o.materialOverride;
        }
    }

    void ApplyVisibility(GameObject obj, AgentOverride o)
    {
        if (o == null)
        {
            return;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = !o.hideRenderers;
        }
    }

    bool ApplyColor(GameObject obj, PropertiesGAMA prop, AgentOverride o)
    {
        if (o == null || o.colorMode == ColorMode.FromGama)
        {
            return false;
        }

        Color color;
        switch (o.colorMode)
        {
            case ColorMode.Override:
                color = o.overrideColor;
                break;
            case ColorMode.RandomPerProperty:
                color = ColorFromHash(prop != null ? (prop.id ?? string.Empty) : string.Empty);
                break;
            case ColorMode.RandomPerAgent:
                color = ColorFromHash(obj.name ?? string.Empty);
                break;
            default:
                return false;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            int id = r.GetInstanceID();
            MaterialPropertyBlock pb;
            if (!propertyBlocks.TryGetValue(id, out pb))
            {
                pb = new MaterialPropertyBlock();
                propertyBlocks[id] = pb;
            }

            r.GetPropertyBlock(pb);
            pb.SetColor("_BaseColor", color);
            pb.SetColor("_Color", color);
            r.SetPropertyBlock(pb);
        }

        return true;
    }

    static Color ColorFromHash(string seed)
    {
        unchecked
        {
            int h = 17;
            for (int i = 0; i < seed.Length; i++)
            {
                h = h * 31 + seed[i];
            }

            float hue = ((h & 0xFFFF) / (float)0xFFFF) % 1f;
            float sat = 0.65f;
            float val = 0.95f;
            return Color.HSVToRGB(hue, sat, val);
        }
    }

    void Track(GameObject obj, PropertiesGAMA prop, Vector3 originalScale, AgentOverride o)
    {
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            if (tracked[i].obj == null)
            {
                tracked.RemoveAt(i);
            }
        }

        tracked.Add(new TrackedInstance
        {
            obj = obj,
            prop = prop,
            originalLocalScale = originalScale,
            lastOverride = o
        });
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        ReapplyAllTracked();
    }
#endif

    void ReapplyAllTracked()
    {
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            TrackedInstance t = tracked[i];
            if (t.obj == null)
            {
                tracked.RemoveAt(i);
                continue;
            }

            AgentOverride o = FindOverride(t.prop, t.obj.name);
            t.obj.transform.localScale = t.originalLocalScale;
            ApplyScale(t.obj, t.prop, o);
            ApplyMaterial(t.obj, o);
            ApplyVisibility(t.obj, o);
            ApplyColor(t.obj, t.prop, o);

            t.lastOverride = o;
            tracked[i] = t;
        }
    }

    void OnGUI()
    {
        if (!overlayEnabled)
        {
            return;
        }

        const int width = 380;
        Rect rect = new Rect(10f, 10f, width, Screen.height - 20f);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label("<b>GAMA Agent Tuner</b>");
        GUILayout.Label("Toggle: " + overlayToggleKey);
        GUILayout.Space(4f);
        GUILayout.Label("Global scale: " + globalScaleAll.ToString("F2"));
        globalScaleAll = GUILayout.HorizontalSlider(globalScaleAll, 0.05f, 5f);
        GUILayout.Label("Vehicle scale: " + globalVehicleScale.ToString("F2"));
        globalVehicleScale = GUILayout.HorizontalSlider(globalVehicleScale, 0.05f, 5f);
        GUILayout.Label("Pedestrian scale: " + globalPedestrianScale.ToString("F2"));
        globalPedestrianScale = GUILayout.HorizontalSlider(globalPedestrianScale, 0.05f, 5f);
        GUILayout.Label("Rotation offset: " + globalRotationOffset.ToString("F1") + " deg");
        globalRotationOffset = GUILayout.HorizontalSlider(globalRotationOffset, -180f, 180f);
        forceHeadingFromGamaStrict = GUILayout.Toggle(forceHeadingFromGamaStrict, "Force heading GAMA strict");
        disableInspectorTintFallback = GUILayout.Toggle(disableInspectorTintFallback, "Disable inspector tint fallback");

        if (GUILayout.Button("Réappliquer aux instances"))
        {
            ReapplyAllTracked();
        }

        GUILayout.Space(8f);
        GUILayout.Label("<b>Tracked agents: " + tracked.Count + "</b>");

        int max = Mathf.Min(tracked.Count, 12);
        for (int i = 0; i < max; i++)
        {
            TrackedInstance t = tracked[i];
            if (t.obj == null)
            {
                continue;
            }

            string label = t.obj.name + "  (" + (t.prop != null ? t.prop.id : "?") + ")";
            string overrideLabel = t.lastOverride != null ? t.lastOverride.label : "-";
            GUILayout.Label(label + " > " + overrideLabel, GUILayout.Width(width - 20));
        }

        GUILayout.EndArea();
    }
}
