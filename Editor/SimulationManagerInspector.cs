using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector personnalisé pour <see cref="SimulationManager"/> et ses dérivés.
/// Affiche les paramètres du <see cref="GamaAgentTuner"/> attaché au même GameObject
/// directement sous l'inspector du manager, pour pouvoir tout régler au même endroit.
/// </summary>
[CustomEditor(typeof(SimulationManager), true)]
public class SimulationManagerInspector : Editor
{
    Editor cachedTunerEditor;
    GamaAgentTuner cachedTuner;
    bool tunerFoldout = true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        SimulationManager manager = (SimulationManager)target;
        if (manager == null)
        {
            return;
        }

        GamaAgentTuner tuner = manager.GetComponent<GamaAgentTuner>();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Tuner runtime (curseurs agents)", EditorStyles.boldLabel);

        if (tuner == null)
        {
            EditorGUILayout.HelpBox(
                "Aucun GamaAgentTuner trouvé sur ce GameObject. Cliquez ci-dessous pour l'ajouter.",
                MessageType.Info);
            if (GUILayout.Button("Ajouter GamaAgentTuner"))
            {
                Undo.AddComponent<GamaAgentTuner>(manager.gameObject);
            }
            return;
        }

        if (cachedTuner != tuner)
        {
            if (cachedTunerEditor != null)
            {
                DestroyImmediate(cachedTunerEditor);
            }

            cachedTuner = tuner;
            cachedTunerEditor = CreateEditor(tuner);
        }

        if (cachedTunerEditor == null)
        {
            return;
        }

        tunerFoldout = EditorGUILayout.InspectorTitlebar(tunerFoldout, tuner);
        if (tunerFoldout)
        {
            EditorGUI.indentLevel++;
            cachedTunerEditor.OnInspectorGUI();
            EditorGUI.indentLevel--;
        }
    }

    void OnDisable()
    {
        if (cachedTunerEditor != null)
        {
            DestroyImmediate(cachedTunerEditor);
            cachedTunerEditor = null;
        }
    }
}
