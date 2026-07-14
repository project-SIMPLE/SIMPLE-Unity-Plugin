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
        public string PropertyId;
        public string PropertyTag;
        public string AgentId;
        public GameObject Root;
        public GameObject VisualRoot;
        public bool IsAdoptedPreview;
        public string PreviewReuseKey;
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
        public Attributes LastAttributes;
    }

    private sealed class RuntimeSyncCounters
    {
        public int Created;
        public int Updated;
        public int Removed;
    }

    private enum LargeSpeciesMode
    {
        CachedObjects = 0,
        BatchedMesh = 1
    }

    public enum PlayerPositionSource
    {
        MainCamera = 0,
        XROriginRoot = 1,
        FPSPlayerRoot = 2,
        ExplicitTransform = 3
    }

    private sealed class RuntimeImportCounters
    {
        public int Created;
        public int Updated;
        public int SkippedUnchanged;
        public int Deferred;
    }

    private sealed class RuntimeImportProfile
    {
        public bool IsInit;
        public int MessageBytes;
        public int NamesCount;
        public int PointsLocCount;
        public int PointsGeomCount;
        public bool IsLarge;
        public long ParseMs;
        public float ApplyStartedAt = -1f;
        public readonly Dictionary<string, int> CountsByPropertyId = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, RuntimeImportCounters> ImportCountersByPropertyId = new Dictionary<string, RuntimeImportCounters>(StringComparer.Ordinal);
    }

    private const int PeopleAttributeDebugMaxLogs = 10;

    private static System.Collections.Generic.Dictionary<string, int> debugLogCounts = new System.Collections.Generic.Dictionary<string, int>();
    private static System.Collections.Generic.Dictionary<string, bool> debugSummaryLogged = new System.Collections.Generic.Dictionary<string, bool>();
    [SerializeField] protected InputActionReference primaryRightHandButton = null;


    [Header("Base GameObjects")]
    [SerializeField] protected GameObject player;
    [SerializeField] protected GameObject Ground;
    
    [SerializeField, Tooltip("Organize runtime agents into [GAMA] Runtime Live Agents / species hierarchy.")]
    protected bool groupRuntimeAgentsBySpecies = true;

    [Header("Prefab viewport streaming")]
    [SerializeField] protected bool streamPrefabsByCameraView = false;
    [SerializeField, Tooltip("Legacy toggle kept for backward compatibility. SceneView camera is ignored; streaming uses Game camera only.")]
    protected bool preferSceneViewCameraInEditor = false;
    [SerializeField] protected bool keepSelectedPrefabsLoaded = true;
    [SerializeField, Min(0f)] protected float prefabViewPadding = 20f;
    [SerializeField, Min(0.02f)] protected float prefabViewUpdateInterval = 0.1f;
    [SerializeField, Tooltip("When enabled, prefabs beyond globalPrefabRenderDistance are deactivated (with hysteresis), in addition to frustum culling.")]
    protected bool enablePrefabRenderDistance = false;
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
    [SerializeField, Tooltip("Enable generic large import diagnostics, unchanged-object skipping, and frame-budgeted application.")]
    protected bool enableIncrementalImport = true;
    [SerializeField, Min(1), Tooltip("Species/property count at which an import is treated as large.")]
    protected int largeSpeciesThreshold = 5000;
    [SerializeField, Min(1), Tooltip("Geometry count at which an import is treated as large.")]
    protected int largeGeometryThreshold = 5000;
    [SerializeField, Min(1024), Tooltip("Raw json_output byte size at which an import is treated as large.")]
    protected int hugeMessageByteThreshold = 2 * 1024 * 1024;
    [SerializeField, Tooltip("Skip transform, mesh, material, and renderer work for agents whose import signature is unchanged.")]
    protected bool skipUnchangedObjects = true;
    [SerializeField, Tooltip("Current large-species rendering mode. BatchedMesh is reserved for a future combined-mesh path.")]
    private LargeSpeciesMode largeSpeciesMode = LargeSpeciesMode.CachedObjects;
    [SerializeField, Tooltip("Limit how many agents are applied per tick to avoid long main-thread spikes.")]
    protected bool limitAgentUpdatesPerTick = true;
    [SerializeField, Min(1), Tooltip("Maximum number of agent entries processed each tick when applying world updates.")]
    protected int maxAgentUpdatesPerTick = 2000;
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

    [Header("Outgoing Player Position")]
    [SerializeField, Tooltip("When disabled, Play Mode keeps the player/camera pose already set in the Unity scene instead of snapping to the initial player position sent by GAMA.")]
    private bool useGamaInitialPlayerPosition = false;
    [SerializeField] private PlayerPositionSource playerPositionSource = PlayerPositionSource.MainCamera;
    [SerializeField] private Transform explicitPlayerPositionSource;
    [SerializeField] private bool logOutgoingPlayerPosition = true;
    [SerializeField] private bool rejectSuspiciousPlayerPositions = true;
    [SerializeField, Min(0f)] private float suspiciousTeleportDistance = 25f;

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
    private bool runtimeAgentsRootOwned;
    private Dictionary<string, Transform> runtimeSpeciesParents;
    private readonly HashSet<Transform> ownedRuntimeSpeciesParents = new HashSet<Transform>();
    protected Dictionary<string, List<object>> geometryMap;
    private readonly HashSet<string> suppressedFollowedGeometryPropertyWarnings =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeAgentRecord> runtimeAgentRecords =
        new Dictionary<string, RuntimeAgentRecord>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> adoptedPreviewKeysByRuntimeKey =
        new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> cachedStableAgentKeyCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<string, int> cachedFallbackRuntimeKeyCounts =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private WorldJSONInfo identityCountSourceWorld;
    private readonly Dictionary<string, RuntimeSyncCounters> runtimeSyncCountersBySpecies =
        new Dictionary<string, RuntimeSyncCounters>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> runtimeAttributeNamesBySpecies =
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
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
    private int peopleAttributeDebugLogCount;
    private bool loggedMissingMainCameraForStreaming;
    private readonly Dictionary<string, int> missingAgentTickCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, int> lastImportSignatureByName = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly RuntimeImportCounters detachedRuntimeImportCounters = new RuntimeImportCounters();
    private RuntimeImportProfile currentImportProfile;
    private int runtimeLiveTickSerial;
    private Vector3 lastOutgoingPlayerUnityPosition;
    private bool hasLastOutgoingPlayerUnityPosition;
    private float nextOutgoingPlayerPositionLogTime;
    private float nextOutgoingPlayerWarningLogTime;
    private const float OutgoingPlayerPositionLogIntervalSeconds = 1f;
    private const float OutgoingPlayerWarningLogIntervalSeconds = 1f;
    private GamaPreviewReuseRegistry previewReuseRegistry;
    private bool previewReuseInitializationAttempted;
    private bool previewReuseRestoreInProgress;
    private bool previewReuseConnectionWasAuthenticated;

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
    private int runtimePerfLogCount;
    private float nextRuntimePlayerBootstrapTime;
    private int runtimePlayerBootstrapAttempts;
    private bool runtimePlayerBootstrapConfirmed;
    private const float RuntimePlayerBootstrapRetrySeconds = 1f;
    private const int RuntimePlayerBootstrapMaxAttempts = 20;

    // ############################################ UNITY FUNCTIONS ############################################
    void Awake()
    {
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.InitializeGama();
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.SetupPlayer(this, true);
        ProjectSimple.GamaUnity.Runtime.GamaInitializer.SetupGround(this, true);

        hasSimulator = UnityEngine.Object.FindFirstObjectByType<XRDeviceSimulator>() != null;
        connectionID["id"] = ConnectionManager.Instance != null ? ConnectionManager.Instance.GetConnectionId() : StaticInformation.getId();
        GamaLog.Dev("[GAMA] SimulationManager initialized");
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
            GamaLog.Error("[GAMA] SimulationManager could not find or create a player object.");
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
        if (previewReuseRegistry == null)
        {
            previewReuseInitializationAttempted = false;
        }
        TrySubscribeConnectionManager();
    }

    void OnDisable()
    {
        PrepareForEditorPlayExit();
        UnsubscribeConnectionEvents();
    }

    void OnDestroy()
    {
        PrepareForEditorPlayExit();
        UnsubscribeConnectionEvents();
        DrainPrefabPools();
    }

    /// <summary>
    /// Restores every static preview object claimed by this manager. Editor play
    /// guards call this before leaving Play Mode; OnDisable/OnDestroy are fallback
    /// paths for normal teardown and scene changes.
    /// </summary>
    public void PrepareForEditorPlayExit()
    {
        if (previewReuseRestoreInProgress)
        {
            return;
        }

        previewReuseRestoreInProgress = true;
        try
        {
            HashSet<GameObject> adoptedObjects = new HashSet<GameObject>();
            HashSet<GameObject> runtimeOnlyObjects = new HashSet<GameObject>();

            foreach (KeyValuePair<string, RuntimeAgentRecord> pair in runtimeAgentRecords)
            {
                RuntimeAgentRecord record = pair.Value;
                if (record == null || record.Root == null)
                {
                    continue;
                }

                if (record.IsAdoptedPreview || adoptedPreviewKeysByRuntimeKey.ContainsKey(pair.Key))
                {
                    adoptedObjects.Add(record.Root);
                }
                else
                {
                    runtimeOnlyObjects.Add(record.Root);
                }
            }

            if (geometryMap != null)
            {
                foreach (KeyValuePair<string, List<object>> pair in geometryMap)
                {
                    GameObject obj;
                    if (!TryReadRuntimeObject(pair.Value, out obj) || obj == null)
                    {
                        continue;
                    }

                    if (adoptedPreviewKeysByRuntimeKey.ContainsKey(pair.Key) || adoptedObjects.Contains(obj))
                    {
                        adoptedObjects.Add(obj);
                    }
                    else
                    {
                        runtimeOnlyObjects.Add(obj);
                    }
                }
            }

            foreach (GameObject adopted in adoptedObjects)
            {
                RemoveManagedRuntimeListeners(adopted);
                if (toFollow != null)
                {
                    toFollow.Remove(adopted);
                }
            }

            // The restore must happen before any runtime hierarchy is destroyed:
            // claimed objects are currently children of that hierarchy.
            if (previewReuseRegistry != null)
            {
                previewReuseRegistry.RestoreAll();
            }

            foreach (GameObject runtimeOnly in runtimeOnlyObjects)
            {
                if (runtimeOnly == null || adoptedObjects.Contains(runtimeOnly))
                {
                    continue;
                }

                // Destroy only objects tracked by this manager. Even an owned
                // hierarchy can be shared by a second manager that found it by
                // name, so deleting the whole root before all managers restore
                // their claims would be unsafe.
                DestroyManagedRuntimeObject(runtimeOnly, true);
            }

            DrainPrefabPools(true);

            if (geometryMap != null) geometryMap.Clear();
            runtimeAgentRecords.Clear();
            adoptedPreviewKeysByRuntimeKey.Clear();
            if (toFollow != null) toFollow.Clear();
            if (SelectedObjects != null) SelectedObjects.Clear();
            previousPrefabPositions.Clear();
            previousPrefabPropertyIds.Clear();
            prefabHeadingSourcePositions.Clear();
            prefabHeadingSourcePropertyIds.Clear();
            consumedPrefabHeadingSources.Clear();
            missingAgentTickCounts.Clear();
            lastImportSignatureByName.Clear();
            runtimeSyncCountersBySpecies.Clear();
            runtimeAttributeNamesBySpecies.Clear();
            invalidGeometryFallbackCounts.Clear();
            cachedStableAgentKeyCounts.Clear();
            cachedFallbackRuntimeKeyCounts.Clear();
            identityCountSourceWorld = null;
            toRemove.Clear();
            pendingWorldUpdateRemovalPass = false;
            pendingWorldAgentIndex = 0;
            pendingWorldPrefabIndex = 0;
            pendingWorldGeomIndex = 0;
            infoWorld = null;
            currentImportProfile = null;
            previewReuseRegistry = null;
            previewReuseInitializationAttempted = false;
            RestoreStaticPreviewHiddenByRuntimeData();
            staticPreviewHiddenAfterRuntimeData = false;
        }
        finally
        {
            previewReuseRestoreInProgress = false;
        }
    }

    void Start()
    {
        visualStateCache = new Dictionary<string, GamaAgentVisualState>(StringComparer.Ordinal);
        resolvedPrefabSignatures = new Dictionary<string, string>(StringComparer.Ordinal);
        runtimeSpeciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        runtimeAgentsRoot = null;
        runtimeAgentsRootOwned = false;
        ownedRuntimeSpeciesParents.Clear();
        runtimeAgentRecords.Clear();
        adoptedPreviewKeysByRuntimeKey.Clear();
        cachedStableAgentKeyCounts.Clear();
        cachedFallbackRuntimeKeyCounts.Clear();
        identityCountSourceWorld = null;
        previewReuseRegistry = null;
        previewReuseInitializationAttempted = false;
        previewReuseRestoreInProgress = false;
        previewReuseConnectionWasAuthenticated = false;
        runtimeSyncCountersBySpecies.Clear();
        runtimeAttributeNamesBySpecies.Clear();
        invalidGeometryFallbackCounts.Clear();
        lastImportSignatureByName.Clear();
        currentImportProfile = null;
        runtimeLiveTickSerial = 0;
        runtimeFlowLogCount = 0;
        runtimeCreateLogCount = 0;
            runtimePerfLogCount = 0;
            peopleAttributeDebugLogCount = 0;
            staticPreviewHiddenAfterRuntimeData = false;
            hasLastOutgoingPlayerUnityPosition = false;
            nextRuntimePlayerBootstrapTime = 0f;
            runtimePlayerBootstrapAttempts = 0;
            runtimePlayerBootstrapConfirmed = false;

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

        if (IsGameState(GameState.WAITING))
        {
            TryBootstrapRuntimePlayer();
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

           // GamaLog.Dev("handleGroundParametersRequested: " + handleGroundParametersRequested);

        }

        if (handleGeometriesRequested && infoWorld != null && infoWorld.isInit)// && propertyMap != null)
        {

            sendMessageToReactivatePositionSent = true;
            bool initCompleted = GenerateGeometries(true, null);
            if (initCompleted)
            {
                handleGeometriesRequested = false;
                UpdateGameState(GameState.GAME);
            }


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
           // GamaLog.Dev("readyToSendPosition: " + readyToSendPosition + " readyToSendPositionInit:" + readyToSendPositionInit + " TimerSendPosition: "+ TimerSendPosition);
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
            GamaLog.Dev("TryReconnectButton activated");
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
        BeginImportApplyIfNeeded();

        if (infoWorld == null || infoWorld.names == null || infoWorld.propertyID == null)
        {
            CompleteImportProfileIfNeeded(true);
            ClearRuntimeAgentIdentityCountCache();
            infoWorld = null;
            return true;
        }

        if (infoWorld.position != null && infoWorld.position.Count > 1 && (initGame || !sendMessageToReactivatePositionSent))
        {
            Vector3 pos = converter.fromGAMACRS(infoWorld.position[0], infoWorld.position[1], infoWorld.position[2]);
            if (useGamaInitialPlayerPosition)
            {
                LogPlayerSetPosition("gama_world_position", XROrigin.localPosition, pos);
                XROrigin.localPosition = pos;
            }
            else if (GamaLog.VerboseEnabled)
            {
                GamaLog.Dev("[GAMA][PLAYER][KEEP_POSITION] ignored GAMA initial player position=" + FormatVector(pos) +
                          " current=" + FormatVector(XROrigin.localPosition));
            }

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

        bool largeImport = currentImportProfile != null && currentImportProfile.IsLarge;
        bool budgetedPass = enableIncrementalImport &&
                            limitAgentUpdatesPerTick &&
                            maxAgentUpdatesPerTick > 0 &&
                            (!initGame || largeImport);
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

        Dictionary<string, int> stableAgentKeyCounts;
        Dictionary<string, int> fallbackRuntimeKeyCounts;
        BuildRuntimeAgentIdentityCounts(
            infoWorld,
            out stableAgentKeyCounts,
            out fallbackRuntimeKeyCounts);

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
            LogPeopleAttributeDebug(name, propId, attributes);
            GamaAgentVisualState visualState = ResolveAgentVisualState(name, prop, attributes);
            string speciesName = ResolveRuntimeSpeciesName(prop, propId);
            RegisterObservedRuntimeAttributes(propId, speciesName, attributes);
            string stableAgentKey;
            bool hasStableAgentKey = GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                NormalizePreviewReuseSpeciesKey(speciesName),
                name,
                attributes,
                out stableAgentKey,
                out _);
            int stableKeyCount = 0;
            bool hasUniqueStableAgentKey =
                hasStableAgentKey &&
                stableAgentKeyCounts.TryGetValue(stableAgentKey, out stableKeyCount) &&
                stableKeyCount == 1;

            string fallbackRuntimeKey = MakeRuntimeAgentKey(speciesName, name);
            int fallbackKeyCount = 0;
            fallbackRuntimeKeyCounts.TryGetValue(fallbackRuntimeKey, out fallbackKeyCount);
            string agentKey = hasUniqueStableAgentKey
                ? stableAgentKey
                : fallbackRuntimeKey + (fallbackKeyCount > 1 ? "::index=" + i : string.Empty);
            bool dynamicUpdate = !initGame;

            GameObject obj = null;

            if (prop.hasPrefab)
            {
                if (infoWorld.pointsLoc == null || cptPrefab >= infoWorld.pointsLoc.Count)
                {
                    continue;
                }

                GAMAPoint pointLoc = infoWorld.pointsLoc[cptPrefab];
                GameObject desiredPrefabAsset;
                string desiredPrefabSignature;
                TryResolvePrefabAsset(
                    prop,
                    attributes,
                    out desiredPrefabAsset,
                    out desiredPrefabSignature);
                int importSignature = ComputeImportSignature(
                    agentKey,
                    propId,
                    pointLoc,
                    null,
                    0,
                    attributes,
                    visualState,
                    desiredPrefabSignature);
                bool existingBefore = geometryMap != null && geometryMap.ContainsKey(agentKey);
                if (TrySkipUnchangedImport(agentKey, importSignature, propId, speciesName, name, prop, attributes, visualState, dynamicUpdate, toRemove, out obj))
                {
                    cptPrefab++;
                    if (ShouldDeferImportAfterProcessed(budgetedPass, ref processedAgentCount, budget, i, cptPrefab, cptGeom))
                    {
                        return false;
                    }
                    continue;
                }

                if (!geometryMap.ContainsKey(agentKey))
                {
                    if (TryAdoptPreviewAgent(
                            agentKey,
                            propId,
                            prop,
                            hasUniqueStableAgentKey ? stableAgentKey : string.Empty,
                            GamaPreviewRepresentationKind.Prefab,
                            desiredPrefabSignature,
                            desiredPrefabAsset,
                            out obj))
                    {
                        ConfigureAdoptedPreviewAgent(
                            obj,
                            name,
                            agentKey,
                            speciesName,
                            prop);
                    }
                    else
                    {
                        obj = instantiatePrefab(name, agentKey, speciesName, prop, attributes, desiredPrefabSignature, initGame);
                    }

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
                        ReleaseRuntimeAgentObject(agentKey, obj2);
                        UnregisterRuntimeAgent(agentKey);
                        if (toFollow != null && toFollow.Contains(obj2))
                            toFollow.Remove(obj2);

                        obj = instantiatePrefab(name, agentKey, speciesName, prop, attributes, desiredPrefabSignature, initGame);

                    }

                }
                List<int> pt = pointLoc.c;
                Vector3 basePos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                basePos.y += prop.yOffsetF;
                Vector3 pos = basePos + visualState.PositionOffset;
                Quaternion baseRotation = ResolvePrefabHeadingRotation(agentKey, prop, pt, basePos);
                if (adoptedPreviewKeysByRuntimeKey.ContainsKey(agentKey))
                {
                    Quaternion nativePrefabRotation = GetPrefabBaseRotation(obj);
                    if (string.IsNullOrWhiteSpace(GetPrefabSignature(obj)))
                    {
                        Quaternion visualHeading = baseRotation * Quaternion.Euler(visualState.RotationOffsetEuler);
                        nativePrefabRotation = Quaternion.Inverse(visualHeading) * obj.transform.rotation;
                    }
                    SetPrefabSignature(obj, desiredPrefabSignature, nativePrefabRotation);
                }
                Quaternion rotation = ComposePrefabRuntimeRotation(baseRotation, visualState, obj);
                if (GamaLog.VerboseEnabled &&
                    (agentKey.ToLower().Contains("car") || agentKey.ToLower().Contains("voiture") || agentKey.ToLower().Contains("vehicle"))) {
                    GamaLog.Dev($"[GAMA][ROTATION] {agentKey} pt[3]={(pt.Count > 3 ? pt[3].ToString() : "N/A")} baseRot={baseRotation.eulerAngles} finalRot={rotation.eulerAngles}");
                }

                obj.transform.SetPositionAndRotation(pos, rotation);
                previousPrefabPositions[agentKey] = basePos;
                previousPrefabPropertyIds[agentKey] = prop.id ?? string.Empty;
                ApplyAgentVisualState(obj, prop, visualState, true, Vector3.zero);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                //obj.SetActive(true);
                RegisterRuntimeAgent(agentKey, speciesName, name, obj, dynamicUpdate, visualState, attributes, basePos, baseRotation, basePos, prop.id, prop.tag);
                if(toRemove != null)
                {
                    toRemove.Remove(agentKey);
                    toRemove.Remove(name);
                }
                StoreImportSignature(agentKey, importSignature, propId, existingBefore);
                cptPrefab++;

            }
            else
            {
                if (infoWorld.pointsGeom == null || cptGeom >= infoWorld.pointsGeom.Count)
                {
                    continue;
                }

                GAMAPoint pointGeom = infoWorld.pointsGeom[cptGeom];
                int rawOffsetY = infoWorld.offsetYGeom != null && cptGeom < infoWorld.offsetYGeom.Count
                    ? infoWorld.offsetYGeom[cptGeom]
                    : 0;
                int importSignature = ComputeImportSignature(
                    agentKey,
                    propId,
                    null,
                    pointGeom,
                    rawOffsetY,
                    attributes,
                    visualState,
                    null);
                bool existingBefore = geometryMap != null && geometryMap.ContainsKey(agentKey);
                if (TrySkipUnchangedImport(agentKey, importSignature, propId, speciesName, name, prop, attributes, visualState, dynamicUpdate, toRemove, out obj))
                {
                    cptGeom++;
                    if (ShouldDeferImportAfterProcessed(budgetedPass, ref processedAgentCount, budget, i, cptPrefab, cptGeom))
                    {
                        return false;
                    }
                    continue;
                }

                if (polyGen == null)
                {
                    polyGen = PolygonGenerator.GetInstance();
                    polyGen.Init(converter);
                }

                int[] pt = pointGeom.c.ToArray();
                float yOffset = (0.0f + rawOffsetY) / (0.0f + parameters.precision);
                bool polygonInputValid = IsRuntimePolygonInputValid(pt);

                Vector3 computedWorldAnchor = Vector3.zero;
                bool hasComputedWorldAnchor = false;
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
                        hasComputedWorldAnchor = true;
                    }
                }

                Vector3 polygonBasePosition = hasComputedWorldAnchor
                    ? computedWorldAnchor
                    : new Vector3(0f, yOffset, 0f);

                if(!geometryMap.ContainsKey(agentKey))
                {
                    bool adoptedPreview = polygonInputValid && TryAdoptPreviewAgent(
                        agentKey,
                        propId,
                        prop,
                        hasUniqueStableAgentKey ? stableAgentKey : string.Empty,
                        GamaPreviewRepresentationKind.Geometry,
                        BuildPreviewReuseSourceSignature(
                            GamaPreviewRepresentationKind.Geometry,
                            propId,
                            prop),
                        null,
                        out obj);
                    if (adoptedPreview)
                    {
                        ConfigureAdoptedPreviewAgent(
                            obj,
                            name,
                            agentKey,
                            speciesName,
                            prop);
                        if (polygonInputValid)
                        {
                            polyGen.UpdatePolygon(obj, pt);
                            if (hasComputedWorldAnchor)
                            {
                                RecenterPolygonMeshForStableScale(obj, computedWorldAnchor);
                            }
                        }
                    }
                    else
                    {
                        obj = polygonInputValid
                            ? polyGen.GeneratePolygons(false, name, pt, prop, parameters.precision)
                            : new GameObject(name);
                        if (polygonInputValid && hasComputedWorldAnchor)
                        {
                            RecenterPolygonMeshForStableScale(obj, computedWorldAnchor);
                        }

                        if(prop.hasCollider)
                        {
                            MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                            if (meshFilter != null && meshFilter.sharedMesh != null)
                            {
                                MeshCollider mc = obj.GetComponent<MeshCollider>();
                                if (mc == null)
                                {
                                    mc = obj.AddComponent<MeshCollider>();
                                }
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
                            if (hasComputedWorldAnchor)
                            {
                                RecenterPolygonMeshForStableScale(obj, computedWorldAnchor);
                            }
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

                Quaternion geometryBaseRotation = ResolveGeometryHeadingRotation(agentKey, prop, pt, computedWorldAnchor);
                ApplyAgentVisualState(obj, prop, visualState, false, polygonBasePosition, computedWorldAnchor, geometryBaseRotation);
                HandleInvalidDynamicGeometryFallback(obj, speciesName, visualState, computedWorldAnchor, dynamicUpdate, !polygonInputValid, geometryBaseRotation);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
                RegisterRuntimeAgent(agentKey, speciesName, name, obj, dynamicUpdate, visualState, attributes, polygonBasePosition, geometryBaseRotation, computedWorldAnchor, prop.id, prop.tag);
                if(toRemove != null)
                {
                    toRemove.Remove(agentKey);
                    toRemove.Remove(name);
                }
                StoreImportSignature(agentKey, importSignature, propId, existingBefore);
                cptGeom++;
            }

            if (ShouldDeferImportAfterProcessed(budgetedPass, ref processedAgentCount, budget, i, cptPrefab, cptGeom))
            {
                return false;
            }

        }

        pendingWorldAgentIndex = 0;
        pendingWorldPrefabIndex = 0;
        pendingWorldGeomIndex = 0;
       
        if (infoWorld.attributes != null && infoWorld.attributes.Count > 0)
            ManageAttributes(infoWorld.attributes);


        if (initGame)
            AdditionalInitAfterGeomLoading();

        CompleteImportProfileIfNeeded(true);
        ClearRuntimeAgentIdentityCountCache();
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
                    GamaLog.Dev("[GAMA] Loading initial data from middleware");
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
                GamaLog.Warning("[GAMA] Simulation crashed");
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
        if (converter == null || parameters == null)
        {
            LogOutgoingSkip("missing_converter_or_parameters", "move_player_external");
            return;
        }

        PlayerPositionSource resolvedSource;
        Transform source = ResolvePlayerPositionSource(out resolvedSource);
        if (source == null)
        {
            LogOutgoingSkip("no_player_position_source", "move_player_external");
            return;
        }

        Vector3 unityPos = source.position;
        if (IsSuspiciousOutgoingPlayerPosition(source, resolvedSource, unityPos))
        {
            if (rejectSuspiciousPlayerPositions)
            {
                LogOutgoingSkip("suspicious_player_position", "move_player_external");
                TimerSendPosition = TimeSendPosition;
                return;
            }
        }

        WarnIfRootAndCameraDiverge(resolvedSource, source);

        int angle = ResolveOutgoingPlayerAngle(source);
        List<int> p = converter.toGAMACRS3D(unityPos);
        LogOutgoingPlayerPosition(resolvedSource, unityPos, p, angle);
        Dictionary<string, string> args = new Dictionary<string, string> {
             {"id", ConnectionManager.Instance != null ? ConnectionManager.Instance.GetConnectionId() : StaticInformation.getId() },
            {"x", "" +p[0]},
            {"y", "" +p[1]}, 
            {"z", "" +p[2]},
            {"angle", "" +angle}
        };

        if (TrySendExecutableAsk("move_player_external", args, "player position"))
        {
            lastOutgoingPlayerUnityPosition = unityPos;
            hasLastOutgoingPlayerUnityPosition = true;
        }

        TimerSendPosition = TimeSendPosition;
    }

    private Transform ResolvePlayerPositionSource(out PlayerPositionSource resolvedSource)
    {
        resolvedSource = playerPositionSource;

        if (playerPositionSource == PlayerPositionSource.ExplicitTransform)
        {
            Transform explicitSource = ValidatePlayerPositionSource(explicitPlayerPositionSource);
            if (explicitSource != null)
            {
                resolvedSource = PlayerPositionSource.ExplicitTransform;
                return explicitSource;
            }
        }

        if (playerPositionSource == PlayerPositionSource.MainCamera)
        {
            Transform cameraSource = Camera.main != null ? ValidatePlayerPositionSource(Camera.main.transform) : null;
            if (cameraSource != null)
            {
                resolvedSource = PlayerPositionSource.MainCamera;
                return cameraSource;
            }
        }

        if (playerPositionSource == PlayerPositionSource.XROriginRoot)
        {
            Transform originSource = ValidatePlayerPositionSource(XROrigin);
            if (originSource != null)
            {
                resolvedSource = PlayerPositionSource.XROriginRoot;
                return originSource;
            }
        }

        if (playerPositionSource == PlayerPositionSource.FPSPlayerRoot)
        {
            Transform fpsSource = ValidatePlayerPositionSource(FindFPSPlayerTransform());
            if (fpsSource != null)
            {
                resolvedSource = PlayerPositionSource.FPSPlayerRoot;
                return fpsSource;
            }
        }

        if (Camera.main != null)
        {
            Transform cameraFallback = ValidatePlayerPositionSource(Camera.main.transform);
            if (cameraFallback != null)
            {
                resolvedSource = PlayerPositionSource.MainCamera;
                return cameraFallback;
            }
        }

        Transform originFallback = ValidatePlayerPositionSource(XROrigin);
        if (originFallback != null)
        {
            resolvedSource = PlayerPositionSource.XROriginRoot;
            return originFallback;
        }

        Transform fpsFallback = ValidatePlayerPositionSource(FindFPSPlayerTransform());
        if (fpsFallback != null)
        {
            resolvedSource = PlayerPositionSource.FPSPlayerRoot;
            return fpsFallback;
        }

        return null;
    }

    private Transform ValidatePlayerPositionSource(Transform candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        if (candidate == transform ||
            candidate.GetComponent<SimulationManager>() != null ||
            candidate.GetComponent<ConnectionManager>() != null)
        {
            LogOutgoingWarning("[GAMA][OUT][WARN] rejected manager transform as player position source source=" + candidate.name);
            return null;
        }

        return candidate;
    }

    private Transform FindFPSPlayerTransform()
    {
        if (player != null)
        {
            return player.transform;
        }

        GameObject taggedPlayer = GamaSceneUtility.FindGameObjectWithTag("player");
        if (taggedPlayer != null)
        {
            return taggedPlayer.transform;
        }

        GameObject fpsPlayer = GameObject.Find("FPSPlayer");
        return fpsPlayer != null ? fpsPlayer.transform : null;
    }

    private int ResolveOutgoingPlayerAngle(Transform source)
    {
        Transform directionSource = Camera.main != null ? Camera.main.transform : source;
        if (directionSource == null || parameters == null)
        {
            return 0;
        }

        Vector2 playerForward = new Vector2(directionSource.forward.x, directionSource.forward.z);
        if (playerForward.sqrMagnitude < 0.0001f)
        {
            return 0;
        }

        playerForward.Normalize();
        Vector2 worldForward = Vector2.up;
        float dot = Mathf.Clamp(Vector2.Dot(playerForward, worldForward), -1f, 1f);
        float cross = playerForward.x * worldForward.y - playerForward.y * worldForward.x;
        float signedDegrees = ((cross > 0f) ? -1f : 1f) * Mathf.Rad2Deg * Mathf.Acos(dot);
        return Mathf.RoundToInt(signedDegrees * parameters.precision);
    }

    private bool IsSuspiciousOutgoingPlayerPosition(Transform source, PlayerPositionSource resolvedSource, Vector3 unityPos)
    {
        if (!IsFiniteVector(unityPos))
        {
            LogOutgoingWarning("[GAMA][OUT][WARN] suspicious player position source=" + resolvedSource +
                               " unityPos=" + FormatVector(unityPos) +
                               " lastUnityPos=" + FormatLastOutgoingPosition());
            return true;
        }

        bool suspicious = false;
        if (unityPos.sqrMagnitude < 0.000001f)
        {
            suspicious = true;
        }

        if (source == transform || source.GetComponent<SimulationManager>() != null || source.GetComponent<ConnectionManager>() != null)
        {
            suspicious = true;
        }

        if (hasLastOutgoingPlayerUnityPosition &&
            suspiciousTeleportDistance > 0f &&
            Vector3.Distance(lastOutgoingPlayerUnityPosition, unityPos) > suspiciousTeleportDistance)
        {
            suspicious = true;
        }

        if (GamaLog.VerboseEnabled && suspicious)
        {
            LogOutgoingWarning("[GAMA][OUT][WARN] suspicious player position source=" + resolvedSource +
                               " unityPos=" + FormatVector(unityPos) +
                               " lastUnityPos=" + FormatLastOutgoingPosition());
        }

        return suspicious;
    }

    private void WarnIfRootAndCameraDiverge(PlayerPositionSource resolvedSource, Transform source)
    {
        if (!GamaLog.VerboseEnabled ||
            source == null ||
            Camera.main == null ||
            resolvedSource == PlayerPositionSource.MainCamera ||
            resolvedSource == PlayerPositionSource.ExplicitTransform)
        {
            return;
        }

        Vector3 rootPos = source.position;
        Vector3 cameraPos = Camera.main.transform.position;
        if (Vector3.Distance(rootPos, cameraPos) > 1f)
        {
            LogOutgoingWarning("[GAMA][OUT][WARN] player root and camera positions differ root=" +
                               FormatVector(rootPos) + " camera=" + FormatVector(cameraPos));
        }
    }

    private void LogOutgoingPlayerPosition(PlayerPositionSource source, Vector3 unityPos, List<int> gamaPos, int angle)
    {
        if (!GamaLog.VerboseEnabled || !logOutgoingPlayerPosition)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextOutgoingPlayerPositionLogTime)
        {
            return;
        }

        nextOutgoingPlayerPositionLogTime = now + OutgoingPlayerPositionLogIntervalSeconds;
        ConnectionManager manager = ConnectionManager.Instance;
        bool socketOpen = manager != null && manager.CanSendRuntimeMessages;
        string gama = gamaPos != null && gamaPos.Count >= 3
            ? "(" + gamaPos[0] + "," + gamaPos[1] + "," + gamaPos[2] + ")"
            : "(missing)";
        GamaLog.Dev("[GAMA][OUT][PLAYER_POS] source=" + source +
                  " unityPos=" + FormatVector(unityPos) +
                  " gamaPos=" + gama +
                  " angle=" + angle +
                  " focus=" + Application.isFocused +
                  " socketOpen=" + socketOpen);
    }

    private void LogOutgoingSkip(string reason, string action)
    {
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextOutgoingPlayerWarningLogTime)
        {
            return;
        }

        nextOutgoingPlayerWarningLogTime = now + OutgoingPlayerWarningLogIntervalSeconds;
        GamaLog.DevWarning("[GAMA][OUT][SKIP] reason=" + reason + " action=" + action);
    }

    private void LogOutgoingWarning(string message)
    {
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextOutgoingPlayerWarningLogTime)
        {
            return;
        }

        nextOutgoingPlayerWarningLogTime = now + OutgoingPlayerWarningLogIntervalSeconds;
        GamaLog.DevWarning(message);
    }

    private void LogPlayerSetPosition(string reason, Vector3 oldPosition, Vector3 newPosition)
    {
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        GamaLog.Dev("[GAMA][PLAYER][SET_POSITION] reason=" + reason +
                  " old=" + FormatVector(oldPosition) +
                  " new=" + FormatVector(newPosition));
    }

    private static bool IsFiniteVector(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private string FormatLastOutgoingPosition()
    {
        return hasLastOutgoingPlayerUnityPosition ? FormatVector(lastOutgoingPlayerUnityPosition) : "(none)";
    }

    private static string FormatVector(Vector3 value)
    {
        return "(" + value.x.ToString("F3") + "," + value.y.ToString("F3") + "," + value.z.ToString("F3") + ")";
    }


    private void instantiateGO(GameObject obj, String name, PropertiesGAMA prop)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        obj.name = name;
        if (ShouldSendFollowedGeometryToGama(prop))
        {
            if (toFollow != null && !toFollow.Contains(obj))
            {
                toFollow.Add(obj);
            }
        }
        else if (prop != null && prop.toFollow)
        {
            LogSuppressedFollowedGeometrySync(prop);
        }
        if (prop.tag != null && !string.IsNullOrEmpty(prop.tag))
            GamaSceneUtility.TrySetTag(obj, prop.tag);

        if (prop.isInteractable)
        {
            if (interactionManager == null)
                interactionManager = GameObject.FindFirstObjectByType<XRInteractionManager>();

            // Static preview objects may already have been configured during an
            // earlier live pass. Reuse the existing interactable and replace only
            // this manager's callbacks so one object never accumulates duplicate
            // XR components or listeners across release/re-adoption cycles.
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interaction =
                obj.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            if (interaction == null && prop.isGrabable)
            {
                interaction = obj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            }
            else if (interaction == null)
            {
                interaction = obj.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            }

            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (prop.isGrabable && rb != null && prop.constraints != null && prop.constraints.Count == 6)
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

            if (interaction.colliders.Count == 0)
            {
                Collider[] cs = obj.GetComponentsInChildren<Collider>(true);
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
            interaction.selectEntered.RemoveListener(SelectInteraction);
            interaction.firstHoverEntered.RemoveListener(HoverEnterInteraction);
            interaction.hoverExited.RemoveListener(HoverExitInteraction);
            interaction.selectEntered.AddListener(SelectInteraction);
            interaction.firstHoverEntered.AddListener(HoverEnterInteraction);
            interaction.hoverExited.AddListener(HoverExitInteraction);

        }
    }

    private static bool ShouldSendFollowedGeometryToGama(PropertiesGAMA prop)
    {
        return prop != null && prop.toFollow && (prop.isInteractable || prop.isGrabable);
    }

    private void LogSuppressedFollowedGeometrySync(PropertiesGAMA prop)
    {
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        string propertyId = string.IsNullOrWhiteSpace(prop.id) ? "(unknown)" : prop.id;
        if (!suppressedFollowedGeometryPropertyWarnings.Add(propertyId))
        {
            return;
        }

        GamaLog.DevWarning("[GAMA][OUT][FOLLOW] suppressed propertyID=" + propertyId +
                         " reason=not_unity_controlled toFollow=True isInteractable=" + prop.isInteractable +
                         " isGrabable=" + prop.isGrabable);
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

    private bool TryAdoptPreviewAgent(
        string runtimeKey,
        string propertyId,
        PropertiesGAMA prop,
        string stableAgentKey,
        GamaPreviewRepresentationKind representationKind,
        string sourceSignature,
        GameObject sourcePrefabAsset,
        out GameObject obj)
    {
        obj = null;
        if (string.IsNullOrWhiteSpace(runtimeKey) ||
            string.IsNullOrWhiteSpace(stableAgentKey) ||
            prop == null ||
            !TryInitializePreviewReuseRegistry())
        {
            return false;
        }

        if (!previewReuseRegistry.TryTake(
                stableAgentKey,
                propertyId,
                representationKind,
                sourceSignature,
                sourcePrefabAsset,
                out obj) ||
            obj == null)
        {
            obj = null;
            return false;
        }

        adoptedPreviewKeysByRuntimeKey[runtimeKey] = stableAgentKey;
        return true;
    }

    private bool TryInitializePreviewReuseRegistry()
    {
        if (previewReuseRegistry != null)
        {
            return true;
        }

        if (previewReuseInitializationAttempted)
        {
            return false;
        }

        previewReuseInitializationAttempted = true;
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        string expectedExperimentKey = string.Empty;
        int authorizedSessionCount = 0;
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (session == null ||
                session.stale ||
                !session.reuseAuthorizedForPlay ||
                string.IsNullOrWhiteSpace(session.authorizedStableExperimentKey) ||
                string.IsNullOrWhiteSpace(session.stableExperimentKey) ||
                !string.Equals(
                    session.authorizedStableExperimentKey,
                    session.stableExperimentKey,
                    StringComparison.Ordinal) ||
                (session.activeGamaSelection &&
                 (string.IsNullOrWhiteSpace(session.authorizedMonitorExperimentId) ||
                  !string.Equals(
                      session.authorizedMonitorExperimentId,
                      session.monitorExperimentId,
                      StringComparison.Ordinal))))
            {
                continue;
            }

            string stableExperimentKey;
            if (!session.TryGetStableExperimentKey(out stableExperimentKey) ||
                !string.Equals(
                    session.authorizedStableExperimentKey,
                    stableExperimentKey,
                    StringComparison.Ordinal))
            {
                continue;
            }

            authorizedSessionCount++;
            expectedExperimentKey = stableExperimentKey;
        }

        // Multiple authorized snapshots are ambiguous even when their text keys
        // happen to match. Reuse remains disabled instead of guessing a root.
        if (authorizedSessionCount != 1 || string.IsNullOrWhiteSpace(expectedExperimentKey))
        {
            return false;
        }

        return GamaPreviewReuseRegistry.TryCreate(expectedExperimentKey, out previewReuseRegistry) &&
               previewReuseRegistry != null;
    }

    private static string BuildPreviewReuseSourceSignature(
        GamaPreviewRepresentationKind representationKind,
        string propertyId,
        PropertiesGAMA prop)
    {
        string normalizedPropertyId = NormalizeKey(propertyId);
        if (representationKind == GamaPreviewRepresentationKind.Prefab)
        {
            return "prefab:" + normalizedPropertyId + ":" +
                   NormalizeKey(prop != null ? prop.prefab : string.Empty);
        }

        return "geometry:" + normalizedPropertyId;
    }

    private void ConfigureAdoptedPreviewAgent(
        GameObject obj,
        string name,
        string runtimeKey,
        string speciesName,
        PropertiesGAMA prop)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        obj.name = name;
        obj.SetActive(true);
        EnableGpuInstancing(obj);
        EnsureColliderSetup(obj, prop);
        instantiateGO(obj, name, prop);
        if (groupRuntimeAgentsBySpecies)
        {
            ParentRuntimeAgent(obj, string.IsNullOrWhiteSpace(speciesName) ? prop.id : speciesName);
        }
        else
        {
            // Detach the claimed object before hiding the remaining static
            // snapshot. The registry retains its original parent for restoration.
            obj.transform.SetParent(null, true);
            HideStaticPreviewAfterRuntimeData();
        }

        if (geometryMap != null)
        {
            geometryMap[runtimeKey] = new List<object> { obj, prop };
        }
    }

    private void ParentRuntimeAgent(GameObject obj, string speciesKey)
    {
        if (obj == null) return;
        if (!groupRuntimeAgentsBySpecies)
        {
            HideStaticPreviewAfterRuntimeData();
            return;
        }
        
        if (runtimeAgentsRoot == null)
        {
            GameObject rootObj = GameObject.Find("[GAMA] Runtime Live Agents");
            if (rootObj == null)
            {
                rootObj = new GameObject("[GAMA] Runtime Live Agents");
                runtimeAgentsRootOwned = true;
                GamaLog.Dev("[GAMA][RUNTIME] Created runtime hierarchy root: [GAMA] Runtime Live Agents");
            }
            else
            {
                runtimeAgentsRootOwned = false;
            }

            runtimeAgentsRoot = rootObj.transform;

            if (runtimeAgentsRootOwned)
            {
                runtimeAgentsRoot.position = Vector3.zero;
                runtimeAgentsRoot.rotation = Quaternion.identity;
                runtimeAgentsRoot.localScale = Vector3.one;
            }
        }

        string safeSpecies = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();

        Transform speciesParent;
        if (runtimeSpeciesParents == null) 
        {
            runtimeSpeciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        }
        
        if (!runtimeSpeciesParents.TryGetValue(safeSpecies, out speciesParent) || speciesParent == null)
        {
            bool speciesParentOwned = false;
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
                ownedRuntimeSpeciesParents.Add(speciesParent);
                speciesParentOwned = true;
            }

            if (speciesParentOwned)
            {
                speciesParent.localPosition = Vector3.zero;
                speciesParent.localRotation = Quaternion.identity;
                speciesParent.localScale = Vector3.one;
            }
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

    private void BuildRuntimeAgentIdentityCounts(
        WorldJSONInfo world,
        out Dictionary<string, int> stableKeyCounts,
        out Dictionary<string, int> fallbackKeyCounts)
    {
        stableKeyCounts = cachedStableAgentKeyCounts;
        fallbackKeyCounts = cachedFallbackRuntimeKeyCounts;
        if (ReferenceEquals(identityCountSourceWorld, world))
        {
            return;
        }

        identityCountSourceWorld = world;
        stableKeyCounts.Clear();
        fallbackKeyCounts.Clear();
        if (world == null || world.names == null || world.propertyID == null)
        {
            return;
        }

        int count = Mathf.Min(world.names.Count, world.propertyID.Count);
        for (int i = 0; i < count; i++)
        {
            string name = world.names[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "agent_" + i;
            }

            string propertyId = world.propertyID[i];
            PropertiesGAMA prop;
            if (string.IsNullOrWhiteSpace(propertyId) ||
                propertyMap == null ||
                !propertyMap.TryGetValue(propertyId, out prop) ||
                prop == null)
            {
                continue;
            }

            string speciesName = ResolveRuntimeSpeciesName(prop, propertyId);
            IncrementRuntimeIdentityCount(
                fallbackKeyCounts,
                MakeRuntimeAgentKey(speciesName, name));

            string stableAgentKey;
            if (GamaPreviewReuseIdentity.TryBuildStableAgentKey(
                    NormalizePreviewReuseSpeciesKey(speciesName),
                    name,
                    world.GetAttributesAt(i),
                    out stableAgentKey,
                    out _))
            {
                IncrementRuntimeIdentityCount(stableKeyCounts, stableAgentKey);
            }
        }
    }

    private static void IncrementRuntimeIdentityCount(
        Dictionary<string, int> counts,
        string key)
    {
        if (counts == null || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        int count;
        counts.TryGetValue(key, out count);
        counts[key] = count + 1;
    }

    private void ClearRuntimeAgentIdentityCountCache()
    {
        cachedStableAgentKeyCounts.Clear();
        cachedFallbackRuntimeKeyCounts.Clear();
        identityCountSourceWorld = null;
    }

    private static string NormalizePreviewReuseSpeciesKey(string speciesName)
    {
        string value = string.IsNullOrWhiteSpace(speciesName)
            ? "unknown"
            : speciesName.Trim();
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
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

                if (string.Equals(record.AgentId, keepName, StringComparison.Ordinal) ||
                    string.Equals(record.Key, keepName, StringComparison.Ordinal))
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
        Attributes attributes = null,
        Vector3? basePosition = null,
        Quaternion? baseRotation = null,
        Vector3? visualAnchor = null,
        string propertyId = null,
        string propertyTag = null)
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
        if (!string.IsNullOrWhiteSpace(propertyId))
        {
            record.PropertyId = propertyId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(propertyTag))
        {
            record.PropertyTag = propertyTag.Trim();
        }
        record.AgentId = string.IsNullOrWhiteSpace(agentId) ? key : agentId.Trim();
        record.Root = root;
        record.VisualRoot = ResolveRuntimeVisualRoot(root);
        string previewReuseKey = string.Empty;
        record.IsAdoptedPreview =
            previewReuseRegistry != null &&
            adoptedPreviewKeysByRuntimeKey.TryGetValue(key, out previewReuseKey) &&
            !string.IsNullOrWhiteSpace(previewReuseKey);
        record.PreviewReuseKey = record.IsAdoptedPreview ? previewReuseKey : string.Empty;
        record.IsDynamic = record.IsDynamic || dynamicUpdate;
        if (attributes != null)
        {
            record.LastAttributes = attributes;
        }
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

        if (GamaLog.VerboseEnabled && created && runtimeCreateLogCount < 20)
        {
            GamaLog.Dev("[GAMA][RUNTIME][CREATE] species=" + record.SpeciesName + " agent=" + record.AgentId);
            runtimeCreateLogCount++;
        }

        record.CurrentlyVisible = visualState.Visible && root.activeSelf;
        record.UsesPrefabOverride = visualState.PrefabOverride != null ||
                                    !string.IsNullOrWhiteSpace(visualState.PrefabResourcePath);
        record.LastPositionOffset = visualState.PositionOffset;
        record.LastRotationOffsetEuler = visualState.RotationOffsetEuler;
        missingAgentTickCounts.Remove(key);
    }

    private void RegisterObservedRuntimeAttributes(string propertyId, string speciesName, Attributes attributes)
    {
        if (attributes == null)
        {
            return;
        }

        List<string> attributeNames = attributes.GetAttributeNames();
        if (attributeNames == null || attributeNames.Count == 0)
        {
            return;
        }

        AddObservedRuntimeAttributes(propertyId, attributeNames);
        AddObservedRuntimeAttributes(speciesName, attributeNames);
    }

    private void AddObservedRuntimeAttributes(string speciesName, List<string> attributeNames)
    {
        if (string.IsNullOrWhiteSpace(speciesName) || attributeNames == null || attributeNames.Count == 0)
        {
            return;
        }

        HashSet<string> set;
        if (!runtimeAttributeNamesBySpecies.TryGetValue(speciesName, out set) || set == null)
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            runtimeAttributeNamesBySpecies[speciesName] = set;
        }

        for (int i = 0; i < attributeNames.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(attributeNames[i]))
            {
                set.Add(attributeNames[i].Trim());
            }
        }
    }

    public string[] GetRuntimeAttributeNamesForSpecies(string speciesName)
    {
        if (string.IsNullOrWhiteSpace(speciesName))
        {
            return new string[0];
        }

        HashSet<string> set;
        if (!runtimeAttributeNamesBySpecies.TryGetValue(speciesName, out set) || set == null || set.Count == 0)
        {
            return new string[0];
        }

        List<string> names = new List<string>(set);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.ToArray();
    }

    private bool TrySkipUnchangedImport(
        string key,
        int importSignature,
        string propertyId,
        string speciesName,
        string agentId,
        PropertiesGAMA prop,
        Attributes attributes,
        GamaAgentVisualState visualState,
        bool dynamicUpdate,
        HashSet<string> removalSet,
        out GameObject obj)
    {
        obj = null;
        if (!enableIncrementalImport || !skipUnchangedObjects || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        int previousSignature;
        if (!lastImportSignatureByName.TryGetValue(key, out previousSignature) || previousSignature != importSignature)
        {
            return false;
        }

        List<object> entry;
        if (geometryMap == null ||
            !geometryMap.TryGetValue(key, out entry) ||
            entry == null ||
            entry.Count == 0)
        {
            return false;
        }

        obj = entry[0] as GameObject;
        if (obj == null)
        {
            return false;
        }

        RegisterRuntimeAgent(
            key,
            speciesName,
            agentId,
            obj,
            dynamicUpdate,
            visualState,
            attributes,
            propertyId: prop != null ? prop.id : null,
            propertyTag: prop != null ? prop.tag : null);
        if (removalSet != null)
        {
            removalSet.Remove(key);
            removalSet.Remove(agentId);
        }

        GetRuntimeImportCounters(propertyId).SkippedUnchanged++;
        return true;
    }

    private void LogPeopleAttributeDebug(string agentName, string propertyId, Attributes attributes)
    {
        if (!GamaLog.VerboseEnabled ||
            peopleAttributeDebugLogCount >= PeopleAttributeDebugMaxLogs ||
            !string.Equals(propertyId, "people", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        peopleAttributeDebugLogCount++;
        if (attributes == null)
        {
            GamaLog.Dev("[GAMA][ATTR_DEBUG] agent=" + agentName +
                      " species=" + propertyId +
                      " hasAttributes=false");
            return;
        }

        bool boolValue;
        string stringValue;
        float floatValue;
        bool hasBool = attributes.TryGetBool(out boolValue, "is_infected");
        bool hasString = attributes.TryGetString(out stringValue, "is_infected");
        bool hasFloat = attributes.TryGetFloat(out floatValue, "is_infected");
        string raw = attributes.ToDebugString();

        if (!hasBool && !hasString && !hasFloat)
        {
            GamaLog.Dev("[GAMA][ATTR_DEBUG] agent=" + agentName +
                      " species=" + propertyId +
                      " hasAttributes=true is_infected_missing raw=" + raw);
            return;
        }

        GamaLog.Dev("[GAMA][ATTR_DEBUG] agent=" + agentName +
                  " species=" + propertyId +
                  " hasAttributes=true" +
                  " is_infected_bool=" + hasBool +
                  " value=" + (hasBool ? boolValue.ToString() : "(missing)") +
                  " is_infected_string=" + hasString +
                  " stringValue=" + (hasString ? stringValue : "(missing)") +
                  " is_infected_float=" + hasFloat +
                  " floatValue=" + (hasFloat ? floatValue.ToString("G9") : "(missing)") +
                  " raw=" + raw);
    }

    private void StoreImportSignature(string key, int importSignature, string propertyId, bool existingBefore)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lastImportSignatureByName[key] = importSignature;
        RuntimeImportCounters counters = GetRuntimeImportCounters(propertyId);
        if (existingBefore)
        {
            counters.Updated++;
        }
        else
        {
            counters.Created++;
        }
    }

    private bool ShouldDeferImportAfterProcessed(
        bool budgetedPass,
        ref int processedAgentCount,
        int budget,
        int currentAgentIndex,
        int nextPrefabIndex,
        int nextGeomIndex)
    {
        if (!budgetedPass)
        {
            return false;
        }

        processedAgentCount++;
        if (processedAgentCount < budget || infoWorld == null || infoWorld.names == null || currentAgentIndex + 1 >= infoWorld.names.Count)
        {
            return false;
        }

        pendingWorldAgentIndex = currentAgentIndex + 1;
        pendingWorldPrefabIndex = nextPrefabIndex;
        pendingWorldGeomIndex = nextGeomIndex;
        int deferred = infoWorld.names.Count - pendingWorldAgentIndex;
        MarkRuntimeImportDeferred(deferred);
        EmitAgentUpdateBudgetDiagnostic(processedAgentCount, infoWorld.names.Count, pendingWorldAgentIndex);
        return true;
    }

    private int ComputeImportSignature(
        string agentKey,
        string propertyId,
        GAMAPoint pointLoc,
        GAMAPoint pointGeom,
        int rawGeometryYOffset,
        Attributes attributes,
        GamaAgentVisualState visualState,
        string prefabSignature)
    {
        unchecked
        {
            int hash = 17;
            hash = HashString(hash, agentKey);
            hash = HashString(hash, propertyId);
            hash = HashPoint(hash, pointLoc);
            hash = HashPoint(hash, pointGeom);
            hash = hash * 31 + rawGeometryYOffset;
            hash = hash * 31 + (attributes != null ? attributes.ComputeStableHash() : 0);
            hash = HashVisualState(hash, visualState);
            hash = HashString(hash, prefabSignature);
            return hash;
        }
    }

    private static int HashPoint(int hash, GAMAPoint point)
    {
        unchecked
        {
            if (point == null || point.c == null)
            {
                return hash * 31;
            }

            hash = hash * 31 + point.c.Count;
            for (int i = 0; i < point.c.Count; i++)
            {
                hash = hash * 31 + point.c[i];
            }

            return hash;
        }
    }

    private static int HashVisualState(int hash, GamaAgentVisualState state)
    {
        unchecked
        {
            hash = hash * 31 + (state.Visible ? 1 : 0);
            hash = hash * 31 + (state.HasColor ? 1 : 0);
            hash = hash * 31 + state.Color.r;
            hash = hash * 31 + state.Color.g;
            hash = hash * 31 + state.Color.b;
            hash = hash * 31 + state.Color.a;
            hash = hash * 31 + Mathf.RoundToInt(state.ScaleMultiplier * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.PositionOffset.x * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.PositionOffset.y * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.PositionOffset.z * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.RotationOffsetEuler.x * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.RotationOffsetEuler.y * 100000f);
            hash = hash * 31 + Mathf.RoundToInt(state.RotationOffsetEuler.z * 100000f);
            hash = HashString(hash, state.PrefabResourcePath);
            hash = HashString(hash, state.PrefabOverride != null ? state.PrefabOverride.name : null);
            return hash;
        }
    }

    private static int HashString(int hash, string value)
    {
        unchecked
        {
            return hash * 31 + (string.IsNullOrEmpty(value) ? 0 : StringComparer.Ordinal.GetHashCode(value));
        }
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

    private void ReleaseRuntimeAgentObject(string key, GameObject instance)
    {
        if (TryReleaseAdoptedPreview(key, instance))
        {
            return;
        }

        ReleasePrefabInstance(instance);
    }

    private void RemoveManagedRuntimeListeners(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable[] interactions =
            instance.GetComponentsInChildren<
                UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>(true);
        for (int i = 0; i < interactions.Length; i++)
        {
            UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable interaction = interactions[i];
            if (interaction == null)
            {
                continue;
            }

            interaction.selectEntered.RemoveListener(SelectInteraction);
            interaction.firstHoverEntered.RemoveListener(HoverEnterInteraction);
            interaction.hoverExited.RemoveListener(HoverExitInteraction);
        }
    }

    private bool TryReleaseAdoptedPreview(string key, GameObject instance)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        RuntimeAgentRecord record;
        string previewReuseKey = string.Empty;
        bool recordIsAdopted =
            runtimeAgentRecords.TryGetValue(key, out record) &&
            record != null &&
            record.IsAdoptedPreview;
        bool mappingIsAdopted = adoptedPreviewKeysByRuntimeKey.TryGetValue(key, out previewReuseKey);
        if (!recordIsAdopted && !mappingIsAdopted)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(previewReuseKey) && record != null)
        {
            previewReuseKey = record.PreviewReuseKey;
        }

        if (toFollow != null && instance != null)
        {
            toFollow.Remove(instance);
        }
        RemoveManagedRuntimeListeners(instance);
        if (instance != null)
        {
            prefabDistanceCulled.Remove(instance.GetInstanceID());
        }

        adoptedPreviewKeysByRuntimeKey.Remove(key);
        if (record != null)
        {
            record.IsAdoptedPreview = false;
            record.PreviewReuseKey = string.Empty;
        }

        if (previewReuseRegistry != null && !string.IsNullOrWhiteSpace(previewReuseKey))
        {
            previewReuseRegistry.Release(previewReuseKey);
        }

        // Adopted preview objects are never passed to Destroy or the prefab pool,
        // even if teardown already restored the registry in an earlier callback.
        return true;
    }

    private void UnregisterRuntimeAgent(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        RuntimeAgentRecord record;
        if (runtimeAgentRecords.TryGetValue(key, out record) && record != null && record.IsAdoptedPreview)
        {
            TryReleaseAdoptedPreview(key, record.Root);
        }

        runtimeAgentRecords.Remove(key);
        adoptedPreviewKeysByRuntimeKey.Remove(key);
        missingAgentTickCounts.Remove(key);
        lastImportSignatureByName.Remove(key);
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

        GamaLog.Warning(
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
                if (childCollider == null)
                {
                    child.AddComponent<BoxCollider>();
                }
            }

            return;
        }

        Collider collider = obj.GetComponent<Collider>();
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

    private void DrainPrefabPools(bool destroyImmediately = false)
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
                    DestroyManagedRuntimeObject(pooled, destroyImmediately);
                }
            }
        }

        prefabPools.Clear();
        gpuInstancingTouchedMaterials.Clear();
        prefabDistanceCulled.Clear();
        prefabStreamingKeys.Clear();
        if (prefabPoolRoot != null)
        {
            DestroyManagedRuntimeObject(prefabPoolRoot.gameObject, destroyImmediately);
            prefabPoolRoot = null;
        }

        if (runtimeAgentsRoot != null)
        {
            Transform[] ownedParents = new Transform[ownedRuntimeSpeciesParents.Count];
            ownedRuntimeSpeciesParents.CopyTo(ownedParents);
            for (int i = 0; i < ownedParents.Length; i++)
            {
                Transform ownedParent = ownedParents[i];
                if (ownedParent != null && ownedParent.childCount == 0)
                {
                    DestroyManagedRuntimeObject(ownedParent.gameObject, destroyImmediately);
                }
            }

            if (runtimeAgentsRootOwned && runtimeAgentsRoot != null && runtimeAgentsRoot.childCount == 0)
            {
                DestroyManagedRuntimeObject(runtimeAgentsRoot.gameObject, destroyImmediately);
            }
            runtimeAgentsRoot = null;
        }
        runtimeAgentsRootOwned = false;
        ownedRuntimeSpeciesParents.Clear();
        
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

    private static void DestroyManagedRuntimeObject(GameObject obj, bool immediately)
    {
        if (obj == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (immediately)
        {
            UnityEngine.Object.DestroyImmediate(obj);
            return;
        }
#endif
        UnityEngine.Object.Destroy(obj);
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
                !string.Equals(record.AgentId, agentId, StringComparison.Ordinal) ||
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
        bool headingFromMovement = false;

        if (rawHeading == 0 && TryResolveHeadingFromPreviousMovement(agentName, prop, currentPosition, out float movementHeading))
        {
            heading = movementHeading;
            headingFromMovement = true;
        }

        bool hasHeading = headingFromMovement || rawHeading != 0 || Mathf.Abs(heading) > 0.000001f;
        float rotationCoeff = ResolveRuntimeRotationCoeff(prop, hasHeading);
        float rotationOffset = prop != null ? prop.rotationOffsetF : 0f;
        float rotation = rotationCoeff * heading + rotationOffset;
        return Quaternion.AngleAxis(rotation, Vector3.up);
    }

    private Quaternion ResolveGeometryHeadingRotation(
        string agentName,
        PropertiesGAMA prop,
        int[] pointData,
        Vector3 currentAnchor)
    {
        float heading;
        bool hasHeading = TryResolveHeadingFromPreviousGeometryMovement(agentName, currentAnchor, out heading);
        if (!hasHeading)
        {
            hasHeading = TryComputeHeadingFromPolygon(pointData, out heading);
        }

        if (!hasHeading)
        {
            return Quaternion.identity;
        }

        float rotationCoeff = ResolveRuntimeRotationCoeff(prop, true);
        float rotationOffset = prop != null ? prop.rotationOffsetF : 0f;
        float rotation = rotationCoeff * heading + rotationOffset;
        return Quaternion.AngleAxis(rotation, Vector3.up);
    }

    private static float ResolveRuntimeRotationCoeff(PropertiesGAMA prop, bool hasHeading)
    {
        float rotationCoeff = prop != null ? prop.rotationCoeffF : 1f;
        if (hasHeading && Mathf.Abs(rotationCoeff) <= 0.000001f)
        {
            return 1f;
        }

        return rotationCoeff;
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

    private bool TryResolveHeadingFromPreviousGeometryMovement(
        string agentName,
        Vector3 currentAnchor,
        out float heading)
    {
        heading = 0f;
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return false;
        }

        RuntimeAgentRecord record;
        if (!runtimeAgentRecords.TryGetValue(agentName, out record) || record == null || !record.HasVisualAnchor)
        {
            return false;
        }

        return TryComputeHeadingFromDelta(record.VisualAnchor, currentAnchor, out heading);
    }

    private bool TryComputeHeadingFromPolygon(int[] points, out float heading)
    {
        heading = 0f;
        if (points == null || points.Length < 4)
        {
            return false;
        }

        Vector2 bestDelta = Vector2.zero;
        float bestSqrDistance = 0f;
        int pointCount = points.Length / 2;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 a = converter != null
                ? converter.fromGAMACRS2D(points[i * 2], points[i * 2 + 1])
                : new Vector2(points[i * 2], points[i * 2 + 1]);

            for (int j = i + 1; j < pointCount; j++)
            {
                Vector2 b = converter != null
                    ? converter.fromGAMACRS2D(points[j * 2], points[j * 2 + 1])
                    : new Vector2(points[j * 2], points[j * 2 + 1]);

                Vector2 delta = b - a;
                float sqrDistance = delta.sqrMagnitude;
                if (sqrDistance > bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestDelta = delta;
                }
            }
        }

        if (bestSqrDistance <= 0.0001f)
        {
            return false;
        }

        heading = Mathf.Atan2(bestDelta.y, bestDelta.x) * Mathf.Rad2Deg;
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
        Vector3? computedWorldAnchor = null,
        Quaternion? baseRotation = null)
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
        bool keepLogicalRootScaleStable = hasVisualOverridePrefab;
        obj.transform.localScale = keepLogicalRootScaleStable
            ? Vector3.one
            : new Vector3(scale, scale, scale);

        Quaternion visualRotation = (baseRotation.HasValue ? baseRotation.Value : Quaternion.identity) *
                                    Quaternion.Euler(visualState.RotationOffsetEuler);

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
        else if (!hasVisualOverridePrefab)
        {
            Transform staleVisualOverride = obj.transform.Find("VisualOverride");
            if (staleVisualOverride != null)
            {
                staleVisualOverride.gameObject.SetActive(false);
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
                if (sig == null || sig.signature != visualSignature || !sig.hasBaseLocalScale)
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
                    sig.baseLocalScale = visual.transform.localScale;
                    sig.hasBaseLocalScale = true;
                    visualOverride = visual.transform;
                }
                else if (GamaLog.VerboseEnabled)
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
                        GamaLog.DevWarning("[GAMA][RUNTIME][PREFAB] species=" + species +
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
                visualOverride.rotation = visualRotation;
                visualOverride.localScale = ResolveVisualOverrideLocalScale(visualOverride, visualState);

                if (GamaLog.VerboseEnabled)
                {
                    string speciesKey = prop != null ? prop.id : "unknown";
                    if (!debugLogCounts.ContainsKey(speciesKey)) debugLogCounts[speciesKey] = 0;

                    if (debugLogCounts[speciesKey] < 5)
                    {
                        debugLogCounts[speciesKey]++;
                        GamaLog.Dev($"[GAMA][RUNTIME][PREFAB] species={speciesKey} id={obj.name} agentRootPos={obj.transform.position:F3} visualPos={visualOverride.position:F3} scale={visualOverride.localScale:F3} prefab={visualSignature}");
                    }

                    if (!debugSummaryLogged.ContainsKey(speciesKey))
                    {
                        debugSummaryLogged[speciesKey] = true;
                        GamaLog.Dev($"[GAMA][RUNTIME][PREFAB] species={speciesKey} prefab={visualSignature} scale={visualState.ScaleMultiplier}");
                    }

                    if (keepLogicalRootScaleStable)
                    {
                        string scaleLogKey = "visual-scale:" + speciesKey;
                        if (!debugLogCounts.ContainsKey(scaleLogKey)) debugLogCounts[scaleLogKey] = 0;
                        if (debugLogCounts[scaleLogKey] < 5)
                        {
                            debugLogCounts[scaleLogKey]++;
                            GamaLog.Dev($"[GAMA][RUNTIME][SCALE] species={speciesKey} id={obj.name} parentScale={obj.transform.localScale:F3} visualScale={visualOverride.localScale:F3}");
                        }
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
            else
            {
                RestoreRuntimeColor(visualOverride != null ? visualOverride.gameObject : obj);
            }
        }
        else
        {
            RestoreRuntimeColor(visualOverride != null ? visualOverride.gameObject : obj);
        }

        SetRenderersEnabled(obj, visualState.Visible, visualOverride);
    }

    private static Vector3 ResolveVisualOverrideLocalScale(
        Transform visualOverride,
        GamaAgentVisualState visualState)
    {
        GamaRuntimePrefabSignature marker = visualOverride != null
            ? visualOverride.GetComponent<GamaRuntimePrefabSignature>()
            : null;
        Vector3 baseLocalScale = marker != null && marker.hasBaseLocalScale
            ? marker.baseLocalScale
            : Vector3.one;
        return baseLocalScale * Mathf.Max(0f, visualState.ScaleMultiplier);
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
                !RuntimeRecordMatchesSpeciesSelection(record, speciesName))
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

            GamaAgentVisualState visualState = ResolveAgentVisualState(record.AgentId, prop, record.LastAttributes);
            GameObject root = record.Root;
            Vector3 basePosition = ResolveRuntimeBasePosition(record, root);
            Quaternion baseRotation = ResolveRuntimeBaseRotation(record);

            if (prop.hasPrefab)
            {
                string desiredSignature = ResolvePrefabSignature(prop, record.LastAttributes);
                if (NeedsPrefabRebuild(root, desiredSignature))
                {
                    if (toFollow != null && toFollow.Contains(root))
                    {
                        toFollow.Remove(root);
                    }

                    ReleaseRuntimeAgentObject(key, root);
                    root = instantiatePrefab(record.AgentId, key, record.SpeciesName, prop, record.LastAttributes, desiredSignature, initGame: false);
                    entry[0] = root;
                    record.Root = root;
                    record.VisualRoot = ResolveRuntimeVisualRoot(root);
                    record.IsAdoptedPreview = false;
                    record.PreviewReuseKey = string.Empty;
                }

                root.transform.SetPositionAndRotation(
                    basePosition + visualState.PositionOffset,
                    ComposePrefabRuntimeRotation(baseRotation, visualState, root));
                ApplyAgentVisualState(root, prop, visualState, true, Vector3.zero);
            }
            else
            {
                Vector3? visualAnchor = record.HasVisualAnchor ? record.VisualAnchor : (Vector3?)null;
                bool hasInvalidFallback = root.transform.Find("InvalidGeometryFallback") != null;
                if (hasInvalidFallback && visualAnchor.HasValue)
                {
                    basePosition = visualAnchor.Value;
                }

                ApplyAgentVisualState(root, prop, visualState, false, basePosition, visualAnchor, baseRotation);
                if (hasInvalidFallback)
                {
                    HandleInvalidDynamicGeometryFallback(
                        root,
                        record.SpeciesName,
                        visualState,
                        visualAnchor.HasValue ? visualAnchor.Value : basePosition,
                        record.IsDynamic || hasInvalidFallback,
                        true,
                        baseRotation);
                }
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
            lastImportSignatureByName.Remove(key);
            updated++;
        }

        GamaLog.Dev("[GAMA][RUNTIME][OVERRIDE] refreshed species=" + speciesName + " agents=" + updated);
    }

    private static bool RuntimeRecordMatchesSpeciesSelection(RuntimeAgentRecord record, string speciesSelection)
    {
        if (record == null || string.IsNullOrWhiteSpace(speciesSelection))
        {
            return false;
        }

        string wanted = speciesSelection.Trim();
        return string.Equals(record.SpeciesName, wanted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(record.PropertyId, wanted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(record.PropertyTag, wanted, StringComparison.OrdinalIgnoreCase);
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
        bool forceFallback,
        Quaternion baseRotation)
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

            fallbackObj.transform.SetParent(obj.transform, false);
            fallback = fallbackObj.transform;
        }

        SetOriginalGeometryRenderersEnabled(obj.transform, fallback, false);
        bool hasComputedAnchor = computedWorldAnchor.sqrMagnitude > 0.000001f;
        if (hasComputedAnchor)
        {
            obj.transform.position = computedWorldAnchor + visualState.PositionOffset;
        }

        fallback.localPosition = Vector3.zero;
        fallback.rotation = baseRotation * Quaternion.Euler(visualState.RotationOffsetEuler);
        fallback.localScale = Vector3.one * 0.5f;
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

    private static void RecenterPolygonMeshForStableScale(GameObject obj, Vector3 worldAnchor)
    {
        if (obj == null)
        {
            return;
        }

        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0 || IsRuntimeAuxiliaryVisual(filter.transform))
            {
                continue;
            }

            Vector3[] vertices = mesh.vertices;
            for (int v = 0; v < vertices.Length; v++)
            {
                vertices[v].x -= worldAnchor.x;
                vertices[v].z -= worldAnchor.z;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }
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
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        string species = string.IsNullOrWhiteSpace(speciesName) ? "unknown" : speciesName.Trim();
        int count = 0;
        invalidGeometryFallbackCounts.TryGetValue(species, out count);
        count++;
        invalidGeometryFallbackCounts[species] = count;

        if (count == 1 || count == 10 || count % 100 == 0)
        {
            GamaLog.DevWarning(
                "[GAMA][RUNTIME][GEOMETRY] species=" + species +
                " invalidPolygonFallback=" + count);
        }
    }

    private static void SetRenderersEnabled(GameObject obj, bool visible, Transform visualOverride)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            GamaRuntimeRendererAppearanceBaseline baseline =
                renderer.GetComponent<GamaRuntimeRendererAppearanceBaseline>();
            if (baseline == null)
            {
                baseline = renderer.gameObject.AddComponent<GamaRuntimeRendererAppearanceBaseline>();
            }
            baseline.Capture(renderer);

            if (visualOverride != null)
            {
                if (renderer.transform == visualOverride || renderer.transform.IsChildOf(visualOverride))
                {
                    if (visible)
                    {
                        baseline.RestoreRendererState(renderer);
                    }
                    else
                    {
                        renderer.enabled = false;
                    }
                }
                else
                {
                    renderer.enabled = false;
                }
            }
            else if (visible)
            {
                baseline.RestoreRendererState(renderer);
            }
            else
            {
                renderer.enabled = false;
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
        if (!GamaLog.VerboseEnabled || !logPrefabStreamingStats)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now - prefabStreamingLastDiagTime < prefabStreamingStatsInterval)
        {
            return;
        }

        prefabStreamingLastDiagTime = now;
        GamaLog.Dev(
            "[GAMA] Prefab streaming tick: evaluated=" + processedThisTick +
            " round_robin_total=" + totalPrefabAgents +
            " budget=" + prefabStreamingBudgetPerTick +
            " pooling=" + enablePrefabPooling +
            " render_dist=" + enablePrefabRenderDistance);
    }

    private void EmitAgentUpdateBudgetDiagnostic(int processedThisTick, int totalAgents, int nextAgentIndex)
    {
        if (!GamaLog.VerboseEnabled || !logAgentUpdateBudgetStats)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now - agentUpdateBudgetLastDiagTime < agentUpdateBudgetStatsInterval)
        {
            return;
        }

        agentUpdateBudgetLastDiagTime = now;
        GamaLog.Dev(
            "[GAMA] Agent update budget tick: processed=" + processedThisTick +
            " total=" + totalAgents +
            " next_index=" + nextAgentIndex +
            " max_per_tick=" + maxAgentUpdatesPerTick);
    }

    private void EmitRuntimeSyncSummaryIfNeeded()
    {
        if (!GamaLog.VerboseEnabled || !logAgentUpdateBudgetStats || runtimeSyncCountersBySpecies.Count == 0)
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
            GamaLog.Dev(
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
            GamaLog.Warning("[GAMA] Streaming culling disabled because Camera.main is missing. Tag the runtime game camera as MainCamera.");
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
            if (toFollow.Contains(obj))
                toFollow.Remove(obj);
            ReleaseRuntimeAgentObject(id, obj);
            UnregisterRuntimeAgent(id);
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
        SyncConnectionIdFromManager();
        ConnectionManager manager = subscribedConnectionManager != null
            ? subscribedConnectionManager
            : ConnectionManager.Instance;

        // player has been added to the simulation by the middleware
        if (state == ConnectionState.AUTHENTICATED)
        {
            previewReuseConnectionWasAuthenticated = true;
            if (manager != null && !manager.IsCurrentPlayerAuthenticated)
            {
                runtimePlayerBootstrapConfirmed = false;
                UpdateGameState(GameState.WAITING);
                TryBootstrapRuntimePlayer();
            }
            else
            {
                runtimePlayerBootstrapConfirmed = true;
                GamaLog.Info("[GAMA] Loading simulation data.");
                UpdateGameState(GameState.LOADING_DATA);
            }
        }
        else if (state == ConnectionState.CONNECTED)
        {
            bool returnedFromAuthenticatedState = previewReuseConnectionWasAuthenticated;
            if (returnedFromAuthenticatedState)
            {
                RevokePreviewReuseForConnectionChange();
            }
            runtimePlayerBootstrapConfirmed = false;
            runtimePlayerBootstrapAttempts = 0;
            nextRuntimePlayerBootstrapTime = 0f;
            if (returnedFromAuthenticatedState)
            {
                loadedAlready = false;
                GamaLog.Info("[GAMA] Experiment connection ended; waiting to authenticate the next runtime session.");
                UpdateGameState(GameState.WAITING);
            }
            else if (IsGameState(GameState.MENU))
            {
                GamaLog.Info("[GAMA] Connected to simple.webplatform.");
                UpdateGameState(GameState.WAITING);
            }
        }
        else if (state == ConnectionState.DISCONNECTED)
        {
            RevokePreviewReuseForConnectionChange();
            GamaLog.Info("[GAMA] Disconnected from simple.webplatform.");            runtimePlayerBootstrapConfirmed = false;
            runtimePlayerBootstrapAttempts = 0;
            nextRuntimePlayerBootstrapTime = 0f;
            loadedAlready = false;
            UpdateGameState(GameState.MENU);
        }
    }

    private void RevokePreviewReuseForConnectionChange()
    {
        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < sessions.Length; i++)
        {
            if (sessions[i] != null)
            {
                sessions[i].ClearRuntimeReuseAuthorization();
            }
        }

        PrepareForEditorPlayExit();

        // Reuse is a one-shot authorization made before this Play session. A
        // reconnect or experiment restart must build fresh runtime objects until
        // the next explicit Play launch validates the experiment again.
        previewReuseInitializationAttempted = true;
        previewReuseConnectionWasAuthenticated = false;
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
        SyncConnectionIdFromManager();
        GamaLog.Dev("[GAMA][RUNTIME][CONNECTION] subscribed to ConnectionManager");
        if (subscribedConnectionManager.IsConnectionState(ConnectionState.AUTHENTICATED))
        {
            HandleConnectionStateChanged(ConnectionState.AUTHENTICATED);
        }
        else if (subscribedConnectionManager.IsConnectionState(ConnectionState.CONNECTED))
        {
            HandleConnectionStateChanged(ConnectionState.CONNECTED);
        }
        return true;
    }

    private void SyncConnectionIdFromManager()
    {
        ConnectionManager manager = subscribedConnectionManager != null
            ? subscribedConnectionManager
            : ConnectionManager.Instance;
        string id = manager != null ? manager.GetConnectionId() : StaticInformation.getId();
        if (!string.IsNullOrWhiteSpace(id))
        {
            connectionID["id"] = id;
        }
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

    private bool CanSendRuntimeAsk(string sendLabel, string action = null)
    {
        TrySubscribeConnectionManager();
        ConnectionManager manager = ConnectionManager.Instance;
        if (manager != null && manager.CanSendRuntimeMessages)
        {
            return true;
        }

        if (GamaLog.VerboseEnabled)
        {
            float now = Time.unscaledTime;
            if (now >= nextSocketClosedWarningTime)
            {
                string reason = manager == null ? "connection_manager_missing" : "socket_not_open";
                if (!string.IsNullOrWhiteSpace(action))
                {
                    GamaLog.DevWarning("[GAMA][OUT][SKIP] reason=" + reason + " action=" + action);
                }
                else
                {
                    GamaLog.DevWarning("[GAMA][RUNTIME][CONNECTION] " + reason + "; skipping " + sendLabel + " send");
                }
                nextSocketClosedWarningTime = now + SocketClosedWarningIntervalSeconds;
            }
        }

        return false;
    }

    private bool TrySendExecutableAsk(string action, Dictionary<string, string> arguments, string sendLabel)
    {
        if (!CanSendRuntimeAsk(sendLabel, action))
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
        GamaLog.Dev("[GAMA][RUNTIME] Static preview hidden after live runtime data arrived.");
    }

    private void RestoreStaticPreviewHiddenByRuntimeData()
    {
        if (!staticPreviewHiddenAfterRuntimeData)
        {
            return;
        }

        GamaPreviewSession[] sessions = UnityEngine.Object.FindObjectsByType<GamaPreviewSession>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < sessions.Length; i++)
        {
            GamaPreviewSession session = sessions[i];
            if (session == null ||
                session.gameObject == null ||
                session.gameObject.name != "[GAMA] Static Experiment Preview")
            {
                continue;
            }

            if (!session.gameObject.activeSelf)
            {
                session.gameObject.SetActive(true);
            }
        }
    }

    private void LogRuntimeFlow(WorldJSONInfo world)
    {
        if (!GamaLog.VerboseEnabled)
        {
            return;
        }

        runtimeFlowLogCount++;
        if (runtimeFlowLogCount > 20 && runtimeFlowLogCount % 100 != 0)
        {
            return;
        }

        int names = world != null && world.names != null ? world.names.Count : 0;
        int propertyIds = world != null && world.propertyID != null ? world.propertyID.Count : 0;
        GamaLog.Dev("[GAMA][RUNTIME][FLOW] received json_output names=" + names + " propertyIDs=" + propertyIds);
    }

    private RuntimeImportProfile AnalyzeRuntimeImport(WorldJSONInfo world, int messageBytes, long parseMs)
    {
        RuntimeImportProfile profile = new RuntimeImportProfile
        {
            IsInit = world != null && world.isInit,
            MessageBytes = messageBytes,
            NamesCount = world != null && world.names != null ? world.names.Count : 0,
            PointsLocCount = world != null && world.pointsLoc != null ? world.pointsLoc.Count : 0,
            PointsGeomCount = world != null && world.pointsGeom != null ? world.pointsGeom.Count : 0,
            ParseMs = parseMs
        };

        if (world != null && world.propertyID != null)
        {
            for (int i = 0; i < world.propertyID.Count; i++)
            {
                string propertyId = string.IsNullOrWhiteSpace(world.propertyID[i]) ? "unknown" : world.propertyID[i];
                int count;
                profile.CountsByPropertyId.TryGetValue(propertyId, out count);
                profile.CountsByPropertyId[propertyId] = count + 1;
            }
        }

        profile.IsLarge =
            messageBytes >= hugeMessageByteThreshold ||
            profile.NamesCount >= largeGeometryThreshold ||
            profile.PointsLocCount >= largeGeometryThreshold ||
            profile.PointsGeomCount >= largeGeometryThreshold ||
            HasLargeSpecies(profile);

        LogRuntimeImportProfile(profile);
        return profile;
    }

    private bool HasLargeSpecies(RuntimeImportProfile profile)
    {
        if (profile == null)
        {
            return false;
        }

        foreach (KeyValuePair<string, int> pair in profile.CountsByPropertyId)
        {
            if (pair.Value >= largeSpeciesThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private void LogRuntimeImportProfile(RuntimeImportProfile profile)
    {
        if (!GamaLog.VerboseEnabled || profile == null)
        {
            return;
        }

        runtimePerfLogCount++;
        bool shouldLog =
            runtimePerfLogCount <= 5 ||
            runtimePerfLogCount % 100 == 0 ||
            (profile.IsLarge && runtimePerfLogCount % 10 == 0);
        if (!shouldLog)
        {
            return;
        }

        GamaLog.Dev(
            "[GAMA][PERF][STREAM] isInit=" + profile.IsInit +
            " bytes=" + profile.MessageBytes +
            " names=" + profile.NamesCount +
            " pointsLoc=" + profile.PointsLocCount +
            " pointsGeom=" + profile.PointsGeomCount +
            " large=" + profile.IsLarge);

        foreach (KeyValuePair<string, int> pair in profile.CountsByPropertyId)
        {
            if (profile.IsLarge || pair.Value >= largeSpeciesThreshold)
            {
                GamaLog.Dev("[GAMA][PERF][SPECIES] propertyID=" + pair.Key + " count=" + pair.Value + " mode=" + largeSpeciesMode);
            }
        }

        GamaLog.Dev("[GAMA][PERF][JSON] parseMs=" + profile.ParseMs + " applyMs=0");
    }

    private void BeginImportApplyIfNeeded()
    {
        if (currentImportProfile != null && currentImportProfile.ApplyStartedAt < 0f)
        {
            currentImportProfile.ApplyStartedAt = Time.realtimeSinceStartup;
        }
    }

    private void CompleteImportProfileIfNeeded(bool completed)
    {
        if (!completed || currentImportProfile == null)
        {
            return;
        }

        long applyMs = currentImportProfile.ApplyStartedAt >= 0f
            ? (long)((Time.realtimeSinceStartup - currentImportProfile.ApplyStartedAt) * 1000f)
            : 0L;

        if (GamaLog.VerboseEnabled)
        {
            foreach (KeyValuePair<string, RuntimeImportCounters> pair in currentImportProfile.ImportCountersByPropertyId)
            {
                RuntimeImportCounters counters = pair.Value;
                GamaLog.Dev(
                    "[GAMA][PERF][IMPORT] propertyID=" + pair.Key +
                    " created=" + counters.Created +
                    " updated=" + counters.Updated +
                    " skippedUnchanged=" + counters.SkippedUnchanged +
                    " deferred=" + counters.Deferred);
            }
        }

        if (currentImportProfile.IsInit)
        {
            GamaLog.Info(
                "[GAMA] Initial import complete: " + currentImportProfile.NamesCount +
                " agent(s), " + currentImportProfile.PointsGeomCount +
                " geometries, " + currentImportProfile.PointsLocCount +
                " prefab position(s).");
        }

        if (GamaLog.VerboseEnabled)
        {
            GamaLog.Dev("[GAMA][PERF][JSON] parseMs=" + currentImportProfile.ParseMs + " applyMs=" + applyMs);
        }
        currentImportProfile = null;
    }

    private RuntimeImportCounters GetRuntimeImportCounters(string propertyId)
    {
        if (currentImportProfile == null)
        {
            return detachedRuntimeImportCounters;
        }

        string key = string.IsNullOrWhiteSpace(propertyId) ? "unknown" : propertyId;
        RuntimeImportCounters counters;
        if (!currentImportProfile.ImportCountersByPropertyId.TryGetValue(key, out counters) || counters == null)
        {
            counters = new RuntimeImportCounters();
            currentImportProfile.ImportCountersByPropertyId[key] = counters;
        }

        return counters;
    }

    private void MarkRuntimeImportDeferred(int deferred)
    {
        if (currentImportProfile == null)
        {
            return;
        }

        foreach (KeyValuePair<string, RuntimeImportCounters> pair in currentImportProfile.ImportCountersByPropertyId)
        {
            pair.Value.Deferred = Mathf.Max(pair.Value.Deferred, deferred);
        }
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
            GamaRuntimeRendererAppearanceBaseline baseline =
                renderer.GetComponent<GamaRuntimeRendererAppearanceBaseline>();
            if (baseline == null)
            {
                baseline = renderer.gameObject.AddComponent<GamaRuntimeRendererAppearanceBaseline>();
            }
            baseline.Capture(renderer);
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

            Material[] indexedMaterials = renderer.sharedMaterials;
            for (int m = 0; m < indexedMaterials.Length; m++)
            {
                Material material = indexedMaterials[m];
                MaterialPropertyBlock indexedBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(indexedBlock, m);
                bool indexedApplied = false;
                for (int c = 0; c < colorIds.Length; c++)
                {
                    int propertyId = colorIds[c];
                    if (material != null && material.HasProperty(propertyId))
                    {
                        indexedBlock.SetColor(propertyId, color);
                        indexedApplied = true;
                    }
                }
                if (!indexedApplied)
                {
                    indexedBlock.SetColor(colorIds[1], color);
                }
                renderer.SetPropertyBlock(indexedBlock, m);
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
                    int messageBytes = string.IsNullOrEmpty(content) ? 0 : System.Text.Encoding.UTF8.GetByteCount(content);
                    System.Diagnostics.Stopwatch parseWatch = System.Diagnostics.Stopwatch.StartNew();
                    infoWorld = WorldJSONInfo.CreateFromJSON(content);
                    parseWatch.Stop();
                    currentImportProfile = AnalyzeRuntimeImport(infoWorld, messageBytes, parseWatch.ElapsedMilliseconds);
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
                GamaLog.Dev("[GAMA] Connected to middleware");
                UpdateGameState(GameState.WAITING);
            }

            nextRuntimePlayerBootstrapTime = 0f;
        }
        else
        {
            // stay in MENU state

        }
    }

    private static void RestoreRuntimeColor(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            GamaRuntimeRendererAppearanceBaseline baseline =
                renderers[i].GetComponent<GamaRuntimeRendererAppearanceBaseline>();
            baseline?.Restore(renderers[i]);
        }
    }

    private void TryBootstrapRuntimePlayer()
    {
        if (runtimePlayerBootstrapConfirmed)
        {
            return;
        }

        TrySubscribeConnectionManager();
        ConnectionManager manager = ConnectionManager.Instance;
        if (manager == null || !manager.CanSendRuntimeMessages)
        {
            return;
        }

        if (manager.IsCurrentPlayerAuthenticated)
        {
            runtimePlayerBootstrapConfirmed = true;
            if (!IsGameState(GameState.LOADING_DATA) && !IsGameState(GameState.GAME))
            {
                UpdateGameState(GameState.LOADING_DATA);
            }
            return;
        }

        if (!manager.IsConnectionState(ConnectionState.CONNECTED) && !manager.IsConnectionState(ConnectionState.AUTHENTICATED))
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextRuntimePlayerBootstrapTime)
        {
            return;
        }

        if (runtimePlayerBootstrapAttempts >= RuntimePlayerBootstrapMaxAttempts)
        {
            if (GamaLog.VerboseEnabled && runtimePlayerBootstrapAttempts == RuntimePlayerBootstrapMaxAttempts)
            {
                runtimePlayerBootstrapAttempts++;
                GamaLog.DevWarning("[GAMA][RUNTIME][BOOTSTRAP] create_player did not authenticate after " +
                                 RuntimePlayerBootstrapMaxAttempts + " attempts. Check simple.webplatform/GAMA logs.");
            }
            return;
        }

        string id = manager.GetConnectionId();
        if (string.IsNullOrWhiteSpace(id))
        {
            id = StaticInformation.getId();
        }

        connectionID["id"] = id;
        string expression = "do create_player(\"" + EscapeGamlString(id) + "\");";
        runtimePlayerBootstrapAttempts++;
        nextRuntimePlayerBootstrapTime = now + RuntimePlayerBootstrapRetrySeconds;
        GamaLog.Dev("[GAMA][RUNTIME][BOOTSTRAP] create_player attempt " + runtimePlayerBootstrapAttempts +
                  "/" + RuntimePlayerBootstrapMaxAttempts + " id=" + id);
        manager.SendExecutableExpression(expression);
    }

    private static string EscapeGamlString(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
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
    public Vector3 baseLocalScale = Vector3.one;
    public bool hasBaseLocalScale;
}

