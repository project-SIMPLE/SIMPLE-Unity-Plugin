using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace ProjectSimple.GamaUnity.Runtime
{
    public static class GamaInitializer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void InitializeGama()
        {
            Light light = EnsureLight();
            GameObject ground = EnsureTeleportAreaAndGround();
            GameObject player = EnsurePlayer();
            EnsureManagers(player, ground, light != null ? light.gameObject : null);
        }

        public static void SetupPlayer(global::SimulationManager simManager, bool createIfMissing = false)
        {
            if (simManager == null)
            {
                return;
            }

            FieldInfo field = GetField(simManager.GetType(), "player");
            if (field == null || HasLiveUnityReference(field.GetValue(simManager)))
            {
                return;
            }

            GameObject player = FindPlayer();
            if (player == null && createIfMissing)
            {
                player = EnsurePlayer();
            }

            if (player != null)
            {
                Debug.Log("[GAMA] Auto-assigned player object: " + player.name);
                field.SetValue(simManager, player);
            }
        }

        public static void SetupGround(global::SimulationManager simManager, bool createIfMissing = false)
        {
            if (simManager == null)
            {
                return;
            }

            FieldInfo field = GetField(simManager.GetType(), "Ground");
            if (field == null || HasLiveUnityReference(field.GetValue(simManager)))
            {
                return;
            }

            GameObject ground = FindGround();
            if (ground == null && createIfMissing)
            {
                ground = EnsureTeleportAreaAndGround();
            }

            if (ground != null)
            {
                Debug.Log("[GAMA] Auto-assigned ground object: " + ground.name);
                field.SetValue(simManager, ground);
            }
        }

        private static void EnsureManagers(GameObject player, GameObject ground, GameObject lightObject)
        {
            global::ConnectionManager connectionManager = Object.FindFirstObjectByType<global::ConnectionManager>();
            global::SimulationManager simulationManager = Object.FindFirstObjectByType<global::SimulationManager>();

            GameObject managersObj = GameObject.Find("ManagersSolo") ?? GameObject.Find("GAMA Managers");
            if (managersObj == null)
            {
                managersObj = new GameObject("ManagersSolo");
            }

            if (connectionManager == null)
            {
                GameObject connectionObj = GamaSceneUtility.GetOrCreateChild(managersObj, "Connection Manager");
                connectionManager = GamaSceneUtility.GetOrAddComponent<global::ConnectionManager>(connectionObj);
            }

            if (simulationManager == null)
            {
                GameObject gameManagerObj = GamaSceneUtility.GetOrCreateChild(managersObj, "Game Manager");
                simulationManager = GamaSceneUtility.GetOrAddComponent<global::SimulationManagerSolo>(gameManagerObj);
            }

            SetupPlayer(simulationManager, true);
            SetupGround(simulationManager, true);
            AssignField(simulationManager, "lightObject", lightObject);
        }

        private static Light EnsureLight()
        {
            Light light = Object.FindFirstObjectByType<Light>();
            if (light != null)
            {
                return light;
            }

            GameObject lightObj = new GameObject("Directional Light");
            light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            return light;
        }

        private static GameObject EnsureTeleportAreaAndGround()
        {
            GameObject ground = FindGround();
            GameObject teleportArea = GameObject.Find("Teleport Area");
            if (teleportArea == null && ground != null && ground.transform.parent != null)
            {
                teleportArea = ground.transform.parent.gameObject;
            }

            if (teleportArea == null)
            {
                teleportArea = new GameObject("Teleport Area");
            }

            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.localScale = new Vector3(10f, 1f, 10f);
            }

            ground.transform.SetParent(teleportArea.transform, false);
            GamaSceneUtility.TrySetTag(ground, "ground");
            GamaSceneUtility.TrySetTag(teleportArea, "Teleportation");

            TeleportationArea groundArea = ground.GetComponent<TeleportationArea>();
            if (groundArea != null)
            {
                DestroyUnityObject(groundArea);
            }

            TeleportationArea area = GamaSceneUtility.GetOrAddComponent<TeleportationArea>(teleportArea);
            Collider collider = ground.GetComponent<Collider>();
            if (collider != null && !area.colliders.Contains(collider))
            {
                area.colliders.Add(collider);
            }

            return ground;
        }

        private static GameObject EnsurePlayer()
        {
            GameObject player = FindPlayer();
            if (player == null)
            {
                player = new GameObject("FPSPlayer");
            }
            else if (player.name == "XR Origin (XR Rig)" || player.name == "XR Origin" || player.name == "XROrigin" || player.name == "Player")
            {
                player.name = "FPSPlayer";
            }

            GamaSceneUtility.TrySetTag(player, "player");
            GamaSceneUtility.AddOptionalComponent(
                player,
                "UnityEngine.XR.Interaction.Toolkit.XRInteractionManager",
                "UnityEngine.XR.Interaction.Toolkit.Interaction.XRInteractionManager");
            GamaSceneUtility.AddOptionalComponent(
                player,
                "UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager");

            XROrigin origin = GamaSceneUtility.GetOrAddComponent<XROrigin>(player);
            GameObject cameraOffset = origin.CameraFloorOffsetObject != null
                ? origin.CameraFloorOffsetObject
                : GamaSceneUtility.GetOrCreateChild(player, "Camera Offset");

            if (cameraOffset.transform.parent != player.transform)
            {
                cameraOffset.transform.SetParent(player.transform, false);
            }

            if (cameraOffset.transform.localPosition == Vector3.zero)
            {
                cameraOffset.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            }

            Camera camera = origin.Camera ?? player.GetComponentInChildren<Camera>(true) ?? Camera.main ?? Object.FindFirstObjectByType<Camera>();
            GameObject cameraObj;
            if (camera == null)
            {
                cameraObj = new GameObject("Main Camera");
                camera = cameraObj.AddComponent<Camera>();
            }
            else
            {
                cameraObj = camera.gameObject;
            }

            cameraObj.name = "Main Camera";
            cameraObj.transform.SetParent(cameraOffset.transform, false);
            GamaSceneUtility.TrySetTag(cameraObj, "MainCamera");
            GamaSceneUtility.GetOrAddComponent<AudioListener>(cameraObj);
            GamaSceneUtility.AddOptionalComponent(
                cameraObj,
                "UnityEngine.InputSystem.XR.TrackedPoseDriver",
                "UnityEngine.SpatialTracking.TrackedPoseDriver");

            origin.CameraFloorOffsetObject = cameraOffset;
            origin.Camera = camera;

            EnsureGazeHierarchy(cameraOffset);
            EnsureControllerHierarchy(cameraOffset, "Left");
            EnsureControllerHierarchy(cameraOffset, "Right");
            EnsureLocomotionHierarchy(player);

            return player;
        }

        private static void EnsureGazeHierarchy(GameObject cameraOffset)
        {
            GameObject gazeInteractor = GamaSceneUtility.GetOrCreateChild(cameraOffset, "Gaze Interactor");
            GamaSceneUtility.AddOptionalComponent(
                gazeInteractor,
                "UnityEngine.XR.Interaction.Toolkit.Interactors.XRGazeInteractor",
                "UnityEngine.XR.Interaction.Toolkit.XRGazeInteractor");

            GameObject stabilized = GamaSceneUtility.GetOrCreateChild(cameraOffset, "Gaze Stabilized");
            GamaSceneUtility.AddOptionalComponent(
                stabilized,
                "UnityEngine.XR.Interaction.Toolkit.Transformers.XRTransformStabilizer",
                "UnityEngine.XR.Interaction.Toolkit.TransformStabilizer");
            GamaSceneUtility.GetOrCreateChild(stabilized, "Gaze Stabilized Attach");
        }

        private static void EnsureControllerHierarchy(GameObject cameraOffset, string side)
        {
            GameObject controller = GamaSceneUtility.GetOrCreateChild(cameraOffset, side + " Controller");
            float x = side == "Left" ? -0.25f : 0.25f;
            if (controller.transform.localPosition == Vector3.zero)
            {
                controller.transform.localPosition = new Vector3(x, -0.25f, 0.45f);
            }

            GamaSceneUtility.AddOptionalComponent(
                controller,
                "UnityEngine.XR.Interaction.Toolkit.ActionBasedController",
                "UnityEngine.XR.Interaction.Toolkit.Inputs.Controllers.ActionBasedController",
                "UnityEngine.XR.Interaction.Toolkit.XRController");
            GamaSceneUtility.AddOptionalComponent(
                controller,
                "UnityEngine.XR.Interaction.Toolkit.Inputs.ControllerInputActionManager");

            GameObject pokeInteractor = GamaSceneUtility.GetOrCreateChild(controller, "Poke Interactor");
            GamaSceneUtility.AddOptionalComponent(
                pokeInteractor,
                "UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor",
                "UnityEngine.XR.Interaction.Toolkit.XRPokeInteractor");

            GameObject pokePoint = GamaSceneUtility.GetOrCreateChild(pokeInteractor, "Poke Point");
            GamaSceneUtility.GetOrCreateChild(pokePoint, "Pinch_Pointer_LOD0");

            GameObject pokeAffordances = GamaSceneUtility.GetOrCreateChild(pokeInteractor, "Poke Point Affordances");
            GamaSceneUtility.GetOrCreateChild(pokeAffordances, "Poke");
            GamaSceneUtility.GetOrCreateChild(pokeAffordances, "NearFar");

            GameObject nearFarInteractor = GamaSceneUtility.GetOrCreateChild(controller, "Near-Far Interactor");
            GamaSceneUtility.AddOptionalComponent(
                nearFarInteractor,
                "UnityEngine.XR.Interaction.Toolkit.Interactors.XRNearFarInteractor",
                "UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor",
                "UnityEngine.XR.Interaction.Toolkit.XRRayInteractor");
            GamaSceneUtility.GetOrCreateChild(nearFarInteractor, "LineVisual");

            GameObject teleportInteractor = GamaSceneUtility.GetOrCreateChild(controller, "Teleport Interactor");
            GamaSceneUtility.AddOptionalComponent(
                teleportInteractor,
                "UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor",
                "UnityEngine.XR.Interaction.Toolkit.XRRayInteractor");

            EnsureControllerVisual(controller, side);

            GameObject teleportStabilized = GamaSceneUtility.GetOrCreateChild(cameraOffset, side + " Controller Teleport Stabilized Origin");
            GamaSceneUtility.AddOptionalComponent(
                teleportStabilized,
                "UnityEngine.XR.Interaction.Toolkit.Transformers.XRTransformStabilizer",
                "UnityEngine.XR.Interaction.Toolkit.TransformStabilizer");
            GamaSceneUtility.GetOrCreateChild(teleportStabilized, side + " Controller Stabilized Attach");
        }

        private static void EnsureControllerVisual(GameObject controller, string side)
        {
            GameObject visual = GamaSceneUtility.GetOrCreateChild(controller, side + " Controller Visual");
            GameObject universalController = GamaSceneUtility.GetOrCreateChild(visual, "UniversalController");

            GamaSceneUtility.GetOrCreateChild(universalController, "Bumper");
            GamaSceneUtility.GetOrCreateChild(universalController, "Button_Home");
            GamaSceneUtility.GetOrCreateChild(universalController, "Controller_Base");
            GamaSceneUtility.GetOrCreateChild(universalController, "TouchPad");
            GamaSceneUtility.GetOrCreateChild(universalController, "Trigger");

            GameObject thumbstickButtons = GamaSceneUtility.GetOrCreateChild(universalController, "XRController_Thumbstick_Buttons");
            GamaSceneUtility.GetOrCreateChild(thumbstickButtons, "Button_A");
            GamaSceneUtility.GetOrCreateChild(thumbstickButtons, "Button_B");
            GamaSceneUtility.GetOrCreateChild(thumbstickButtons, "ThumbStick");
            GamaSceneUtility.GetOrCreateChild(thumbstickButtons, "ThumbStick_Base");
        }

        private static void EnsureLocomotionHierarchy(GameObject player)
        {
            GameObject locomotion = GamaSceneUtility.GetOrCreateChild(player, "Locomotion");
            GamaSceneUtility.TrySetTag(locomotion, "locomotion");
            GamaSceneUtility.GetOrAddComponent<global::MoveHorizontal>(locomotion);
            GamaSceneUtility.GetOrAddComponent<global::MoveVertical>(locomotion);
            GamaSceneUtility.AddOptionalComponent(
                locomotion,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionMediator",
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer");

            GameObject turn = GamaSceneUtility.GetOrCreateChild(locomotion, "Turn");
            GamaSceneUtility.AddOptionalComponent(
                turn,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider",
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.ActionBasedSnapTurnProvider",
                "UnityEngine.XR.Interaction.Toolkit.ActionBasedSnapTurnProvider");

            GameObject move = GamaSceneUtility.GetOrCreateChild(locomotion, "Move");
            GamaSceneUtility.TrySetTag(move, "move");
            GamaSceneUtility.AddOptionalComponent(
                move,
                "UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.DynamicMoveProvider",
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.DynamicMoveProvider",
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider",
                "UnityEngine.XR.Interaction.Toolkit.ActionBasedContinuousMoveProvider");

            GameObject grabMove = GamaSceneUtility.GetOrCreateChild(locomotion, "Grab Move");
            GamaSceneUtility.AddOptionalComponent(
                grabMove,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.GrabMoveProvider",
                "UnityEngine.XR.Interaction.Toolkit.GrabMoveProvider");

            GameObject teleportation = GamaSceneUtility.GetOrCreateChild(locomotion, "Teleportation");
            GamaSceneUtility.AddOptionalComponent(
                teleportation,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider",
                "UnityEngine.XR.Interaction.Toolkit.TeleportationProvider");

            GameObject climb = GamaSceneUtility.GetOrCreateChild(locomotion, "Climb");
            GamaSceneUtility.AddOptionalComponent(
                climb,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing.ClimbProvider",
                "UnityEngine.XR.Interaction.Toolkit.ClimbProvider");

            GameObject climbTeleport = GamaSceneUtility.GetOrCreateChild(climb, "Climb Teleport");
            GamaSceneUtility.AddOptionalComponent(
                climbTeleport,
                "UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing.ClimbTeleportDestinationIndicator",
                "UnityEngine.XR.Interaction.Toolkit.ClimbTeleportDestinationIndicator");
        }

        private static GameObject FindPlayer()
        {
            GameObject player = GamaSceneUtility.FindGameObjectWithTag("player");
            if (player != null)
            {
                return player;
            }

            XROrigin xrOrigin = Object.FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null)
            {
                return xrOrigin.gameObject;
            }

            return GameObject.Find("FPSPlayer") ??
                   GameObject.Find("XR Origin (XR Rig)") ??
                   GameObject.Find("XR Origin") ??
                   GameObject.Find("XROrigin") ??
                   GameObject.Find("Player") ??
                   GameObject.Find("XR Rig");
        }

        private static GameObject FindGround()
        {
            return GamaSceneUtility.FindGameObjectWithTag("ground") ??
                   GameObject.Find("Ground") ??
                   GameObject.Find("Plane") ??
                   GameObject.Find("Teleport Area/Ground") ??
                   GameObject.Find("Floor");
        }

        private static void AssignField(object target, string fieldName, object value)
        {
            if (target == null || value == null)
            {
                return;
            }

            FieldInfo field = GetField(target.GetType(), fieldName);
            if (field != null && !HasLiveUnityReference(field.GetValue(target)))
            {
                field.SetValue(target, value);
            }
        }

        private static bool HasLiveUnityReference(object value)
        {
            if (value == null)
            {
                return false;
            }

            if (value is Object unityObject)
            {
                return unityObject != null;
            }

            return true;
        }

        private static void DestroyUnityObject(Object unityObject)
        {
            if (unityObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(unityObject);
            }
            else
            {
                Object.DestroyImmediate(unityObject);
            }
        }

        private static FieldInfo GetField(System.Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
