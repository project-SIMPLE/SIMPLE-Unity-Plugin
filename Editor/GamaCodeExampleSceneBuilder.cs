using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ProjectSimple.GamaUnity.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal enum GamaCodeExampleKind
{
    LimitPlayerMovement,
    ManageAnimationForAgents,
    ModifyPlayerProperties,
    MultiPlayer,
    ReceiveDemData,
    ReceiveDynamicData,
    ReceiveStaticData,
    ReceiveWaterData,
    SendReceiveMessage,
    UserInteractions
}

internal sealed class GamaCodeExampleSceneInfo
{
    public GamaCodeExampleSceneInfo(string displayName, GamaCodeExampleKind kind, string description)
    {
        DisplayName = displayName;
        Kind = kind;
        Description = description;
    }

    public string DisplayName { get; private set; }
    public GamaCodeExampleKind Kind { get; private set; }
    public string Description { get; private set; }
}

internal static class GamaCodeExampleSceneBuilder
{
    private const string TargetRootAssetPath = "Assets/Scenes/Code Examples";
    private const string ExampleRootPrefix = "[GAMA] ";
    private const string ExampleRootSuffix = " Example";

    private static readonly GamaCodeExampleSceneInfo[] SceneInfos =
    {
        new GamaCodeExampleSceneInfo("Limit Player Movement", GamaCodeExampleKind.LimitPlayerMovement, "Movement bounds and invisible walls."),
        new GamaCodeExampleSceneInfo("Manage Animation for Agents", GamaCodeExampleKind.ManageAnimationForAgents, "Animated agent preview objects."),
        new GamaCodeExampleSceneInfo("Modify Player Properties", GamaCodeExampleKind.ModifyPlayerProperties, "Camera, player and render-distance settings."),
        new GamaCodeExampleSceneInfo("Multi-player", GamaCodeExampleKind.MultiPlayer, "Selectable shared tokens with color messages."),
        new GamaCodeExampleSceneInfo("Receive DEM Data", GamaCodeExampleKind.ReceiveDemData, "Terrain-like DEM preview."),
        new GamaCodeExampleSceneInfo("Receive Dynamic Data", GamaCodeExampleKind.ReceiveDynamicData, "Dynamic color/type update preview."),
        new GamaCodeExampleSceneInfo("Receive Static Data", GamaCodeExampleKind.ReceiveStaticData, "Static roads, buildings and agents."),
        new GamaCodeExampleSceneInfo("Receive Water Data", GamaCodeExampleKind.ReceiveWaterData, "Water zones and banks."),
        new GamaCodeExampleSceneInfo("Send Receive Message", GamaCodeExampleKind.SendReceiveMessage, "Bidirectional custom message manager."),
        new GamaCodeExampleSceneInfo("User Interactions", GamaCodeExampleKind.UserInteractions, "Hover/select interactable objects.")
    };

    public static IEnumerable<GamaCodeExampleSceneInfo> GetSceneInfos()
    {
        return SceneInfos;
    }

