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
        RemoveMissingScriptsFromScene();
        EnsureRequiredTags();
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        RemoveMissingScriptsFromScene();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Scene setup complete.");
    }

    private static void RemoveMissingScriptsFromScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return;
        }

        int removed = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < transforms.Length; j++)
            {
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[j].gameObject);
            }
        }

        if (removed > 0)
        {
            Debug.Log("[GAMA] Removed " + removed + " obsolete missing script component(s) from the scene.");
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
