using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(GamaSpeciesWizard), true)]
[CanEditMultipleObjects]
public class GamaSpeciesWizardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        NormalizeSelectedWizardContainerScales();

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

    private void NormalizeSelectedWizardContainerScales()
    {
        foreach (var t in targets)
        {
            GamaSpeciesWizard wizard = t as GamaSpeciesWizard;
            if (wizard == null || wizard.transform == null)
            {
                continue;
            }

            if ((wizard.transform.localScale - Vector3.one).sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Undo.RecordObject(wizard.transform, "Reset GAMA species parent scale");
            wizard.NormalizeSpeciesContainerScale();
        }
    }
}
