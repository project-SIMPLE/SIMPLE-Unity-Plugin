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

        DrawPropertiesExcluding(serializedObject, excludedFields);

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

        EditorGUILayout.HelpBox(
            "GAMA values are read-only references. Enable an override to edit the Unity value applied at runtime.",
            MessageType.None);

        // Draw each species entry
        for (int i = 0; i < count; i++)
        {
            SerializedProperty entry = propertySettings.GetArrayElementAtIndex(i);
            if (entry == null)
            {
                continue;
            }

            SerializedProperty propertyId = entry.FindPropertyRelative("propertyId");
            SerializedProperty tag = entry.FindPropertyRelative("tag");
            SerializedProperty prefab = entry.FindPropertyRelative("prefab");
            SerializedProperty importedColor = entry.FindPropertyRelative("importedColor");
            SerializedProperty importedBaseScale = entry.FindPropertyRelative("importedBaseScale");
            SerializedProperty manual = entry.FindPropertyRelative("manual");

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

            // Manual overrides
            if (manual != null)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Unity Overrides", EditorStyles.miniBoldLabel);

                // Color override
                SerializedProperty overrideColor = manual.FindPropertyRelative("overrideColor");
                SerializedProperty colorProp = manual.FindPropertyRelative("color");
                if (overrideColor != null && colorProp != null)
                {
                    EditorGUILayout.PropertyField(overrideColor, new GUIContent("Override Color"));
                    EditorGUI.BeginDisabledGroup(!overrideColor.boolValue);
                    colorProp.colorValue = EditorGUILayout.ColorField("Color Override", colorProp.colorValue);
                    EditorGUI.EndDisabledGroup();
                }

                // Scale override
                SerializedProperty overrideScale = manual.FindPropertyRelative("overrideScaleMultiplier");
                SerializedProperty scaleProp = manual.FindPropertyRelative("scaleMultiplier");
                if (overrideScale != null && scaleProp != null)
                {
                    EditorGUILayout.PropertyField(overrideScale, new GUIContent("Override Scale"));
                    EditorGUI.BeginDisabledGroup(!overrideScale.boolValue);
                    scaleProp.floatValue = EditorGUILayout.FloatField("Scale Multiplier", Mathf.Max(0f, scaleProp.floatValue));
                    EditorGUI.EndDisabledGroup();
                }

                // Position override
                SerializedProperty overridePosition = manual.FindPropertyRelative("overridePositionOffset");
                SerializedProperty positionProp = manual.FindPropertyRelative("positionOffset");
                if (overridePosition != null && positionProp != null)
                {
                    EditorGUILayout.PropertyField(overridePosition, new GUIContent("Override Position Offset"));
                    EditorGUI.BeginDisabledGroup(!overridePosition.boolValue);
                    EditorGUILayout.PropertyField(positionProp, new GUIContent("Position Offset"));
                    EditorGUI.EndDisabledGroup();
                }

                // Rotation override
                SerializedProperty overrideRotation = manual.FindPropertyRelative("overrideRotationOffset");
                SerializedProperty rotationProp = manual.FindPropertyRelative("rotationOffsetEuler");
                if (overrideRotation != null && rotationProp != null)
                {
                    EditorGUILayout.PropertyField(overrideRotation, new GUIContent("Override Rotation Offset"));
                    EditorGUI.BeginDisabledGroup(!overrideRotation.boolValue);
                    EditorGUILayout.PropertyField(rotationProp, new GUIContent("Rotation Offset Euler"));
                    EditorGUI.EndDisabledGroup();
                }

                // Visibility override
                SerializedProperty overrideVis = manual.FindPropertyRelative("overrideVisibility");
                SerializedProperty visProp = manual.FindPropertyRelative("visible");
                if (overrideVis != null && visProp != null)
                {
                    EditorGUILayout.PropertyField(overrideVis, new GUIContent("Override Visibility"));
                    EditorGUI.BeginDisabledGroup(!overrideVis.boolValue);
                    visProp.boolValue = EditorGUILayout.Toggle("Visible", visProp.boolValue);
                    EditorGUI.EndDisabledGroup();
                }
            }

            // Prefab override (stored directly on SimulationManager)
            DrawPrefabOverrideForSpecies(speciesName);

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

    }

    private void DrawPrefabOverrideForSpecies(string propertyId)
    {
        if (string.IsNullOrEmpty(propertyId))
        {
            return;
        }

        SerializedProperty bindings = serializedObject.FindProperty("propertyBindings");
        if (bindings == null || !bindings.isArray)
        {
            return;
        }

        for (int i = 0; i < bindings.arraySize; i++)
        {
            SerializedProperty binding = bindings.GetArrayElementAtIndex(i);
            if (binding == null)
            {
                continue;
            }

            SerializedProperty bindPropId = binding.FindPropertyRelative("propertyId");
            if (bindPropId == null ||
                !string.Equals(bindPropId.stringValue, propertyId, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SerializedProperty unityPrefab = binding.FindPropertyRelative("unityPrefab");
            SerializedProperty unityResourcesPath = binding.FindPropertyRelative("unityResourcesPath");

            if (unityPrefab != null)
            {
                EditorGUILayout.PropertyField(unityPrefab, new GUIContent("Prefab Override"));
            }

            if (unityResourcesPath != null)
            {
                EditorGUILayout.PropertyField(unityResourcesPath, new GUIContent("Resources Path Override"));
            }

            return;
        }

        // No binding found for this species
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Prefab Override", null, typeof(GameObject), false);
        EditorGUI.EndDisabledGroup();
    }

    private void DrawPropertiesExcluding(SerializedObject obj, params string[] excludedProperties)
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
