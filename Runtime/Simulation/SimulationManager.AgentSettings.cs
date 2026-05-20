using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public abstract partial class SimulationManager
{
    [Header("Middleware import")]
    [SerializeField] private bool createAgentEntries = true;
    [SerializeField] private int maxAgentEntries = 0;
    [SerializeField] private bool logWhenAgentEntriesCapReached = true;
    [SerializeField] private bool keepManualAgentEntriesWhenMissing = true;

    [Header("Defaults imported from GAMA properties")]
    [SerializeField] private List<GamaAgentPropertySettings> propertySettings = new List<GamaAgentPropertySettings>();

    [Header("Ordered rule overrides")]
    [SerializeField] private bool applyRuleOverrides = true;
    [SerializeField] private List<GamaAgentRuleSettings> ruleSettings = new List<GamaAgentRuleSettings>();

    [Header("Instance corrections")]
    [SerializeField] private List<GamaAgentInstanceSettings> agentSettings = new List<GamaAgentInstanceSettings>();

    private readonly Dictionary<string, GamaAgentPropertySettings> agentPropertyLookup =
        new Dictionary<string, GamaAgentPropertySettings>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, GamaAgentInstanceSettings> agentLookup =
        new Dictionary<string, GamaAgentInstanceSettings>(StringComparer.OrdinalIgnoreCase);

    private bool maxEntriesWarningLogged;

    public void ImportAgentProperties(IEnumerable<PropertiesGAMA> properties, int precision)
    {
        RebuildAgentLookups();
        HashSet<string> importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (properties == null)
        {
            return;
        }

        foreach (PropertiesGAMA property in properties)
        {
            if (property == null || string.IsNullOrEmpty(property.id))
            {
                continue;
            }

            importedIds.Add(property.id);
            property.PrepareRuntime(precision);

            GamaAgentPropertySettings settings;
            if (!agentPropertyLookup.TryGetValue(property.id, out settings))
            {
                settings = new GamaAgentPropertySettings();
                propertySettings.Add(settings);
                agentPropertyLookup[property.id] = settings;
            }

            settings.ImportFrom(property, precision);
        }

        if (!keepManualAgentEntriesWhenMissing)
        {
            propertySettings.RemoveAll(s => s == null || !importedIds.Contains(s.PropertyId));
        }
        else
        {
            propertySettings.RemoveAll(s =>
                s == null ||
                (string.IsNullOrEmpty(s.PropertyId)) ||
                (!importedIds.Contains(s.PropertyId) && !s.Manual.HasAnyOverride()));
        }

        RebuildAgentLookups();
    }

    public GamaAgentVisualState ResolveVisualState(
        string agentName,
        PropertiesGAMA property,
        Attributes attributes,
        int precision)
    {
        GamaAgentVisualState state = CreateDefaultVisualState(property, attributes, precision);
        TrackAgentDefaults(agentName, property, attributes, state);

        GamaAgentPropertySettings propertyOverride;
        if (property != null &&
            !string.IsNullOrEmpty(property.id) &&
            agentPropertyLookup.TryGetValue(property.id, out propertyOverride))
        {
            propertyOverride.Manual.ApplyTo(ref state);
        }

        if (applyRuleOverrides)
        {
            ApplyRuleOverrides(
                new GamaAgentMatchContext(
                    agentName,
                    property != null ? property.id : string.Empty,
                    property != null ? property.tag : string.Empty,
                    property != null ? property.prefab : string.Empty),
                ref state);
        }

        GamaAgentInstanceSettings agentOverride;
        if (!string.IsNullOrEmpty(agentName) && agentLookup.TryGetValue(agentName, out agentOverride))
        {
            agentOverride.Manual.ApplyTo(ref state);
        }

        if (Application.isPlaying)
        {
            GamaSpeciesRenderOverrideEntry previewOverride;
            if (GamaRuntimePreviewOverrideApplier.TryGetOverride(property != null ? property.id : string.Empty, out previewOverride))
            {
                if (previewOverride.overrideColor)
                {
                    state.Color = (Color32)previewOverride.color;
                    state.HasColor = true;
                    state.HasManualColorOverride = true;
                }
                if (Math.Abs(previewOverride.scaleMultiplier - 1f) > 0.0001f)
                {
                    state.ScaleMultiplier *= Mathf.Max(0f, previewOverride.scaleMultiplier);
                }
                if (previewOverride.positionOffset.sqrMagnitude > 0.0001f)
                {
                    state.PositionOffset += previewOverride.positionOffset;
                }
                if (previewOverride.rotationOffsetEuler.sqrMagnitude > 0.0001f)
                {
                    state.RotationOffsetEuler += previewOverride.rotationOffsetEuler;
                }
                if (previewOverride.overrideVisibility || previewOverride.overrideRuntimeVisibility)
                {
                    state.Visible = previewOverride.visibleInRuntime;
                }
            }
        }

        return state;
    }

    public static GamaAgentVisualState CreateDefaultVisualState(PropertiesGAMA property, Attributes attributes, int precision)
    {
        if (property != null)
        {
            property.PrepareRuntime(precision);
        }

        GamaAgentVisualState state = new GamaAgentVisualState
        {
            HasColor = property != null,
            Color = property != null ? property.GetUnityColor() : new Color32(255, 255, 255, 255),
            ScaleMultiplier = 1f,
            PositionOffset = Vector3.zero,
            RotationOffsetEuler = Vector3.zero,
            Visible = property == null || property.visible
        };

        if (attributes != null)
        {
            Color32 attributeColor;
            if (attributes.TryGetColor(out attributeColor))
            {
                state.Color = attributeColor;
                state.HasColor = true;
                state.HasAttributeColor = true;
            }

            bool visible;
            if (attributes.TryGetBool(
                    out visible,
                    "visible",
                    "isVisible",
                    "is_visible",
                    "unityVisible",
                    "unity_visible"))
            {
                state.Visible = visible;
            }

            float opacity;
            if (attributes.TryGetFloat(out opacity, "opacity", "alpha"))
            {
                state.Visible = opacity > 0f;
            }

            float relativeScale;
            if (attributes.TryGetFloat(
                    out relativeScale,
                    "scale",
                    "scaleFactor",
                    "scale_factor",
                    "relativeScale",
                    "relative_scale",
                    "unityScale",
                    "unity_scale"))
            {
                state.ScaleMultiplier *= Mathf.Max(0f, relativeScale);
            }

            Vector3 positionOffset;
            if (attributes.TryGetVector3(
                    out positionOffset,
                    "positionOffset",
                    "position_offset",
                    "offsetPosition",
                    "offset_position",
                    "unityOffset",
                    "unity_offset"))
            {
                state.PositionOffset += positionOffset;
            }

            Vector3 rotationOffset;
            if (attributes.TryGetVector3(
                    out rotationOffset,
                    "rotationOffset",
                    "rotation_offset",
                    "orientationOffset",
                    "orientation_offset",
                    "unityRotationOffset",
                    "unity_rotation_offset"))
            {
                state.RotationOffsetEuler += rotationOffset;
            }

            float yawOffset;
            if (attributes.TryGetFloat(
                    out yawOffset,
                    "headingOffset",
                    "heading_offset",
                    "yawOffset",
                    "yaw_offset",
                    "rotationOffsetY",
                    "rotation_offset_y"))
            {
                state.RotationOffsetEuler += new Vector3(0f, yawOffset, 0f);
            }
        }

        return state;
    }

    private void ApplyRuleOverrides(GamaAgentMatchContext context, ref GamaAgentVisualState state)
    {
        for (int i = 0; i < ruleSettings.Count; i++)
        {
            GamaAgentRuleSettings rule = ruleSettings[i];
            if (rule == null || !rule.Enabled)
            {
                continue;
            }

            if (rule.Matches(context))
            {
                rule.Manual.ApplyTo(ref state);
            }
        }
    }

    private void TrackAgentDefaults(
        string agentName,
        PropertiesGAMA property,
        Attributes attributes,
        GamaAgentVisualState state)
    {
        if (!createAgentEntries || string.IsNullOrEmpty(agentName))
        {
            return;
        }

        GamaAgentInstanceSettings settings;
        if (!agentLookup.TryGetValue(agentName, out settings))
        {
            if (maxAgentEntries > 0 && agentSettings.Count >= maxAgentEntries)
            {
                if (logWhenAgentEntriesCapReached && !maxEntriesWarningLogged)
                {
                    maxEntriesWarningLogged = true;
                    Debug.LogWarning(
                        "[GAMA] SimulationManager agent settings reached maxAgentEntries=" +
                        maxAgentEntries +
                        ". New runtime agents will not be tracked automatically.");
                }

                return;
            }

            settings = new GamaAgentInstanceSettings();
            agentSettings.Add(settings);
            agentLookup[agentName] = settings;
        }

        settings.ImportFrom(agentName, property, attributes, state);
    }

    private void RebuildAgentLookups()
    {
        maxAgentEntries = Mathf.Max(0, maxAgentEntries);
        agentPropertyLookup.Clear();
        agentLookup.Clear();

        for (int i = 0; i < propertySettings.Count; i++)
        {
            GamaAgentPropertySettings settings = propertySettings[i];
            if (settings != null && !string.IsNullOrEmpty(settings.PropertyId) && !agentPropertyLookup.ContainsKey(settings.PropertyId))
            {
                agentPropertyLookup.Add(settings.PropertyId, settings);
            }
        }

        for (int i = 0; i < agentSettings.Count; i++)
        {
            GamaAgentInstanceSettings settings = agentSettings[i];
            if (settings != null && !string.IsNullOrEmpty(settings.AgentName) && !agentLookup.ContainsKey(settings.AgentName))
            {
                agentLookup.Add(settings.AgentName, settings);
            }
        }
    }
}

