using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#if UNITY_EDITOR
using UnityEditor;
#endif

public abstract partial class SimulationManager : MonoBehaviour
{
    private sealed class RuntimeAgentRecord
    {
        public string Key;
        public string SpeciesName;
        public string AgentId;
        public GameObject Root;
        public GameObject VisualRoot;
        public bool IsDynamic;
        public int LastSeenTick;
        public bool CurrentlyVisible = true;
        public bool UsesPrefabOverride;
        public Vector3 BasePosition;
        public Quaternion BaseRotation = Quaternion.identity;
        public bool HasBaseTransform;
        public Vector3 VisualAnchor;
        public bool HasVisualAnchor;
        public Vector3 LastPositionOffset;
        public Vector3 LastRotationOffsetEuler;
    }

    private sealed class RuntimeSyncCounters
    {
        public int Created;
        public int Updated;
        public int Removed;
    }

    private static System.Collections.Generic.Dictionary<string, int> debugLogCounts = new System.Collections.Generic.Dictionary<string, int>();
    private static System.Collections.Generic.Dictionary<string, bool> debugSummaryLogged = new System.Collections.Generic.Dictionary<string, bool>();
    [SerializeField] protected InputActionReference primaryRightHandButton = null;


    [Header("Base GameObjects")]
    [SerializeField] protected GameObject player;
    [SerializeField] protected GameObject Ground;
    
    [SerializeField, Tooltip("Organize runtime agents into [GAMA] Runtime Live Agents / species hierarchy.")]
    protected bool groupRuntimeAgentsBySpecies = true;

    [Header("Prefab viewport streaming")]
    [SerializeField] protected bool streamPrefabsByCameraView = true;
    [SerializeField, Tooltip("Legacy toggle kept for backward compatibility. SceneView camera is ignored; streaming uses Game camera only.")]
    protected bool preferSceneViewCameraInEditor = false;
    [SerializeField] protected bool keepSelectedPrefabsLoaded = true;
    [SerializeField, Min(0f)] protected float prefabViewPadding = 20f;
    [SerializeField, Min(0.02f)] protected float prefabViewUpdateInterval = 0.1f;
    [SerializeField, Tooltip("When enabled, prefabs beyond globalPrefabRenderDistance are deactivated (with hysteresis), in addition to frustum culling.")]
    protected bool enablePrefabRenderDistance = true;
    [SerializeField, Min(0f), Tooltip("World-space distance from camera at which streaming may disable the prefab (uses bounds closest point).")]
    protected float globalPrefabRenderDistance = 1500f;
    [SerializeField, Min(0f), Tooltip("Reactivation requires coming this much closer than globalPrefabRenderDistance to avoid flicker.")]
    protected float prefabRenderDistanceHysteresis = 75f;
    [SerializeField, Min(1), Tooltip("Max prefab agents evaluated per streaming tick (round-robin). Lower = less CPU per frame, slower convergence.")]
    protected int prefabStreamingBudgetPerTick = 1500;
    [SerializeField, Tooltip("Reuse released prefab instances instead of Destroy/Instantiate when signature matches.")]
    protected bool enablePrefabPooling = true;
    [SerializeField, Min(0), Tooltip("Cap pooled instances per prefab signature; excess are destroyed. 0 = always destroy.")]
    protected int maxPooledPrefabsPerSignature = 128;
    [SerializeField, Tooltip("Sets enableInstancing on shared materials once (author LOD Group on prefabs for mesh LOD).")]
    protected bool enableGpuInstancingForPrefabMaterials = true;
    [SerializeField] protected bool logPrefabStreamingStats = false;
    [SerializeField, Min(0.5f)] protected float prefabStreamingStatsInterval = 3f;

    [Header("Agent update throttling")]
    [SerializeField, Tooltip("Limit how many agents are applied per tick to avoid long main-thread spikes.")]
    protected bool limitAgentUpdatesPerTick = true;
    [SerializeField, Min(1), Tooltip("Maximum number of agent entries processed each tick when applying world updates.")]
    protected int maxAgentUpdatesPerTick = 1000;
    [SerializeField] protected bool logAgentUpdateBudgetStats = false;
    [SerializeField, Min(0.5f)] protected float agentUpdateBudgetStatsInterval = 3f;
    [SerializeField, Tooltip("If false, non-prefab geometries (roads/buildings) are never destroyed when missing from a tick; they remain in hierarchy and only rendering is toggled by streaming.")]
    protected bool removeMissingGeometryAgents = false;
    [SerializeField, Min(1), Tooltip("Number of consecutive missing world ticks before an agent is culled/removed. Prevents one-frame global disappearances on partial updates.")]
    protected int missingTicksBeforeCull = 2;


    // optional: define a scale between GAMA and Unity for the location given
    [Header("Coordinate conversion parameters")]
    protected float GamaCRSCoefX = 1.0f;
    protected float GamaCRSCoefY = 1.0f;
     protected float GamaCRSOffsetX = 0.0f;
    protected float GamaCRSOffsetY = 0.0f;


    protected Transform XROrigin;
   
    // Z offset and scale
     protected float GamaCRSOffsetZ = 0.0f;

    protected List<GameObject> toFollow;

    XRInteractionManager interactionManager;

    // ################################ EVENTS ################################
    // called when the current game state changes
    public static event Action<GameState> OnGameStateChanged;
    // called when the game is restarted
    public static event Action OnGameRestarted;

    // called when the world data is received
    //    public static event Action<WorldJSONInfo> OnWorldDataReceived;
    // ########################################################################

    protected Dictionary<string, GamaAgentVisualState> visualStateCache;
    protected Dictionary<string, string> resolvedPrefabSignatures;
    
    private Transform runtimeAgentsRoot;
    private Dictionary<string, Transform> runtimeSpeciesParents;
    protected Dictionary<string, List<object>> geometryMap;
    private readonly Dictionary<string, RuntimeAgentRecord> runtimeAgentRecords =
        new Dictionary<string, RuntimeAgentRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeSyncCounters> runtimeSyncCountersBySpecies =
        new Dictionary<string, RuntimeSyncCounters>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> invalidGeometryFallbackCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    protected Dictionary<string, PropertiesGAMA> propertyMap = null;
    private static readonly HashSet<string> missingPrefabWarnings = new HashSet<string>();
    private readonly Dictionary<string, Vector3> previousPrefabPositions = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, string> previousPrefabPropertyIds = new Dictionary<string, string>();
    private readonly Dictionary<string, Vector3> prefabHeadingSourcePositions = new Dictionary<string, Vector3>();
    private readonly Dictionary<string, string> prefabHeadingSourcePropertyIds = new Dictionary<string, string>();
    private readonly HashSet<string> consumedPrefabHeadingSources = new HashSet<string>();
    private readonly Plane[] prefabStreamingPlanes = new Plane[6];
    private readonly List<string> prefabStreamingKeys = new List<string>();
    private readonly Dictionary<string, Stack<GameObject>> prefabPools = new Dictionary<string, Stack<GameObject>>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Per-instance hysteresis when culling prefabs farther than render distance (key = GetInstanceID).</summary>
    private readonly Dictionary<int, bool> prefabDistanceCulled = new Dictionary<int, bool>();
    private readonly HashSet<int> gpuInstancingTouchedMaterials = new HashSet<int>();
    private float prefabViewTimer;
    private int prefabStreamingCursor;
    private Transform prefabPoolRoot;
    private float prefabStreamingLastDiagTime;
    private bool pendingWorldUpdateRemovalPass;
    private int pendingWorldAgentIndex;
    private int pendingWorldPrefabIndex;
    private int pendingWorldGeomIndex;
    private float agentUpdateBudgetLastDiagTime;
    private bool loggedMissingMainCameraForStreaming;
    private readonly Dictionary<string, int> missingAgentTickCounts = new Dictionary<string, int>();
    private int runtimeLiveTickSerial;

    protected List<GameObject> SelectedObjects;


    protected bool handleGeometriesRequested;
    protected bool handleGroundParametersRequested;

    protected CoordinateConverter converter;
    protected PolygonGenerator polyGen;
    protected ConnectionParameter parameters;
    protected AllProperties propertiesGAMA;
    protected WorldJSONInfo infoWorld;
    protected AnimationInfo infoAnimation = null;
    protected GameState currentState;

    public static SimulationManager Instance = null;


    //allows to define the minimal time between two interactions
    protected float timeWithoutInteraction = 1.0f; //in second

    protected float remainingTime = 0.0f;


    protected bool sendMessageToReactivatePositionSent = false;

    protected float maxTimePing = 1.0f;
    protected float currentTimePing = 0.0f;

    protected List<GameObject> toDelete;

    protected bool readyToSendPosition = false;

    protected bool readyToSendPositionInit = true;

    protected float TimeSendPosition = 0.05f;
    protected float TimeSendPositionAfterMoving = 1.0f;
    protected float TimerSendPosition = 0.0f;

    protected List<GameObject> locomotion;
    protected MoveHorizontal mh = null;
    protected MoveVertical mv = null;

    protected DEMData data;
    protected DEMDataLoc dataLoc;
    protected TeleoportAreaInfo dataTeleport;
    protected WallInfo dataWall;
    protected EnableMoveInfo enableMove;


    protected float TimeSendInit = 0.5f;
    protected float TimerSendInit ;

    //Cache
    Dictionary<string, string> connectionID = new Dictionary<string, string>();
    HashSet<string> toRemove = new HashSet<string>();

    bool hasSimulator ;
    private ConnectionManager subscribedConnectionManager;
    private const float ConnectionSubscribeRetryIntervalSeconds = 0.5f;
    private const float SocketClosedWarningIntervalSeconds = 2f;
    private float nextConnectionSubscribeRetryTime;
    private float nextSocketClosedWarningTime;
    private bool staticPreviewHiddenAfterRuntimeData;
    private int runtimeFlowLogCount;
    private int runtimeCreateLogCount;

    // ############################################ UNITY FUNCTIONS ############################################
    void Awake()
    {
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.SetupPlayer(this, true);
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.SetupGround(this, true);

        hasSimulator = UnityEngine.Object.FindFirstObjectByType<XRDeviceSimulator>() != null;
        connectionID["id"] = ConnectionManager.Instance != null ? ConnectionManager.Instance.GetConnectionId() : StaticInformation.getId();
        Debug.Log("[GAMA] SimulationManager initialized");
        Instance = this;
        TrySubscribeConnectionManager();
        SelectedObjects = new List<GameObject>();
        // toDelete = new List<GameObject>();

        locomotion = new List<GameObject>(GamaSceneUtility.FindGameObjectsWithTag("locomotion"));
        if (player == null)
        {
            player = GamaSceneUtility.FindGameObjectWithTag("player") ??
                     GameObject.Find("FPSPlayer") ??
                     GameObject.Find("XR Origin (XR Rig)") ??
                     GameObject.Find("XR Origin") ??
                     GameObject.Find("XROrigin");
        }

        if (player == null)
        {
            Debug.LogError("[GAMA] SimulationManager could not find or create a player object.");
            enabled = false;
            return;
        }

        mh = player.GetComponentInChildren<MoveHorizontal>(true);
        mv = player.GetComponentInChildren<MoveVertical>(true);
         
        XROrigin = player.transform;
        playerMovement(false);
        toFollow = new List<GameObject>();

       
    }


    void OnEnable()
    {
        TrySubscribeConnectionManager();
    }

    void OnDisable()
    {
        UnsubscribeConnectionEvents();
    }

    void OnDestroy()
    {
        UnsubscribeConnectionEvents();
        DrainPrefabPools();
    }

    void Start()
    {
        visualStateCache = new Dictionary<string, GamaAgentVisualState>(StringComparer.Ordinal);
        resolvedPrefabSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        runtimeSpeciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        runtimeAgentRecords.Clear();
        runtimeSyncCountersBySpecies.Clear();
        invalidGeometryFallbackCounts.Clear();
        runtimeLiveTickSerial = 0;
        runtimeFlowLogCount = 0;
        runtimeCreateLogCount = 0;
        staticPreviewHiddenAfterRuntimeData = false;

        geometryMap = new Dictionary<string, List<object>>();
        handleGeometriesRequested = false;
        // handlePlayerParametersRequested = false;
        handleGroundParametersRequested = false;
        TrySubscribeConnectionManager();
    }


