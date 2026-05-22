using System;
using System.Collections.Generic;
using System.Reflection;
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

    // [MenuItem("GAMA/Setup Scene")] // Hidden for demo — accessible via GAMA Panel > Setup Scene
    public static void SetupScene()
    {
        SetupSceneCore(true);
    }

    // [MenuItem("GAMA/Setup Scene (VR Simulator)")] // Hidden for demo — accessible via GAMA Panel > Setup Scene
    public static void SetupSceneVrSimulator()
    {
        SetupSceneCore(true);
    }

    // [MenuItem("GAMA/Setup Scene (Headset Ready)")] // Hidden for demo — accessible via GAMA Panel > Setup Scene
    public static void SetupSceneHeadsetReady()
    {
        SetupSceneCore(false);
    }

    public static void ConfigureVrProjectSettings()
    {
        EnsureRequiredTags();
        EnsureVrReadyProjectSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] VR project settings configured.");
    }

    private static void SetupSceneCore(bool configureEditorSimulator)
    {
        EnsureRequiredTags();
        EnsureVrReadyProjectSettings();
        int removedRootObjects = ClearActiveSceneObjects(configureEditorSimulator);

        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        if (configureEditorSimulator)
        {
            EnsureEditorVrSimulator();
        }

        ApplyDeterministicEnvironmentSettings();
        RemoveMissingScriptsFromScene();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA] Scene setup complete (" + (configureEditorSimulator ? "VR simulator" : "headset-ready") + "). Removed " + removedRootObjects + " previous root object(s).");
    }

    private static void EnsureVrReadyProjectSettings()
    {
        EnsureInputSystemEnabled();

        List<BuildTargetGroup> buildTargetGroups = GetVrBuildTargetGroups();
        bool configuredAnyTarget = false;
        for (int i = 0; i < buildTargetGroups.Count; i++)
        {
            configuredAnyTarget |= TryConfigureOpenXR(buildTargetGroups[i]);
        }

        if (!configuredAnyTarget)
        {
            Debug.LogWarning("[GAMA] OpenXR project setup was skipped because the OpenXR/XR Management editor APIs are not available yet. Unity should install package dependencies first, then run GAMA > Setup Scene again.");
        }
    }

    private static List<BuildTargetGroup> GetVrBuildTargetGroups()
    {
        List<BuildTargetGroup> buildTargetGroups = new List<BuildTargetGroup>();
        AddBuildTargetGroup(buildTargetGroups, BuildTargetGroup.Standalone);

        BuildTargetGroup activeBuildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        AddBuildTargetGroup(buildTargetGroups, activeBuildTargetGroup);

        return buildTargetGroups;
    }

    private static void AddBuildTargetGroup(List<BuildTargetGroup> buildTargetGroups, BuildTargetGroup buildTargetGroup)
    {
        if (buildTargetGroup == BuildTargetGroup.Unknown || !IsOpenXRBuildTargetGroup(buildTargetGroup) || buildTargetGroups.Contains(buildTargetGroup))
        {
            return;
        }

        buildTargetGroups.Add(buildTargetGroup);
    }

    private static bool IsOpenXRBuildTargetGroup(BuildTargetGroup buildTargetGroup)
    {
        string buildTargetName = buildTargetGroup.ToString();
        return buildTargetName == "Standalone" || buildTargetName == "Android" || buildTargetName == "WSA";
    }

    private static void EnsureInputSystemEnabled()
    {
        try
        {
            PropertyInfo activeInputHandling = typeof(PlayerSettings).GetProperty("activeInputHandling", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (activeInputHandling == null || !activeInputHandling.CanWrite)
            {
                return;
            }

            Type inputHandlingType = activeInputHandling.PropertyType;
            if (!inputHandlingType.IsEnum)
            {
                return;
            }

            object currentValue = activeInputHandling.GetValue(null, null);
            object inputSystemPackage = TryParseEnum(inputHandlingType, "InputSystemPackage");
            object both = TryParseEnum(inputHandlingType, "Both");

            if (currentValue != null && inputSystemPackage != null && currentValue.Equals(inputSystemPackage))
            {
                return;
            }

            object targetValue = both ?? inputSystemPackage;
            if (targetValue == null || targetValue.Equals(currentValue))
            {
                return;
            }

            activeInputHandling.SetValue(null, targetValue, null);
            Debug.Log("[GAMA] Enabled Unity Input System support for VR input.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[GAMA] Could not update the active input handling setting: " + exception.GetBaseException().Message);
        }
    }

    private static object TryParseEnum(Type enumType, string value)
    {
        try
        {
            return Enum.Parse(enumType, value);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryConfigureOpenXR(BuildTargetGroup buildTargetGroup)
    {
        try
        {
            Type xrGeneralSettingsPerBuildTargetType = Type.GetType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, Unity.XR.Management.Editor");
            Type xrPackageMetadataStoreType = Type.GetType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore, Unity.XR.Management.Editor");
            if (xrGeneralSettingsPerBuildTargetType == null || xrPackageMetadataStoreType == null)
            {
                return false;
            }

            object buildTargetSettings = InvokeStaticMethod(xrGeneralSettingsPerBuildTargetType, "GetOrCreate", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (buildTargetSettings == null)
            {
                return false;
            }

            EnsureBuildTargetSettings(buildTargetSettings, buildTargetGroup);

            object generalSettings = InvokeInstanceMethod(buildTargetSettings, "SettingsForBuildTarget", buildTargetGroup);
            if (generalSettings == null)
            {
                return false;
            }

            SetProperty(generalSettings, "InitManagerOnStart", true);

            object managerSettings = GetProperty(generalSettings, "AssignedSettings") ?? GetProperty(generalSettings, "Manager");
            if (managerSettings == null)
            {
                return false;
            }

            MethodInfo assignLoader = xrPackageMetadataStoreType.GetMethod("AssignLoader", BindingFlags.Static | BindingFlags.Public);
            bool loaderAssigned = assignLoader != null
                && assignLoader.Invoke(null, new object[] { managerSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", buildTargetGroup }) is bool result
                && result;

            bool openXrSettingsConfigured = ConfigureOpenXRFeatures(buildTargetGroup);

            if (generalSettings is UnityEngine.Object generalSettingsObject)
            {
                EditorUtility.SetDirty(generalSettingsObject);
            }

            if (managerSettings is UnityEngine.Object managerSettingsObject)
            {
                EditorUtility.SetDirty(managerSettingsObject);
            }

            AssetDatabase.SaveAssets();

            if (loaderAssigned)
            {
                Debug.Log("[GAMA] OpenXR enabled for " + buildTargetGroup + " and set to initialize on startup.");
            }
            else
            {
                Debug.LogWarning("[GAMA] OpenXR project settings were created for " + buildTargetGroup + ", but assigning the OpenXR loader failed. Check Project Settings > XR Plug-in Management > OpenXR.");
            }

            return loaderAssigned || openXrSettingsConfigured;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[GAMA] OpenXR setup failed for " + buildTargetGroup + ": " + exception.GetBaseException().Message);
            return false;
        }
    }

    private static void EnsureBuildTargetSettings(object buildTargetSettings, BuildTargetGroup buildTargetGroup)
    {
        if (!InvokeBool(buildTargetSettings, "HasSettingsForBuildTarget", buildTargetGroup))
        {
            InvokeInstanceMethod(buildTargetSettings, "CreateDefaultSettingsForBuildTarget", buildTargetGroup);
        }

        if (!InvokeBool(buildTargetSettings, "HasManagerSettingsForBuildTarget", buildTargetGroup))
        {
            InvokeInstanceMethod(buildTargetSettings, "CreateDefaultManagerSettingsForBuildTarget", buildTargetGroup);
        }
    }

    private static bool ConfigureOpenXRFeatures(BuildTargetGroup buildTargetGroup)
    {
        Type openXRPackageSettingsType = Type.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings, Unity.XR.OpenXR.Editor");
        Type featureHelpersType = Type.GetType("UnityEditor.XR.OpenXR.Features.FeatureHelpers, Unity.XR.OpenXR.Editor");
        Type openXRSettingsType = Type.GetType("UnityEngine.XR.OpenXR.OpenXRSettings, Unity.XR.OpenXR");
        if (openXRPackageSettingsType == null || featureHelpersType == null || openXRSettingsType == null)
        {
            return false;
        }

        object packageSettings = InvokeStaticMethod(openXRPackageSettingsType, "GetOrCreateInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (packageSettings == null)
        {
            return false;
        }

        InvokeStaticMethod(featureHelpersType, "RefreshFeatures", BindingFlags.Static | BindingFlags.Public, buildTargetGroup);

        string[] defaultInteractionFeatureIds =
        {
            "com.unity.openxr.feature.input.khrsimpleprofile",
            "com.unity.openxr.feature.input.oculustouch",
            "com.unity.openxr.feature.input.metaquestpro",
            "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.valveindex",
            "com.unity.openxr.feature.input.htcvive",
            "com.unity.openxr.feature.input.hpreverb",
            "com.unity.openxr.feature.input.microsoftmotioncontroller"
        };

        bool changed = false;
        for (int i = 0; i < defaultInteractionFeatureIds.Length; i++)
        {
            object feature = InvokeStaticMethod(featureHelpersType, "GetFeatureWithIdForBuildTarget", BindingFlags.Static | BindingFlags.Public, buildTargetGroup, defaultInteractionFeatureIds[i]);
            if (feature == null)
            {
                continue;
            }

            object enabled = GetProperty(feature, "enabled");
            if (!(enabled is bool isEnabled) || isEnabled)
            {
                continue;
            }

            SetProperty(feature, "enabled", true);
            if (feature is UnityEngine.Object featureObject)
            {
                EditorUtility.SetDirty(featureObject);
            }

            changed = true;
        }

        object openXRSettings = InvokeStaticMethod(openXRSettingsType, "GetSettingsForBuildTargetGroup", BindingFlags.Static | BindingFlags.Public, buildTargetGroup);
        if (openXRSettings is UnityEngine.Object openXRSettingsObject)
        {
            EditorUtility.SetDirty(openXRSettingsObject);
        }

        if (changed)
        {
            Debug.Log("[GAMA] Enabled default OpenXR interaction profiles for " + buildTargetGroup + ".");
        }

        return true;
    }

    private static object InvokeStaticMethod(Type type, string methodName, BindingFlags bindingFlags, params object[] parameters)
    {
        MethodInfo method = FindMethod(type, methodName, bindingFlags, parameters);
        return method != null ? method.Invoke(null, parameters) : null;
    }

    private static object InvokeInstanceMethod(object target, string methodName, params object[] parameters)
    {
        if (target == null)
        {
            return null;
        }

        MethodInfo method = FindMethod(target.GetType(), methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, parameters);
        return method != null ? method.Invoke(target, parameters) : null;
    }

    private static bool InvokeBool(object target, string methodName, params object[] parameters)
    {
        object value = InvokeInstanceMethod(target, methodName, parameters);
        return value is bool result && result;
    }

    private static MethodInfo FindMethod(Type type, string methodName, BindingFlags bindingFlags, object[] parameters)
    {
        MethodInfo[] methods = type.GetMethods(bindingFlags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.Name != methodName)
            {
                continue;
            }

            ParameterInfo[] methodParameters = method.GetParameters();
            if (methodParameters.Length != parameters.Length)
            {
                continue;
            }

            bool matches = true;
            for (int j = 0; j < methodParameters.Length; j++)
            {
                if (parameters[j] != null && !methodParameters[j].ParameterType.IsInstanceOfType(parameters[j]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return method;
            }
        }

        return null;
    }

    private static object GetProperty(object target, string propertyName)
    {
        if (target == null)
        {
            return null;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property != null ? property.GetValue(target, null) : null;
    }

    private static void SetProperty(object target, string propertyName, object value)
    {
        if (target == null)
        {
            return;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, value, null);
        }
    }

    private static void EnsureEditorVrSimulator()
    {
        if (FindRootObjectWithComponent("XRDeviceSimulator") != null)
        {
            EnsureEventSystemExists();
            return;
        }

        string[] simulatorGuids = AssetDatabase.FindAssets("\"XR Device Simulator\" t:Prefab");
        string prefabPath = null;
        for (int i = 0; i < simulatorGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(simulatorGuids[i]);
            if (path.EndsWith("XR Device Simulator.prefab", StringComparison.OrdinalIgnoreCase))
            {
                prefabPath = path;
                break;
            }
        }

        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogWarning("[GAMA] XR Device Simulator sample was not found in the project. Import XR Interaction Toolkit > Samples > XR Device Simulator, then run GAMA > Setup Scene (VR Simulator) again.");
            return;
        }

        GameObject simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (simulatorPrefab == null)
        {
            Debug.LogWarning("[GAMA] XR Device Simulator prefab could not be loaded from " + prefabPath + ".");
            return;
        }

        GameObject simulatorInstance = PrefabUtility.InstantiatePrefab(simulatorPrefab, SceneManager.GetActiveScene()) as GameObject;
        if (simulatorInstance != null)
        {
            simulatorInstance.name = simulatorPrefab.name;
            EnsureEventSystemExists();
            Debug.Log("[GAMA] Added XR Device Simulator from imported XR Interaction Toolkit samples.");
        }
    }

    private static void EnsureEventSystemExists()
    {
        if (FindRootObjectWithComponent("EventSystem") != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        AddOptionalComponentByTypeName(eventSystemObject, "UnityEngine.EventSystems.EventSystem, UnityEngine.UI");

        if (AddOptionalComponentByTypeName(eventSystemObject, "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem") == null)
        {
            AddOptionalComponentByTypeName(eventSystemObject, "UnityEngine.EventSystems.StandaloneInputModule, UnityEngine.UI");
        }
    }

    private static Component AddOptionalComponentByTypeName(GameObject gameObject, string assemblyQualifiedTypeName)
    {
        if (gameObject == null || string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
        {
            return null;
        }

        Type type = Type.GetType(assemblyQualifiedTypeName);
        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            return null;
        }

        Component existing = gameObject.GetComponent(type);
        return existing != null ? existing : gameObject.AddComponent(type);
    }

    private static GameObject FindRootObjectWithComponent(string componentName)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            return null;
        }

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i] != null && roots[i].GetComponent(componentName) != null)
            {
                return roots[i];
            }
        }

        return null;
    }

    private static int ClearActiveSceneObjects(bool preserveSimulatorRoots)
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

            if (ShouldPreserveRootObjectDuringSetup(roots[i], preserveSimulatorRoots))
            {
                continue;
            }

            Undo.DestroyObjectImmediate(roots[i]);
            removed++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        return removed;
    }

    private static bool ShouldPreserveRootObjectDuringSetup(GameObject root, bool preserveSimulatorRoots)
    {
        if (root == null)
        {
            return false;
        }

        if (!preserveSimulatorRoots)
        {
            return false;
        }

        string rootName = root.name ?? string.Empty;
        if (rootName.StartsWith("XR Device Simulator", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(rootName, "EventSystem", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (root.GetComponent("XRDeviceSimulator") != null ||
            root.GetComponent("EventSystem") != null ||
            root.GetComponent("InputSystemUIInputModule") != null ||
            root.GetComponent("StandaloneInputModule") != null)
        {
            return true;
        }

        return false;
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
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
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
        Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
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
        Camera mainCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (mainCamera == null)
        {
            return;
        }

        mainCamera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        mainCamera.backgroundColor = Color.black;
    }
}
