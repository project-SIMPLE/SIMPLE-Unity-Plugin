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
        if (subscribedConnectionManager != null)
        {
            return;
        }

        if (ConnectionManager.Instance == null)
        {
            return;
        }

        subscribedConnectionManager = ConnectionManager.Instance;
        subscribedConnectionManager.OnServerMessageReceived += HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted += HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged += HandleConnectionStateChanged;

    }

    void OnDisable()
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

    void OnDestroy()
    {
        DrainPrefabPools();
    }

    void Start()
    {
        visualStateCache = new Dictionary<string, GamaAgentVisualState>(StringComparer.Ordinal);
        resolvedPrefabSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        runtimeSpeciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        geometryMap = new Dictionary<string, List<object>>();
        handleGeometriesRequested = false;
        // handlePlayerParametersRequested = false;
        handleGroundParametersRequested = false;
        OnEnable();
    }


    void FixedUpdate()
    {
        if (ConnectionManager.Instance == null)
        {
            return;
        }

        if (sendMessageToReactivatePositionSent)
        { 
            ConnectionManager.Instance.SendExecutableAsk("player_position_updated", connectionID);
            sendMessageToReactivatePositionSent = false;
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
                if (ConnectionManager.Instance != null)
                    ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
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
            if (!geometryMap.ContainsKey(n)) continue;            
            List<object> o = geometryMap[n];
            
            if (o == null && o.Count == 0) continue;
            GameObject obj = (GameObject)o[0];

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

        if(toRemove != null) foreach (string n in infoWorld.keepNames) toRemove.Remove(n);

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
     
        for (int i = startAgentIndex; i < infoWorld.names.Count; i++)
        {
            string name = infoWorld.names[i];
            string propId = infoWorld.propertyID[i];
           
            PropertiesGAMA prop = null;
            if (propertyMap == null || !propertyMap.TryGetValue(propId, out prop) || prop == null)
            {
                continue;
            }
            Attributes attributes = infoWorld.GetAttributesAt(i);
            GamaAgentVisualState visualState = ResolveAgentVisualState(name, prop, attributes);

            GameObject obj = null;

            if (prop.hasPrefab)
            {
                string desiredPrefabSignature = ResolvePrefabSignature(prop, attributes);
                if (initGame || !geometryMap.ContainsKey(name))
                {
                    obj = instantiatePrefab(name, prop, attributes, desiredPrefabSignature, initGame);
         
                }
                else
                {
                    List<object> o = geometryMap[name];
                    GameObject obj2 = (GameObject)o[0];
                    PropertiesGAMA p = (PropertiesGAMA)o[1];
                    if (p == prop && !NeedsPrefabRebuild(obj2, desiredPrefabSignature))
                    {
                        obj = obj2;
                    }
                    else
                    {

                        obj2.transform.position = new Vector3(0, -100, 0);
                        geometryMap.Remove(name);
                        previousPrefabPositions.Remove(name);
                        previousPrefabPropertyIds.Remove(name);
                        if (toFollow != null && toFollow.Contains(obj2))
                            toFollow.Remove(obj2);

                        ReleasePrefabInstance(obj2);
                        obj = instantiatePrefab(name, prop, attributes, desiredPrefabSignature, initGame);

                    }

                }
                List<int> pt = infoWorld.pointsLoc[cptPrefab].c;
                Vector3 pos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                pos.y += prop.yOffsetF;
                pos += visualState.PositionOffset;
                Quaternion rotation = ResolvePrefabRotation(name, prop, visualState, pt, pos, obj);
                obj.transform.SetPositionAndRotation(pos, rotation);
                previousPrefabPositions[name] = pos;
                previousPrefabPropertyIds[name] = prop.id ?? string.Empty;
                ApplyAgentVisualState(obj, prop, visualState, true, Vector3.zero);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                //obj.SetActive(true);
                if(toRemove != null) toRemove.Remove(name);
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

                if(initGame || !geometryMap.ContainsKey(name))
                {
                    obj = polyGen.GeneratePolygons(false, name, pt, prop, parameters.precision);
                   if(prop.hasCollider)
                    {
                        MeshCollider mc = obj.AddComponent<MeshCollider>();
                        mc.sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
                        if (prop.isGrabable) mc.convex = true;
                    }
                    instantiateGO(obj, name, prop);
                    ParentRuntimeAgent(obj, prop.id);
                    if (geometryMap != null)
                    {
                        geometryMap[name] = new List<object> { obj, prop };
                    }
                    
                }
                else
                {
                    List<object> o = geometryMap[name];
                    GameObject obj2 = (GameObject)o[0];
                    PropertiesGAMA p = (PropertiesGAMA)o[1];
                    if (p == prop)
                    {
                        obj = obj2;
                        polyGen.UpdatePolygon(obj, pt);
                        if(prop.hasCollider) obj.GetComponent<MeshCollider>().sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
                    }
                }
                
                ApplyAgentVisualState(obj, prop, visualState, false, polygonBasePosition);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                if(toRemove != null) toRemove.Remove(name);
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
                    if (ConnectionManager.Instance != null)
                        ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
                    
                    TimerSendInit = TimeSendInit;
                    loadedAlready = true;
                }
                break;

            case GameState.GAME:
                loadedAlready = false;
                if (ConnectionManager.Instance != null)
                    ConnectionManager.Instance.SendExecutableAsk("player_ready_to_receive_geometries", connectionID);
                
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
        if (toFollow.Count > 0 && ConnectionManager.Instance != null && converter != null)
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

            ConnectionManager.Instance.SendExecutableAsk("move_geoms_followed", args);

        }
    }


    // ############################################ UPDATERS ############################################
    private void UpdatePlayerPosition()
    {
        if (Camera.main == null || converter == null || parameters == null || XROrigin == null)
        {
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
        
        if (ConnectionManager.Instance != null)
            ConnectionManager.Instance.SendExecutableAsk("move_player_external", args);

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
            geometryMap[name] = pL;
        }

        if (!pooledInstance)
        {
            instantiateGO(obj, name, prop);
        }

        ParentRuntimeAgent(obj, prop.id);

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
        int rawHeading = pointData != null && pointData.Count > 3 ? pointData[3] : 0;
        float heading = DecodeGamaAngle(rawHeading);

        if (rawHeading == 0 && TryResolveHeadingFromPreviousMovement(agentName, prop, currentPosition, out float movementHeading))
        {
            heading = movementHeading;
        }

        float rotation = prop.rotationCoeffF * heading + prop.rotationOffsetF;
        return Quaternion.AngleAxis(rotation, Vector3.up) *
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
        Vector3 basePosition)
    {
        if (obj == null)
        {
            return;
        }

        int precision = parameters != null ? parameters.precision : 1;
        float baseScale = prefabAgent && prop != null ? prop.GetUnityScale(precision) : 1f;
        float scale = Mathf.Max(0f, baseScale * visualState.ScaleMultiplier);
        obj.transform.localScale = new Vector3(scale, scale, scale);

        if (!prefabAgent)
        {
            obj.transform.position = basePosition + visualState.PositionOffset;
            obj.transform.rotation = Quaternion.Euler(visualState.RotationOffsetEuler);
        }

        if (visualState.HasColor)
        {
            bool isRealPrefab = prefabAgent && !GetPrefabSignature(obj).StartsWith("placeholder:");

            // Only apply color if it's NOT a real prefab, OR if the user manually overrode the color
            // OR if the color was explicitly sent via the agent's dynamic attributes (e.g. Red vs Blue in GAML)
            if (!isRealPrefab || visualState.HasManualColorOverride || visualState.HasAttributeColor)
            {
                ChangeColor(obj, visualState.Color);
            }
        }

        SetRenderersEnabled(obj, visualState.Visible);
    }

    private static void SetRenderersEnabled(GameObject obj, bool visible)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = visible;
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
            bool applyDistance = prop.hasPrefab;
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
        bool needDistance = prop.hasPrefab && enablePrefabRenderDistance && globalPrefabRenderDistance > Mathf.Epsilon;
        if ((needFrustum || needDistance) && streamingCamera == null)
        {
            // Keep current state when no valid game camera is available.
            return;
        }

        if (needFrustum && !frustumReady)
        {
            return;
        }

        bool applyDistance = prop.hasPrefab;
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
                continue;
            }

            bool shouldCullFromMissingData = prop != null && (prop.hasPrefab || removeMissingGeometryAgents);
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

    private void SubscribeConnectionEvents()
    {
        if (subscribedConnectionManager != null)
        {
            return;
        }

        if (ConnectionManager.Instance == null)
        {

            return;
        }

        subscribedConnectionManager = ConnectionManager.Instance;
        subscribedConnectionManager.OnServerMessageReceived += HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted += HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged += HandleConnectionStateChanged;

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
        ConnectionManager.Instance.SendExecutableAsk("ping_GAMA", connectionID);
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