    void FixedUpdate()
    {
        TrySubscribeConnectionManager();

        if (ConnectionManager.Instance == null)
        {
            return;
        }

        if (sendMessageToReactivatePositionSent)
        {
            if (TrySendExecutableAsk("player_position_updated", connectionID, "player position updated"))
            {
                sendMessageToReactivatePositionSent = false;
            }
        }

        if (handleGroundParametersRequested)
        {
            InitGroundParameters();
            handleGroundParametersRequested = false;

           // Debug.Log("handleGroundParametersRequested: " + handleGroundParametersRequested);

        }

        if (handleGeometriesRequested && infoWorld != null && infoWorld.isInit)// && propertyMap != null)
        {

            sendMessageToReactivatePositionSent = true;
            GenerateGeometries(true, null);
            handleGeometriesRequested = false;
            UpdateGameState(GameState.GAME);
         

        }
        if (infoWorld != null && !infoWorld.isInit && IsGameState(GameState.LOADING_DATA))
        {
            infoWorld = null;
        }
        if (converter != null && data != null)
        {
            manageUpdateTerrain();
        }
        if (converter != null && dataLoc != null)
        {
            manageSetValueTerrain();
        }
        if (converter != null && dataTeleport != null)
        {
            manageTeleportationArea();
        }
        if (converter != null &&  dataWall != null)
        {
            manageWalls();
        }
        if (enableMove != null)
        {
            playerMovement(enableMove.enableMove);
            enableMove = null;
        }

        if (infoAnimation != null)
        {
            updateAnimation();
            infoAnimation = null;
        }

        if (IsGameState(GameState.LOADING_DATA))
        {
            if (TimerSendInit > 0)
                TimerSendInit -= Time.deltaTime;
            if (TimerSendInit <= 0)
            {
                TimerSendInit = TimeSendInit;
                TrySendExecutableAsk("send_init_data", connectionID, "initial data");
            }
        }

        if (IsGameState(GameState.GAME))
        {
           // Debug.Log("readyToSendPosition: " + readyToSendPosition + " readyToSendPositionInit:" + readyToSendPositionInit + " TimerSendPosition: "+ TimerSendPosition);
            if ((readyToSendPosition && TimerSendPosition <= 0.0f)|| readyToSendPositionInit)
                UpdatePlayerPosition();
            UpdateGameToFollowPosition();
            if (infoWorld != null && !infoWorld.isInit)
                UpdateAgentsList();
        }

    }

    private void Update()
    {
        RetrySubscribeConnectionManagerIfNeeded();

        if (remainingTime > 0)
            remainingTime -= Time.deltaTime;
        if (TimerSendPosition > 0)
        {
            TimerSendPosition -= Time.deltaTime;
        }
        if (currentTimePing > 0)
        { 
            currentTimePing -= Time.deltaTime;
            if (currentTimePing <= 0 && ConnectionManager.Instance != null)
            {
                ConnectionManager.Instance.Reconnect();
            }
        }


        if (primaryRightHandButton != null && primaryRightHandButton.action.triggered)
        {
            TriggerMainButton();
        }
      /*  if (TryReconnectButton != null && TryReconnectButton.action.triggered)
        {
            Debug.Log("TryReconnectButton activated");
            TryReconnect();
        }*/

        OtherUpdate();
        UpdatePrefabViewportStreaming(Time.deltaTime);
    }


    

    private void updateAnimation()
    {

        foreach (String n in infoAnimation.names) {
            GameObject obj;
            if (!TryGetRuntimeAgentObjectByAgentId(n, out obj)) continue;

            Animator m_animator = obj.GetComponent<Animator>();
            if (m_animator == null)
            {
                m_animator = obj.GetComponentInChildren<Animator>();
            }

            if (m_animator != null)
            {
                foreach (ParameterVal p in infoAnimation.parameters)
                {
                    if (p.type.Equals("int"))
                        m_animator.SetInteger(p.key, p.intVal);
                    else if (p.type.Equals("float"))
                        m_animator.SetFloat(p.key, p.floatVal);
                    else if (p.type.Equals("bool"))
                        m_animator.SetBool(p.key, p.boolVal);
                }
                foreach (String t in infoAnimation.triggers)
                {
                    m_animator.SetTrigger(t);

                }
            }
           
        }
       
    }
    private void manageTeleportationArea()
    {
        if (polyGen == null)
        {
            polyGen = PolygonGenerator.GetInstance();
            polyGen.Init(converter);
        }
        UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea ta = null;
        GameObject[] objs = GamaSceneUtility.FindGameObjectsWithTag("Teleportation");
        foreach (GameObject o in objs)
        {
            if (o.name.Equals(dataTeleport.teleportId))
            {
                ta = o.GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
                if (ta != null)
                {
                    foreach(Collider col in ta.colliders)
                    {
                        GameObject.DestroyImmediate(col.gameObject);
                    }
                    ta.colliders.Clear(); 
                }
                break;
                
            }
        }
        if (ta == null)
        {
            ta = CreateTeleportationAreaObject(dataTeleport.teleportId);
        }
        
      
        for (int i = 0; i < dataTeleport.pointsGeom.Count; i++)
        {
            List<int> pt = dataTeleport.pointsGeom[i].c;
            float YoffSet = (0.0f + dataTeleport.offsetYGeom[i]) / (0.0f + parameters.precision);

            PropertiesGAMA prop = new PropertiesGAMA();
            prop.id = dataTeleport.teleportId + "_"+ i;
            prop.hasCollider = true;
            prop.isInteractable = false; 
            prop.isGrabable = false;
            prop.hasPrefab = false;
            prop.visible = true;
            prop.is3D = true;
            prop.height = dataTeleport.height;
            prop.toFollow = false;

            GameObject obj = polyGen.GeneratePolygons(false, prop.id, pt.ToArray(), prop, parameters.precision);

            obj.transform.position = new Vector3(obj.transform.position.x, obj.transform.position.y + YoffSet, obj.transform.position.z);
            MeshCollider mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = polyGen.bottomMesh;
            obj.transform.parent = ta.gameObject.transform;
            ta.colliders.Add(mc);
           

        }
        //to take into account the new colliders
        ta.enabled = false;
        ta.enabled = true;

        dataTeleport = null;
    }

    private UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea CreateTeleportationAreaObject(string objectName)
    {
        GameObject obj = new GameObject(objectName);
        GamaSceneUtility.TrySetTag(obj, "Teleportation");
        return obj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
    }

    private void manageWalls()
    {
       
       if (polyGen == null)
        {
            polyGen = PolygonGenerator.GetInstance();
            polyGen.Init(converter);
        }

        GameObject wallObj = new GameObject("Walls");

        GameObject[] objs =   GamaSceneUtility.FindGameObjectsWithTag("InvisibleWall");
        foreach (GameObject o in objs)
        {
            if (o.name.Equals(dataWall.wallId))
            GameObject.DestroyImmediate(o);

        }

        for (int i = 0; i < dataWall.pointsGeom.Count;i++ )
        {
            List<int> pt = dataWall.pointsGeom[i].c;
            float YoffSet = (0.0f + dataWall.offsetYGeom[i]) / (0.0f + parameters.precision);

            PropertiesGAMA prop = new PropertiesGAMA();
            prop.id = dataWall.wallId;
            prop.hasCollider = true;
            prop.tag = "InvisibleWall";
            prop.isInteractable = false;
            prop.isGrabable = false;
            prop.hasPrefab = false;
            prop.visible = false;
            prop.height = dataWall.height;
            prop.is3D = true;
            prop.toFollow = false;

           GameObject obj = polyGen.GeneratePolygons(false, dataWall.wallId, pt.ToArray(), prop, parameters.precision);
        
            obj.transform.position = new Vector3(obj.transform.position.x, obj.transform.position.y + YoffSet, obj.transform.position.z);
            obj.transform.parent = wallObj.transform;
            MeshCollider mc = obj.AddComponent<MeshCollider>();
            mc.sharedMesh = polyGen.surroundMesh;
            
        }

        dataWall = null;
    }


    private void manageSetValueTerrain()
    {
        Terrain[] terrains = Terrain.activeTerrains;
        if (dataLoc.rows.Count == 0) return;
        foreach (Terrain t in terrains)
        {

            if (t.name == dataLoc.id)
            {
                float valMax = t.terrainData.size.y;

                int resolution = t.terrainData.heightmapResolution;

                if (dataLoc.valMax > valMax)
                {
                    float oldV = valMax;
                    valMax = dataLoc.valMax;
                    float[,] heightsT = new float[t.terrainData.heightmapResolution, t.terrainData.heightmapResolution];
                    for (int j = 0; j < resolution; j++)
                    {
                        for (int i = 0; i < resolution; i++)
                        {
                            float v = t.terrainData.GetHeight(i, j);
                            heightsT[i, j] = v * oldV / valMax;
                        }
                    }

                    t.terrainData.SetHeights(0, 0, heightsT);
                }
                float[,] heights = new float[dataLoc.rows[0].h.Count, dataLoc.rows.Count];
                int x = 1;
                foreach (Row r in dataLoc.rows)
                {
                   int y = 0;
                   foreach (int v in r.h)
                   {
                        heights[dataLoc.rows.Count - x, y] = ((v + 0.0f) / (valMax + 0.0f));
                        y++;
                   }
                   x++;
                }

                t.terrainData.SetHeights(dataLoc.indexX, resolution - 1 - dataLoc.indexY, heights);
                break;
            }
        }
        dataLoc = null;
    }

    private void manageUpdateTerrain()
    {
        Terrain[] terrains = Terrain.activeTerrains;

        foreach (Terrain t in terrains)
        {

            if (t.name == data.id)
            {
                t.gameObject.transform.position = new Vector3(0, 0,-1 * data.sizeY);
                t.terrainData.size = new Vector3(data.sizeX, data.valMax, data.sizeY);
                float[,] heights = new float[t.terrainData.heightmapResolution, t.terrainData.heightmapResolution];
                int x = 1;
                foreach (Row r in data.rows)
                {
                    int y = 0;
                    foreach (int v in r.h)
                    {
                        heights[data.rows.Count - x, y] = ((v + 0.0f) / (data.valMax + 0.0f));

                        y++;
                    }
                    x++;
                }
                t.terrainData.SetHeights(0, 0, heights);

                break;
            }
        }
        data = null;
    }
    

    void playerMovement(Boolean active)
    {
        foreach (GameObject loc in locomotion)
        {
            loc.SetActive(active);
        }
         if (mh != null)
         {
             mh.enabled = active;
         }
         if (mv != null)
         {
             mv.enabled = active;
         }
        readyToSendPositionInit = active;
    }


