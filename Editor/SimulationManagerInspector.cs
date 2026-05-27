using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(SimulationManager), true)]
[CanEditMultipleObjects]
public class SimulationManagerInspector : Editor
{
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";

    private bool showSpeciesPanel = true;
    private bool showRenderingSettings = true;
    private bool showPreviewAndPlaySettings = true;
    private bool showSceneReferences = false;
    private bool showInteractionScenario = false;
    private bool showPerformanceAndStreaming = false;
    private bool showAdvancedDebug = false;

    // We keep a HashSet of explicitly mapped fields so we don't double-draw them
    private HashSet<string> explicitFields = new HashSet<string>
    {
        "m_Script",
        "createAgentEntries", "maxAgentEntries", "logWhenAgentEntriesCapReached", "keepManualAgentEntriesWhenMissing",
        "propertySettings", "applyRuleOverrides", "ruleSettings", "agentSettings",
        "createPropertyEntries", "propertyBindings", "applyKeyTranslations", "keyTranslations",
        "allowResourcesLookup", "allowFileNameFallback", "logMissingPrefabOnce", "keepManualPrefabEntriesWhenMissing",
        
        "streamPrefabsByCameraView", "preferSceneViewCameraInEditor", "keepSelectedPrefabsLoaded", "prefabViewPadding",
        "prefabViewUpdateInterval", "enablePrefabRenderDistance", "globalPrefabRenderDistance", "prefabRenderDistanceHysteresis",
        "enableGpuInstancingForPrefabMaterials",
        
        "primaryRightHandButton", "player", "Ground", "XROrigin", "toFollow", "lightObject", "groupRuntimeAgentsBySpecies",
        "useGamaInitialPlayerPosition", "playerPositionSource", "explicitPlayerPositionSource", "logOutgoingPlayerPosition",
        "rejectSuspiciousPlayerPositions", "suspiciousTeleportDistance",
        
        "dayNight", "hotspots", "interactionTags", "gamaAsks", "visualFeedback", "interactionRules",
        
        "enablePrefabPooling", "maxPooledPrefabsPerSignature", "enableIncrementalImport", "largeSpeciesThreshold",
        "largeGeometryThreshold", "hugeMessageByteThreshold", "skipUnchangedObjects", "largeSpeciesMode",
        "limitAgentUpdatesPerTick", "maxAgentUpdatesPerTick",
        "removeMissingGeometryAgents", "missingTicksBeforeCull",
        
        "logPrefabStreamingStats", "prefabStreamingStatsInterval", "logAgentUpdateBudgetStats", "agentUpdateBudgetStatsInterval",
        "debugOptions"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. GAMA Species Overview and Attributes
        showSpeciesPanel = EditorGUILayout.Foldout(showSpeciesPanel, "GAMA Species Overview and Attributes", true, EditorStyles.foldoutHeader);
        if (showSpeciesPanel)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Detected species and shared visual overrides. These settings are used by both the static preview and the Play runtime.", MessageType.Info);
            DrawSpeciesOverview();
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 2. Rendering Settings
        showRenderingSettings = EditorGUILayout.Foldout(showRenderingSettings, "Rendering Settings", true, EditorStyles.foldoutHeader);
        if (showRenderingSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Controls which agents are rendered based on camera visibility and distance. This improves performance in large GAMA scenes.\nCamera FOV is read from the active Game camera.", MessageType.Info);
            DrawProperty("streamPrefabsByCameraView", "Cull Agents Outside Camera View");
            DrawProperty("preferSceneViewCameraInEditor", "Use Scene View Camera In Editor");
            DrawProperty("keepSelectedPrefabsLoaded", "Keep Selected Agents Visible");
            DrawProperty("prefabViewPadding", "Camera View Padding");
            DrawProperty("prefabViewUpdateInterval", "Visibility Update Interval");
            DrawProperty("enablePrefabRenderDistance", "Enable Max Render Distance");
            DrawProperty("globalPrefabRenderDistance", "Max Render Distance");
            DrawProperty("prefabRenderDistanceHysteresis", "Render Distance Hysteresis");
            DrawProperty("enableGpuInstancingForPrefabMaterials", "Enable GPU Instancing");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 3. Preview and Play Settings
        showPreviewAndPlaySettings = EditorGUILayout.Foldout(showPreviewAndPlaySettings, "Preview and Play Settings", true, EditorStyles.foldoutHeader);
        if (showPreviewAndPlaySettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Controls how the static preview is synchronized with the Play runtime.\n- Static preview is visible in Edit Mode.\n- Static preview is hidden during Play.\n- Species overrides are applied to live agents during Play.", MessageType.Info);
            
            EditorGUI.BeginChangeCheck();
            bool autoHide = EditorPrefs.GetBool("ProjectSimple.GamaUnity.Panel.AutoHidePreviewOnPlay", true);
            autoHide = EditorGUILayout.Toggle("Hide Preview During Play", autoHide);
            
            bool applyToPlay = EditorPrefs.GetBool("ProjectSimple.GamaUnity.Panel.ApplyPreviewSettingsToPlay", true);
            applyToPlay = EditorGUILayout.Toggle("Apply Preview Settings to Play", applyToPlay);

            DrawProperty("useGamaInitialPlayerPosition", "Use GAMA Initial Player Position");
            
            bool liveUpdate = EditorPrefs.GetBool("ProjectSimple.GamaUnity.Panel.AutoUpdatePreview", false);
            liveUpdate = EditorGUILayout.Toggle("Live Update Preview", liveUpdate);
            
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool("ProjectSimple.GamaUnity.Panel.AutoHidePreviewOnPlay", autoHide);
                EditorPrefs.SetBool("ProjectSimple.GamaUnity.Panel.ApplyPreviewSettingsToPlay", applyToPlay);
                EditorPrefs.SetBool("ProjectSimple.GamaUnity.Panel.AutoUpdatePreview", liveUpdate);
            }

            if (GUILayout.Button("Apply Settings to Preview", GUILayout.Height(22f)))
            {
                GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
            }

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 4. Scene References
        showSceneReferences = EditorGUILayout.Foldout(showSceneReferences, "Scene References", true, EditorStyles.foldoutHeader);
        if (showSceneReferences)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("References required by the scene setup and XR interaction workflow.", MessageType.None);
            DrawProperty("primaryRightHandButton");
            DrawProperty("player");
            DrawProperty("Ground");
            DrawProperty("XROrigin");
            DrawProperty("toFollow");
            DrawProperty("lightObject");
            
            EditorGUILayout.Space(4);
            DrawProperty("groupRuntimeAgentsBySpecies", "Group Runtime Agents By Species");
            DrawProperty("playerPositionSource", "Player Position Source");
            DrawProperty("explicitPlayerPositionSource", "Explicit Player Position Source");
            DrawProperty("logOutgoingPlayerPosition", "Log Outgoing Player Position");
            DrawProperty("rejectSuspiciousPlayerPositions", "Reject Suspicious Player Positions");
            DrawProperty("suspiciousTeleportDistance", "Suspicious Teleport Distance");
            
            // Draw any unknown serialized properties here
            DrawUnknownProperties();
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 5. Interaction Scenario
        showInteractionScenario = EditorGUILayout.Foldout(showInteractionScenario, "Interaction Scenario", true, EditorStyles.foldoutHeader);
        if (showInteractionScenario)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Scenario-specific behavior for interactions, hotspots, vehicles, and visual feedback.", MessageType.None);
            DrawProperty("dayNight");
            DrawProperty("hotspots");
            DrawProperty("interactionTags");
            DrawProperty("gamaAsks");
            DrawProperty("visualFeedback");
            DrawProperty("interactionRules");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 6. Performance and Streaming
        showPerformanceAndStreaming = EditorGUILayout.Foldout(showPerformanceAndStreaming, "Performance and Streaming", true, EditorStyles.foldoutHeader);
        if (showPerformanceAndStreaming)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Performance options for large simulations. Keep default values unless profiling shows a bottleneck.", MessageType.None);
            DrawProperty("enablePrefabPooling", "Enable Object Pooling");
            DrawProperty("maxPooledPrefabsPerSignature", "Max Pooled Objects Per Type");
            DrawProperty("enableIncrementalImport", "Enable Incremental Import");
            DrawProperty("largeSpeciesThreshold", "Large Species Threshold");
            DrawProperty("largeGeometryThreshold", "Large Geometry Threshold");
            DrawProperty("hugeMessageByteThreshold", "Huge Message Byte Threshold");
            DrawProperty("skipUnchangedObjects", "Skip Unchanged Objects");
            DrawProperty("largeSpeciesMode", "Large Species Mode");
            DrawProperty("limitAgentUpdatesPerTick", "Limit Agent Updates Per Frame");
            DrawProperty("maxAgentUpdatesPerTick", "Max Agent Updates Per Frame");
            DrawProperty("removeMissingGeometryAgents", "Remove Missing Geometry Agents");
            DrawProperty("missingTicksBeforeCull", "Missing Ticks Before Hide");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // 7. Advanced Debug
        showAdvancedDebug = EditorGUILayout.Foldout(showAdvancedDebug, "Advanced Debug", true, EditorStyles.foldoutHeader);
        if (showAdvancedDebug)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Debug-only settings. Keep disabled during demos unless troubleshooting.", MessageType.Warning);
            DrawProperty("logPrefabStreamingStats");
            DrawProperty("prefabStreamingStatsInterval");
            DrawProperty("logAgentUpdateBudgetStats");
            DrawProperty("agentUpdateBudgetStatsInterval");
            DrawProperty("debugOptions");
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawProperty(string propName, string label = null)
    {
        SerializedProperty prop = serializedObject.FindProperty(propName);
        if (prop != null)
        {
            if (label != null) EditorGUILayout.PropertyField(prop, new GUIContent(label), true);
            else EditorGUILayout.PropertyField(prop, true);
        }
    }

    private void DrawUnknownProperties()
    {
        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (!explicitFields.Contains(iterator.name))
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }

    private void DrawSpeciesOverview()
    {
        SerializedProperty propertySettings = serializedObject.FindProperty("propertySettings");
        if (propertySettings == null || !propertySettings.isArray)
        {
            EditorGUILayout.LabelField("Species will appear after generating a preview or receiving data from GAMA.", EditorStyles.wordWrappedLabel);
            return;
        }

        int count = propertySettings.arraySize;
        if (count == 0)
        {
            EditorGUILayout.LabelField("Species will appear after generating a preview or receiving data from GAMA.", EditorStyles.wordWrappedLabel);
            return;
        }

        if (!TryResolvePreviewOverrideContext(
                out GamaSpeciesRenderOverrides asset,
                out string modelPath,
                out string experimentName))
        {
            EditorGUILayout.HelpBox("Could not load shared GAMA Species Render Overrides asset.", MessageType.Error);
            return;
        }

        AssignOverrideContextToTargetManager(asset, modelPath, experimentName);

        bool assetChanged = false;
        List<string> changedSpecies = new List<string>();
        SimulationManager manager = target as SimulationManager;

        // Draw each species entry
        for (int i = 0; i < count; i++)
        {
            SerializedProperty entry = propertySettings.GetArrayElementAtIndex(i);
            if (entry == null) continue;

            SerializedProperty propertyId = entry.FindPropertyRelative("propertyId");
            SerializedProperty tag = entry.FindPropertyRelative("tag");
            SerializedProperty importedVisible = entry.FindPropertyRelative("importedVisible");
            SerializedProperty importedColor = entry.FindPropertyRelative("importedColor");

            string speciesName = propertyId != null ? propertyId.stringValue : "(unknown)";
            string tagStr = tag != null && !string.IsNullOrEmpty(tag.stringValue) ? " [" + tag.stringValue + "]" : "";
            Color defaultColor = importedColor != null ? importedColor.colorValue : Color.white;
            bool defaultVisible = importedVisible == null || importedVisible.boolValue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Species header
            EditorGUILayout.LabelField(speciesName + tagStr, EditorStyles.boldLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Agent Attributes", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            
            GamaSpeciesRenderOverrideEntry overrideEntry = asset.GetOrCreateEntry(modelPath, experimentName, speciesName);

            EditorGUI.BeginChangeCheck();

            GameObject editedPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab Override", overrideEntry.prefabOverride, typeof(GameObject), false);
            if (editedPrefab != overrideEntry.prefabOverride)
            {
                overrideEntry.prefabOverride = editedPrefab;
                overrideEntry.prefabResourcePath = TryGetResourcesPath(editedPrefab, out string resourcesPath)
                    ? resourcesPath
                    : string.Empty;
            }

            Color editedColor = EditorGUILayout.ColorField("Color", overrideEntry.overrideColor ? overrideEntry.color : defaultColor);
            overrideEntry.overrideColor = !ColorsApproximately(editedColor, defaultColor);
            overrideEntry.color = overrideEntry.overrideColor ? editedColor : defaultColor;

            bool dynamicColorChanged = DrawDynamicColorControls(
                overrideEntry,
                manager,
                speciesName,
                tag != null ? tag.stringValue : string.Empty);

            float editedScale = EditorGUILayout.DelayedFloatField("Scale Multiplier", overrideEntry.GetEffectiveScaleMultiplier());
            editedScale = Mathf.Max(0.0001f, editedScale);
            overrideEntry.overrideScaleMultiplier = Mathf.Abs(editedScale - 1f) > 0.0001f;
            overrideEntry.scaleMultiplier = overrideEntry.overrideScaleMultiplier ? editedScale : 1f;

            overrideEntry.positionOffset = EditorGUILayout.Vector3Field("Position Offset", overrideEntry.GetEffectivePositionOffset());
            overrideEntry.overridePositionOffset = overrideEntry.positionOffset.sqrMagnitude > 0.0001f;

            overrideEntry.rotationOffsetEuler = EditorGUILayout.Vector3Field("Rotation Offset", overrideEntry.GetEffectiveRotationOffsetEuler());
            overrideEntry.overrideRotationOffset = overrideEntry.rotationOffsetEuler.sqrMagnitude > 0.0001f;

            bool editedVisible = EditorGUILayout.Toggle("Visible", overrideEntry.UsesRuntimeVisibilityOverride() ? overrideEntry.GetEffectiveRuntimeVisible() : defaultVisible);
            bool visibilityDiffersFromDefault = editedVisible != defaultVisible;
            overrideEntry.overridePreviewVisibility = visibilityDiffersFromDefault;
            overrideEntry.visibleInPreview = editedVisible;
            overrideEntry.overrideRuntimeVisibility = visibilityDiffersFromDefault;
            overrideEntry.visibleInRuntime = editedVisible;
            overrideEntry.overrideVisibility = visibilityDiffersFromDefault;
            overrideEntry.visible = editedVisible;

            bool rowChanged = EditorGUI.EndChangeCheck() || dynamicColorChanged;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset to GAMA attributes", GUILayout.Width(180f)))
            {
                ResetSpeciesOverrideEntry(overrideEntry, defaultColor, defaultVisible);
                rowChanged = true;
            }
            EditorGUILayout.EndHorizontal();

            if (rowChanged)
            {
                assetChanged = true;
                TrackChangedSpecies(changedSpecies, speciesName, tag != null ? tag.stringValue : string.Empty);
                Debug.Log($"[GAMA][OVERRIDES] GameManager editing species={speciesName} scale={overrideEntry.GetEffectiveScaleMultiplier()}");
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (assetChanged)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
            ApplyRuntimeOverridesIfPlaying(changedSpecies);
        }
    }

    private void AssignOverrideContextToTargetManager(
        GamaSpeciesRenderOverrides asset,
        string modelPath,
        string experimentName)
    {
        SimulationManager manager = target as SimulationManager;
        if (manager == null || asset == null)
        {
            return;
        }

        if (manager.SetSpeciesRenderOverridesContext(asset, modelPath, experimentName))
        {
            EditorUtility.SetDirty(manager);
        }
    }

    private static void TrackChangedSpecies(List<string> changedSpecies, string speciesName, string tag)
    {
        if (changedSpecies == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(speciesName) && !changedSpecies.Contains(speciesName))
        {
            changedSpecies.Add(speciesName);
        }

        if (!string.IsNullOrWhiteSpace(tag) && !changedSpecies.Contains(tag))
        {
            changedSpecies.Add(tag);
        }
    }

    private static bool ColorsApproximately(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.001f &&
               Mathf.Abs(a.g - b.g) < 0.001f &&
               Mathf.Abs(a.b - b.b) < 0.001f &&
               Mathf.Abs(a.a - b.a) < 0.001f;
    }

    private static bool DrawDynamicColorControls(
        GamaSpeciesRenderOverrideEntry entry,
        SimulationManager manager,
        string speciesName,
        string tag)
    {
        if (entry == null)
        {
            return false;
        }

        bool changed = false;
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Dynamic Color", EditorStyles.miniBoldLabel);
        EditorGUI.indentLevel++;

        bool overrideDynamic = EditorGUILayout.Toggle("Override Dynamic Color", entry.overrideDynamicColor);
        if (overrideDynamic != entry.overrideDynamicColor)
        {
            entry.overrideDynamicColor = overrideDynamic;
            changed = true;
        }

        using (new EditorGUI.DisabledScope(!entry.overrideDynamicColor))
        {
            GamaDynamicColorMode mode = (GamaDynamicColorMode)EditorGUILayout.EnumPopup("Dynamic Color Mode", entry.dynamicColorMode);
            if (mode != entry.dynamicColorMode)
            {
                entry.dynamicColorMode = mode;
                changed = true;
            }

            bool attributeChanged;
            string attributeName = DrawDynamicColorAttributeSelector(
                entry.dynamicColorAttribute,
                manager,
                speciesName,
                tag,
                out attributeChanged);
            if (attributeChanged)
            {
                entry.dynamicColorAttribute = attributeName;
                changed = true;
            }

            if (entry.dynamicColorMode == GamaDynamicColorMode.Discrete)
            {
                changed |= DrawDiscreteColorRules(entry);
            }
            else if (entry.dynamicColorMode == GamaDynamicColorMode.Continuous)
            {
                changed |= DrawContinuousColorSettings(entry);
            }
        }

        EditorGUI.indentLevel--;
        return changed;
    }

    private static string DrawDynamicColorAttributeSelector(
        string currentAttribute,
        SimulationManager manager,
        string speciesName,
        string tag,
        out bool changed)
    {
        changed = false;
        currentAttribute = currentAttribute ?? string.Empty;

        string[] detectedAttributes = GetDetectedRuntimeAttributes(manager, speciesName, tag);
        if (detectedAttributes == null || detectedAttributes.Length == 0)
        {
            string editedAttribute = EditorGUILayout.TextField("Attribute Name", currentAttribute);
            changed = editedAttribute != currentAttribute;
            EditorGUILayout.LabelField("Detected Attributes", "None received yet", EditorStyles.miniLabel);
            return editedAttribute;
        }

        List<string> values = new List<string>();
        List<string> labels = new List<string>();
        values.Add(string.Empty);
        labels.Add("(None)");

        bool currentInDetected = string.IsNullOrWhiteSpace(currentAttribute);
        for (int i = 0; i < detectedAttributes.Length; i++)
        {
            string attribute = detectedAttributes[i];
            if (string.IsNullOrWhiteSpace(attribute))
            {
                continue;
            }

            values.Add(attribute);
            labels.Add(attribute);
            if (string.Equals(attribute, currentAttribute, System.StringComparison.OrdinalIgnoreCase))
            {
                currentInDetected = true;
            }
        }

        if (!currentInDetected)
        {
            values.Add(currentAttribute);
            labels.Add(currentAttribute + " (current)");
        }

        int selectedIndex = 0;
        for (int i = 1; i < values.Count; i++)
        {
            if (string.Equals(values[i], currentAttribute, System.StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        int newIndex = EditorGUILayout.Popup("Attribute Name", selectedIndex, labels.ToArray());
        string selectedAttribute = newIndex > 0 && newIndex < values.Count ? values[newIndex] : string.Empty;
        changed = selectedAttribute != currentAttribute;
        return selectedAttribute;
    }

    private static string[] GetDetectedRuntimeAttributes(SimulationManager manager, string speciesName, string tag)
    {
        if (manager == null)
        {
            return new string[0];
        }

        HashSet<string> names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        AddDetectedRuntimeAttributes(manager, speciesName, names);
        AddDetectedRuntimeAttributes(manager, tag, names);

        if (names.Count == 0)
        {
            return new string[0];
        }

        List<string> sortedNames = new List<string>(names);
        sortedNames.Sort(System.StringComparer.OrdinalIgnoreCase);
        return sortedNames.ToArray();
    }

    private static void AddDetectedRuntimeAttributes(SimulationManager manager, string speciesName, HashSet<string> names)
    {
        if (manager == null || string.IsNullOrWhiteSpace(speciesName) || names == null)
        {
            return;
        }

        string[] detected = manager.GetRuntimeAttributeNamesForSpecies(speciesName);
        if (detected == null)
        {
            return;
        }

        for (int i = 0; i < detected.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(detected[i]))
            {
                names.Add(detected[i]);
            }
        }
    }

    private static bool DrawDiscreteColorRules(GamaSpeciesRenderOverrideEntry entry)
    {
        bool changed = false;
        if (entry.discreteColorRules == null)
        {
            entry.discreteColorRules = new List<GamaDiscreteColorRule>();
            changed = true;
        }

        EditorGUILayout.LabelField("Discrete Rules", EditorStyles.miniLabel);
        int removeIndex = -1;
        for (int i = 0; i < entry.discreteColorRules.Count; i++)
        {
            GamaDiscreteColorRule rule = entry.discreteColorRules[i];
            if (rule == null)
            {
                rule = new GamaDiscreteColorRule();
                entry.discreteColorRules[i] = rule;
                changed = true;
            }

            EditorGUILayout.BeginHorizontal();
            string value = EditorGUILayout.TextField(rule.value ?? string.Empty);
            if (value != rule.value)
            {
                rule.value = value;
                changed = true;
            }

            Color color = EditorGUILayout.ColorField(rule.color, GUILayout.MaxWidth(90f));
            if (!ColorsApproximately(color, rule.color))
            {
                rule.color = color;
                changed = true;
            }

            if (GUILayout.Button("-", GUILayout.Width(24f)))
            {
                removeIndex = i;
            }
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            entry.discreteColorRules.RemoveAt(removeIndex);
            changed = true;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Rule"))
        {
            entry.discreteColorRules.Add(new GamaDiscreteColorRule());
            changed = true;
        }

        if (GUILayout.Button("Add true/false rules"))
        {
            AddOrUpdateDiscreteRule(entry, "false", Color.green);
            AddOrUpdateDiscreteRule(entry, "true", Color.red);
            changed = true;
        }
        EditorGUILayout.EndHorizontal();

        return changed;
    }

    private static bool DrawContinuousColorSettings(GamaSpeciesRenderOverrideEntry entry)
    {
        bool changed = false;

        Color baseColor = EditorGUILayout.ColorField("Base Color", entry.continuousBaseColor);
        if (!ColorsApproximately(baseColor, entry.continuousBaseColor))
        {
            entry.continuousBaseColor = baseColor;
            changed = true;
        }

        float min = EditorGUILayout.FloatField("Min Value", entry.continuousMinValue);
        if (!Mathf.Approximately(min, entry.continuousMinValue))
        {
            entry.continuousMinValue = min;
            changed = true;
        }

        float max = EditorGUILayout.FloatField("Max Value", entry.continuousMaxValue);
        if (!Mathf.Approximately(max, entry.continuousMaxValue))
        {
            entry.continuousMaxValue = max;
            changed = true;
        }

        bool invert = EditorGUILayout.Toggle("Invert", entry.continuousInvert);
        if (invert != entry.continuousInvert)
        {
            entry.continuousInvert = invert;
            changed = true;
        }

        float light = EditorGUILayout.Slider("Light Amount", entry.continuousLightAmount, 0f, 1f);
        if (!Mathf.Approximately(light, entry.continuousLightAmount))
        {
            entry.continuousLightAmount = light;
            changed = true;
        }

        float dark = EditorGUILayout.Slider("Dark Amount", entry.continuousDarkAmount, 0f, 1f);
        if (!Mathf.Approximately(dark, entry.continuousDarkAmount))
        {
            entry.continuousDarkAmount = dark;
            changed = true;
        }

        if (GUILayout.Button("Configure 0..1 green gradient"))
        {
            entry.continuousBaseColor = Color.green;
            entry.continuousMinValue = 0f;
            entry.continuousMaxValue = 1f;
            entry.continuousInvert = false;
            changed = true;
        }

        return changed;
    }

    private static void AddOrUpdateDiscreteRule(GamaSpeciesRenderOverrideEntry entry, string value, Color color)
    {
        if (entry.discreteColorRules == null)
        {
            entry.discreteColorRules = new List<GamaDiscreteColorRule>();
        }

        for (int i = 0; i < entry.discreteColorRules.Count; i++)
        {
            GamaDiscreteColorRule rule = entry.discreteColorRules[i];
            if (rule != null && string.Equals(rule.value, value, System.StringComparison.OrdinalIgnoreCase))
            {
                rule.color = color;
                return;
            }
        }

        entry.discreteColorRules.Add(new GamaDiscreteColorRule
        {
            value = value,
            color = color
        });
    }

    private static bool TryGetResourcesPath(GameObject prefab, out string resourcesPath)
    {
        resourcesPath = string.Empty;
        if (prefab == null)
        {
            return false;
        }

        string assetPath = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return false;
        }

        assetPath = assetPath.Replace('\\', '/');
        const string resourcesSegment = "/Resources/";
        int resourcesIndex = assetPath.IndexOf(resourcesSegment, System.StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
        {
            resourcesPath = assetPath.Substring(resourcesIndex + resourcesSegment.Length);
        }
        else if (assetPath.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
        {
            resourcesPath = assetPath.Substring("Resources/".Length);
        }
        else
        {
            return false;
        }

        const string prefabExtension = ".prefab";
        if (resourcesPath.EndsWith(prefabExtension, System.StringComparison.OrdinalIgnoreCase))
        {
            resourcesPath = resourcesPath.Substring(0, resourcesPath.Length - prefabExtension.Length);
        }

        resourcesPath = resourcesPath.Trim('/');
        return !string.IsNullOrWhiteSpace(resourcesPath);
    }

    private static void ResetSpeciesOverrideEntry(
        GamaSpeciesRenderOverrideEntry entry,
        Color defaultColor,
        bool defaultVisible)
    {
        if (entry == null)
        {
            return;
        }

        entry.prefabOverride = null;
        entry.materialOverride = null;
        entry.prefabResourcePath = string.Empty;

        entry.overrideColor = false;
        entry.color = defaultColor;

        entry.overrideDynamicColor = false;
        entry.dynamicColorMode = GamaDynamicColorMode.None;
        entry.dynamicColorAttribute = string.Empty;
        if (entry.discreteColorRules != null)
        {
            entry.discreteColorRules.Clear();
        }
        entry.continuousBaseColor = Color.green;
        entry.continuousMinValue = 0f;
        entry.continuousMaxValue = 1f;
        entry.continuousInvert = false;
        entry.continuousLightAmount = 0.45f;
        entry.continuousDarkAmount = 0.45f;
        entry.fallbackToStaticColor = true;

        entry.overrideScaleMultiplier = false;
        entry.scaleMultiplier = 1f;

        entry.overridePositionOffset = false;
        entry.positionOffset = Vector3.zero;

        entry.overrideRotationOffset = false;
        entry.rotationOffsetEuler = Vector3.zero;

        entry.overridePreviewVisibility = false;
        entry.visibleInPreview = defaultVisible;
        entry.overrideRuntimeVisibility = false;
        entry.visibleInRuntime = defaultVisible;
        entry.overrideVisibility = false;
        entry.visible = defaultVisible;

        entry.renderMode = GamaSpeciesRenderMode.Default;
        entry.notesDebug = string.Empty;
    }

    private static bool TryResolvePreviewOverrideContext(
        out GamaSpeciesRenderOverrides asset,
        out string modelPath,
        out string experimentName)
    {
        asset = null;
        modelPath = string.Empty;
        experimentName = string.Empty;

        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager != null &&
            manager.TryGetSpeciesRenderOverridesContext(out asset, out modelPath, out experimentName) &&
            asset != null &&
            (!EditorApplication.isPlaying || !string.IsNullOrWhiteSpace(modelPath) || !string.IsNullOrWhiteSpace(experimentName)))
        {
            return true;
        }

        GamaPreviewSession session = FindCurrentPreviewSession();
        if (session != null)
        {
            asset = session.speciesOverrides != null
                ? session.speciesOverrides
                : GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();

            if (asset != null && session.speciesOverrides == null)
            {
                session.speciesOverrides = asset;
                EditorUtility.SetDirty(session);
            }

            modelPath = session.modelPath ?? string.Empty;
            experimentName = session.experimentName ?? string.Empty;
            return asset != null;
        }

        asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        return asset != null;
    }

    private static GamaPreviewSession FindCurrentPreviewSession()
    {
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        GamaPreviewSession fallback = null;
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (session == null)
            {
                continue;
            }

            if (session.useThisPreviewForPlay && !session.stale)
            {
                return session;
            }

            if (fallback == null)
            {
                fallback = session;
            }

            if (!session.stale && session.gameObject != null && session.gameObject.name == StaticPreviewRootName)
            {
                fallback = session;
            }
        }

        return fallback;
    }

    private void ApplyRuntimeOverridesIfPlaying(List<string> speciesNames)
    {
        if (!EditorApplication.isPlaying || speciesNames == null || speciesNames.Count == 0)
        {
            return;
        }

        GamaRuntimePreviewOverrideApplier.RefreshNow();
        SimulationManager manager = target as SimulationManager;
        if (manager == null)
        {
            manager = Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        }

        if (manager == null)
        {
            return;
        }

        for (int i = 0; i < speciesNames.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(speciesNames[i]))
            {
                manager.ApplyRuntimeSpeciesOverrideNow(speciesNames[i]);
            }
        }
    }
}