[Serializable]
public class GamaAgentPropertySettings
{
    [SerializeField] private string propertyId;
    [SerializeField] private string tag;
    [SerializeField] private string prefab;
    [SerializeField] private bool importedVisible = true;
    [SerializeField] private Color importedColor = Color.white;
    [SerializeField] private float importedBaseScale = 1f;
    [SerializeField] private float importedYOffset;
    [SerializeField] private float importedRotationOffsetY;
    [SerializeField] private GamaAgentManualOverrides manual = new GamaAgentManualOverrides();

    public string PropertyId { get { return propertyId; } }
    public GamaAgentManualOverrides Manual
    {
        get
        {
            if (manual == null)
            {
                manual = new GamaAgentManualOverrides();
            }

            return manual;
        }
    }

    public void ImportFrom(PropertiesGAMA property, int precision)
    {
        propertyId = property.id;
        tag = property.tag;
        prefab = property.prefab;
        importedVisible = property.visible;
        importedColor = property.GetUnityColor();
        importedBaseScale = property.GetUnityScale(precision);
        importedYOffset = property.yOffsetF;
        importedRotationOffsetY = property.rotationOffsetF;
    }
}

[Serializable]
public class GamaAgentRuleSettings
{
    [SerializeField] private bool enabled = true;
    [SerializeField] private string label = "Override";
    [SerializeField] private string propertyId;
    [SerializeField] private string tag;
    [SerializeField] private string prefabContains;
    [SerializeField] private string agentNameContains;
    [SerializeField] private string agentNameRegex;
    [SerializeField] private GamaAgentManualOverrides manual = new GamaAgentManualOverrides();