    bool GenerateGeometries(bool initGame, HashSet<string> toRemove)
    {

        SnapshotPrefabHeadingSources();

        if (infoWorld.position != null && infoWorld.position.Count > 1 && (initGame || !sendMessageToReactivatePositionSent))
        {
            Vector3 pos = converter.fromGAMACRS(infoWorld.position[0], infoWorld.position[1], infoWorld.position[2]);
            XROrigin.localPosition = pos;
            sendMessageToReactivatePositionSent = true;
            readyToSendPosition = true;
            TimerSendPosition = TimeSendPositionAfterMoving;

            playerMovement(true);
        }

        Camera immediateStreamingCamera = GetPrefabStreamingCamera();
        bool immediateFrustumEnabled = streamPrefabsByCameraView && immediateStreamingCamera != null;
        if (immediateFrustumEnabled)
        {
            GeometryUtility.CalculateFrustumPlanes(immediateStreamingCamera, prefabStreamingPlanes);
        }

        bool budgetedPass = !initGame && limitAgentUpdatesPerTick && maxAgentUpdatesPerTick > 0;
        int budget = budgetedPass ? maxAgentUpdatesPerTick : int.MaxValue;
        int startAgentIndex = budgetedPass ? pendingWorldAgentIndex : 0;
        int cptPrefab = budgetedPass ? pendingWorldPrefabIndex : 0;
        int cptGeom = budgetedPass ? pendingWorldGeomIndex : 0;
        int processedAgentCount = 0;
        if (!initGame && startAgentIndex == 0)
        {
            runtimeLiveTickSerial++;
            runtimeSyncCountersBySpecies.Clear();
        }

        if (toRemove != null) RemoveKeptRuntimeAgentNames(toRemove, infoWorld.keepNames);

        for (int i = startAgentIndex; i < infoWorld.names.Count; i++)
        {
            string name = infoWorld.names[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "agent_" + i;
            }

            string propId = infoWorld.propertyID[i];

            PropertiesGAMA prop = null;
            if (propertyMap == null || !propertyMap.TryGetValue(propId, out prop) || prop == null)
            {
                continue;
            }
            Attributes attributes = infoWorld.GetAttributesAt(i);
            GamaAgentVisualState visualState = ResolveAgentVisualState(name, prop, attributes);
            string speciesName = ResolveRuntimeSpeciesName(prop, propId);
            string agentKey = MakeRuntimeAgentKey(speciesName, name);
            bool dynamicUpdate = !initGame;

            GameObject obj = null;

            if (prop.hasPrefab)
            {
                string desiredPrefabSignature = ResolvePrefabSignature(prop, attributes);
                if (initGame || !geometryMap.ContainsKey(agentKey))
                {
                    obj = instantiatePrefab(name, agentKey, speciesName, prop, attributes, desiredPrefabSignature, initGame);

                }
                else
                {
                    List<object> o = geometryMap[agentKey];
                    GameObject obj2 = (GameObject)o[0];
                    PropertiesGAMA p = (PropertiesGAMA)o[1];
                    if (p == prop && !NeedsPrefabRebuild(obj2, desiredPrefabSignature))
                    {
                        obj = obj2;
                    }
                    else
                    {

                        obj2.transform.position = new Vector3(0, -100, 0);
                        geometryMap.Remove(agentKey);
                        previousPrefabPositions.Remove(agentKey);
                        previousPrefabPropertyIds.Remove(agentKey);
                        UnregisterRuntimeAgent(agentKey);
                        if (toFollow != null && toFollow.Contains(obj2))
                            toFollow.Remove(obj2);

                        ReleasePrefabInstance(obj2);
                        obj = instantiatePrefab(name, agentKey, speciesName, prop, attributes, desiredPrefabSignature, initGame);

                    }

                }
                List<int> pt = infoWorld.pointsLoc[cptPrefab].c;
                Vector3 basePos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                basePos.y += prop.yOffsetF;
                Vector3 pos = basePos + visualState.PositionOffset;
                Quaternion baseRotation = ResolvePrefabHeadingRotation(agentKey, prop, pt, basePos);
                Quaternion rotation = ComposePrefabRuntimeRotation(baseRotation, visualState, obj);
                obj.transform.SetPositionAndRotation(pos, rotation);
                previousPrefabPositions[agentKey] = basePos;
                previousPrefabPropertyIds[agentKey] = prop.id ?? string.Empty;
                ApplyAgentVisualState(obj, prop, visualState, true, Vector3.zero);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                //obj.SetActive(true);
                RegisterRuntimeAgent(agentKey, speciesName, name, obj, dynamicUpdate, visualState, basePos, baseRotation, basePos);
                if(toRemove != null)
                {
                    toRemove.Remove(agentKey);
                    toRemove.Remove(name);
                }
                cptPrefab++;

            }
            else
            {

                if (polyGen == null)
                {
                    polyGen = PolygonGenerator.GetInstance();
                    polyGen.Init(converter);
                }

                int[] pt = infoWorld.pointsGeom[cptGeom].c.ToArray();
                float yOffset = (0.0f + infoWorld.offsetYGeom[cptGeom]) / (0.0f + parameters.precision);
                Vector3 polygonBasePosition = new Vector3(0f, yOffset, 0f);
                bool polygonInputValid = IsRuntimePolygonInputValid(pt);

                Vector3 computedWorldAnchor = Vector3.zero;
                if (pt != null && pt.Length >= 2)
                {
                    int pointCount = pt.Length / 2;
                    if (pointCount > 0)
                    {
                        Vector3 sum = Vector3.zero;
                        for (int ptIdx = 0; ptIdx < pointCount; ptIdx++)
                        {
                            Vector2 pt2d = converter.fromGAMACRS2D(pt[ptIdx * 2], pt[ptIdx * 2 + 1]);
                            sum += new Vector3(pt2d.x, yOffset, pt2d.y);
                        }
                        computedWorldAnchor = sum / pointCount;
                    }
                }

                if(initGame || !geometryMap.ContainsKey(agentKey))
                {
                    obj = polygonInputValid
                        ? polyGen.GeneratePolygons(false, name, pt, prop, parameters.precision)
                        : new GameObject(name);
                   if(prop.hasCollider)
                    {
                        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            MeshCollider mc = obj.AddComponent<MeshCollider>();
                            mc.sharedMesh = meshFilter.sharedMesh;
                            if (prop.isGrabable) mc.convex = true;
                        }
                    }
                    instantiateGO(obj, name, prop);
                    ParentRuntimeAgent(obj, speciesName);
                    if (geometryMap != null)
                    {
                        geometryMap[agentKey] = new List<object> { obj, prop };
                    }

                }
                else
                {
                    List<object> o = geometryMap[agentKey];
                    GameObject obj2 = (GameObject)o[0];
                    PropertiesGAMA p = (PropertiesGAMA)o[1];
                    if (p == prop)
                    {
                        obj = obj2;
                        if (polygonInputValid)
                        {
                            polyGen.UpdatePolygon(obj, pt);
                        }

                        if(prop.hasCollider)
                        {
                            MeshCollider collider = obj.GetComponent<MeshCollider>();
                            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                            if (collider != null && meshFilter != null)
                            {
                                collider.sharedMesh = meshFilter.sharedMesh;
                            }
                        }
                    }
                }

                ApplyAgentVisualState(obj, prop, visualState, false, polygonBasePosition, computedWorldAnchor);
                HandleInvalidDynamicGeometryFallback(obj, speciesName, visualState, computedWorldAnchor, dynamicUpdate, !polygonInputValid);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                RegisterRuntimeAgent(agentKey, speciesName, name, obj, dynamicUpdate, visualState, polygonBasePosition, Quaternion.identity, computedWorldAnchor);
                if(toRemove != null)
                {
                    toRemove.Remove(agentKey);
                    toRemove.Remove(name);
                }
                cptGeom++;
            }

            if (budgetedPass)
            {
                processedAgentCount++;
                if (processedAgentCount >= budget && i + 1 < infoWorld.names.Count)
                {
                    pendingWorldAgentIndex = i + 1;
                    pendingWorldPrefabIndex = cptPrefab;
                    pendingWorldGeomIndex = cptGeom;
                    EmitAgentUpdateBudgetDiagnostic(processedAgentCount, infoWorld.names.Count, pendingWorldAgentIndex);
                    return false;
                }
            }

        }

        pendingWorldAgentIndex = 0;
        pendingWorldPrefabIndex = 0;
        pendingWorldGeomIndex = 0;
       
        if (infoWorld.attributes != null && infoWorld.attributes.Count > 0)
            ManageAttributes(infoWorld.attributes);


        if (initGame)
            AdditionalInitAfterGeomLoading();

