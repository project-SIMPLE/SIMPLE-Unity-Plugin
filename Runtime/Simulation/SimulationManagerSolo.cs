using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class SimulationManagerSolo : SimulationManager
{
    // -------------------------------------------------------------------------
    // Legacy compatibility
    // -------------------------------------------------------------------------
    // Keeps the old serialized field alive if existing scenes already reference it.
    // Prefer using Day Night > Target Light in the Inspector.
    [HideInInspector]
    public GameObject lightObject;

    protected bool isNight = false;

    // -------------------------------------------------------------------------
    // Inspector sections
    // -------------------------------------------------------------------------

    [SerializeField]
    [Tooltip("Controls the simple day/night toggle used by the main interaction button.")]
    private DayNightSection dayNight = new DayNightSection();

    [SerializeField]
    [Tooltip("Defines how GAMA hotspots are detected and initialized after geometry loading.")]
    private HotspotSection hotspots = new HotspotSection();

    [SerializeField]
    [Tooltip("Defines which Unity tags are treated as selectable objects or removable vehicles.")]
    private InteractionTagsSection interactionTags = new InteractionTagsSection();

    [SerializeField]
    [Tooltip("Defines the names of executable asks sent back to GAMA.")]
    private GamaAskSection gamaAsks = new GamaAskSection();

    [SerializeField]
    [Tooltip("Controls the visual feedback applied during hover and selection interactions.")]
    private VisualFeedbackSection visualFeedback = new VisualFeedbackSection();

    [SerializeField]
    [Tooltip("Controls which interactions are enabled during Play Mode.")]
    private InteractionRulesSection interactionRules = new InteractionRulesSection();

    [SerializeField]
    [Tooltip("Optional debug logs for demo or troubleshooting.")]
    private DebugSection debugOptions = new DebugSection();

    // -------------------------------------------------------------------------
    // Foldout data sections
    // -------------------------------------------------------------------------

    [Serializable]
    private sealed class DayNightSection
    {
        [Header("Day / Night Toggle")]

        [Tooltip("If enabled, the main button toggles the light object on and off.")]
        public bool enableDayNightToggle = true;

        [Tooltip("Light or parent object that should be disabled when night mode is active.")]
        public GameObject targetLight;

        [Tooltip("Initial night mode state applied after geometry loading.")]
        public bool startInNightMode = false;

        [Tooltip("If true, the target light is active during the day and inactive during the night.")]
        public bool lightEnabledDuringDay = true;
    }

    [Serializable]
    private sealed class HotspotSection
    {
        [Header("Hotspot Initialization")]

        [Tooltip("If enabled, objects listed in parameters.hotspots are selected after geometry loading.")]
        public bool initializeHotspotsFromGamaParameters = true;

        [Tooltip("Only objects with this tag can be initialized as hotspots.")]
        public string hotspotTag = "selectable";

        [Tooltip("Color applied to objects that are already selected as hotspots.")]
        public Color initialSelectedColor = Color.red;
    }

    [Serializable]
    private sealed class InteractionTagsSection
    {
        [Header("Unity Tags")]

        [Tooltip("Objects with this tag can be selected or unselected as hotspots.")]
        public string selectableTag = "selectable";

        [Tooltip("Objects with this tag are treated as cars and can be removed through GAMA.")]
        public string carTag = "car";

        [Tooltip("Objects with this tag are treated as motorcycles and can be removed through GAMA.")]
        public string motorcycleTag = "moto";
    }

    [Serializable]
    private sealed class GamaAskSection
    {
        [Header("Executable Ask Names")]

        [Tooltip("Executable ask sent to GAMA when a hotspot is selected or unselected.")]
        public string updateHotspotAsk = "update_hotspot";

        [Tooltip("Executable ask sent to GAMA when a vehicle is selected for removal.")]
        public string removeVehicleAsk = "remove_vehicle";

        [Tooltip("Argument key used to send the selected Unity object name to GAMA.")]
        public string idArgumentKey = "id";
    }

    [Serializable]
    private sealed class VisualFeedbackSection
    {
        [Header("Interaction Colors")]

        [Tooltip("Color applied while hovering a selectable object or vehicle.")]
        public Color hoverColor = Color.blue;

        [Tooltip("Color applied to selected hotspot objects.")]
        public Color selectedHotspotColor = Color.red;

        [Tooltip("Color applied to unselected hotspot objects.")]
        public Color unselectedHotspotColor = Color.gray;

        [Tooltip("Color restored on vehicles when hover exits.")]
        public Color vehicleDefaultColor = Color.white;

        [Tooltip("If enabled, vehicle color is restored when hover exits.")]
        public bool restoreVehicleColorOnHoverExit = true;
    }

    [Serializable]
    private sealed class InteractionRulesSection
    {
        [Header("Interaction Rules")]

        [Tooltip("Allow hotspot selection and unselection.")]
        public bool allowHotspotSelection = true;

        [Tooltip("Allow vehicle removal through GAMA ask messages.")]
        public bool allowVehicleRemoval = true;

        [Tooltip("If enabled, interactions are rate-limited using timeWithoutInteraction.")]
        public bool useInteractionCooldown = true;

        [Tooltip("If enabled, removed vehicles are hidden locally immediately after the GAMA ask is sent.")]
        public bool hideVehicleImmediatelyAfterAsk = true;
    }

    [Serializable]
    private sealed class DebugSection
    {
        [Header("Debug Logs")]

        [Tooltip("Log interactions ignored because of missing objects, disabled rules, or cooldown.")]
        public bool logIgnoredInteractions = false;

        [Tooltip("Log executable asks sent to GAMA.")]
        public bool logGamaAsks = false;

        [Tooltip("Log hotspot initialization after geometry loading.")]
        public bool logHotspotInitialization = false;
    }

    // -------------------------------------------------------------------------
    // SimulationManager hooks
    // -------------------------------------------------------------------------

    protected override void TriggerMainButton()
    {
        if (!dayNight.enableDayNightToggle)
        {
            LogIgnored("Main button ignored because day/night toggle is disabled.");
            return;
        }

        isNight = !isNight;
        ApplyDayNightState();
    }

    protected override void AdditionalInitAfterGeomLoading()
    {
        isNight = dayNight.startInNightMode;
        ApplyDayNightState();

        InitializeHotspots();
    }

    protected override void ManageOtherInformation()
    {
        // Reserved for scenario-specific data coming from GAMA.
    }

    protected override void OtherUpdate()
    {
        // Reserved for scenario-specific per-frame logic.
    }

    protected override void ManageOtherMessages(string content)
    {
        // Reserved for scenario-specific custom WebSocket messages.
    }

    // -------------------------------------------------------------------------
    // XR interaction hooks
    // -------------------------------------------------------------------------

    protected override void HoverEnterInteraction(HoverEnterEventArgs ev)
    {
        GameObject obj = GetInteractionObject(ev);
        if (obj == null)
        {
            LogIgnored("Hover enter ignored because the interactable object is null.");
            return;
        }

        if (IsSelectable(obj) || IsVehicle(obj))
        {
            ApplyColor(obj, visualFeedback.hoverColor);
        }
    }

    protected override void HoverExitInteraction(HoverExitEventArgs ev)
    {
        GameObject obj = GetInteractionObject(ev);
        if (obj == null)
        {
            LogIgnored("Hover exit ignored because the interactable object is null.");
            return;
        }

        if (IsSelectable(obj))
        {
            bool isSelected = SelectedObjects.Contains(obj);
            ApplyColor(obj, isSelected ? visualFeedback.selectedHotspotColor : visualFeedback.unselectedHotspotColor);
            return;
        }

        if (IsVehicle(obj) && visualFeedback.restoreVehicleColorOnHoverExit)
        {
            ApplyColor(obj, visualFeedback.vehicleDefaultColor);
        }
    }

    protected override void SelectInteraction(SelectEnterEventArgs ev)
    {
        if (!CanInteractNow())
        {
            LogIgnored("Selection ignored because the interaction cooldown is active.");
            return;
        }

        GameObject obj = GetInteractionObject(ev);
        if (obj == null)
        {
            LogIgnored("Selection ignored because the interactable object is null.");
            return;
        }

        bool handled = false;

        if (IsSelectable(obj))
        {
            handled = TryToggleHotspot(obj);
        }
        else if (IsVehicle(obj))
        {
            handled = TryRemoveVehicle(obj);
        }
        else
        {
            LogIgnored("Selection ignored because object has no supported interaction tag: " + obj.name);
        }

        if (handled && interactionRules.useInteractionCooldown)
        {
            remainingTime = timeWithoutInteraction;
        }
    }

    // -------------------------------------------------------------------------
    // Scenario logic
    // -------------------------------------------------------------------------

    private void ApplyDayNightState()
    {
        GameObject targetLight = GetTargetLight();
        if (targetLight == null)
        {
            LogIgnored("Day/night state changed but no target light is assigned.");
            return;
        }

        bool shouldBeActive = dayNight.lightEnabledDuringDay ? !isNight : isNight;
        targetLight.SetActive(shouldBeActive);
    }

    private GameObject GetTargetLight()
    {
        if (dayNight.targetLight != null)
        {
            return dayNight.targetLight;
        }

        return lightObject;
    }

    private void InitializeHotspots()
    {
        if (!hotspots.initializeHotspotsFromGamaParameters)
        {
            return;
        }

        if (parameters == null || parameters.hotspots == null || parameters.hotspots.Count == 0)
        {
            LogIgnored("No GAMA hotspots found in parameters.");
            return;
        }

        GameObject[] candidates = GamaSceneUtility.FindGameObjectsWithTag(hotspots.hotspotTag);
        int initializedCount = 0;

        foreach (GameObject candidate in candidates)
        {
            if (candidate == null)
            {
                continue;
            }

            if (!parameters.hotspots.Contains(candidate.name))
            {
                continue;
            }

            if (!SelectedObjects.Contains(candidate))
            {
                SelectedObjects.Add(candidate);
            }

            ApplyColor(candidate, hotspots.initialSelectedColor);
            initializedCount++;
        }

        if (debugOptions.logHotspotInitialization)
        {
            Debug.Log("[GAMA][SOLO] Initialized " + initializedCount + " hotspot(s) from GAMA parameters.");
        }
    }

    private bool TryToggleHotspot(GameObject hotspot)
    {
        if (!interactionRules.allowHotspotSelection)
        {
            LogIgnored("Hotspot selection ignored because it is disabled.");
            return false;
        }

        bool isNewSelection = !SelectedObjects.Contains(hotspot);

        SendAskToGama(gamaAsks.updateHotspotAsk, hotspot.name);

        if (isNewSelection)
        {
            SelectedObjects.Add(hotspot);
        }
        else
        {
            SelectedObjects.Remove(hotspot);
        }

        ApplyColor(hotspot, isNewSelection ? visualFeedback.selectedHotspotColor : visualFeedback.unselectedHotspotColor);
        return true;
    }

    private bool TryRemoveVehicle(GameObject vehicle)
    {
        if (!interactionRules.allowVehicleRemoval)
        {
            LogIgnored("Vehicle removal ignored because it is disabled.");
            return false;
        }

        SendAskToGama(gamaAsks.removeVehicleAsk, vehicle.name);

        if (interactionRules.hideVehicleImmediatelyAfterAsk)
        {
            vehicle.SetActive(false);
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private bool CanInteractNow()
    {
        if (!interactionRules.useInteractionCooldown)
        {
            return true;
        }

        return remainingTime <= 0.0;
    }

    private GameObject GetInteractionObject(BaseInteractionEventArgs ev)
    {
        if (ev == null || ev.interactableObject == null || ev.interactableObject.transform == null)
        {
            return null;
        }

        return ev.interactableObject.transform.gameObject;
    }

    private bool IsSelectable(GameObject obj)
    {
        return HasTag(obj, interactionTags.selectableTag);
    }

    private bool IsVehicle(GameObject obj)
    {
        return HasTag(obj, interactionTags.carTag) || HasTag(obj, interactionTags.motorcycleTag);
    }

    private static bool HasTag(GameObject obj, string tagName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        return string.Equals(obj.tag, tagName, StringComparison.Ordinal);
    }

    private void SendAskToGama(string askName, string objectId)
    {
        if (string.IsNullOrWhiteSpace(askName))
        {
            LogIgnored("Cannot send GAMA ask because the ask name is empty.");
            return;
        }

        if (ConnectionManager.Instance == null)
        {
            LogIgnored("Cannot send GAMA ask because ConnectionManager.Instance is null.");
            return;
        }

        Dictionary<string, string> args = new Dictionary<string, string>
        {
            { gamaAsks.idArgumentKey, objectId }
        };

        ConnectionManager.Instance.SendExecutableAsk(askName, args);

        if (debugOptions.logGamaAsks)
        {
            Debug.Log("[GAMA][SOLO] Sent executable ask '" + askName + "' with " + gamaAsks.idArgumentKey + "=" + objectId);
        }
    }

    private void ApplyColor(GameObject obj, Color color)
    {
        if (obj == null)
        {
            return;
        }

        try
        {
            SimulationManagerSolo.ChangeColor(obj, color);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[GAMA][SOLO] Failed to apply color to " + obj.name + ": " + exception.GetBaseException().Message);
        }
    }

    private void LogIgnored(string message)
    {
        if (!debugOptions.logIgnoredInteractions)
        {
            return;
        }

        Debug.Log("[GAMA][SOLO] " + message);
    }
}