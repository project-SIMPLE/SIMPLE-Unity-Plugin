using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GamaSpeciesWizard), true)]
[CanEditMultipleObjects]
public class GamaSpeciesWizardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        
        serializedObject.Update();
        DrawDefaultInspector();
        
        bool changed = serializedObject.ApplyModifiedProperties() || EditorGUI.EndChangeCheck();

        if (changed)
        {
            foreach (var t in targets)
            {
                GamaSpeciesWizard wizard = t as GamaSpeciesWizard;
                if (wizard != null)
                {
                    wizard.SaveCurrentSettingsToAsset();
                }
            }

            GamaEditorPreviewOverrideApplier.ScheduleApplyOverridesToCurrentPreview();
        }

        if (GUILayout.Button("Apply To Preview Now", GUILayout.Height(24f)))
        {
            foreach (var t in targets)
            {
                GamaSpeciesWizard wizard = t as GamaSpeciesWizard;
                if (wizard != null)
                {
                    wizard.SaveCurrentSettingsToAsset();
                }
            }
            GamaEditorPreviewOverrideApplier.ApplyOverridesToCurrentPreview();
        }
    }
}
