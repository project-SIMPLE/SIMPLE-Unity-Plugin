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

            bool rowChanged = EditorGUI.EndChangeCheck();

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