[DisallowMultipleComponent]
public sealed class GamaRuntimeRendererAppearanceBaseline : MonoBehaviour
{
    [NonSerialized] private bool captured;
    [NonSerialized] private bool hadPropertyBlock;
    [NonSerialized] private MaterialPropertyBlock propertyBlock;
    [NonSerialized] private bool rendererEnabled;
    [NonSerialized] private UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
    [NonSerialized] private bool receiveShadows;
    [NonSerialized] private bool[] hadMaterialPropertyBlocks;
    [NonSerialized] private MaterialPropertyBlock[] materialPropertyBlocks;

    public void Capture(Renderer renderer)
    {
        if (captured || renderer == null)
        {
            return;
        }

        propertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        hadPropertyBlock = !propertyBlock.isEmpty;
        int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
        hadMaterialPropertyBlocks = new bool[materialCount];
        materialPropertyBlocks = new MaterialPropertyBlock[materialCount];
        for (int i = 0; i < materialCount; i++)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, i);
            hadMaterialPropertyBlocks[i] = !block.isEmpty;
            materialPropertyBlocks[i] = block;
        }
        rendererEnabled = renderer.enabled;
        shadowCastingMode = renderer.shadowCastingMode;
        receiveShadows = renderer.receiveShadows;
        captured = true;
    }

    public void Restore(Renderer renderer)
    {
        if (!captured || renderer == null)
        {
            return;
        }

        renderer.SetPropertyBlock(hadPropertyBlock ? propertyBlock : null);
        int materialCount = renderer.sharedMaterials != null ? renderer.sharedMaterials.Length : 0;
        for (int i = 0; i < materialCount; i++)
        {
            bool hadBlock = hadMaterialPropertyBlocks != null &&
                            i < hadMaterialPropertyBlocks.Length &&
                            hadMaterialPropertyBlocks[i];
            MaterialPropertyBlock block = materialPropertyBlocks != null &&
                                          i < materialPropertyBlocks.Length
                ? materialPropertyBlocks[i]
                : null;
            renderer.SetPropertyBlock(hadBlock ? block : null, i);
        }
    }

    public void RestoreRendererState(Renderer renderer)
    {
        if (!captured || renderer == null)
        {
            return;
        }

        renderer.enabled = rendererEnabled;
        renderer.shadowCastingMode = shadowCastingMode;
        renderer.receiveShadows = receiveShadows;
    }
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
