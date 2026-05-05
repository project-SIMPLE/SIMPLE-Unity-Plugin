using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// Ordre &gt; <see cref="ConnectionManager"/> pour que l’instance websocket existe déjà en <c>Awake</c>/<c>OnEnable</c>.
/// </summary>
[DefaultExecutionOrder(10)]
public class SimulationManager : MonoBehaviour
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
  
    [Header("Base GameObjects")]
    [SerializeField] protected GameObject player;
    [SerializeField] protected GameObject Ground;


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
        Debug.Log("Simulation Manager");
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

            return;
        }

        warnedMissingConnectionManager = false;
        subscribedConnectionManager = cm;
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
        Debug.Log("SimulationManager: OnDestroy");
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
                ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
                Debug.Log("[GAMA] Sending send_init_data (retry)...");
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
            if (currentTimePing <= 0)
            {
                Debug.Log("Try to reconnect to the server");
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


    void GenerateGeometries(bool initGame, HashSet<string> toRemove)
    {
        if (geometryMap == null)
        {
            geometryMap = new Dictionary<string, List<object>>();
        }

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

        int cptPrefab = 0;
        int cptGeom = 0;
     
        for (int i = 0; i < infoWorld.names.Count; i++)
        {
            string name = infoWorld.names[i];
            string propId = infoWorld.propertyID[i];
           
            PropertiesGAMA prop = propertyMap[propId];

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
                        if (toFollow != null && toFollow.Contains(obj2))
                            toFollow.Remove(obj2);

                        GameObject.Destroy(obj2);
                        obj = instantiatePrefab(name, prop, initGame);

                    }

                }
                Vector3 unityPosBeforeApply = obj.transform.position;
                List<int> pt = infoWorld.pointsLoc[cptPrefab].c;
                Vector3 pos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                pos.y += prop.yOffsetF;
                Quaternion orientation =
                    ResolvePrefabOrientation(unityPosBeforeApply, pos, pt, prop, skipVelocityInference: initGame, obj);
                obj.transform.SetPositionAndRotation(pos, orientation);
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
                    if(!initGame) geometryMap.Add(name, new List<object> { obj, prop });
                    
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
                
                if(toRemove != null) toRemove.Remove(name);
                cptGeom++;
            }

            ApplyVisualAttributes(i, obj);
            ApplyPrefabInspectorTintIfNeeded(prop, obj, i);

        }

       
        if (infoWorld.attributes != null && infoWorld.attributes.Count > 0)
            ManageAttributes(infoWorld.attributes);


        if (initGame)
            AdditionalInitAfterGeomLoading();

        infoWorld = null;
    }


    bool loadedAlready = false;

    // ############################################ GAMESTATE UPDATER ############################################
    public void UpdateGameState(GameState newState)
    {

        switch (newState)
        {

            case GameState.MENU:
                Debug.Log("SimulationManager: UpdateGameState -> MENU");
                break;

            case GameState.WAITING:
                Debug.Log("SimulationManager: UpdateGameState -> WAITING");
                break;

            case GameState.LOADING_DATA:
                if (!loadedAlready)
                {
                    Debug.Log("SimulationManager: UpdateGameState -> LOADING_DATA");
                    
                        
                        ConnectionManager.Instance.SendExecutableAsk("send_init_data", connectionID);
                    
                    TimerSendInit = TimeSendInit;
                    loadedAlready = true;
                }
                break;

            case GameState.GAME:
                Debug.Log("SimulationManager: UpdateGameState -> GAME");
                loadedAlready = false;
                
                   
                    ConnectionManager.Instance.SendExecutableAsk("player_ready_to_receive_geometries", connectionID);
                
                break;

            case GameState.END:
                Debug.Log("SimulationManager: UpdateGameState -> END");
                break;

            case GameState.CRASH:
                Debug.Log("SimulationManager: UpdateGameState -> CRASH");
                break;

            default:
                Debug.Log("SimulationManager: UpdateGameState -> UNKNOWN");
                break;
        }

        currentState = newState;
        OnGameStateChanged?.Invoke(currentState);
    }



    // ############################# INITIALIZERS ####################################


    private void InitGroundParameters()
    {
        Debug.Log("GroundParameters : Beginnig ground initialization");
        if (Ground == null)
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
        Debug.Log("SimulationManager: Ground parameters initialized");
    }


    private void UpdateGameToFollowPosition()
    {
        if (toFollow.Count > 0)
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
            {"id",ConnectionManager.Instance.GetConnectionId()  },
            {"x", "" +p[0]},
            {"y", "" +p[1]}, 
            {"z", "" +p[2]},
            {"angle", "" +angle}
        };
        
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
        List<object> pL = new List<object>();
        pL.Add(obj); pL.Add(prop);
        if (!initGame) geometryMap.Add(name, pL);
        instantiateGO(obj, name, prop);
        ApplySpeciesScaleOverride(obj, prop);

        return obj;
    }

    private SpeciesVisualOverride ResolveSpeciesVisualOverride(PropertiesGAMA prop)
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
        SpeciesVisualOverride o = ResolveSpeciesVisualOverride(prop);
        return o != null ? o.prefabOverride : null;
    }

    private void ApplySpeciesScaleOverride(GameObject obj, PropertiesGAMA prop)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        SpeciesVisualOverride o = ResolveSpeciesVisualOverride(prop);
        if (o == null)
        {
            return;
        }

        float scale = Mathf.Max(0.01f, o.scaleMultiplier);
        obj.transform.localScale *= scale;
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
        if (toRemove == null)
        {
            return;
        }

        toRemove.Clear();
        toRemove.UnionWith(geometryMap.Keys);

        // foreach (List<object> obj in geometryMap.Values) {
        //((GameObject) obj[0]).SetActive(false);
        //}
        // toRemove.addAll(toRemoveAfter.k);
        GenerateGeometries(false, toRemove);


        // List<string> ids = new List<string>(geometryMap.Keys);
        foreach (string id in toRemove)
        {
            List<object> o = geometryMap[id];
            GameObject obj = (GameObject)o[0];
            obj.transform.position = new Vector3(0, -100, 0);
            geometryMap.Remove(id);
            if (toFollow.Contains(obj))
                toFollow.Remove(obj);
            GameObject.Destroy(obj);
        }
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
        Debug.Log("HandleConnectionStateChanged: " + state);
        // player has been added to the simulation by the middleware
        if (state == ConnectionState.AUTHENTICATED)
        {
            Debug.Log("SimulationManager: Player added to simulation, waiting for initial parameters");
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
            Debug.Log("No connection manager");
            return;
        }

        subscribedConnectionManager = ConnectionManager.Instance;
        subscribedConnectionManager.OnServerMessageReceived += HandleServerMessageReceived;
        subscribedConnectionManager.OnConnectionAttempted += HandleConnectionAttempted;
        subscribedConnectionManager.OnConnectionStateChanged += HandleConnectionStateChanged;
        Debug.Log("SimulationManager: OnEnable");
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


    private static readonly string[] colorNames = { "_BaseColor", "_Color", "_MainColor", "Color", "BaseColor" };
    static public void ChangeColor(GameObject obj, Color color)
    {
        Renderer[] renderers = obj.gameObject.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].material;
           
            foreach (string prop in colorNames)
            { 
                if (mat.HasProperty(prop))
                {
                    mat.SetColor(prop, color);
                    break; 
                }
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

                if ( parameters.cameraclippingfar != -1) Camera.main.farClipPlane = Convert.ToSingle(parameters.cameraclippingfar);

                if (parameters.cameraclippingnear != -1) Camera.main.nearClipPlane = Convert.ToSingle(parameters.cameraclippingnear);


                break;

            case "properties":
                propertiesGAMA = AllProperties.CreateFromJSON(content);
                propertyMap = new Dictionary<string, PropertiesGAMA>();
                foreach (PropertiesGAMA p in propertiesGAMA.properties)
                {
                    propertyMap.Add(p.id, p);
                }
                SyncSpeciesOverridesFromProperties(propertiesGAMA.properties);
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
        Debug.Log("SimulationManager: Connection attempt " + (success ? "successful" : "failed"));
        if (success)
        {
            if (IsGameState(GameState.MENU))
            {
                Debug.Log("SimulationManager: Successfully connected to middleware");
                UpdateGameState(GameState.WAITING);
            }
        }
        else
        {
            // stay in MENU state
            Debug.Log("Unable to connect to middleware");
        }
    }

    private void TryReconnect()
    {
        ConnectionManager.Instance.SendExecutableAsk("ping_GAMA", connectionID);

        currentTimePing = maxTimePing;
        Debug.Log("Sent Ping test");

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