    [NonSerialized] private Regex cachedRegex;
    [NonSerialized] private string cachedPattern;
    [NonSerialized] private bool regexInvalid;

    public bool Enabled { get { return enabled; } }
    public GamaAgentManualOverrides Manual
    {
        get
        {
            if (manual == null)
            {
                manual = new GamaAgentManualOverrides();
            }

            return manual;
        }
    }

    public bool Matches(GamaAgentMatchContext context)
    {
        if (!enabled)
        {
            return false;
        }

        if (!IsEmptyOrEqual(propertyId, context.PropertyId))
        {
            return false;
        }

        if (!IsEmptyOrEqual(tag, context.Tag))
        {
            return false;
        }

        if (!IsEmptyOrContains(prefabContains, context.PrefabPath))
        {
            return false;
        }

        if (!IsEmptyOrContains(agentNameContains, context.AgentName))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(agentNameRegex))
        {
            return true;
        }

        if (!EnsureRegexCompiled())
        {
            return false;
        }

        return cachedRegex != null && cachedRegex.IsMatch(context.AgentName ?? string.Empty);
    }

    private bool EnsureRegexCompiled()
    {
        string pattern = agentNameRegex ?? string.Empty;
        if (cachedRegex != null && string.Equals(pattern, cachedPattern, StringComparison.Ordinal))
        {
            return !regexInvalid;
        }

        cachedRegex = null;
        cachedPattern = pattern;
        regexInvalid = false;

        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        try
        {
            cachedRegex = new Regex(pattern, RegexOptions.IgnoreCase);
            return true;
        }
        catch (ArgumentException)
        {
            regexInvalid = true;
            Debug.LogWarning("[GAMA] Invalid agentNameRegex on rule '" + label + "': " + pattern);
            return false;
        }
    }

    private static bool IsEmptyOrEqual(string expected, string value)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return string.Equals(expected.Trim(), (value ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmptyOrContains(string fragment, string value)
    {
        if (string.IsNullOrWhiteSpace(fragment))
        {
            return true;
        }

        return !string.IsNullOrEmpty(value) &&
               value.IndexOf(fragment.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

[Serializable]
public class GamaAgentInstanceSettings
{
    [SerializeField] private string agentName;
    [SerializeField] private string propertyId;
    [SerializeField] private string tag;
    [SerializeField] private string prefab;
    [SerializeField] private bool importedVisible = true;
    [SerializeField] private bool importedColorFromAttributes;
    [SerializeField] private Color importedColor = Color.white;
    [SerializeField] private float importedScaleMultiplier = 1f;
    [SerializeField] private Vector3 importedPositionOffset;
    [SerializeField] private Vector3 importedRotationOffsetEuler;
    [SerializeField] private GamaAgentManualOverrides manual = new GamaAgentManualOverrides();

    public string AgentName { get { return agentName; } }
    public GamaAgentManualOverrides Manual
    {
        get
        {
            if (manual == null)
            {
                manual = new GamaAgentManualOverrides();
            }

            return manual;
        }
    }

    public void ImportFrom(string name, PropertiesGAMA property, Attributes attributes, GamaAgentVisualState state)
    {
        agentName = name;
        propertyId = property != null ? property.id : string.Empty;
        tag = property != null ? property.tag : string.Empty;
        prefab = property != null ? property.prefab : string.Empty;
        importedVisible = state.Visible;
        importedColor = state.Color;
        importedScaleMultiplier = state.ScaleMultiplier;
        importedPositionOffset = state.PositionOffset;
        importedRotationOffsetEuler = state.RotationOffsetEuler;

        Color32 attributeColor;
        importedColorFromAttributes = attributes != null && attributes.TryGetColor(out attributeColor);
    }
}

[Serializable]
public class GamaAgentManualOverrides
{
    [Header("Manual Unity corrections")]
    public bool overrideColor;
    public Color color = Color.white;
    public bool overrideScaleMultiplier;
    [Min(0f)] public float scaleMultiplier = 1f;
    public bool overridePositionOffset;
    public Vector3 positionOffset;
    public bool overrideRotationOffset;
    public Vector3 rotationOffsetEuler;
    public bool overrideVisibility;
    public bool visible = true;

    public void ApplyTo(ref GamaAgentVisualState state)
    {
        if (overrideColor)
        {
            state.Color = (Color32)color;
            state.HasColor = true;
            state.HasManualColorOverride = true;
        }

        if (overrideScaleMultiplier)
        {
            state.ScaleMultiplier *= Mathf.Max(0f, scaleMultiplier);
        }

        if (overridePositionOffset)
        {
            state.PositionOffset += positionOffset;
        }

        if (overrideRotationOffset)
        {
            state.RotationOffsetEuler += rotationOffsetEuler;
        }

        if (overrideVisibility)
        {
            state.Visible = visible;
        }
    }

    public bool HasAnyOverride()
    {
        return overrideColor ||
               overrideScaleMultiplier ||
               overridePositionOffset ||
               overrideRotationOffset ||
               overrideVisibility;
    }
}

public struct GamaAgentVisualState
{
    public bool HasColor;
    public bool HasManualColorOverride;
    public bool HasAttributeColor;
    public Color32 Color;
    public float ScaleMultiplier;
    public Vector3 PositionOffset;
    public Vector3 RotationOffsetEuler;
    public bool Visible;
}

public struct GamaAgentMatchContext
{
    public readonly string AgentName;
    public readonly string PropertyId;
    public readonly string Tag;
    public readonly string PrefabPath;

    public GamaAgentMatchContext(string agentName, string propertyId, string tag, string prefabPath)
    {
        AgentName = agentName;
        PropertyId = propertyId;
        Tag = tag;
        PrefabPath = prefabPath;
    }
}
