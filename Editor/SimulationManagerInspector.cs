using UnityEditor;
/// <summary>
/// Inspector personnalisé pour <see cref="SimulationManager"/> et ses dérivés.
/// </summary>
[CustomEditor(typeof(SimulationManager), true)]
public class SimulationManagerInspector : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}
