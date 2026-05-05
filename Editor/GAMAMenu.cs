using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class GAMAMenu : ScriptableObject
{
    private static readonly string[] RequiredTags =
    {
        "player",
        "ground",
        "locomotion",
        "move",
        "Teleportation",
        "InvisibleWall",
        "road",
        "building",
        "selectable",
        "car",
        "moto",
        "pedestrian",
        "HUD",
        "textIP",
        "textPN",
        "useMiddleWare"
    };

    [MenuItem("GAMA/Setup Scene")]
    public static void SetupScene()
    {
        EnsureRequiredTags();
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        EnsureAgentSceneSettings();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Scene setup complete.");
    }

    private static void EnsureAgentSceneSettings()
    {
        SimulationManager[] managers = Object.FindObjectsByType<SimulationManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (SimulationManager manager in managers)
        {
            if (manager.GetComponent<GamaAgentSceneSettings>() == null)
            {
                Undo.AddComponent<GamaAgentSceneSettings>(manager.gameObject);
                EditorUtility.SetDirty(manager.gameObject);
            }

            if (manager.GetComponent<GamaPrefabSceneSettings>() == null)
            {
                Undo.AddComponent<GamaPrefabSceneSettings>(manager.gameObject);
                EditorUtility.SetDirty(manager.gameObject);
            }
        }
    }

    private static void EnsureRequiredTags()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning("[GAMA] Could not open TagManager.asset; tags were not created.");
            return;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty tags = tagManager.FindProperty("tags");
        if (tags == null)
        {
            Debug.LogWarning("[GAMA] Could not find the tags list in TagManager.asset.");
            return;
        }

        foreach (string tag in RequiredTags)
        {
            EnsureTag(tags, tag);
        }

        tagManager.ApplyModifiedProperties();
    }

    private static void EnsureTag(SerializedProperty tags, string tag)
    {
        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == tag)
            {
                return;
            }
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
    }
}
