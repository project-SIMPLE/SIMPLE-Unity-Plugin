using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(SimulationManager), true)]
[CanEditMultipleObjects]
public class SimulationManagerInspector : Editor
{
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
        
        "dayNight", "hotspots", "interactionTags", "gamaAsks", "visualFeedback", "interactionRules",
        
        "enablePrefabPooling", "maxPooledPrefabsPerSignature", "limitAgentUpdatesPerTick", "maxAgentUpdatesPerTick",
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

        GamaSpeciesRenderOverrides asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        if (asset == null)
        {
            EditorGUILayout.HelpBox("Could not load shared GAMA Species Render Overrides asset.", MessageType.Error);
            return;
        }

        bool assetChanged = false;
        List<string> changedSpecies = new List<string>();

        // Draw each species entry
        for (int i = 0; i < count; i++)
        {
            SerializedProperty entry = propertySettings.GetArrayElementAtIndex(i);
            if (entry == null) continue;

            SerializedProperty propertyId = entry.FindPropertyRelative("propertyId");
            SerializedProperty tag = entry.FindPropertyRelative("tag");
            SerializedProperty prefab = entry.FindPropertyRelative("prefab");
            SerializedProperty importedColor = entry.FindPropertyRelative("importedColor");
            SerializedProperty importedBaseScale = entry.FindPropertyRelative("importedBaseScale");

            string speciesName = propertyId != null ? propertyId.stringValue : "(unknown)";
            string tagStr = tag != null && !string.IsNullOrEmpty(tag.stringValue) ? " [" + tag.stringValue + "]" : "";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Species header
            EditorGUILayout.LabelField(speciesName + tagStr, EditorStyles.boldLabel);

            // GAMA Attributes (read-only)
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("GAMA Attributes", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(true);
            if (prefab != null)
            {
                EditorGUILayout.TextField("GAMA Prefab", prefab.stringValue);
            }
            if (importedColor != null)
            {
                EditorGUILayout.ColorField("GAMA Color", importedColor.colorValue);
            }
            if (importedBaseScale != null)
            {
                EditorGUILayout.FloatField("GAMA Base Scale", importedBaseScale.floatValue);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;

            // Unity Overrides
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Unity Overrides", EditorStyles.miniBoldLabel);
            EditorGUI.indentLevel++;
            
            GamaSpeciesRenderOverrideEntry overrideEntry = asset.GetOrCreateEntry(speciesName);

            EditorGUI.BeginChangeCheck();

            // Prefab overrides
            overrideEntry.prefabOverride = (GameObject)EditorGUILayout.ObjectField("Prefab Override", overrideEntry.prefabOverride, typeof(GameObject), false);
            overrideEntry.prefabResourcePath = EditorGUILayout.TextField("Resources Path Override", overrideEntry.prefabResourcePath);

            // Color override
            overrideEntry.overrideColor = EditorGUILayout.Toggle("Override Color", overrideEntry.overrideColor);
            EditorGUI.BeginDisabledGroup(!overrideEntry.overrideColor);
            overrideEntry.color = EditorGUILayout.ColorField("Color", overrideEntry.color);
            EditorGUI.EndDisabledGroup();

            // Scale override
            overrideEntry.scaleMultiplier = EditorGUILayout.FloatField("Scale Multiplier", overrideEntry.scaleMultiplier);
            overrideEntry.scaleMultiplier = Mathf.Max(0f, overrideEntry.scaleMultiplier);

            // Position override
            overrideEntry.positionOffset = EditorGUILayout.Vector3Field("Position Offset", overrideEntry.positionOffset);

            // Rotation override
            overrideEntry.rotationOffsetEuler = EditorGUILayout.Vector3Field("Rotation Offset", overrideEntry.rotationOffsetEuler);

            // Visibility override
            overrideEntry.overrideRuntimeVisibility = EditorGUILayout.Toggle("Override Visibility", overrideEntry.overrideRuntimeVisibility);
            EditorGUI.BeginDisabledGroup(!overrideEntry.overrideRuntimeVisibility);
            overrideEntry.visibleInRuntime = EditorGUILayout.Toggle("Visible", overrideEntry.visibleInRuntime);
            EditorGUI.EndDisabledGroup();

            if (EditorGUI.EndChangeCheck())
            {
                assetChanged = true;
                if (!changedSpecies.Contains(speciesName))
                {
                    changedSpecies.Add(speciesName);
                }

                if (tag != null && !string.IsNullOrWhiteSpace(tag.stringValue) && !changedSpecies.Contains(tag.stringValue))
                {
                    changedSpecies.Add(tag.stringValue);
                }

                Debug.Log($"[GAMA][OVERRIDES] GameManager editing species={speciesName} scale={overrideEntry.scaleMultiplier}");
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (assetChanged)
        {
            EditorUtility.SetDirty(asset);
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
            ApplyRuntimeOverridesIfPlaying(changedSpecies);
        }
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