        infoWorld = null;
        return true;
    }


    bool loadedAlready = false;

    // ############################################ GAMESTATE UPDATER ############################################
    public void UpdateGameState(GameState newState)
    {

        switch (newState)
        {

            case GameState.MENU:
                break;

            case GameState.WAITING:
                break;

            case GameState.LOADING_DATA:
                if (!loadedAlready)
                {
                    Debug.Log("[GAMA] Loading initial data from middleware");
                    TrySendExecutableAsk("send_init_data", connectionID, "initial data");

                    TimerSendInit = TimeSendInit;
                    loadedAlready = true;
                }
                break;

            case GameState.GAME:
                loadedAlready = false;
                TrySendExecutableAsk("player_ready_to_receive_geometries", connectionID, "player ready");

                break;

            case GameState.END:
                break;

            case GameState.CRASH:
                Debug.LogWarning("[GAMA] Simulation crashed");
                break;

            default:
                break;
        }

        currentState = newState;
        OnGameStateChanged?.Invoke(currentState);
    }



    // ############################# INITIALIZERS ####################################


    private void InitGroundParameters()
    {

        if (Ground == null || converter == null || parameters == null || parameters.world == null || parameters.world.Count < 2)
        {
            return;
        }
        Vector3 ls = converter.fromGAMACRS(parameters.world[0], parameters.world[1], 0);

        if (ls.z < 0)
            ls.z = -ls.z;
        if (ls.x < 0)
            ls.x = -ls.x;
        ls.y = Ground.transform.localScale.y;

        Ground.transform.localScale = ls;
        Vector3 ps = converter.fromGAMACRS(parameters.world[0] / 2, parameters.world[1] / 2, 0);

        Ground.transform.position = ps;

    }


    private void UpdateGameToFollowPosition()
    {
        if (toFollow.Count > 0 && converter != null && CanSendRuntimeAsk("followed geometry"))
        {

            String names = "";
            String points = "";
            string sep = ConnectionManager.Instance.GetMessageSeparator();

            foreach (GameObject obj in toFollow)
            {
                names += obj.name + sep;
                List<int> p = converter.toGAMACRS3D(obj.transform.position);

                points += p[0] + sep;

                points += p[1] + sep;
                points += p[2] + sep;

            }
            Dictionary<string, string> args = new Dictionary<string, string> {
            {"ids", names  },
            {"points", points},
            {"sep", sep}
            };

            TrySendExecutableAsk("move_geoms_followed", args, "followed geometry");

        }
    }


    // ############################################ UPDATERS ############################################
    private void UpdatePlayerPosition()
    {
        if (Camera.main == null || converter == null || parameters == null || XROrigin == null)
        {
            return;
        }

        if (!CanSendRuntimeAsk("player position"))
        {
            TimerSendPosition = TimeSendPosition;
            return;
        }

        Vector2 vF = new Vector2(Camera.main.transform.forward.x, Camera.main.transform.forward.z);
        Vector2 vR = new Vector2(transform.forward.x, transform.forward.z);
        vF.Normalize();
        vR.Normalize();
        float c = vF.x * vR.x + vF.y * vR.y;
        float s = vF.x * vR.y - vF.y * vR.x;
        int angle = (int)(((s > 0) ? -1.0 : 1.0) * (180 / Math.PI) * Math.Acos(c) * parameters.precision);



      //  Vector3 v = new Vector3(Camera.main.transform.position.x, Camera.main.transform.position.y - yOffsetCamera, Camera.main.transform.position.z);
        Vector3 v = hasSimulator ? new Vector3(Camera.main.transform.localPosition.x + XROrigin.localPosition.x, Camera.main.transform.localPosition.y + XROrigin.localPosition.y,Camera.main.transform.localPosition.z + XROrigin.localPosition.z)
 : new Vector3(XROrigin.localPosition.x, XROrigin.localPosition.y, XROrigin.localPosition.z);

        List<int> p = converter.toGAMACRS3D(v);
        Dictionary<string, string> args = new Dictionary<string, string> {
             {"id", ConnectionManager.Instance != null ? ConnectionManager.Instance.GetConnectionId() : StaticInformation.getId() },
            {"x", "" +p[0]},
            {"y", "" +p[1]}, 
            {"z", "" +p[2]},
            {"angle", "" +angle}
        };

        TrySendExecutableAsk("move_player_external", args, "player position");

        TimerSendPosition = TimeSendPosition;
    }
   

    private void instantiateGO(GameObject obj, String name, PropertiesGAMA prop)
    {
        obj.name = name;
        if (prop.toFollow)
        {
            toFollow.Add(obj);
        }
        if (prop.tag != null && !string.IsNullOrEmpty(prop.tag))
            GamaSceneUtility.TrySetTag(obj, prop.tag);

        if (prop.isInteractable)
        {
            if (interactionManager == null)
                interactionManager = GameObject.FindFirstObjectByType<XRInteractionManager>();
          
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interaction = null;
            if (prop.isGrabable)
            {
              
                interaction = obj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                Rigidbody rb = obj.GetComponent<Rigidbody>();
                if (prop.constraints != null && prop.constraints.Count == 6)
                {
                    if (prop.constraints[0])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezePositionX;
                    if (prop.constraints[1])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezePositionY;
                    if (prop.constraints[2])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezePositionZ;
                    if (prop.constraints[3])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezeRotationX;
                    if (prop.constraints[4])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezeRotationY;
                    if (prop.constraints[5])
                        rb.constraints = rb.constraints | RigidbodyConstraints.FreezeRotationZ;
                }
                


            }
            else
            {

                interaction = obj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();


            }

            if (interaction.colliders.Count == 0)
            {
                Collider[] cs = obj.GetComponentsInChildren<Collider>();
                if (cs != null)
                {
                    foreach (Collider c in cs)
                    {
                        interaction.colliders.Add(c);
                    } 
                }
            }
            interaction.interactionManager = interactionManager;
            interaction.ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase.Dynamic);
            interaction.selectEntered.AddListener(SelectInteraction);
            interaction.firstHoverEntered.AddListener(HoverEnterInteraction);
            interaction.hoverExited.AddListener(HoverExitInteraction);

        }
    }



    private GameObject instantiatePrefab(
        string name,
        string runtimeKey,
        string speciesName,
        PropertiesGAMA prop,
        Attributes attributes,
        string prefabSignature,
        bool initGame)
    {
        GameObject sourcePrefab;
        string resolvedSignature;
        bool hasPrefab = TryResolvePrefabAsset(prop, attributes, out sourcePrefab, out resolvedSignature);

        if (!string.IsNullOrWhiteSpace(prefabSignature))
        {
            resolvedSignature = prefabSignature;
        }

        GameObject obj = null;
        bool pooledInstance = false;
        if (enablePrefabPooling)
        {
            pooledInstance = TryGetPooledPrefab(resolvedSignature, out obj);
        }
        if (pooledInstance)
        {
            obj.name = name;
            obj.SetActive(true);
        }
        else if (!hasPrefab || sourcePrefab == null)
        {
            WarnMissingPrefabOnce(prop, name);
            obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name + " (Placeholder)";
            GamaSceneUtility.TrySetTag(obj, prop.tag);

            float pScale = (float)prop.size / Mathf.Max(parameters != null ? parameters.precision : 1, 1);
            obj.transform.localScale = new Vector3(pScale, pScale, pScale);
            resolvedSignature = string.IsNullOrWhiteSpace(resolvedSignature)
                ? "placeholder:" + SimulationManager.NormalizeKey(prop.prefab)
                : resolvedSignature;
        }
        else
        {
            obj = Instantiate(sourcePrefab);
            obj.name = name;
            float scale = (float)prop.size / Mathf.Max(parameters != null ? parameters.precision : 1, 1);
            obj.transform.localScale = new Vector3(scale, scale, scale);
            obj.SetActive(true);
        }

        EnableGpuInstancing(obj);
        EnsureColliderSetup(obj, prop);
        SetPrefabSignature(obj, resolvedSignature, obj.transform.rotation);

        List<object> pL = new List<object> { obj, prop };
        if (geometryMap != null)
        {
            geometryMap[string.IsNullOrWhiteSpace(runtimeKey) ? name : runtimeKey] = pL;
        }

        if (!pooledInstance)
        {
            instantiateGO(obj, name, prop);
        }

        ParentRuntimeAgent(obj, string.IsNullOrWhiteSpace(speciesName) ? prop.id : speciesName);

        return obj;
    }

    private void ParentRuntimeAgent(GameObject obj, string speciesKey)
    {
        if (!groupRuntimeAgentsBySpecies || obj == null) return;
        
        if (runtimeAgentsRoot == null)
        {
            GameObject rootObj = GameObject.Find("[GAMA] Runtime Live Agents");
            if (rootObj == null)
            {
                rootObj = new GameObject("[GAMA] Runtime Live Agents");
                Debug.Log("[GAMA][RUNTIME] Created runtime hierarchy root: [GAMA] Runtime Live Agents");
            }
            runtimeAgentsRoot = rootObj.transform;
            runtimeAgentsRoot.position = Vector3.zero;
            runtimeAgentsRoot.rotation = Quaternion.identity;
            runtimeAgentsRoot.localScale = Vector3.one;
        }

        string safeSpecies = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();

        Transform speciesParent;
        if (runtimeSpeciesParents == null) 
        {
            runtimeSpeciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        }
        
        if (!runtimeSpeciesParents.TryGetValue(safeSpecies, out speciesParent) || speciesParent == null)
        {
            Transform existingParent = runtimeAgentsRoot.Find(safeSpecies);
            if (existingParent != null)
            {
                speciesParent = existingParent;
            }
            else
            {
                GameObject parentObj = new GameObject(safeSpecies);
                parentObj.transform.SetParent(runtimeAgentsRoot, false);
                speciesParent = parentObj.transform;
            }
            
            speciesParent.position = Vector3.zero;
            speciesParent.rotation = Quaternion.identity;
            speciesParent.localScale = Vector3.one;
            runtimeSpeciesParents[safeSpecies] = speciesParent;
        }

        obj.transform.SetParent(speciesParent, true);
        HideStaticPreviewAfterRuntimeData();
    }

    private static string ResolveRuntimeSpeciesName(PropertiesGAMA prop, string propertyId)
    {
        if (prop != null)
        {
            if (!string.IsNullOrWhiteSpace(prop.tag))
            {
                return prop.tag.Trim();
            }

            if (!string.IsNullOrWhiteSpace(prop.id))
            {
                return prop.id.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(propertyId) ? "unknown" : propertyId.Trim();
    }

    private static string MakeRuntimeAgentKey(string speciesName, string agentId)
    {
        string species = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        string id = string.IsNullOrWhiteSpace(agentId) ? "unknown" : agentId.Trim();
        return species + "::" + id;
    }

    private void RemoveKeptRuntimeAgentNames(HashSet<string> removalSet, List<string> keepNames)
    {
        if (removalSet == null || keepNames == null || keepNames.Count == 0)
        {
            return;
        }

        for (int i = 0; i < keepNames.Count; i++)
        {
            string keepName = keepNames[i];
            if (string.IsNullOrWhiteSpace(keepName))
            {
                continue;
            }

            removalSet.Remove(keepName);
            foreach (RuntimeAgentRecord record in runtimeAgentRecords.Values)
            {
                if (record == null)
                {
                    continue;
                }

                if (string.Equals(record.AgentId, keepName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.Key, keepName, StringComparison.OrdinalIgnoreCase))
                {
                    removalSet.Remove(record.Key);
                }
            }
        }
    }

    private void RegisterRuntimeAgent(
        string key,
        string speciesName,
        string agentId,
        GameObject root,
        bool dynamicUpdate,
        GamaAgentVisualState visualState,
        Vector3? basePosition = null,
        Quaternion? baseRotation = null,
        Vector3? visualAnchor = null)
    {
        if (string.IsNullOrWhiteSpace(key) || root == null)
        {
            return;
        }

        RuntimeAgentRecord record;
        bool created = !runtimeAgentRecords.TryGetValue(key, out record) || record == null;
        if (created)
        {
            record = new RuntimeAgentRecord();
            runtimeAgentRecords[key] = record;
        }

        record.Key = key;
        record.SpeciesName = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        record.AgentId = string.IsNullOrWhiteSpace(agentId) ? key : agentId.Trim();
        record.Root = root;
        record.VisualRoot = ResolveRuntimeVisualRoot(root);
        record.IsDynamic = record.IsDynamic || dynamicUpdate;
        if (basePosition.HasValue)
        {
            record.BasePosition = basePosition.Value;
            record.HasBaseTransform = true;
        }
        if (baseRotation.HasValue)
        {
            record.BaseRotation = baseRotation.Value;
            record.HasBaseTransform = true;
        }
        if (visualAnchor.HasValue)
        {
            record.VisualAnchor = visualAnchor.Value;
            record.HasVisualAnchor = true;
        }
        if (dynamicUpdate)
        {
            record.LastSeenTick = runtimeLiveTickSerial;
            RuntimeSyncCounters counters = GetRuntimeSyncCounters(record.SpeciesName);
            if (created)
            {
                counters.Created++;
            }
            else
            {
                counters.Updated++;
            }
        }

        if (created && runtimeCreateLogCount < 20)
        {
            Debug.Log("[GAMA][RUNTIME][CREATE] species=" + record.SpeciesName + " agent=" + record.AgentId);
            runtimeCreateLogCount++;
        }

        record.CurrentlyVisible = visualState.Visible && root.activeSelf;
        record.UsesPrefabOverride = visualState.PrefabOverride != null ||
                                    !string.IsNullOrWhiteSpace(visualState.PrefabResourcePath);
        record.LastPositionOffset = visualState.PositionOffset;
        record.LastRotationOffsetEuler = visualState.RotationOffsetEuler;
        missingAgentTickCounts.Remove(key);
    }

    private static GameObject ResolveRuntimeVisualRoot(GameObject root)
    {
        if (root == null)
        {
            return null;
        }

        Transform visualOverride = root.transform.Find("VisualOverride");
        if (visualOverride != null)
        {
            return visualOverride.gameObject;
        }

        Transform fallback = root.transform.Find("InvalidGeometryFallback");
        return fallback != null ? fallback.gameObject : root;
    }

    private void UnregisterRuntimeAgent(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        runtimeAgentRecords.Remove(key);
        missingAgentTickCounts.Remove(key);
    }

    private RuntimeSyncCounters GetRuntimeSyncCounters(string speciesName)
    {
        string species = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        RuntimeSyncCounters counters;
        if (!runtimeSyncCountersBySpecies.TryGetValue(species, out counters) || counters == null)
        {
            counters = new RuntimeSyncCounters();
            runtimeSyncCountersBySpecies[species] = counters;
        }

        return counters;
    }

    private static void WarnMissingPrefabOnce(PropertiesGAMA prop, string sampleAgentName)
    {
        string prefab = prop != null ? prop.prefab : string.Empty;
        string propertyId = prop != null ? prop.id : string.Empty;
        string key = propertyId + "|" + prefab;
        if (!missingPrefabWarnings.Add(key))
        {
            return;
        }

        Debug.LogWarning(
            "[GAMA] Prefab '" + prefab + "' not found for property '" + propertyId +
            "'. Agent sample='" + sampleAgentName + "'. Using placeholder cubes.");
    }

    private void EnsureColliderSetup(GameObject obj, PropertiesGAMA prop)
    {
        if (!prop.hasCollider || obj == null)
        {
            return;
        }

        if (obj.TryGetComponent<LODGroup>(out var lod))
        {
            foreach (LOD l in lod.GetLODs())
            {
                if (l.renderers == null || l.renderers.Length == 0 || l.renderers[0] == null)
                {
                    continue;
                }

                GameObject child = l.renderers[0].gameObject;
                Collider childCollider = child.GetComponent<Collider>();
                if (childCollider != null && childCollider.bounds.extents.x == 0)
                {
                    childCollider = null;
                }

                if (childCollider == null)
                {
                    child.AddComponent<BoxCollider>();
                }
            }

            return;
        }

        Collider collider = obj.GetComponent<Collider>();
        if (collider != null && collider.bounds.extents.x == 0)
        {
            collider = null;
        }

        if (collider == null)
        {
            obj.AddComponent<BoxCollider>();
        }
    }

    private bool TryResolvePrefabAsset(
        PropertiesGAMA prop,
        Attributes attributes,
        out GameObject prefab,
        out string signature)
    {
        prefab = null;
        signature = string.Empty;
        if (prop == null || !prop.hasPrefab)
        {
            return false;
        }

        if (TryResolvePrefab(prop, attributes, out prefab, out signature))
        {
            return prefab != null;
        }

        if (prop.prefabObj == null)
        {
            prop.loadPrefab(parameters != null ? parameters.precision : 1);
        }

        if (prop.prefabObj != null)
        {
            prefab = prop.prefabObj;
            signature = "legacy:" + SimulationManager.NormalizeKey(prop.prefab);
            return true;
        }

        signature = "placeholder:" + SimulationManager.NormalizeKey(prop.prefab);
        return false;
    }

    private string ResolvePrefabSignature(PropertiesGAMA prop, Attributes attributes)
    {
        GameObject resolvedPrefab;
        string signature;
        TryResolvePrefabAsset(prop, attributes, out resolvedPrefab, out signature);
        return signature;
    }

    private static bool NeedsPrefabRebuild(GameObject instance, string desiredSignature)
    {
        string currentSignature = GetPrefabSignature(instance);
        return !string.Equals(currentSignature, desiredSignature, StringComparison.OrdinalIgnoreCase);
    }

    private static void SetPrefabSignature(GameObject instance, string signature, Quaternion baseRotation)
    {
        if (instance == null)
        {
            return;
        }

        GamaRuntimePrefabSignature marker = instance.GetComponent<GamaRuntimePrefabSignature>();
        if (marker == null)
        {
            marker = instance.AddComponent<GamaRuntimePrefabSignature>();
        }

        marker.signature = signature ?? string.Empty;
        marker.baseRotation = baseRotation;
    }

    private static string GetPrefabSignature(GameObject instance)
    {
        if (instance == null)
        {
            return string.Empty;
        }

        GamaRuntimePrefabSignature marker = instance.GetComponent<GamaRuntimePrefabSignature>();
        return marker != null ? marker.signature : string.Empty;
    }

    private void EnsurePrefabPoolRoot()
    {
        if (prefabPoolRoot != null)
        {
            return;
        }

        GameObject root = new GameObject("[GAMA] Prefab Pools");
        root.hideFlags = HideFlags.HideAndDontSave;
        prefabPoolRoot = root.transform;
        DontDestroyOnLoad(root);
    }

    private bool TryGetPooledPrefab(string signature, out GameObject pooled)
    {
        pooled = null;
        if (!enablePrefabPooling || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        string sig = signature.Trim();
        if (sig.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Stack<GameObject> stack;
        if (!prefabPools.TryGetValue(sig, out stack) || stack == null || stack.Count == 0)
        {
            return false;
        }

        pooled = stack.Pop();
        if (pooled == null)
        {
            return false;
        }

        pooled.transform.SetParent(null, worldPositionStays: false);
        return true;
    }

    /// <summary>Return a prefab instance to the pool or destroy it.</summary>
    private void ReleasePrefabInstance(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        int id = instance.GetInstanceID();
        prefabDistanceCulled.Remove(id);

        if (!enablePrefabPooling)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        string signature = GetPrefabSignature(instance);
        if (string.IsNullOrWhiteSpace(signature) || signature.StartsWith("placeholder:", StringComparison.OrdinalIgnoreCase))
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        EnsurePrefabPoolRoot();
        Stack<GameObject> stack;
        if (!prefabPools.TryGetValue(signature, out stack) || stack == null)
        {
            stack = new Stack<GameObject>();
            prefabPools[signature] = stack;
        }

        if (stack.Count >= maxPooledPrefabsPerSignature || maxPooledPrefabsPerSignature <= 0)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        instance.transform.SetParent(prefabPoolRoot, worldPositionStays: false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.SetActive(false);
        stack.Push(instance);
    }

    private void DrainPrefabPools()
    {
        foreach (KeyValuePair<string, Stack<GameObject>> kv in prefabPools)
        {
            Stack<GameObject> stack = kv.Value;
            if (stack == null)
            {
                continue;
            }

            while (stack.Count > 0)
            {
                GameObject pooled = stack.Pop();
                if (pooled != null)
                {
                    UnityEngine.Object.Destroy(pooled);
                }
            }
        }

        prefabPools.Clear();
        gpuInstancingTouchedMaterials.Clear();
        prefabDistanceCulled.Clear();
        prefabStreamingKeys.Clear();
        if (prefabPoolRoot != null)
        {
            UnityEngine.Object.Destroy(prefabPoolRoot.gameObject);
            prefabPoolRoot = null;
        }

        if (runtimeAgentsRoot != null)
        {
            UnityEngine.Object.Destroy(runtimeAgentsRoot.gameObject);
            runtimeAgentsRoot = null;
        }
        
        if (runtimeSpeciesParents != null)
        {
            runtimeSpeciesParents.Clear();
        }
    }

    /// <summary>Prefer enabling GPU Instancing on shared materials ahead of time; this promotes the flag once per material asset when safe.</summary>
    private void EnableGpuInstancing(GameObject root)
    {
        if (!enableGpuInstancingForPrefabMaterials || root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            if (renderer == null)
            {
                continue;
            }

            Material[] shared = renderer.sharedMaterials;
            if (shared == null)
            {
                continue;
            }

            for (int m = 0; m < shared.Length; m++)
            {
                Material mat = shared[m];
                if (mat == null)
                {
                    continue;
                }

                int mid = mat.GetInstanceID();
                if (!gpuInstancingTouchedMaterials.Add(mid))
                {
                    continue;
                }

                mat.enableInstancing = true;
            }
        }
    }

    private bool TryGetRuntimeAgentObjectByAgentId(string agentId, out GameObject obj)
    {
        obj = null;
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return false;
        }

        List<object> legacyEntry;
        if (geometryMap != null &&
            geometryMap.TryGetValue(agentId, out legacyEntry) &&
            TryReadRuntimeObject(legacyEntry, out obj))
        {
            return true;
        }

        foreach (RuntimeAgentRecord record in runtimeAgentRecords.Values)
        {
            if (record == null ||
                !string.Equals(record.AgentId, agentId, StringComparison.OrdinalIgnoreCase) ||
                record.Root == null)
            {
                continue;
            }

            obj = record.Root;
            return true;
        }

        return false;
    }

    private static bool TryReadRuntimeObject(List<object> entry, out GameObject obj)
    {
        obj = null;
        if (entry == null || entry.Count == 0)
        {
            return false;
        }

        obj = entry[0] as GameObject;
        return obj != null;
    }

    /// <returns>Whether the agent satisfies frustum (+ optional hysteresis distance) constraints.</returns>
    private bool PrefabPassesStreamingHeuristics(GameObject obj, Camera cam, bool applyDistance)
    {
        Bounds bounds = GetPrefabStreamingBounds(obj);
        bounds.Expand(prefabViewPadding);

        bool inFrustum = true;
        if (streamPrefabsByCameraView)
        {
            inFrustum = GeometryUtility.TestPlanesAABB(prefabStreamingPlanes, bounds);
            if (!inFrustum)
            {
                prefabDistanceCulled.Remove(obj.GetInstanceID());
                return false;
            }
        }

        if (!applyDistance || !enablePrefabRenderDistance || cam == null || globalPrefabRenderDistance <= Mathf.Epsilon)
        {
            return true;
        }

        float hysteresis = Mathf.Max(0f, prefabRenderDistanceHysteresis);
        Vector3 camPos = cam.transform.position;
        Vector3 closest = bounds.ClosestPoint(camPos);
        float distance = Vector3.Distance(closest, camPos);

        int id = obj.GetInstanceID();
        bool wasCulledDistance;
        bool hasState = prefabDistanceCulled.TryGetValue(id, out wasCulledDistance);

        bool nowCulledDistance;
        if (!hasState || !wasCulledDistance)
        {
            nowCulledDistance = distance > globalPrefabRenderDistance;
        }
        else
        {
            float resumeDistance = Mathf.Max(0f, globalPrefabRenderDistance - hysteresis);
            nowCulledDistance = !(distance < resumeDistance);
        }

        prefabDistanceCulled[id] = nowCulledDistance;
        return !nowCulledDistance;
    }

    private GamaAgentVisualState ResolveAgentVisualState(string agentName, PropertiesGAMA prop, Attributes attributes)
    {
        int precision = parameters != null ? parameters.precision : 1;
        return ResolveVisualState(agentName, prop, attributes, precision);
    }

    private void SnapshotPrefabHeadingSources()
    {
        prefabHeadingSourcePositions.Clear();
        prefabHeadingSourcePropertyIds.Clear();
        consumedPrefabHeadingSources.Clear();

        foreach (KeyValuePair<string, Vector3> entry in previousPrefabPositions)
        {
            prefabHeadingSourcePositions[entry.Key] = entry.Value;

            string propertyId;
            prefabHeadingSourcePropertyIds[entry.Key] =
                previousPrefabPropertyIds.TryGetValue(entry.Key, out propertyId) ? propertyId ?? string.Empty : string.Empty;
        }
    }

    private Quaternion ResolvePrefabRotation(
        string agentName,
        PropertiesGAMA prop,
        GamaAgentVisualState visualState,
        List<int> pointData,
        Vector3 currentPosition,
        GameObject prefabInstance)
    {
        Quaternion headingRotation = ResolvePrefabHeadingRotation(agentName, prop, pointData, currentPosition);
        return ComposePrefabRuntimeRotation(headingRotation, visualState, prefabInstance);
    }

    private Quaternion ResolvePrefabHeadingRotation(
        string agentName,
        PropertiesGAMA prop,
        List<int> pointData,
        Vector3 currentPosition)
    {
        int rawHeading = pointData != null && pointData.Count > 3 ? pointData[3] : 0;
        float heading = DecodeGamaAngle(rawHeading);

        if (rawHeading == 0 && TryResolveHeadingFromPreviousMovement(agentName, prop, currentPosition, out float movementHeading))
        {
            heading = movementHeading;
        }

        float rotation = prop.rotationCoeffF * heading + prop.rotationOffsetF;
        return Quaternion.AngleAxis(rotation, Vector3.up);
    }

    private static Quaternion ComposePrefabRuntimeRotation(
        Quaternion headingRotation,
        GamaAgentVisualState visualState,
        GameObject prefabInstance)
    {
        return headingRotation *
               Quaternion.Euler(visualState.RotationOffsetEuler) *
               GetPrefabBaseRotation(prefabInstance);
    }

    private bool TryResolveHeadingFromPreviousMovement(
        string agentName,
        PropertiesGAMA prop,
        Vector3 currentPosition,
        out float heading)
    {
        heading = 0f;
        string propertyId = prop != null ? prop.id ?? string.Empty : string.Empty;

        Vector3 previousPosition;
        if (TryGetPreviousHeadingSource(agentName, propertyId, out previousPosition))
        {
            return TryComputeHeadingFromDelta(previousPosition, currentPosition, out heading);
        }

        string bestKey = null;
        float bestSqrDistance = float.MaxValue;

        foreach (KeyValuePair<string, Vector3> entry in prefabHeadingSourcePositions)
        {
            if (consumedPrefabHeadingSources.Contains(entry.Key))
            {
                continue;
            }

            string candidatePropertyId;
            if (!prefabHeadingSourcePropertyIds.TryGetValue(entry.Key, out candidatePropertyId) ||
                !string.Equals(candidatePropertyId, propertyId, StringComparison.Ordinal))
            {
                continue;
            }

            Vector3 delta = currentPosition - entry.Value;
            delta.y = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestKey = entry.Key;
                previousPosition = entry.Value;
            }
        }

        if (bestKey == null)
        {
            return false;
        }

        consumedPrefabHeadingSources.Add(bestKey);
        return TryComputeHeadingFromDelta(prefabHeadingSourcePositions[bestKey], currentPosition, out heading);
    }

    private bool TryGetPreviousHeadingSource(string key, string propertyId, out Vector3 previousPosition)
    {
        previousPosition = Vector3.zero;
        if (string.IsNullOrEmpty(key) || consumedPrefabHeadingSources.Contains(key))
        {
            return false;
        }

        string sourcePropertyId;
        if (!prefabHeadingSourcePropertyIds.TryGetValue(key, out sourcePropertyId) ||
            !string.Equals(sourcePropertyId, propertyId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!prefabHeadingSourcePositions.TryGetValue(key, out previousPosition))
        {
            return false;
        }

        consumedPrefabHeadingSources.Add(key);
        return true;
    }

    private bool TryComputeHeadingFromDelta(Vector3 previousPosition, Vector3 currentPosition, out float heading)
    {
        Vector3 delta = currentPosition - previousPosition;
        delta.y = 0f;
        if (delta.sqrMagnitude <= 0.0001f)
        {
            heading = 0f;
            return false;
        }

        float gamaDeltaX = delta.x;
        float gamaDeltaY = delta.z;
        if (converter != null)
        {
            if (Mathf.Abs(converter.GamaCRSCoefX) > 0.000001f)
            {
                gamaDeltaX = delta.x / converter.GamaCRSCoefX;
            }

            if (Mathf.Abs(converter.GamaCRSCoefY) > 0.000001f)
            {
                gamaDeltaY = delta.z / converter.GamaCRSCoefY;
            }
        }

        heading = Mathf.Atan2(gamaDeltaY, gamaDeltaX) * Mathf.Rad2Deg;
        return true;
    }

    private float DecodeGamaAngle(int rawAngle)
    {
        int precision = parameters != null ? Mathf.Max(1, parameters.precision) : 1;
        return rawAngle / (float)precision;
    }

    private static Quaternion GetPrefabBaseRotation(GameObject prefabInstance)
    {
        if (prefabInstance == null)
        {
            return Quaternion.identity;
        }

        GamaRuntimePrefabSignature marker = prefabInstance.GetComponent<GamaRuntimePrefabSignature>();
        return marker != null ? marker.baseRotation : Quaternion.identity;
    }

    private void ApplyAgentVisualState(
        GameObject obj,
        PropertiesGAMA prop,
        GamaAgentVisualState visualState,
        bool prefabAgent,
        Vector3 basePosition,
        Vector3? computedWorldAnchor = null)
    {
        if (obj == null)
        {
            return;
        }

        int precision = parameters != null ? parameters.precision : 1;
        float baseScale = prefabAgent && prop != null ? prop.GetUnityScale(precision) : 1f;
        float scale = Mathf.Max(0f, baseScale * visualState.ScaleMultiplier);
        bool hasVisualOverridePrefab = !prefabAgent &&
                                       (visualState.PrefabOverride != null ||
                                        !string.IsNullOrEmpty(visualState.PrefabResourcePath));
        bool keepLogicalRootScaleStable = hasVisualOverridePrefab && IsVegetationCell(prop, obj);
        obj.transform.localScale = keepLogicalRootScaleStable
            ? Vector3.one
            : new Vector3(scale, scale, scale);

        if (!prefabAgent)
        {
            obj.transform.position = basePosition + visualState.PositionOffset;
            obj.transform.rotation = Quaternion.Euler(visualState.RotationOffsetEuler);
        }

        Transform visualOverride = null;
        if (prefabAgent)
        {
            Transform staleVisualOverride = obj.transform.Find("VisualOverride");
            if (staleVisualOverride != null)
            {
                UnityEngine.Object.Destroy(staleVisualOverride.gameObject);
            }
        }

        if (hasVisualOverridePrefab)
        {
            visualOverride = obj.transform.Find("VisualOverride");
            string visualSignature = visualState.PrefabOverride != null
                ? "object:" + visualState.PrefabOverride.GetInstanceID()
                : "resources:" + visualState.PrefabResourcePath;
            bool needsNewInstantiate = false;
            if (visualOverride != null)
            {
                GamaRuntimePrefabSignature sig = visualOverride.GetComponent<GamaRuntimePrefabSignature>();
                if (sig == null || sig.signature != visualSignature)
                {
                    UnityEngine.Object.Destroy(visualOverride.gameObject);
                    visualOverride = null;
                    needsNewInstantiate = true;
                }
            }
            else
            {
                needsNewInstantiate = true;
            }

            if (needsNewInstantiate)
            {
                GameObject loadedPrefab = visualState.PrefabOverride != null
                    ? visualState.PrefabOverride
                    : Resources.Load<GameObject>(visualState.PrefabResourcePath);
                if (loadedPrefab != null)
                {
                    GameObject visual = Instantiate(loadedPrefab, obj.transform);
                    visual.name = "VisualOverride";
                    visual.transform.localRotation = Quaternion.identity;

                    GamaRuntimePrefabSignature sig = visual.AddComponent<GamaRuntimePrefabSignature>();
                    sig.signature = visualSignature;
                    visualOverride = visual.transform;
                }
                else
                {
                    string species = prop != null ? prop.id : "unknown";
                    string warningKey = "missing-runtime-prefab:" + species + ":" + visualState.PrefabResourcePath;
                    if (!debugLogCounts.ContainsKey(warningKey))
                    {
                        debugLogCounts[warningKey] = 0;
                    }

                    if (debugLogCounts[warningKey] < 1)
                    {
                        debugLogCounts[warningKey]++;
                        Debug.LogWarning("[GAMA][RUNTIME][PREFAB] species=" + species +
                                         " cannot load prefabResourcePath=" + visualState.PrefabResourcePath);
                    }
                }
            }

            if (visualOverride != null)
            {
                if (computedWorldAnchor.HasValue)
                {
                    visualOverride.position = computedWorldAnchor.Value + visualState.PositionOffset;
                }
                else
                {
                    visualOverride.position = ResolveCurrentVisualWorldAnchor(obj);
                }
                visualOverride.rotation = Quaternion.Euler(visualState.RotationOffsetEuler);
                visualOverride.localScale = ResolveVisualOverrideLocalScale(scale, visualState, keepLogicalRootScaleStable);

                string speciesKey = prop != null ? prop.id : "unknown";
                if (!debugLogCounts.ContainsKey(speciesKey)) debugLogCounts[speciesKey] = 0;

                if (debugLogCounts[speciesKey] < 5)
                {
                    debugLogCounts[speciesKey]++;
                    Debug.Log($"[GAMA][RUNTIME][PREFAB] species={speciesKey} id={obj.name} agentRootPos={obj.transform.position:F3} visualPos={visualOverride.position:F3} scale={visualOverride.localScale:F3} prefab={visualSignature}");
                }

                if (!debugSummaryLogged.ContainsKey(speciesKey))
                {
                    debugSummaryLogged[speciesKey] = true;
                    Debug.Log($"[GAMA][RUNTIME][PREFAB] species={speciesKey} prefab={visualSignature} scale={visualState.ScaleMultiplier}");
                }

                if (keepLogicalRootScaleStable)
                {
                    string scaleLogKey = "visual-scale:" + speciesKey;
                    if (!debugLogCounts.ContainsKey(scaleLogKey)) debugLogCounts[scaleLogKey] = 0;
                    if (debugLogCounts[scaleLogKey] < 5)
                    {
                        debugLogCounts[scaleLogKey]++;
                        Debug.Log($"[GAMA][RUNTIME][SCALE] species={speciesKey} id={obj.name} parentScale={obj.transform.localScale:F3} visualScale={visualOverride.localScale:F3}");
                    }
                }
            }
        }

        if (visualState.HasColor)
        {
            bool isRealPrefab = prefabAgent && !GetPrefabSignature(obj).StartsWith("placeholder:");
            if (visualOverride != null) isRealPrefab = true;

            if (!isRealPrefab || visualState.HasManualColorOverride || visualState.HasAttributeColor)
            {
                if (visualOverride != null)
                {
                    ChangeColor(visualOverride.gameObject, visualState.Color);
                }
                else
                {
                    ChangeColor(obj, visualState.Color);
                }
            }
        }

        SetRenderersEnabled(obj, visualState.Visible, visualOverride);
    }

    private static Vector3 ResolveVisualOverrideLocalScale(
        float rootScale,
        GamaAgentVisualState visualState,
        bool keepLogicalRootScaleStable)
    {
        if (keepLogicalRootScaleStable)
        {
            return Vector3.one * Mathf.Max(0f, rootScale);
        }

        float parentScale = Mathf.Max(0.0001f, rootScale);
        float targetWorldScale = Mathf.Max(0f, visualState.ScaleMultiplier);
        return Vector3.one * (targetWorldScale / parentScale);
    }

    private static bool IsVegetationCell(PropertiesGAMA prop, GameObject obj)
    {
        return ContainsVegetationCell(prop != null ? prop.id : null) ||
               ContainsVegetationCell(prop != null ? prop.tag : null) ||
               ContainsVegetationCell(prop != null ? prop.prefab : null) ||
               ContainsVegetationCell(obj != null ? obj.name : null);
    }

    private static bool ContainsVegetationCell(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.IndexOf("vegetation_cell", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Vector3 ResolveRuntimeBasePosition(RuntimeAgentRecord record, GameObject root)
    {
        if (record != null && record.HasBaseTransform)
        {
            return record.BasePosition;
        }

        if (root == null)
        {
            return Vector3.zero;
        }

        Vector3 lastOffset = record != null ? record.LastPositionOffset : Vector3.zero;
        return root.transform.position - lastOffset;
    }

    private static Quaternion ResolveRuntimeBaseRotation(RuntimeAgentRecord record)
    {
        if (record != null && record.HasBaseTransform)
        {
            return record.BaseRotation;
        }

        return Quaternion.identity;
    }

    public void ApplyRuntimeSpeciesOverrideNow(string speciesName)
    {
        if (string.IsNullOrWhiteSpace(speciesName))
        {
            return;
        }

        GamaRuntimePreviewOverrideApplier.RefreshNow();

        List<string> matchingKeys = new List<string>();
        foreach (KeyValuePair<string, RuntimeAgentRecord> pair in runtimeAgentRecords)
        {
            RuntimeAgentRecord record = pair.Value;
            if (record == null ||
                record.Root == null ||
                !string.Equals(record.SpeciesName, speciesName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchingKeys.Add(pair.Key);
        }

        int updated = 0;
        for (int i = 0; i < matchingKeys.Count; i++)
        {
            string key = matchingKeys[i];
            RuntimeAgentRecord record;
            if (!runtimeAgentRecords.TryGetValue(key, out record) || record == null || record.Root == null)
            {
                continue;
            }

            List<object> entry;
            if (geometryMap == null ||
                !geometryMap.TryGetValue(key, out entry) ||
                entry == null ||
                entry.Count < 2)
            {
                continue;
            }

            PropertiesGAMA prop = entry[1] as PropertiesGAMA;
            if (prop == null)
            {
                continue;
            }

            GamaAgentVisualState visualState = ResolveAgentVisualState(record.AgentId, prop, null);
            GameObject root = record.Root;
            Vector3 basePosition = ResolveRuntimeBasePosition(record, root);
            Quaternion baseRotation = ResolveRuntimeBaseRotation(record);

            if (prop.hasPrefab)
            {
                string desiredSignature = ResolvePrefabSignature(prop, null);
                if (NeedsPrefabRebuild(root, desiredSignature))
                {
                    if (toFollow != null && toFollow.Contains(root))
                    {
                        toFollow.Remove(root);
                    }

                    ReleasePrefabInstance(root);
                    root = instantiatePrefab(record.AgentId, key, record.SpeciesName, prop, null, desiredSignature, initGame: false);
                    entry[0] = root;
                    record.Root = root;
                    record.VisualRoot = ResolveRuntimeVisualRoot(root);
                }

                root.transform.SetPositionAndRotation(
                    basePosition + visualState.PositionOffset,
                    ComposePrefabRuntimeRotation(baseRotation, visualState, root));
                ApplyAgentVisualState(root, prop, visualState, true, Vector3.zero);
            }
            else
            {
                Vector3? visualAnchor = record.HasVisualAnchor ? record.VisualAnchor : (Vector3?)null;
                ApplyAgentVisualState(root, prop, visualState, false, basePosition, visualAnchor);
            }

            ApplyImmediateStreamingState(root, prop, GetPrefabStreamingCamera(), frustumReady: false);
            record.CurrentlyVisible = visualState.Visible && root.activeSelf;
            record.UsesPrefabOverride = visualState.PrefabOverride != null ||
                                        !string.IsNullOrWhiteSpace(visualState.PrefabResourcePath);
            record.BasePosition = basePosition;
            record.BaseRotation = baseRotation;
            record.HasBaseTransform = true;
            if (!prop.hasPrefab && !record.HasVisualAnchor)
            {
                Vector3 fallbackAnchor = ResolveCurrentVisualWorldAnchor(root);
                if (fallbackAnchor.sqrMagnitude > 0.000001f)
                {
                    record.VisualAnchor = fallbackAnchor - visualState.PositionOffset;
                    record.HasVisualAnchor = true;
                }
            }
            record.LastPositionOffset = visualState.PositionOffset;
            record.LastRotationOffsetEuler = visualState.RotationOffsetEuler;
            if (prop.hasPrefab)
            {
                previousPrefabPositions[key] = basePosition;
                previousPrefabPropertyIds[key] = prop.id ?? string.Empty;
            }
            updated++;
        }

        Debug.Log("[GAMA][RUNTIME][OVERRIDE] refreshed species=" + speciesName + " agents=" + updated);
    }

    private static Vector3 GetRuntimeAgentWorldAnchor(GameObject agentRoot)
    {
        if (agentRoot == null) return Vector3.zero;

        // 1. If position is meaningful (not exactly 0,0,0 or very close) and it's a prefab
        if (agentRoot.transform.position.sqrMagnitude > 0.0001f)
        {
            return agentRoot.transform.position;
        }

        // 2. Try Renderer bounds (if the mesh is already updated)
        MeshRenderer[] renderers = agentRoot.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            if (bounds.extents.sqrMagnitude > 0.0001f)
            {
                return bounds.center;
            }
        }

        // 3. Try MeshFilter bounds directly
        MeshFilter[] filters = agentRoot.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length > 0)
        {
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh != null)
                {
                    Bounds localBounds = filter.sharedMesh.bounds;
                    Vector3 worldCenter = filter.transform.TransformPoint(localBounds.center);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(worldCenter, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(worldCenter);
                    }
                }
            }
            if (hasBounds)
            {
                return bounds.center;
            }
        }

        return agentRoot.transform.position;
    }

    private static Vector3 ResolveCurrentVisualWorldAnchor(GameObject agentRoot)
    {
        if (agentRoot == null)
        {
            return Vector3.zero;
        }

        Transform visualOverride = agentRoot.transform.Find("VisualOverride");
        if (TryGetRendererBoundsCenter(visualOverride, out Vector3 visualCenter))
        {
            return visualCenter;
        }

        Transform invalidFallback = agentRoot.transform.Find("InvalidGeometryFallback");
        if (TryGetRendererBoundsCenter(invalidFallback, out Vector3 fallbackCenter))
        {
            return fallbackCenter;
        }

        return GetRuntimeAgentWorldAnchor(agentRoot);
    }

    private static bool TryGetRendererBoundsCenter(Transform root, out Vector3 center)
    {
        center = Vector3.zero;
        if (root == null)
        {
            return false;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        center = bounds.center;
        return true;
    }

    private void HandleInvalidDynamicGeometryFallback(
        GameObject obj,
        string speciesName,
        GamaAgentVisualState visualState,
        Vector3 computedWorldAnchor,
        bool dynamicUpdate,
        bool forceFallback)
    {
        if (obj == null)
        {
            return;
        }

        bool originalGeometryValid = HasValidOriginalGeometryMesh(obj);
        Transform existingFallback = obj.transform.Find("InvalidGeometryFallback");

        if (!dynamicUpdate ||
            visualState.PrefabOverride != null ||
            !string.IsNullOrWhiteSpace(visualState.PrefabResourcePath) ||
            (!forceFallback && originalGeometryValid))
        {
            if (existingFallback != null)
            {
                UnityEngine.Object.Destroy(existingFallback.gameObject);
            }

            return;
        }

        Transform fallback = existingFallback;
        if (fallback == null)
        {
            GameObject fallbackObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            fallbackObj.name = "InvalidGeometryFallback";
            Collider collider = fallbackObj.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            fallbackObj.transform.SetParent(obj.transform, true);
            fallback = fallbackObj.transform;
        }

        SetOriginalGeometryRenderersEnabled(obj.transform, fallback, false);
        bool hasComputedAnchor = computedWorldAnchor.sqrMagnitude > 0.000001f;
        fallback.position = hasComputedAnchor
            ? computedWorldAnchor + visualState.PositionOffset
            : GetRuntimeAgentWorldAnchor(obj);
        fallback.rotation = Quaternion.Euler(visualState.RotationOffsetEuler);
        float parentScale = Mathf.Max(0.0001f, visualState.ScaleMultiplier);
        fallback.localScale = Vector3.one * (Mathf.Max(0.2f, visualState.ScaleMultiplier) / parentScale);
        ChangeColor(fallback.gameObject, visualState.HasColor ? visualState.Color : new Color32(255, 80, 80, 255));

        Renderer[] renderers = fallback.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = visualState.Visible;
            }
        }

        LogInvalidGeometryFallback(speciesName);
    }

    private bool IsRuntimePolygonInputValid(int[] points)
    {
        if (points == null || points.Length < 6)
        {
            return false;
        }

        int pointCount = points.Length / 2;
        if (pointCount < 3)
        {
            return false;
        }

        List<Vector2> cleaned = new List<Vector2>(pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 point = converter != null
                ? converter.fromGAMACRS2D(points[i * 2], points[i * 2 + 1])
                : new Vector2(points[i * 2], points[i * 2 + 1]);

            if (float.IsNaN(point.x) || float.IsNaN(point.y) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y))
            {
                return false;
            }

            if (cleaned.Count == 0 || Vector2.Distance(cleaned[cleaned.Count - 1], point) > 0.000001f)
            {
                cleaned.Add(point);
            }
        }

        if (cleaned.Count > 1 && Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) <= 0.000001f)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        if (cleaned.Count < 3)
        {
            return false;
        }

        float area = 0f;
        for (int i = 0; i < cleaned.Count; i++)
        {
            Vector2 a = cleaned[i];
            Vector2 b = cleaned[(i + 1) % cleaned.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return Mathf.Abs(area) > 0.000001f;
    }

    private static void SetOriginalGeometryRenderersEnabled(Transform root, Transform fallbackRoot, bool enabled)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (fallbackRoot != null && (renderer.transform == fallbackRoot || renderer.transform.IsChildOf(fallbackRoot)))
            {
                continue;
            }

            renderer.enabled = enabled;
        }
    }

    private static bool HasValidOriginalGeometryMesh(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            if (filter == null || IsRuntimeAuxiliaryVisual(filter.transform))
            {
                continue;
            }

            Mesh mesh = filter.sharedMesh;
            if (mesh != null && mesh.vertexCount > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRuntimeAuxiliaryVisual(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == "VisualOverride" || current.name == "InvalidGeometryFallback")
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void LogInvalidGeometryFallback(string speciesName)
    {
        string species = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        int count = 0;
        invalidGeometryFallbackCounts.TryGetValue(species, out count);
        count++;
        invalidGeometryFallbackCounts[species] = count;

        if (count == 1 || count == 10 || count % 100 == 0)
        {
            Debug.LogWarning(
                "[GAMA][RUNTIME][GEOMETRY] species=" + species +
                " invalidPolygonFallback=" + count);
        }
    }

    private static void SetRenderersEnabled(GameObject obj, bool visible, Transform visualOverride)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (visualOverride != null)
            {
                if (renderers[i].transform == visualOverride || renderers[i].transform.IsChildOf(visualOverride))
                {
                    renderers[i].enabled = visible;
                }
                else
                {
                    renderers[i].enabled = false;
                }
            }
            else
            {
                renderers[i].enabled = visible;
            }
        }
    }

    private void UpdatePrefabViewportStreaming(float deltaTime)
    {
        if (geometryMap == null || geometryMap.Count == 0)
        {
            return;
        }

        prefabViewTimer -= deltaTime;
        if (prefabViewTimer > 0f)
        {
            return;
        }

        prefabViewTimer = Mathf.Max(0.02f, prefabViewUpdateInterval);

        Camera streamingCamera = GetPrefabStreamingCamera();
        bool needCameraForFrustum = streamPrefabsByCameraView;
        bool needCameraForDistance = enablePrefabRenderDistance && globalPrefabRenderDistance > Mathf.Epsilon;
        if ((needCameraForFrustum || needCameraForDistance) && streamingCamera == null)
        {
            // Keep current active states when camera is temporarily unavailable.
            // Reactivating everything here causes visible pop/flicker loops.
            return;
        }

        if (needCameraForFrustum && streamingCamera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(streamingCamera, prefabStreamingPlanes);
        }

        bool testFrustum = streamPrefabsByCameraView && streamingCamera != null;
        bool testDistance = enablePrefabRenderDistance && streamingCamera != null;
        if (!testFrustum && !testDistance)
        {
            SetAllPrefabStreamingActive(true);
            return;
        }

        prefabStreamingKeys.Clear();
        foreach (KeyValuePair<string, List<object>> entry in geometryMap)
        {
            List<object> value = entry.Value;
            if (value == null || value.Count < 2)
            {
                continue;
            }

            GameObject obj = value[0] as GameObject;
            PropertiesGAMA prop = value[1] as PropertiesGAMA;
            if (obj != null && prop != null)
            {
                prefabStreamingKeys.Add(entry.Key);
            }
        }

        int total = prefabStreamingKeys.Count;
        if (total == 0)
        {
            return;
        }

        int budget = Mathf.Clamp(prefabStreamingBudgetPerTick, 1, total);
        int processed = 0;
        for (int b = 0; b < budget; b++)
        {
            int idx = (prefabStreamingCursor + b) % total;
            string key = prefabStreamingKeys[idx];
            List<object> value;
            if (!geometryMap.TryGetValue(key, out value) || value == null || value.Count < 2)
            {
                continue;
            }

            GameObject obj = value[0] as GameObject;
            PropertiesGAMA prop = value[1] as PropertiesGAMA;
            if (obj == null || prop == null)
            {
                continue;
            }

            bool keepLoaded = keepSelectedPrefabsLoaded && IsSelectedPrefab(obj);
            bool applyDistance = true;
            bool wantActive = keepLoaded || PrefabPassesStreamingHeuristics(obj, streamingCamera, applyDistance);
            SetAgentStreamingActive(obj, prop, wantActive);
            processed++;
        }

        prefabStreamingCursor = (prefabStreamingCursor + budget) % total;
        EmitPrefabStreamingDiagnostic(processed, total);
    }

    private void EmitPrefabStreamingDiagnostic(int processedThisTick, int totalPrefabAgents)
    {
        if (!logPrefabStreamingStats)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now - prefabStreamingLastDiagTime < prefabStreamingStatsInterval)
        {
            return;
        }

        prefabStreamingLastDiagTime = now;
        Debug.Log(
            "[GAMA] Prefab streaming tick: evaluated=" + processedThisTick +
            " round_robin_total=" + totalPrefabAgents +
            " budget=" + prefabStreamingBudgetPerTick +
            " pooling=" + enablePrefabPooling +
            " render_dist=" + enablePrefabRenderDistance);
    }

    private void EmitAgentUpdateBudgetDiagnostic(int processedThisTick, int totalAgents, int nextAgentIndex)
    {
        if (!logAgentUpdateBudgetStats)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now - agentUpdateBudgetLastDiagTime < agentUpdateBudgetStatsInterval)
        {
            return;
        }

        agentUpdateBudgetLastDiagTime = now;
        Debug.Log(
            "[GAMA] Agent update budget tick: processed=" + processedThisTick +
            " total=" + totalAgents +
            " next_index=" + nextAgentIndex +
            " max_per_tick=" + maxAgentUpdatesPerTick);
    }

    private void EmitRuntimeSyncSummaryIfNeeded()
    {
        if (!logAgentUpdateBudgetStats || runtimeSyncCountersBySpecies.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, RuntimeSyncCounters> pair in runtimeSyncCountersBySpecies)
        {
            RuntimeSyncCounters counters = pair.Value;
            if (counters == null)
            {
                continue;
            }

            int active = CountActiveDynamicAgents(pair.Key);
            Debug.Log(
                "[GAMA][RUNTIME][SYNC] tick=" + runtimeLiveTickSerial +
                " species=" + pair.Key +
                " active=" + active +
                " created=" + counters.Created +
                " updated=" + counters.Updated +
                " removed=" + counters.Removed);
        }
    }

    private int CountActiveDynamicAgents(string speciesName)
    {
        int count = 0;
        foreach (RuntimeAgentRecord record in runtimeAgentRecords.Values)
        {
            if (record == null || !record.IsDynamic)
            {
                continue;
            }

            if (string.Equals(record.SpeciesName, speciesName, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private void SetAllPrefabStreamingActive(bool active)
    {
        foreach (KeyValuePair<string, List<object>> entry in geometryMap)
        {
            List<object> value = entry.Value;
            if (value == null || value.Count < 2)
            {
                continue;
            }

            GameObject obj = value[0] as GameObject;
            PropertiesGAMA prop = value[1] as PropertiesGAMA;
            if (obj != null && prop != null)
            {
                SetAgentStreamingActive(obj, prop, active);
            }
        }
    }

    private void SetAgentStreamingActive(GameObject obj, PropertiesGAMA prop, bool active)
    {
        if (obj == null || prop == null)
        {
            return;
        }
        
        // Apply the same streaming strategy as cars/prefabs to every agent type.
        SetPrefabStreamingActive(obj, active);
    }

    private void ApplyImmediateStreamingState(GameObject obj, PropertiesGAMA prop, Camera streamingCamera, bool frustumReady)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        bool needFrustum = streamPrefabsByCameraView;
        bool needDistance = enablePrefabRenderDistance && globalPrefabRenderDistance > Mathf.Epsilon;
        if ((needFrustum || needDistance) && streamingCamera == null)
        {
            // Keep current state when no valid game camera is available.
            return;
        }

        if (needFrustum && !frustumReady)
        {
            return;
        }

        bool applyDistance = true;
        bool wantActive = PrefabPassesStreamingHeuristics(obj, streamingCamera, applyDistance);
        SetAgentStreamingActive(obj, prop, wantActive);
    }

    private static void SetPrefabStreamingActive(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
        {
            obj.SetActive(active);
        }
    }

    private Camera GetPrefabStreamingCamera()
    {
        if (Camera.main != null)
        {
            loggedMissingMainCameraForStreaming = false;
            return Camera.main;
        }
        if (!loggedMissingMainCameraForStreaming)
        {
            loggedMissingMainCameraForStreaming = true;
            Debug.LogWarning("[GAMA] Streaming culling disabled because Camera.main is missing. Tag the runtime game camera as MainCamera.");
        }

        return null;
    }

    private static Bounds GetPrefabStreamingBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds bounds = new Bounds(obj.transform.position, Vector3.one);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

    private bool IsSelectedPrefab(GameObject obj)
    {
        if (SelectedObjects != null && SelectedObjects.Contains(obj))
        {
            return true;
        }

#if UNITY_EDITOR
        GameObject selected = Selection.activeGameObject;
        return selected != null && (selected == obj || selected.transform.IsChildOf(obj.transform));
#else
        return false;
#endif
    }



    private void UpdateAgentsList()
    {


        ManageOtherInformation();
        if (!pendingWorldUpdateRemovalPass)
        {
            toRemove.Clear();
            toRemove.UnionWith(geometryMap.Keys);
            pendingWorldUpdateRemovalPass = true;
        }

        // foreach (List<object> obj in geometryMap.Values) {
        //((GameObject) obj[0]).SetActive(false);
        //}
        // toRemove.addAll(toRemoveAfter.k);
        bool updateCompleted = GenerateGeometries(false, toRemove);
        if (!updateCompleted)
        {
            return;
        }


        // List<string> ids = new List<string>(geometryMap.Keys);
        foreach (string id in toRemove)
        {
            List<object> o;
            if (!geometryMap.TryGetValue(id, out o) || o == null || o.Count < 2)
            {
                geometryMap.Remove(id);
                previousPrefabPositions.Remove(id);
                previousPrefabPropertyIds.Remove(id);
                missingAgentTickCounts.Remove(id);
                UnregisterRuntimeAgent(id);
                continue;
            }

            GameObject obj = (GameObject)o[0];
            PropertiesGAMA prop = o[1] as PropertiesGAMA;
            if (obj == null)
            {
                geometryMap.Remove(id);
                previousPrefabPositions.Remove(id);
                previousPrefabPropertyIds.Remove(id);
                missingAgentTickCounts.Remove(id);
                UnregisterRuntimeAgent(id);
                continue;
            }

            RuntimeAgentRecord record;
            bool isDynamicAgent =
                runtimeAgentRecords.TryGetValue(id, out record) &&
                record != null &&
                record.IsDynamic;
            bool shouldCullFromMissingData =
                isDynamicAgent ||
                (record == null && prop != null && (prop.hasPrefab || removeMissingGeometryAgents));
            if (!shouldCullFromMissingData)
            {
                // Roads/buildings are handled by camera streaming only; partial data ticks must not hide them.
                missingAgentTickCounts.Remove(id);
                continue;
            }

            int missCount = 0;
            missingAgentTickCounts.TryGetValue(id, out missCount);
            missCount++;
            missingAgentTickCounts[id] = missCount;
            if (missCount < Mathf.Max(1, missingTicksBeforeCull))
            {
                continue;
            }

            obj.transform.position = new Vector3(0, -100, 0);
            geometryMap.Remove(id);
            previousPrefabPositions.Remove(id);
            previousPrefabPropertyIds.Remove(id);
            missingAgentTickCounts.Remove(id);
            if (record != null)
            {
                GetRuntimeSyncCounters(record.SpeciesName).Removed++;
            }
            UnregisterRuntimeAgent(id);
            if (toFollow.Contains(obj))
                toFollow.Remove(obj);
            ReleasePrefabInstance(obj);
        }

        foreach (string id in geometryMap.Keys)
        {
            if (!toRemove.Contains(id))
            {
                missingAgentTickCounts.Remove(id);
            }
        }

        toRemove.Clear();
        pendingWorldUpdateRemovalPass = false;
        EmitRuntimeSyncSummaryIfNeeded();
    }

    protected virtual void ManageAttributes(List<Attributes> attributes)
    {

    }

    protected virtual void ManageOtherInformation()
    {

    }

    // ############################################# HANDLERS ########################################
    private void HandleConnectionStateChanged(ConnectionState state)
    {

        // player has been added to the simulation by the middleware
        if (state == ConnectionState.AUTHENTICATED)
        {
            Debug.Log("[GAMA] Authenticated, loading simulation data");
            UpdateGameState(GameState.LOADING_DATA);
        }
    }

    private bool TrySubscribeConnectionManager()
    {
        if (subscribedConnectionManager != null)
        {
            if (subscribedConnectionManager == ConnectionManager.Instance)
            {
                return true;
            }

            UnsubscribeConnectionEvents();
        }

        ConnectionManager manager = ConnectionManager.Instance;
        if (manager == null)
        {
            return false;
        }

        subscribedConnectionManager = manager;
        subscribedConnectionManager.OnServerMessageReceived += HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted += HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged += HandleConnectionStateChanged;
        connectionID["id"] = subscribedConnectionManager.GetConnectionId();
        Debug.Log("[GAMA][RUNTIME][CONNECTION] subscribed to ConnectionManager");
        return true;
    }

    private void RetrySubscribeConnectionManagerIfNeeded()
    {
        if (subscribedConnectionManager != null)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextConnectionSubscribeRetryTime)
        {
            return;
        }

        nextConnectionSubscribeRetryTime = now + ConnectionSubscribeRetryIntervalSeconds;
        TrySubscribeConnectionManager();
    }

    private void UnsubscribeConnectionEvents()
    {
        if (subscribedConnectionManager == null)
        {
            return;
        }

        subscribedConnectionManager.OnServerMessageReceived -= HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted -= HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged -= HandleConnectionStateChanged;
        subscribedConnectionManager = null;
    }

    private bool CanSendRuntimeAsk(string sendLabel)
    {
        TrySubscribeConnectionManager();
        ConnectionManager manager = ConnectionManager.Instance;
        if (manager != null && manager.CanSendRuntimeMessages)
        {
            return true;
        }

        float now = Time.unscaledTime;
        if (now >= nextSocketClosedWarningTime)
        {
            string reason = manager == null ? "ConnectionManager missing" : "socket not open";
            Debug.LogWarning("[GAMA][RUNTIME][CONNECTION] " + reason + "; skipping " + sendLabel + " send");
            nextSocketClosedWarningTime = now + SocketClosedWarningIntervalSeconds;
        }

        return false;
    }

    private bool TrySendExecutableAsk(string action, Dictionary<string, string> arguments, string sendLabel)
    {
        if (!CanSendRuntimeAsk(sendLabel))
        {
            return false;
        }

        ConnectionManager.Instance.SendExecutableAsk(action, arguments);
        return true;
    }

    private void HideStaticPreviewAfterRuntimeData()
    {
        if (staticPreviewHiddenAfterRuntimeData)
        {
            return;
        }

        GameObject previewRoot = GameObject.Find("[GAMA] Static Experiment Preview");
        if (previewRoot == null || !previewRoot.activeSelf)
        {
            return;
        }

        previewRoot.SetActive(false);
        staticPreviewHiddenAfterRuntimeData = true;
        Debug.Log("[GAMA][RUNTIME] Static preview hidden after live runtime data arrived.");
    }

    private void LogRuntimeFlow(WorldJSONInfo world)
    {
        runtimeFlowLogCount++;
        if (runtimeFlowLogCount > 20 && runtimeFlowLogCount % 100 != 0)
        {
            return;
        }

        int names = world != null && world.names != null ? world.names.Count : 0;
        int propertyIds = world != null && world.propertyID != null ? world.propertyID.Count : 0;
        Debug.Log("[GAMA][RUNTIME][FLOW] received json_output names=" + names + " propertyIDs=" + propertyIds);
    }


    protected virtual void OtherUpdate()
    {

    }

    protected virtual void TriggerMainButton()
    {

    }

    protected virtual void HoverEnterInteraction(HoverEnterEventArgs ev)
    {
    }

    protected virtual void HoverExitInteraction(HoverExitEventArgs ev)
    {

    }

    protected virtual void SelectInteraction(SelectEnterEventArgs ev)
    {

    }


    private static readonly string[] colorPropertyNames =
    {
        "_BaseColor",
        "_Color",
        "_MainColor",
        "Color",
        "BaseColor"
    };

    private static int[] colorPropertyIds;
    private static MaterialPropertyBlock sharedColorPropertyBlock;

    private static int[] ColorPropertyIds
    {
        get
        {
            if (colorPropertyIds == null)
            {
                colorPropertyIds = new int[colorPropertyNames.Length];
                for (int i = 0; i < colorPropertyNames.Length; i++)
                {
                    colorPropertyIds[i] = Shader.PropertyToID(colorPropertyNames[i]);
                }
            }

            return colorPropertyIds;
        }
    }

    private static MaterialPropertyBlock SharedColorPropertyBlock
    {
        get
        {
            if (sharedColorPropertyBlock == null)
            {
                sharedColorPropertyBlock = new MaterialPropertyBlock();
            }

            return sharedColorPropertyBlock;
        }
    }

    static public void ChangeColor(GameObject obj, Color color)
    {
        Renderer[] renderers = obj.gameObject.GetComponentsInChildren<Renderer>(true);
        int[] colorIds = ColorPropertyIds;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            MaterialPropertyBlock colorPropertyBlock = SharedColorPropertyBlock;
            bool applied = false;
            for (int c = 0; c < colorIds.Length; c++)
            {
                int propId = colorIds[c];
                Material[] sharedMaterials = renderer.sharedMaterials;
                for (int m = 0; m < sharedMaterials.Length; m++)
                {
                    Material sharedMat = sharedMaterials[m];
                    if (sharedMat == null || !sharedMat.HasProperty(propId))
                    {
                        continue;
                    }

                    renderer.GetPropertyBlock(colorPropertyBlock);
                    colorPropertyBlock.SetColor(propId, color);
                    renderer.SetPropertyBlock(colorPropertyBlock);
                    colorPropertyBlock.Clear();
                    applied = true;
                    break; 
                }

                if (applied)
                {
                    break;
                }
            }

            if (!applied)
            {
                renderer.GetPropertyBlock(colorPropertyBlock);
                colorPropertyBlock.SetColor(colorIds[1], color);
                renderer.SetPropertyBlock(colorPropertyBlock);
                colorPropertyBlock.Clear();
            }
        }
    }
    protected virtual void AdditionalInitAfterGeomLoading()
    {
         
    }
    protected virtual void ManageOtherMessages(string content)
    {

    }

    private void HandleServerMessageReceived(String firstKey, String content)
    {

        if (content == null || content.Equals("{}")) return;
        if (firstKey == null)
        {
            if (content.Contains("pong"))
            {
                currentTimePing = 0;
                return;
            }
            else if (content.Contains("pointsLoc"))
                firstKey = "pointsLoc";
            else if (content.Contains("precision"))
                firstKey = "precision";
            else if (content.Contains("properties"))
                firstKey = "properties";
            else if (content.Contains("endOfGame"))
                firstKey = "endOfGame";
            else if (content.Contains("rows"))
                firstKey = "rows";
            else if (content.Contains("wallId"))
                firstKey = "wallId";
            else if (content.Contains("teleportId"))
                firstKey = "teleportId";
            else if (content.Contains("indexX"))
                firstKey = "indexX";
            else if (content.Contains("enableMove"))
                firstKey = "enableMove";
            else if (content.Contains("triggers"))
                firstKey = "triggers";

            else
            {
                ManageOtherMessages(content);
                return;
            }

        }


        switch (firstKey)
        {
            // handle general informations about the simulation
            case "precision":
                parameters = ConnectionParameter.CreateFromJSON(content);
                converter = new CoordinateConverter(parameters.precision, GamaCRSCoefX, GamaCRSCoefY, GamaCRSCoefY, GamaCRSOffsetX, GamaCRSOffsetY, GamaCRSOffsetZ);
                if (propertiesGAMA != null)
                {
                    ImportAgentProperties(propertiesGAMA.properties, parameters.precision);
                    ImportPrefabProperties(propertiesGAMA.properties);
                }
                TimeSendPosition = (0.0f + parameters.minPlayerUpdateDuration) / (parameters.precision + 0.0f);
                GameObject loc = (locomotion != null && locomotion.Count > 0) ? locomotion[0] : null;
                if (loc != null)
                {
                    MoveHorizontal h = loc.GetComponent<MoveHorizontal>();
                    MoveVertical v = loc.GetComponent<MoveVertical>();
                   
                    if (h != null)
                    {
                   
                        if (parameters.speedx != -1) h.speed = Convert.ToSingle(parameters.speedx);
                        if (parameters.speedrotation != -1) h.speedRotation = Convert.ToSingle(parameters.speedrotation);
                        h.Strafe = parameters.strafe;
                    }
                    if (v != null)
                    {
                        if ( parameters.miny != -1) v.minY = Convert.ToSingle(parameters.miny);
                        if ( parameters.maxy != -1) v.maxY = Convert.ToSingle(parameters.maxy);
                        if (parameters.speedy != -1) v.Speed = Convert.ToSingle(parameters.speedy);

                    } 
                }

                GameObject moveObj = GamaSceneUtility.FindGameObjectWithTag("move");
                if (moveObj != null)
                {
                    // Use reflection to avoid hard dependency on samples
                    Component p = moveObj.GetComponent("DynamicMoveProvider");
                    if (p != null)
                    {
                        Type type = p.GetType();
                        FieldInfo moveSpeedField = type.GetField("moveSpeed");
                        if (moveSpeedField != null && parameters.speedx != -1)
                            moveSpeedField.SetValue(p, Convert.ToSingle(parameters.speedx));
                        
                        FieldInfo enableStrafeField = type.GetField("enableStrafe");
                        if (enableStrafeField != null)
                            enableStrafeField.SetValue(p, parameters.strafe);
                    }
                }
                handleGroundParametersRequested = true;
                handleGeometriesRequested = true;

                if (Camera.main != null)
                {
                    if (parameters.cameraclippingfar != -1) Camera.main.farClipPlane = Convert.ToSingle(parameters.cameraclippingfar);
                    if (parameters.cameraclippingnear != -1) Camera.main.nearClipPlane = Convert.ToSingle(parameters.cameraclippingnear);
                }


                break;

            case "properties":
                propertiesGAMA = AllProperties.CreateFromJSON(content);
                propertyMap = new Dictionary<string, PropertiesGAMA>();
                foreach (PropertiesGAMA p in propertiesGAMA.properties)
                {
                    p.PrepareRuntime(parameters != null ? parameters.precision : 1);
                    propertyMap.Add(p.id, p);
                }
                ImportAgentProperties(propertiesGAMA.properties, parameters != null ? parameters.precision : 1);
                ImportPrefabProperties(propertiesGAMA.properties);
                break;

            // handle agents while simulation is running
            case "pointsLoc":
                if (infoWorld == null)
                {
                    infoWorld = WorldJSONInfo.CreateFromJSON(content);
                    LogRuntimeFlow(infoWorld);
                }
                break;
            case "endOfGame":
                EndOfGameInfo infoEoG = EndOfGameInfo.CreateFromJSON(content);
                StaticInformation.endOfGame = infoEoG.endOfGame;
                SceneManager.LoadScene("End of Game Menu");
                break;
            case "rows":
                data = DEMData.CreateFromJSON(content);
                break;
            case "wallId":
                dataWall = WallInfo.CreateFromJSON(content);
                break;
            case "teleportId":
                dataTeleport = TeleoportAreaInfo.CreateFromJSON(content);
                break;
            case "indexX":
                dataLoc = DEMDataLoc.CreateFromJSON(content);
                break;
            case "enableMove":
                enableMove = EnableMoveInfo.CreateFromJSON(content);
                break;
            case "triggers":
                infoAnimation = AnimationInfo.CreateFromJSON(content);
                break;
            default:
                ManageOtherMessages(content);
                break;
        }

    }

    private void HandleConnectionAttempted(bool success)
    {

        if (success)
        {
            if (IsGameState(GameState.MENU))
            {
                Debug.Log("[GAMA] Connected to middleware");
                UpdateGameState(GameState.WAITING);
            }
        }
        else
        {
            // stay in MENU state

        }
    }

    private void TryReconnect()
    {
        if (ConnectionManager.Instance == null)
        {
            return;
        }
        TrySendExecutableAsk("ping_GAMA", connectionID, "ping");
        currentTimePing = maxTimePing;

    }

    // ############################################# UTILITY FUNCTIONS ########################################


    public void RestartGame()
    {
        OnGameRestarted?.Invoke();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public bool IsGameState(GameState state)
    {
        return currentState == state;
    }


    public GameState GetCurrentState()
    {
        return currentState;
    }


}

[DisallowMultipleComponent]
public class GamaRuntimePrefabSignature : MonoBehaviour
{
    public string signature;
    public Quaternion baseRotation = Quaternion.identity;
}


// ############################################################
public enum GameState
{
    // not connected to middleware
    MENU,
    // connected to middleware, waiting for authentication
    WAITING,
    // connected to middleware, authenticated, waiting for initial data from middleware
    LOADING_DATA,
    // connected to middleware, authenticated, initial data received, simulation running
    GAME,
    END,
    CRASH
}



public static class Extensions
{
    public static bool TryGetComponent<T>(this GameObject obj, T result) where T : Component
    {
        return (result = obj.GetComponent<T>()) != null;
    }
}