    public static string BuildAndSave(GamaCodeExampleSceneInfo sceneInfo, bool refreshAssetDatabase = true)
    {
        if (sceneInfo == null)
        {
            throw new ArgumentNullException("sceneInfo");
        }

        BuildActiveScene(sceneInfo);

        string targetRootPath = ToFullProjectPath(TargetRootAssetPath);
        Directory.CreateDirectory(targetRootPath);

        string targetSceneAssetPath = TargetRootAssetPath + "/" + SanitizeFileName(sceneInfo.DisplayName) + ".unity";
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), targetSceneAssetPath))
        {
            throw new InvalidOperationException("Unity failed to save the generated scene at " + targetSceneAssetPath + ".");
        }

        if (refreshAssetDatabase)
        {
            AssetDatabase.Refresh();
        }

        return targetSceneAssetPath;
    }

    public static void BuildActiveScene(GamaCodeExampleSceneInfo sceneInfo)
    {
        if (sceneInfo == null)
        {
            throw new ArgumentNullException("sceneInfo");
        }

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GAMAMenu.SetupScene();

        SimulationManager manager = ConfigureSimulationManager(GetManagerType(sceneInfo.Kind));
        ConfigureCommonScene(manager);

        GameObject root = new GameObject(ExampleRootPrefix + sceneInfo.DisplayName + ExampleRootSuffix);
        switch (sceneInfo.Kind)
        {
            case GamaCodeExampleKind.LimitPlayerMovement:
                BuildLimitPlayerMovement(root);
                break;
            case GamaCodeExampleKind.ManageAnimationForAgents:
                BuildManageAnimationForAgents(root);
                break;
            case GamaCodeExampleKind.ModifyPlayerProperties:
                BuildModifyPlayerProperties(root, manager);
                break;
            case GamaCodeExampleKind.MultiPlayer:
                BuildMultiPlayer(root);
                break;
            case GamaCodeExampleKind.ReceiveDemData:
                BuildReceiveDemData(root);
                break;
            case GamaCodeExampleKind.ReceiveDynamicData:
                BuildReceiveDynamicData(root);
                break;
            case GamaCodeExampleKind.ReceiveStaticData:
                BuildReceiveStaticData(root);
                break;
            case GamaCodeExampleKind.ReceiveWaterData:
                BuildReceiveWaterData(root);
                break;
            case GamaCodeExampleKind.SendReceiveMessage:
                BuildSendReceiveMessage(root);
                break;
            case GamaCodeExampleKind.UserInteractions:
                BuildUserInteractions(root);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        CreateLabel(root.transform, sceneInfo.DisplayName, new Vector3(0f, 0.04f, 4.8f), 0.6f, Color.white);
        FrameSceneForEditor();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    private static void ConfigureCommonScene(SimulationManager manager)
    {
        GameObject player = GamaSceneUtility.FindGameObjectWithTag("player") ?? GameObject.Find("FPSPlayer");
        if (player != null)
        {
            player.transform.position = new Vector3(0f, 0f, -6f);
            player.transform.rotation = Quaternion.identity;
        }

        Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (camera != null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.05f, 0.06f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 2000f;
        }

        GameObject ground = GamaSceneUtility.FindGameObjectWithTag("ground") ?? GameObject.Find("Ground");
        if (ground != null)
        {
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8f, 1f, 8f);
            GamaVisualUtility.ApplyColor(ground, new Color32(66, 72, 78, 255));
        }

        Light light = UnityEngine.Object.FindFirstObjectByType<Light>();
        if (light != null)
        {
            light.name = "Directional Light";
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        }

        if (manager != null)
        {
            SetSerializedFloat(manager, "globalPrefabRenderDistance", 1500f);
            SetSerializedBool(manager, "enablePrefabRenderDistance", true);
        }
    }

    private static Type GetManagerType(GamaCodeExampleKind kind)
    {
        switch (kind)
        {
            case GamaCodeExampleKind.ReceiveDynamicData:
                return typeof(GamaReceiveDynamicDataExampleManager);
            case GamaCodeExampleKind.SendReceiveMessage:
                return typeof(GamaSendReceiveMessageExampleManager);
            case GamaCodeExampleKind.MultiPlayer:
                return typeof(GamaMultiPlayerColorExampleManager);
            case GamaCodeExampleKind.UserInteractions:
                return typeof(GamaUserInteractionExampleManager);
            default:
                return typeof(SimulationManagerSolo);
        }
    }

    private static SimulationManager ConfigureSimulationManager(Type managerType)
    {
        GameObject managerObject = FindGameManagerObject();
        if (managerObject == null)
        {
            GameObject managersRoot = GameObject.Find("ManagersSolo") ?? new GameObject("ManagersSolo");
            managerObject = GamaSceneUtility.GetOrCreateChild(managersRoot, "Game Manager");
        }

        SimulationManager[] managers = UnityEngine.Object.FindObjectsByType<SimulationManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            SimulationManager existing = managers[i];
            if (existing == null)
            {
                continue;
            }

            if (existing.gameObject != managerObject || existing.GetType() != managerType)
            {
                UnityEngine.Object.DestroyImmediate(existing);
            }
        }

        SimulationManager manager = managerObject.GetComponent(managerType) as SimulationManager;
        if (manager == null)
        {
            manager = managerObject.AddComponent(managerType) as SimulationManager;
        }

        GamaInitializer.SetupPlayer(manager, true);
        GamaInitializer.SetupGround(manager, true);

        Light light = UnityEngine.Object.FindFirstObjectByType<Light>();
        if (light != null)
        {
            AssignField(manager, "lightObject", light.gameObject);
        }

        return manager;
    }

    private static GameObject FindGameManagerObject()
    {
        SimulationManager manager = UnityEngine.Object.FindFirstObjectByType<SimulationManager>(FindObjectsInactive.Include);
        if (manager != null)
        {
            return manager.gameObject;
        }

        GameObject found = GameObject.Find("ManagersSolo/Game Manager");
        if (found != null)
        {
            return found;
        }

        found = GameObject.Find("GAMA Managers/Game Manager");
        return found;
    }

    private static void BuildLimitPlayerMovement(GameObject root)
    {
        CreateLabel(root.transform, "Movement bounds", new Vector3(-4.5f, 0.03f, -4.5f), 0.32f, new Color32(210, 226, 255, 255));
        CreatePrimitive(root.transform, "Playable Area", PrimitiveType.Cube, new Vector3(0f, 0.015f, 0f), new Vector3(10f, 0.03f, 10f), new Color32(48, 92, 80, 155));

        GameObject north = CreateWall(root.transform, "InvisibleWall_North", new Vector3(0f, 1f, 5.1f), new Vector3(10.4f, 2f, 0.18f));
        GameObject south = CreateWall(root.transform, "InvisibleWall_South", new Vector3(0f, 1f, -5.1f), new Vector3(10.4f, 2f, 0.18f));
        GameObject east = CreateWall(root.transform, "InvisibleWall_East", new Vector3(5.1f, 1f, 0f), new Vector3(0.18f, 2f, 10.4f));
        GameObject west = CreateWall(root.transform, "InvisibleWall_West", new Vector3(-5.1f, 1f, 0f), new Vector3(0.18f, 2f, 10.4f));

        north.GetComponent<Renderer>().enabled = false;
        south.GetComponent<Renderer>().enabled = false;
        east.GetComponent<Renderer>().enabled = false;
        west.GetComponent<Renderer>().enabled = false;

        CreatePrimitive(root.transform, "Visible Boundary", PrimitiveType.Cube, new Vector3(0f, 0.05f, 5.05f), new Vector3(10.4f, 0.1f, 0.08f), new Color32(255, 184, 77, 255));
        CreatePrimitive(root.transform, "Visible Boundary", PrimitiveType.Cube, new Vector3(0f, 0.05f, -5.05f), new Vector3(10.4f, 0.1f, 0.08f), new Color32(255, 184, 77, 255));
        CreatePrimitive(root.transform, "Visible Boundary", PrimitiveType.Cube, new Vector3(5.05f, 0.05f, 0f), new Vector3(0.08f, 0.1f, 10.4f), new Color32(255, 184, 77, 255));
        CreatePrimitive(root.transform, "Visible Boundary", PrimitiveType.Cube, new Vector3(-5.05f, 0.05f, 0f), new Vector3(0.08f, 0.1f, 10.4f), new Color32(255, 184, 77, 255));
    }

    private static void BuildManageAnimationForAgents(GameObject root)
    {
        CreateLabel(root.transform, "Animated agents", new Vector3(-3.7f, 0.03f, -3.2f), 0.32f, new Color32(210, 226, 255, 255));
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI * 2f / 6f;
            Vector3 position = new Vector3(Mathf.Cos(angle) * 3f, 0.7f, Mathf.Sin(angle) * 2.4f);
            GameObject agent = CreatePrimitive(root.transform, "animated_agent_" + (i + 1).ToString(), PrimitiveType.Capsule, position, new Vector3(0.45f, 0.7f, 0.45f), AgentColor(i));
            GamaSceneUtility.TrySetTag(agent, "pedestrian");
            GamaExamplePreviewAnimator animator = agent.AddComponent<GamaExamplePreviewAnimator>();
            animator.rotationSpeed = 25f + i * 12f;
            animator.bobAmplitude = 0.08f;
            animator.bobFrequency = 0.8f + i * 0.2f;
        }

        CreatePrimitive(root.transform, "Animation Path", PrimitiveType.Cylinder, new Vector3(0f, 0.04f, 0f), new Vector3(6.4f, 0.02f, 4.8f), new Color32(120, 170, 210, 105));
    }

    private static void BuildModifyPlayerProperties(GameObject root, SimulationManager manager)
    {
        Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        if (camera != null)
        {
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 650f;
            camera.backgroundColor = new Color(0.015f, 0.035f, 0.055f);
        }

        SetSerializedFloat(manager, "globalPrefabRenderDistance", 650f);
        SetSerializedFloat(manager, "prefabViewPadding", 35f);

        CreateLabel(root.transform, "Player origin", new Vector3(0f, 0.03f, -5.2f), 0.32f, new Color32(210, 226, 255, 255));
        CreatePrimitive(root.transform, "Near Clip Marker", PrimitiveType.Cube, new Vector3(0f, 0.35f, -4.6f), new Vector3(1.2f, 0.7f, 0.04f), new Color32(255, 110, 100, 255));
        CreatePrimitive(root.transform, "Preferred View Zone", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.5f), new Vector3(7f, 0.04f, 7f), new Color32(70, 120, 170, 130));
        CreatePrimitive(root.transform, "Render Distance Marker", PrimitiveType.Cylinder, new Vector3(0f, 0.06f, 4f), new Vector3(7.5f, 0.03f, 7.5f), new Color32(255, 205, 90, 125));
        CreatePrimitive(root.transform, "Camera Target", PrimitiveType.Sphere, new Vector3(0f, 1.2f, 1.2f), new Vector3(0.5f, 0.5f, 0.5f), new Color32(120, 210, 160, 255));
    }

    private static void BuildMultiPlayer(GameObject root)
    {
        CreateLabel(root.transform, "Shared selectable tokens", new Vector3(-4.1f, 0.03f, -3.8f), 0.32f, new Color32(210, 226, 255, 255));
        for (int i = 0; i < 8; i++)
        {
            float x = -3.5f + (i % 4) * 2.3f;
            float z = -1.2f + (i / 4) * 2.4f;
            GameObject token = CreatePrimitive(root.transform, "token_" + (i + 1).ToString(), PrimitiveType.Sphere, new Vector3(x, 0.45f, z), new Vector3(0.7f, 0.7f, 0.7f), AgentColor(i));
            AddSelectable(token, "selectable");
        }

        GameObject hud = CreatePrimitive(root.transform, "HUD Preview Panel", PrimitiveType.Cube, new Vector3(0f, 2.2f, 3.9f), new Vector3(5.4f, 1.2f, 0.08f), new Color32(28, 34, 42, 255));
        GamaSceneUtility.TrySetTag(hud, "HUD");
        CreateLabel(root.transform, "Score / ranking / token count", new Vector3(-2.35f, 2.19f, 3.82f), 0.24f, new Color32(240, 244, 255, 255));
    }

    private static void BuildReceiveDemData(GameObject root)
    {
        CreateLabel(root.transform, "DEM height preview", new Vector3(-4.1f, 0.03f, -4.1f), 0.32f, new Color32(210, 226, 255, 255));
        for (int x = -4; x <= 4; x++)
        {
            for (int z = -4; z <= 4; z++)
            {
                float height = 0.15f + Mathf.PerlinNoise((x + 9) * 0.22f, (z + 9) * 0.22f) * 1.35f;
                byte green = (byte)Mathf.Clamp(95 + height * 80f, 0f, 255f);
                CreatePrimitive(root.transform, "dem_cell_" + (x + 4).ToString() + "_" + (z + 4).ToString(), PrimitiveType.Cube, new Vector3(x * 0.85f, height * 0.5f, z * 0.85f), new Vector3(0.8f, height, 0.8f), new Color32(80, green, 92, 255));
            }
        }
    }

    private static void BuildReceiveDynamicData(GameObject root)
    {
        CreateLabel(root.transform, "Dynamic type colors", new Vector3(-3.8f, 0.03f, -3.8f), 0.32f, new Color32(210, 226, 255, 255));
        Color32[] colors =
        {
            new Color32(235, 235, 235, 255),
            new Color32(80, 135, 255, 255),
            new Color32(230, 72, 72, 255)
        };

        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                GameObject agent = CreatePrimitive(root.transform, "dynamic_type_" + row.ToString() + "_" + col.ToString(), PrimitiveType.Capsule, new Vector3(-3f + col * 2f, 0.7f, -1.8f + row * 1.8f), new Vector3(0.45f, 0.7f, 0.45f), colors[row]);
                GamaSceneUtility.TrySetTag(agent, "pedestrian");
            }
        }
    }

    private static void BuildReceiveStaticData(GameObject root)
    {
        CreateLabel(root.transform, "Static roads and buildings", new Vector3(-4.4f, 0.03f, -4.2f), 0.32f, new Color32(210, 226, 255, 255));
        GameObject roadA = CreatePrimitive(root.transform, "road_main", PrimitiveType.Cube, new Vector3(0f, 0.04f, 0f), new Vector3(9f, 0.08f, 1.1f), new Color32(34, 38, 42, 255));
        GameObject roadB = CreatePrimitive(root.transform, "road_cross", PrimitiveType.Cube, new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.09f, 8f), new Color32(34, 38, 42, 255));
        GamaSceneUtility.TrySetTag(roadA, "road");
        GamaSceneUtility.TrySetTag(roadB, "road");

        for (int i = 0; i < 8; i++)
        {
            float x = i < 4 ? -3.8f + i * 2.5f : -3.8f + (i - 4) * 2.5f;
            float z = i < 4 ? -2.6f : 2.6f;
            float height = 0.8f + (i % 3) * 0.5f;
            GameObject building = CreatePrimitive(root.transform, "building_" + (i + 1).ToString(), PrimitiveType.Cube, new Vector3(x, height * 0.5f, z), new Vector3(1.1f, height, 1.1f), new Color32(155, 165, 172, 255));
            GamaSceneUtility.TrySetTag(building, "building");
        }
    }

    private static void BuildReceiveWaterData(GameObject root)
    {
        CreateLabel(root.transform, "Water depth zones", new Vector3(-4.1f, 0.03f, -4.2f), 0.32f, new Color32(210, 226, 255, 255));
        CreatePrimitive(root.transform, "river_bank_left", PrimitiveType.Cube, new Vector3(-2.9f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 8.6f), new Color32(95, 88, 68, 255));
        CreatePrimitive(root.transform, "river_bank_right", PrimitiveType.Cube, new Vector3(2.9f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 8.6f), new Color32(95, 88, 68, 255));

        for (int i = 0; i < 5; i++)
        {
            float z = -3.2f + i * 1.6f;
            float width = 3f + Mathf.Sin(i * 0.8f) * 0.6f;
            CreatePrimitive(root.transform, "water_depth_" + (i + 1).ToString(), PrimitiveType.Cube, new Vector3(0f, 0.06f, z), new Vector3(width, 0.08f, 1.45f), new Color32(54, 142, 216, (byte)(135 + i * 20)));
        }
    }

    private static void BuildSendReceiveMessage(GameObject root)
    {
        CreateLabel(root.transform, "Custom message loop", new Vector3(-3.8f, 0.03f, -3.8f), 0.32f, new Color32(210, 226, 255, 255));
        CreatePrimitive(root.transform, "Message Console", PrimitiveType.Cube, new Vector3(0f, 1.6f, 1.8f), new Vector3(5.8f, 2.1f, 0.12f), new Color32(20, 28, 34, 255));
        CreateLabel(root.transform, "Unity receive_message <-> GAMA cycle", new Vector3(-2.55f, 1.7f, 1.62f), 0.22f, new Color32(140, 255, 180, 255));
        CreatePrimitive(root.transform, "Outgoing Message", PrimitiveType.Cube, new Vector3(-1.8f, 0.45f, -1.3f), new Vector3(1.1f, 0.55f, 1.1f), new Color32(88, 176, 255, 255));
        CreatePrimitive(root.transform, "Incoming Message", PrimitiveType.Cube, new Vector3(1.8f, 0.45f, -1.3f), new Vector3(1.1f, 0.55f, 1.1f), new Color32(255, 190, 92, 255));
    }

    private static void BuildUserInteractions(GameObject root)
    {
        CreateLabel(root.transform, "Hover and select objects", new Vector3(-3.9f, 0.03f, -3.8f), 0.32f, new Color32(210, 226, 255, 255));
        for (int i = 0; i < 9; i++)
        {
            int row = i / 3;
            int col = i % 3;
            GameObject target = CreatePrimitive(root.transform, "selectable_object_" + (i + 1).ToString(), i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere, new Vector3(-2.4f + col * 2.4f, 0.5f, -1.5f + row * 1.8f), new Vector3(0.85f, 0.85f, 0.85f), new Color32(80, 190, 120, 255));
            AddSelectable(target, "selectable");
        }
    }

    private static GameObject CreateWall(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = CreatePrimitive(parent, name, PrimitiveType.Cube, position, scale, new Color32(255, 185, 70, 90));
        GamaSceneUtility.TrySetTag(wall, "InvisibleWall");
        Collider collider = wall.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = false;
        }

        return wall;
    }

    private static GameObject CreatePrimitive(Transform parent, string name, PrimitiveType primitiveType, Vector3 position, Vector3 scale, Color32 color)
    {
        GameObject obj = GameObject.CreatePrimitive(primitiveType);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localRotation = Quaternion.identity;
        obj.transform.localScale = scale;
        GamaVisualUtility.ApplyColor(obj, color);
        return obj;
    }

    private static void AddSelectable(GameObject gameObject, string tag)
    {
        if (gameObject == null)
        {
            return;
        }

        GamaSceneUtility.TrySetTag(gameObject, tag);
        Rigidbody rigidbody = gameObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = gameObject.AddComponent<Rigidbody>();
        }

        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        GamaSceneUtility.AddOptionalComponent(
            gameObject,
            "UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable",
            "UnityEngine.XR.Interaction.Toolkit.XRSimpleInteractable");
    }

    private static TextMesh CreateLabel(Transform parent, string text, Vector3 position, float size, Color color)
    {
        GameObject labelObj = new GameObject("Label - " + text);
        labelObj.transform.SetParent(parent, false);
        labelObj.transform.localPosition = position;
        labelObj.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);

        TextMesh label = labelObj.AddComponent<TextMesh>();
        label.text = text;
        label.fontSize = 48;
        label.characterSize = size;
        label.anchor = TextAnchor.MiddleLeft;
        label.alignment = TextAlignment.Left;
        label.color = color;
        return label;
    }

    private static Color32 AgentColor(int index)
    {
        Color32[] colors =
        {
            new Color32(80, 150, 255, 255),
            new Color32(255, 168, 77, 255),
            new Color32(92, 205, 132, 255),
            new Color32(220, 92, 116, 255),
            new Color32(170, 130, 240, 255),
            new Color32(245, 218, 95, 255)
        };

        return colors[Mathf.Abs(index) % colors.Length];
    }

    private static void FrameSceneForEditor()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        sceneView.LookAt(new Vector3(0f, 1.4f, 0f), Quaternion.Euler(55f, 0f, 0f), 11f);
        sceneView.Repaint();
    }

    private static void SetSerializedFloat(UnityEngine.Object target, string propertyName, float value)
    {
        if (target == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null && property.propertyType == SerializedPropertyType.Float)
        {
            property.floatValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetSerializedBool(UnityEngine.Object target, string propertyName, bool value)
    {
        if (target == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null && property.propertyType == SerializedPropertyType.Boolean)
        {
            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AssignField(object target, string fieldName, object value)
    {
        if (target == null || value == null)
        {
            return;
        }

        FieldInfo field = FindField(target.GetType(), fieldName);
        if (field != null)
        {
            field.SetValue(target, value);
        }
    }

    private static FieldInfo FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static string SanitizeFileName(string value)
    {
        string safeValue = string.IsNullOrWhiteSpace(value) ? "GAMA Code Example" : value.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        for (int i = 0; i < invalidChars.Length; i++)
        {
            safeValue = safeValue.Replace(invalidChars[i], '_');
        }

        return safeValue;
    }

    private static string ToFullProjectPath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
