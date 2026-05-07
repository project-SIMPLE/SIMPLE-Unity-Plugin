using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
#if UNITY_EDITOR
using UnityEditor;
#endif

<<<<<<< HEAD
/// <summary>
/// Ordre &gt; <see cref="ConnectionManager"/> pour que l’instance websocket existe déjà en <c>Awake</c>/<c>OnEnable</c>.
/// </summary>
[DefaultExecutionOrder(10)]
public class SimulationManager : MonoBehaviour
=======
public abstract partial class SimulationManager : MonoBehaviour
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
{
    [Serializable]
    private class ManualPrefabOverride
    {
        public string label = "Override";
        public bool enabled = true;
        public string propertyIdEquals;
        public string propertyIdContains;
        public string tagContains;
        public string nameContains;
        public GameObject prefab;

        public bool Matches(PropertiesGAMA prop, string objectName)
        {
            if (!enabled || prop == null || prefab == null)
            {
                return false;
            }

            string propId = prop.id ?? string.Empty;
            string tag = prop.tag ?? string.Empty;
            string name = objectName ?? string.Empty;

            if (!string.IsNullOrEmpty(propertyIdEquals) &&
                !propId.Equals(propertyIdEquals, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(propertyIdContains) &&
                propId.IndexOf(propertyIdContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(tagContains) &&
                tag.IndexOf(tagContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(nameContains) &&
                name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return !string.IsNullOrEmpty(propertyIdEquals) ||
                   !string.IsNullOrEmpty(propertyIdContains) ||
                   !string.IsNullOrEmpty(tagContains) ||
                   !string.IsNullOrEmpty(nameContains);
        }
    }

    [Serializable]
    private class SpeciesVisualOverride
    {
        public string label = "Species override";
        public bool enabled = true;
        [Tooltip("Id d'espece/propriete GAMA (ex: people, car, bus).")]
        public string speciesId;
        [Tooltip("Prefab optionnel pour ecraser le prefab lu depuis GAMA (ou quand il n'y en a pas).")]
        public GameObject prefabOverride;
        [Range(0.01f, 20f)]
        public float scaleMultiplier = 1f;
        public bool overrideColor = false;
        public Color colorOverride = Color.white;

        public bool Matches(PropertiesGAMA prop)
        {
            if (!enabled || prop == null || string.IsNullOrWhiteSpace(speciesId))
            {
                return false;
            }

            return string.Equals(prop.id ?? string.Empty, speciesId, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    private class SpeciesVisualOverrideCache
    {
        public string signature;
        public List<SpeciesVisualOverride> entries = new List<SpeciesVisualOverride>();
    }

    [Header("Debug logs")]
    [SerializeField] private bool verboseMessageLogs = false;
    [SerializeField, Tooltip("Intervalle minimal entre deux logs de traitement pointsLoc (secondes).")]
    private float pointsLocProcessingLogIntervalSeconds = 2f;
    private float _nextPointsLocProcessingLogAt;
    [SerializeField, Tooltip("Active des logs diagnostics sur l'origine des couleurs GAMA.")]
    private bool debugColorMessages = false;
    [SerializeField, Tooltip("Nombre maximum de logs couleur avant arrêt automatique (anti-spam).")]
    private int debugColorMessagesMax = 40;
    private int debugColorMessagesCount = 0;
    [SerializeField] protected InputActionReference primaryRightHandButton = null;

<<<<<<< HEAD
    [Header("Teinte prefab (optionnel, par scène Unity)")]
    [Tooltip("Désactivé par défaut : chaque experiment doit fournir la couleur via GAMA (attributes / properties avec RGB). N’active ceci que dans une scene donnée où le JSON n’a pas de couleur prefab mais tu acceptes une teinte fixe (ex. Traffic).")]
    [SerializeField] bool applyInspectorTintWhenPrefabHasNoGamaColor = false;
    [SerializeField] Color prefabTintWhenGamaOmitsRgb = new Color32(180, 180, 180, 255);

    [Header("Prefab overrides manuels (Inspector Unity)")]
    [Tooltip("Permet de forcer un prefab Unity pour certains agents/propriétés reçus de GAMA. Le premier override qui matche est utilisé.")]
    [SerializeField] private List<ManualPrefabOverride> manualPrefabOverrides = new List<ManualPrefabOverride>();
    [Header("Overrides visuels par espece (Inspector Unity)")]
    [Tooltip("Configuration par espece GAMA: prefab optionnel, multiplicateur d'echelle, couleur d'override.")]
    [SerializeField] private List<SpeciesVisualOverride> speciesVisualOverrides = new List<SpeciesVisualOverride>();
  
=======

>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    [Header("Base GameObjects")]
    [SerializeField] protected GameObject player;
    [SerializeField] protected GameObject Ground;

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
    private static bool warnedMissingConnectionManager;
    [SerializeField, HideInInspector] private string cachedSpeciesSignature = string.Empty;

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

        geometryMap = new Dictionary<string, List<object>>();
    }


    void OnEnable()
    {
        LoadSpeciesOverridesFromCache();
        TrySubscribeConnectionManager();
    }

    void TrySubscribeConnectionManager()
    {
        if (subscribedConnectionManager != null)
        {
            return;
        }

        ConnectionManager cm = ConnectionManager.Instance;
        if (cm == null)
        {
<<<<<<< HEAD
            ConnectionManager[] found = UnityEngine.Object.FindObjectsByType<ConnectionManager>(
                UnityEngine.FindObjectsInactive.Include,
                UnityEngine.FindObjectsSortMode.None);
            if (found != null && found.Length > 0)
            {
                cm = found[0];
            }
        }

        if (cm == null)
        {
            if (!warnedMissingConnectionManager)
            {
                warnedMissingConnectionManager = true;
                Debug.LogWarning("[GAMA] Aucun ConnectionManager actif (Instance null). "
                    + "Vérifie la scène : composant présent sur le même GameObject que le middleware "
                    + "et absence de « Missing (Mono Script) ». Relance après réassignation du script.");
            }

=======
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
            return;
        }

        warnedMissingConnectionManager = false;
        subscribedConnectionManager = cm;
        subscribedConnectionManager.OnServerMessageReceived += HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted += HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged += HandleConnectionStateChanged;
<<<<<<< HEAD
=======

>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    }

    void OnDisable()
    {
<<<<<<< HEAD
=======

>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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

    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            SaveSpeciesOverridesToCache();
        }
    }

    void Start()
    {
        if (geometryMap == null)
        {
            geometryMap = new Dictionary<string, List<object>>();
        }

        handleGeometriesRequested = false;
        // handlePlayerParametersRequested = false;
        handleGroundParametersRequested = false;
        OnEnable();
        TrySubscribeConnectionManager();
    }

    private string GetSpeciesCacheKey()
    {
        Scene s = gameObject.scene;
        string sceneKey = !string.IsNullOrEmpty(s.path) ? s.path : s.name;
        return "GAMA_SPECIES_OVERRIDES::" + GetType().FullName + "::" + sceneKey + "::" + gameObject.name;
    }

    private void SaveSpeciesOverridesToCache()
    {
        SpeciesVisualOverrideCache cache = new SpeciesVisualOverrideCache
        {
            signature = cachedSpeciesSignature,
            entries = speciesVisualOverrides ?? new List<SpeciesVisualOverride>()
        };

        string json = JsonUtility.ToJson(cache);
        PlayerPrefs.SetString(GetSpeciesCacheKey(), json);
        PlayerPrefs.Save();
    }

    private void LoadSpeciesOverridesFromCache()
    {
        string key = GetSpeciesCacheKey();
        if (!PlayerPrefs.HasKey(key))
        {
            return;
        }

        string json = PlayerPrefs.GetString(key);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        SpeciesVisualOverrideCache cache = JsonUtility.FromJson<SpeciesVisualOverrideCache>(json);
        if (cache == null)
        {
            return;
        }

        cachedSpeciesSignature = cache.signature ?? string.Empty;
        speciesVisualOverrides = cache.entries ?? new List<SpeciesVisualOverride>();
    }

    private static string BuildSpeciesSignature(List<PropertiesGAMA> properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return string.Empty;
        }

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < properties.Count; i++)
        {
            PropertiesGAMA p = properties[i];
            if (p == null || string.IsNullOrWhiteSpace(p.id))
            {
                continue;
            }

            ids.Add(p.id.Trim());
        }

        if (ids.Count == 0)
        {
            return string.Empty;
        }

        List<string> ordered = ids.ToList();
        ordered.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("|", ordered);
    }

    private void SyncSpeciesOverridesFromProperties(List<PropertiesGAMA> properties)
    {
        if (properties == null || properties.Count == 0)
        {
            return;
        }

        string newSignature = BuildSpeciesSignature(properties);
        if (string.IsNullOrEmpty(newSignature))
        {
            return;
        }

        bool isNewExperiment = !string.IsNullOrEmpty(cachedSpeciesSignature) &&
                               !string.Equals(cachedSpeciesSignature, newSignature, StringComparison.Ordinal);

        if (string.Equals(cachedSpeciesSignature, newSignature, StringComparison.Ordinal) &&
            speciesVisualOverrides != null &&
            speciesVisualOverrides.Count > 0)
        {
            return;
        }

        HashSet<string> speciesIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < properties.Count; i++)
        {
            PropertiesGAMA p = properties[i];
            if (p != null && !string.IsNullOrWhiteSpace(p.id))
            {
                speciesIds.Add(p.id.Trim());
            }
        }

        List<string> ordered = speciesIds.ToList();
        ordered.Sort(StringComparer.OrdinalIgnoreCase);

        speciesVisualOverrides = new List<SpeciesVisualOverride>();
        for (int i = 0; i < ordered.Count; i++)
        {
            speciesVisualOverrides.Add(new SpeciesVisualOverride
            {
                label = ordered[i],
                enabled = true,
                speciesId = ordered[i],
                prefabOverride = null,
                scaleMultiplier = 1f,
                overrideColor = false,
                colorOverride = Color.white
            });
        }

        cachedSpeciesSignature = newSignature;
        SaveSpeciesOverridesToCache();

        if (isNewExperiment)
        {
            Debug.Log("[GAMA] Nouvel experiment detecte: reset des overrides par espece.");
        }
        else
        {
            Debug.Log("[GAMA] Especes detectees automatiquement: " + ordered.Count + ".");
        }
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
<<<<<<< HEAD
                ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
                Debug.Log("[GAMA] Sending send_init_data (retry)...");
=======
                if (ConnectionManager.Instance != null)
                    ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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
<<<<<<< HEAD
        if (geometryMap == null)
        {
            geometryMap = new Dictionary<string, List<object>>();
        }
=======

        SnapshotPrefabHeadingSources();
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

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
           
<<<<<<< HEAD
            PropertiesGAMA prop = propertyMap[propId];
=======
            PropertiesGAMA prop = null;
            if (propertyMap == null || !propertyMap.TryGetValue(propId, out prop) || prop == null)
            {
                continue;
            }
            Attributes attributes = infoWorld.GetAttributesAt(i);
            GamaAgentVisualState visualState = ResolveAgentVisualState(name, prop, attributes);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

            GameObject obj = null;

            if (prop.hasPrefab)
            {
                if (initGame || !geometryMap.ContainsKey(name))
                {
                    obj = instantiatePrefab(name, prop, initGame);
                }
                else
                {
                    List<object> o = geometryMap[name];
                    GameObject obj2 = (GameObject)o[0];
                    PropertiesGAMA p = (PropertiesGAMA)o[1];
                    if (p == prop)
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

<<<<<<< HEAD
                        GameObject.Destroy(obj2);
                        obj = instantiatePrefab(name, prop, initGame);
=======
                        ReleasePrefabInstance(obj2);
                        obj = instantiatePrefab(name, prop, attributes, desiredPrefabSignature, initGame);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

                    }

                }
                Vector3 unityPosBeforeApply = obj.transform.position;
                List<int> pt = infoWorld.pointsLoc[cptPrefab].c;
                Vector3 pos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                pos.y += prop.yOffsetF;
<<<<<<< HEAD
                Quaternion orientation =
                    ResolvePrefabOrientation(unityPosBeforeApply, pos, pt, prop, skipVelocityInference: initGame, obj);
                obj.transform.SetPositionAndRotation(pos, orientation);
=======
                pos += visualState.PositionOffset;
                Quaternion rotation = ResolvePrefabRotation(name, prop, visualState, pt, pos, obj);
                obj.transform.SetPositionAndRotation(pos, rotation);
                previousPrefabPositions[name] = pos;
                previousPrefabPropertyIds[name] = prop.id ?? string.Empty;
                ApplyAgentVisualState(obj, prop, visualState, true, Vector3.zero);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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

                if(initGame || !geometryMap.ContainsKey(name))
                {
                    obj = polyGen.GeneratePolygons(false, name, pt, prop, parameters.precision);
                    obj.transform.position = new Vector3(obj.transform.position.x, obj.transform.position.y + yOffset, obj.transform.position.z);
                   if(prop.hasCollider)
                    {
                        MeshCollider mc = obj.AddComponent<MeshCollider>();
                        mc.sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
                        if (prop.isGrabable) mc.convex = true;
                    }
                    instantiateGO(obj, name, prop);
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
                
<<<<<<< HEAD
=======
                ApplyAgentVisualState(obj, prop, visualState, false, polygonBasePosition);
                ApplyImmediateStreamingState(obj, prop, immediateStreamingCamera, immediateFrustumEnabled);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
                if(toRemove != null) toRemove.Remove(name);
                cptGeom++;
            }

<<<<<<< HEAD
            ApplyVisualAttributes(i, obj);
            ApplyPrefabInspectorTintIfNeeded(prop, obj, i);
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

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
<<<<<<< HEAD
        if (converter == null || parameters == null || XROrigin == null ||
            ConnectionManager.Instance == null)
        {
            return;
        }

        Camera playCamera = Camera.main;
        if (playCamera == null && player != null)
        {
            playCamera = player.GetComponentInChildren<Camera>();
        }

=======
        if (Camera.main == null || converter == null || parameters == null || XROrigin == null)
        {
            return;
        }
        Vector2 vF = new Vector2(Camera.main.transform.forward.x, Camera.main.transform.forward.z);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
        Vector2 vR = new Vector2(transform.forward.x, transform.forward.z);
        vR.Normalize();
        int angle = 0;

        if (playCamera != null)
        {
            Vector2 vF = new Vector2(playCamera.transform.forward.x, playCamera.transform.forward.z);
            vF.Normalize();
            float c = vF.x * vR.x + vF.y * vR.y;
            float s = vF.x * vR.y - vF.y * vR.x;
            angle = (int)(((s > 0) ? -1.0 : 1.0) * (180 / Math.PI) * Math.Acos(Mathf.Clamp(c, -1f, 1f)) *
                    parameters.precision);
        }

        Vector3 v;
        if (hasSimulator && playCamera != null)
        {
            v = new Vector3(
                playCamera.transform.localPosition.x + XROrigin.localPosition.x,
                playCamera.transform.localPosition.y + XROrigin.localPosition.y,
                playCamera.transform.localPosition.z + XROrigin.localPosition.z);
        }
        else
        {
            v = new Vector3(XROrigin.localPosition.x, XROrigin.localPosition.y, XROrigin.localPosition.z);
        }

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



    private GameObject instantiatePrefab(String name, PropertiesGAMA prop, bool initGame)
    {
        prop.loadPrefab(parameters.precision);
        GameObject speciesPrefab = ResolveSpeciesPrefabOverride(prop);
        GameObject manualPrefab = speciesPrefab != null ? speciesPrefab : ResolveManualPrefabOverride(prop, name);
        GameObject obj = GamaVisualUtility.InstantiateVisual(name, prop, parameters.precision, manualPrefab);

        if (prop.hasCollider)
        {
            if (obj.TryGetComponent<LODGroup>(out var lod))
            {
                foreach (LOD l in lod.GetLODs())
                {
                    GameObject b = l.renderers[0].gameObject;
                    Collider c = b.GetComponent<Collider>();
                    if (c != null && c.bounds.extents.x == 0) 
                        c = null;
                
                    if (c == null)
                    {
                        BoxCollider bc = b.AddComponent<BoxCollider>();
                    }
                    // b.tag = obj.tag;
                    // b.name = obj.name;
                    //bc.isTrigger = prop.isTrigger;
                }
            }
            else
            {
                Collider c = obj.GetComponent<Collider>();
                if (c != null && c.bounds.extents.x == 0) 
                    c = null;
                if (c == null)
                {
                    BoxCollider bc = obj.AddComponent<BoxCollider>();
                }
                // bc.isTrigger = prop.isTrigger;
            }
        }
<<<<<<< HEAD
        List<object> pL = new List<object>();
        pL.Add(obj); pL.Add(prop);
        if (!initGame) geometryMap.Add(name, pL);
        instantiateGO(obj, name, prop);
        ApplySpeciesScaleOverride(obj, prop);
=======

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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

        return obj;
    }

<<<<<<< HEAD
    private SpeciesVisualOverride ResolveSpeciesVisualOverride(PropertiesGAMA prop)
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    {
        if (speciesVisualOverrides == null || speciesVisualOverrides.Count == 0 || prop == null)
        {
            return null;
        }

        for (int i = 0; i < speciesVisualOverrides.Count; i++)
        {
            SpeciesVisualOverride o = speciesVisualOverrides[i];
            if (o != null && o.Matches(prop))
            {
                return o;
            }
        }

        return null;
    }

    private GameObject ResolveSpeciesPrefabOverride(PropertiesGAMA prop)
    {
<<<<<<< HEAD
        SpeciesVisualOverride o = ResolveSpeciesVisualOverride(prop);
        return o != null ? o.prefabOverride : null;
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    }

    private void ApplySpeciesScaleOverride(GameObject obj, PropertiesGAMA prop)
    {
<<<<<<< HEAD
        if (obj == null || prop == null)
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
        {
            return;
        }

<<<<<<< HEAD
        SpeciesVisualOverride o = ResolveSpeciesVisualOverride(prop);
        if (o == null)
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
        {
            return;
        }

<<<<<<< HEAD
        float scale = Mathf.Max(0.01f, o.scaleMultiplier);
        obj.transform.localScale *= scale;
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    }

    private GameObject ResolveManualPrefabOverride(PropertiesGAMA prop, string objectName)
    {
        if (manualPrefabOverrides == null || manualPrefabOverrides.Count == 0 || prop == null)
        {
            return null;
        }

        for (int i = 0; i < manualPrefabOverrides.Count; i++)
        {
            ManualPrefabOverride o = manualPrefabOverrides[i];
            if (o != null && o.Matches(prop, objectName))
            {
                return o.prefab;
            }
        }

        return null;
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
        if (geometryMap == null || infoWorld == null)
        {
            return;
        }

        if (converter == null || propertyMap == null || parameters == null)
        {
            return;
        }

        ManageOtherInformation();
<<<<<<< HEAD
        if (toRemove == null)
        {
            return;
        }

        toRemove.Clear();
        toRemove.UnionWith(geometryMap.Keys);
=======
        if (!pendingWorldUpdateRemovalPass)
        {
            toRemove.Clear();
            toRemove.UnionWith(geometryMap.Keys);
            pendingWorldUpdateRemovalPass = true;
        }
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

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

    Quaternion ResolvePrefabOrientation(Vector3 unityPosBeforeApply, Vector3 targetUnityWorld,
        List<int> pointRow, PropertiesGAMA prop, bool skipVelocityInference, GameObject obj)
    {
        Quaternion visualOffset = GetPrefabVisualOffset(prop);
        Quaternion prefabBaseRotation = GetPrefabBaseRotation(prop);

        Quaternion headingYaw = HeadingYawFromGamaDegrees(parameters, prop, pointRow, obj);
        Quaternion gamaRotation = headingYaw * visualOffset * prefabBaseRotation;

        Vector3 deltaWorld = targetUnityWorld - unityPosBeforeApply;
        deltaWorld.y = 0f;

        float moveSq = Mathf.Max(MinMovementSquaredForPrefabOrientation(parameters), 1e-10f);
        bool preferMovementOrientation = IsVehicleLikePrefab(prop);
        bool movementValid =
            !skipVelocityInference &&
            deltaWorld.sqrMagnitude >= moveSq &&
            (preferMovementOrientation || LooksLikePrefabStepRatherThanTeleport(deltaWorld));

        Quaternion movementRotation = movementValid
            ? Quaternion.LookRotation(deltaWorld.normalized, Vector3.up) * visualOffset * prefabBaseRotation
            : gamaRotation;

        return movementValid ? movementRotation : gamaRotation;
    }

    static Quaternion GetPrefabVisualOffset(PropertiesGAMA prop)
    {
        if (prop == null || prop.prefabObj == null)
        {
            return Quaternion.identity;
        }

        return Quaternion.Euler(0f, prop.rotationOffsetF, 0f);
    }

    static Quaternion GetPrefabBaseRotation(PropertiesGAMA prop)
    {
        return prop != null && prop.prefabObj != null
            ? prop.prefabObj.transform.rotation
            : Quaternion.identity;
    }

    static bool IsVehicleLikePrefab(PropertiesGAMA prop)
    {
        if (prop == null)
        {
            return false;
        }

        string descriptor =
            ((prop.prefab ?? string.Empty) + " " +
             (prop.tag ?? string.Empty) + " " +
             (prop.id ?? string.Empty)).ToLowerInvariant();

        return descriptor.Contains("vehicle") ||
               descriptor.Contains("car") ||
               descriptor.Contains("bus") ||
               descriptor.Contains("truck") ||
               descriptor.Contains("van") ||
               descriptor.Contains("taxi") ||
               descriptor.Contains("bike") ||
               descriptor.Contains("bicycle") ||
               descriptor.Contains("scooter") ||
               descriptor.Contains("moto") ||
               descriptor.Contains("motorcycle") ||
               descriptor.Contains("tram");
    }

    bool LooksLikePrefabStepRatherThanTeleport(Vector3 deltaXZ)
    {
        if (parameters == null || parameters.precision <= 0 || converter == null)
        {
            return deltaXZ.sqrMagnitude < 500f * 500f;
        }

        float worldApprox = Mathf.Max(
            Mathf.Abs(converter.GamaCRSCoefX),
            Mathf.Abs(converter.GamaCRSCoefY),
            Mathf.Abs(converter.GamaCRSCoefZ),
            Mathf.Max(2f / (parameters.precision + 1f), 1e-6f));

        float maxStep = Mathf.Max(worldApprox * (parameters.precision * 12f), 80f);

        return deltaXZ.sqrMagnitude <= maxStep * maxStep;
    }

    Quaternion HeadingYawFromGamaDegrees(ConnectionParameter parameters, PropertiesGAMA prop, List<int> pointRow, GameObject obj)
    {
        int prec = parameters != null && parameters.precision > 0 ? parameters.precision : 1;

        if (pointRow == null || pointRow.Count < 4)
        {
            return Quaternion.identity;
        }

        float rawHeadingDegrees = pointRow[3] / (float)prec;
        float coeff = Mathf.Abs(prop.rotationCoeffF) > 1e-6f ? prop.rotationCoeffF : 1f;

        float yawDegrees = coeff * rawHeadingDegrees;
        return Quaternion.AngleAxis(yawDegrees, Vector3.up);
    }

    static float MinMovementSquaredForPrefabOrientation(ConnectionParameter parameters)
    {
        if (parameters == null || parameters.precision <= 0)
        {
            return 1e-9f;
        }

        float worldUnit = Mathf.Max(2f / (parameters.precision + 1f), 1e-6f);
        float delta = Mathf.Max(worldUnit * 0.25f, 0.0005f);

        return delta * delta;
    }

    void ApplyPrefabInspectorTintIfNeeded(PropertiesGAMA prop, GameObject obj, int rowIndex)
    {
        if (!applyInspectorTintWhenPrefabHasNoGamaColor ||
            obj == null ||
            prop == null ||
            infoWorld == null ||
            !prop.hasPrefab)
        {
            return;
        }

        if (GamaVisualUtility.PropertiesMessageIncludesExplicitTint(prop))
        {
            return;
        }

        if (infoWorld.attributes != null && rowIndex < infoWorld.attributes.Count)
        {
            Attributes attrRow = infoWorld.attributes[rowIndex];
            if (attrRow.TryGetColor(out _))
            {
                return;
            }
        }

        GamaVisualUtility.ApplyColor(obj, prefabTintWhenGamaOmitsRgb);
    }

    protected virtual void ManageAttributes(List<Attributes> attributes)
    {

    }

    private void ApplyVisualAttributes(int attributeIndex, GameObject obj)
    {
        if (obj == null || infoWorld == null || attributeIndex < 0)
        {
            return;
        }

        bool hasAttrRow = infoWorld.attributes != null && attributeIndex < infoWorld.attributes.Count;
        string propId = null;
        PropertiesGAMA propForDebug = null;
        if (propertyMap != null && infoWorld.propertyID != null &&
            attributeIndex < infoWorld.propertyID.Count)
        {
            propId = infoWorld.propertyID[attributeIndex];
            propertyMap.TryGetValue(propId, out propForDebug);
        }

        SpeciesVisualOverride speciesOverride = ResolveSpeciesVisualOverride(propForDebug);
        if (speciesOverride != null && speciesOverride.overrideColor)
        {
            Color32 c = (Color32)speciesOverride.colorOverride;
            GamaVisualUtility.ApplyColor(obj, c);
            LogColorDebug("species-override", obj, propId, propForDebug,
                "Applied from species override: rgba(" + c.r + "," + c.g + "," + c.b + "," + c.a + ").");
            return;
        }

        Color32 color;
        if (hasAttrRow && infoWorld.attributes[attributeIndex] != null &&
            infoWorld.attributes[attributeIndex].TryGetColor(out color))
        {
            GamaVisualUtility.ApplyColor(obj, color);
            LogColorDebug("attributes", obj, propId, propForDebug,
                "Applied from attributes: rgba(" + color.r + "," + color.g + "," + color.b + "," + color.a + ").");
            return;
        }

        if (propForDebug != null && GamaVisualUtility.PropertiesMessageIncludesExplicitTint(propForDebug))
        {
            Color32 propColor = GamaVisualUtility.GetColor(propForDebug);
            GamaVisualUtility.ApplyColor(obj, propColor);
            LogColorDebug("properties", obj, propId, propForDebug,
                "Applied from properties: rgba(" + propColor.r + "," + propColor.g + "," + propColor.b + "," + propColor.a + ").");
            return;
        }

        Attributes attr = hasAttrRow ? infoWorld.attributes[attributeIndex] : null;
        string keys = attr != null ? attr.DebugKeysPreview() : "(null attributes)";
        string pref = propForDebug != null ? (propForDebug.prefab ?? "(no prefab path)") : "(no property)";
        LogColorDebug("missing", obj, propId, propForDebug,
            "No color found in attributes/properties. attr keys: [" + keys + "], prefab: " + pref);
    }

    private void LogColorDebug(string source, GameObject obj, string propId, PropertiesGAMA prop, string details)
    {
        if (!debugColorMessages || debugColorMessagesCount >= Mathf.Max(1, debugColorMessagesMax))
        {
            return;
        }

        debugColorMessagesCount++;
        string objName = obj != null ? obj.name : "(null obj)";
        string pid = !string.IsNullOrEmpty(propId) ? propId : (prop != null ? prop.id : "(null propId)");
        Debug.Log("[GAMA][ColorDebug][" + source + "] obj=" + objName + " propId=" + pid + " -> " + details);

        if (debugColorMessagesCount == debugColorMessagesMax)
        {
            Debug.Log("[GAMA][ColorDebug] Max debug color logs reached (" + debugColorMessagesMax + ").");
        }
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
        else if (state == ConnectionState.DISCONNECTED)
        {
            // Stop runtime update loops that keep trying to send messages while offline.
            readyToSendPosition = false;
            readyToSendPositionInit = false;
            UpdateGameState(GameState.MENU);
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


<<<<<<< HEAD
    private static readonly string[] colorNames = { "_BaseColor", "_Color", "_MainColor", "Color", "BaseColor" };
=======
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

>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
    static public void ChangeColor(GameObject obj, Color color)
    {
        Renderer[] renderers = obj.gameObject.GetComponentsInChildren<Renderer>(true);
        int[] colorIds = ColorPropertyIds;
        for (int i = 0; i < renderers.Length; i++)
        {
<<<<<<< HEAD
            Material mat = renderers[i].material;
           
            foreach (string prop in colorNames)
            { 
                if (mat.HasProperty(prop))
                {
                    mat.SetColor(prop, color);
                    break; 
                }
=======
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
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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
        try
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


        if (ShouldLogProcessingMessage(firstKey))
        {
            Debug.Log("[GAMA] Processing message: " + firstKey + " (state=" + currentState + ")");
        }

        switch (firstKey)
        {
            // handle general informations about the simulation
            case "precision":
                parameters = ConnectionParameter.CreateFromJSON(content);
                converter = new CoordinateConverter(parameters.precision, GamaCRSCoefX, GamaCRSCoefY, GamaCRSCoefY, GamaCRSOffsetX, GamaCRSOffsetY, GamaCRSOffsetZ);
<<<<<<< HEAD
=======
                if (propertiesGAMA != null)
                {
                    ImportAgentProperties(propertiesGAMA.properties, parameters.precision);
                    ImportPrefabProperties(propertiesGAMA.properties);
                }
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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
                    propertyMap.Add(p.id, p);
                }
<<<<<<< HEAD
                SyncSpeciesOverridesFromProperties(propertiesGAMA.properties);
=======
                ImportAgentProperties(propertiesGAMA.properties, parameters != null ? parameters.precision : 1);
                ImportPrefabProperties(propertiesGAMA.properties);
>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)
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
        catch (Exception ex)
        {
            Debug.LogError("[GAMA] Error in HandleServerMessageReceived (key=" + firstKey + "): " + ex.Message + "\n" + ex.StackTrace);
        }
    }

    private bool ShouldLogProcessingMessage(string firstKey)
    {
        if (verboseMessageLogs)
        {
            return true;
        }

        if (string.Equals(firstKey, "pointsLoc", StringComparison.Ordinal))
        {
            if (Time.unscaledTime < _nextPointsLocProcessingLogAt)
            {
                return false;
            }

            _nextPointsLocProcessingLogAt = Time.unscaledTime + Mathf.Max(0.1f, pointsLocProcessingLogIntervalSeconds);
            return true;
        }

        return true;
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

<<<<<<< HEAD
=======
[DisallowMultipleComponent]
public class GamaRuntimePrefabSignature : MonoBehaviour
{
    public string signature;
    public Quaternion baseRotation = Quaternion.identity;
}

>>>>>>> c23b158 (fix(unity-package): stabiliser le streaming/culling et supprimer les flickers agents)

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
