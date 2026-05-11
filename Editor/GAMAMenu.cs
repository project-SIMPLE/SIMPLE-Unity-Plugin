using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;

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
        int removedRootObjects = ClearActiveSceneObjects();

        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        ApplyDeterministicEnvironmentSettings();
        RemoveMissingScriptsFromScene();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Scene setup complete. Removed " + removedRootObjects + " previous root object(s).");
    }

    private static int ClearActiveSceneObjects()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return 0;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("GAMA Setup Scene");

        int removed = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] == null)
            {
                continue;
            }

            Undo.DestroyObjectImmediate(roots[i]);
            removed++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        return removed;
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

    private static void ApplyDeterministicEnvironmentSettings()
    {
        // Reset inherited scene template environment to keep Setup Scene deterministic.
        Material defaultSkybox = ResolveDefaultSkyboxMaterial(RenderSettings.skybox);
        RenderSettings.skybox = defaultSkybox;
        RenderSettings.sun = FindDirectionalLight();

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.ambientGroundColor = Color.black;
        RenderSettings.ambientEquatorColor = Color.black;
        RenderSettings.ambientSkyColor = Color.black;

        RenderSettings.fog = false;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Color.gray;
        RenderSettings.fogStartDistance = 0f;
        RenderSettings.fogEndDistance = 300f;
        RenderSettings.fogDensity = 0.01f;

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        RenderSettings.reflectionBounces = 1;

        RenderSettings.subtractiveShadowColor = Color.black;
        DynamicGI.UpdateEnvironment();

        EnsureMainCameraDefaults();
    }

    private static Material ResolveDefaultSkyboxMaterial(Material currentSkybox)
    {
        if (currentSkybox != null)
        {
            return currentSkybox;
        }

        // Built-in fallback available across Unity projects without package dependency.
        Material builtInSkybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        if (builtInSkybox != null)
        {
            return builtInSkybox;
        }

        // Additional defensive fallback for older/newer editor variants.
        return AssetDatabase.GetBuiltinExtraResource<Material>("Skybox-Procedural.mat");
    }

    private static Light FindDirectionalLight()
    {
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
            {
                return lights[i];
            }
        }

        return null;
    }

    private static void EnsureMainCameraDefaults()
    {
        Camera mainCamera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        if (mainCamera == null)
        {
            return;
        }

        mainCamera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = Color.black;
    }
}
