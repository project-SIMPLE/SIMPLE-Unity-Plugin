using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimulationManager), true)]
[CanEditMultipleObjects]
public class SimulationManagerInspector : Editor
{
    private bool showSpeciesPanel = true;
    private bool showAgentSettings = false;
    private bool showPrefabSettings = false;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        string[] excludedFields = new string[] {
            "m_Script",
            "createAgentEntries", "maxAgentEntries", "logWhenAgentEntriesCapReached", "keepManualAgentEntriesWhenMissing",
            "propertySettings", "applyRuleOverrides", "ruleSettings", "agentSettings",
            "createPropertyEntries", "propertyBindings", "applyKeyTranslations", "keyTranslations",
            "allowResourcesLookup", "allowFileNameFallback", "logMissingPrefabOnce", "keepManualPrefabEntriesWhenMissing"
        };

        DrawSerializedPropertiesExcluding(serializedObject, excludedFields);

        EditorGUILayout.Space(12);

        // ====================== SPECIES PANEL ======================
        showSpeciesPanel = EditorGUILayout.Foldout(showSpeciesPanel, "GAMA Species Overview", true, EditorStyles.foldoutHeader);
        if (showSpeciesPanel)
        {
            EditorGUI.indentLevel++;
            DrawSpeciesOverview();
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8);

        // ====================== AGENT SCENE SETTINGS ======================
        showAgentSettings = EditorGUILayout.Foldout(showAgentSettings, "Agent Visual Settings (Advanced)", true, EditorStyles.foldoutHeader);
        if (showAgentSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("createAgentEntries"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("maxAgentEntries"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("logWhenAgentEntriesCapReached"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keepManualAgentEntriesWhenMissing"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("propertySettings"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyRuleOverrides"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ruleSettings"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("agentSettings"));
            EditorGUI.indentLevel--;
        }

        // ====================== PREFAB SCENE SETTINGS ======================
        showPrefabSettings = EditorGUILayout.Foldout(showPrefabSettings, "Prefab Resolution Settings (Advanced)", true, EditorStyles.foldoutHeader);
        if (showPrefabSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("createPropertyEntries"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keepManualPrefabEntriesWhenMissing"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("propertyBindings"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyKeyTranslations"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("keyTranslations"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowResourcesLookup"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("allowFileNameFallback"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("logMissingPrefabOnce"));
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSpeciesOverview()
    {
        SerializedProperty propertySettings = serializedObject.FindProperty("propertySettings");
        if (propertySettings == null || !propertySettings.isArray)
        {
            EditorGUILayout.HelpBox("No species data available. Start a simulation to populate species from GAMA.", MessageType.Info);
            return;
        }

        int count = propertySettings.arraySize;
        if (count == 0)
        {
            EditorGUILayout.HelpBox("No species detected yet. Connect to GAMA middleware and start a simulation.", MessageType.Info);
            return;
        }

        GamaSpeciesRenderOverrides asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        if (asset == null)
        {
            EditorGUILayout.HelpBox("Could not load shared GAMA Species Render Overrides asset.", MessageType.Error);
            return;
        }

        EditorGUILayout.HelpBox(
            "Values below are stored in the shared GamaSpeciesRenderOverrides asset. They apply to both the Static Preview and the Runtime Simulation.",
            MessageType.None);

        bool assetChanged = false;

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

            // Imported values (read-only info)
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

            // Shared manual overrides
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Shared Overrides", EditorStyles.miniBoldLabel);

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
                Debug.Log($"[GAMA][OVERRIDES] GameManager editing species={speciesName} scale={overrideEntry.scaleMultiplier}");
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (assetChanged)
        {
            Debug.Log($"[GAMA][OVERRIDES] source asset = {AssetDatabase.GetAssetPath(asset)}");
            EditorUtility.SetDirty(asset);
            Debug.Log("[GAMA][PREVIEW][AUTO] Schedule requested from GameManager inspector");
            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        }
    }

    private void DrawSerializedPropertiesExcluding(SerializedObject obj, params string[] excludedProperties)
    {
        SerializedProperty iterator = obj.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            bool excluded = false;
            for (int i = 0; i < excludedProperties.Length; i++)
            {
                if (iterator.name == excludedProperties[i])
                {
                    excluded = true;
                    break;
                }
            }

            if (!excluded)
            {
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }
}
