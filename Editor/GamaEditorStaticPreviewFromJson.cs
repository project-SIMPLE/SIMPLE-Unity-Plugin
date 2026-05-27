using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a hierarchy under a preview root using the same data shape as a live GAMA first frame
/// (precision + properties + world / pointsLoc JSON), so agents use real CRS positions and prefabs.
/// </summary>
internal static class GamaEditorStaticPreviewFromJson
{
    private const bool VerbosePreviewBuildDebug = false;
    private static readonly Dictionary<string, int> InvalidGeometryFallbackCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OverridePickLogKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static bool TryBuild(
        SimulationManager simulationManager,
        string precisionJson,
        string propertiesJson,
        string worldJson,
        Transform parent,
        out int prefabCount,
        out int geometryCount,
        out string error,
        GamaSpeciesRenderOverrides speciesOverrides = null,
        string modelPath = "",
        string experimentName = "")
    {
        prefabCount = 0;
        geometryCount = 0;
        error = string.Empty;

        Debug.Log("[GAMA][PREVIEW][BUILD] simulationManager=" + (simulationManager == null ? "null" : "ok"));
        Debug.Log("[GAMA][PREVIEW][BUILD] precisionJson length=" + (precisionJson == null ? -1 : precisionJson.Length));
        Debug.Log("[GAMA][PREVIEW][BUILD] propertiesJson length=" + (propertiesJson == null ? -1 : propertiesJson.Length));
        Debug.Log("[GAMA][PREVIEW][BUILD] worldJson length=" + (worldJson == null ? -1 : worldJson.Length));
        Debug.Log("[GAMA][PREVIEW][BUILD] parent=" + (parent == null ? "null" : GetHierarchyPath(parent)));
        Debug.Log("[GAMA][PREVIEW][BUILD] speciesOverrides=" + (speciesOverrides == null ? "null" : "ok"));

        try
        {
            return TryBuildInternal(
                simulationManager,
                precisionJson,
                propertiesJson,
                worldJson,
                parent,
                out prefabCount,
                out geometryCount,
                out error,
                speciesOverrides,
                modelPath,
                experimentName);
        }
        catch (Exception ex)
        {
            error = "Exception pendant la construction de la preview statique : " + ex.Message;
            Debug.LogError("[GAMA][PREVIEW][BUILD] Exception: " + ex);
            return false;
        }
    }

    private static bool TryBuildInternal(
        SimulationManager simulationManager,
        string precisionJson,
        string propertiesJson,
        string worldJson,
        Transform parent,
        out int prefabCount,
        out int geometryCount,
        out string error,
        GamaSpeciesRenderOverrides speciesOverrides,
        string modelPath,
        string experimentName)
    {
        prefabCount = 0;
        geometryCount = 0;
        error = string.Empty;
        OverridePickLogKeys.Clear();

        if (parent == null)
        {
            error = "Parent de preview null : impossible de construire la hiérarchie.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(precisionJson))
        {
            error = "precisionJson vide : impossible de construire la preview.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            error = "propertiesJson vide : impossible de construire la preview.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(worldJson))
        {
            error = "worldJson vide : impossible de construire la preview.";
            return false;
        }

        ConnectionParameter parameters = ConnectionParameter.CreateFromJSON(precisionJson);
        if (parameters == null || parameters.precision <= 0)
        {
            error = "JSON « precision » invalide ou precision <= 0.";
            return false;
        }

        AllProperties allProperties = AllProperties.CreateFromJSON(propertiesJson);
        if (allProperties == null || allProperties.properties == null || allProperties.properties.Count == 0)
        {
            error = "JSON « properties » invalide ou liste vide.";
            return false;
        }

        WorldJSONInfo world = WorldJSONInfo.CreateFromJSON(worldJson);
        bool hasAgents = world != null && world.names != null && world.names.Count > 0;
        bool hasGeometries = world != null && world.pointsGeom != null && world.pointsGeom.Count > 0;
        Debug.Log("[GAMA][PREVIEW][BUILD] world agents=" + (world != null && world.names != null ? world.names.Count : 0) +
                  " geometries=" + (world != null && world.pointsGeom != null ? world.pointsGeom.Count : 0));
        if (world == null || (!hasAgents && !hasGeometries))
        {
            error = "JSON « monde » (pointsLoc / world) invalide ou vide (pas d’agents ni de géométries). Essayez un tick plus tard via le curseur.";
            return false;
        }

        if (!hasAgents)
        {
            Debug.LogWarning("[GAMA] Aperçu statique : aucun agent à ce tick — seules les géométries seront affichées.");
        }

        Dictionary<string, PropertiesGAMA> propertyMap = new Dictionary<string, PropertiesGAMA>();
        for (int i = 0; i < allProperties.properties.Count; i++)
        {
            PropertiesGAMA p = allProperties.properties[i];
            if (p == null || string.IsNullOrEmpty(p.id))
            {
                continue;
            }

            propertyMap[p.id] = p;
        }

        float coefX = 1f;
        float coefY = 1f;
        float offX = 0f;
        float offY = 0f;
        float offZ = 0f;
        if (simulationManager != null)
        {
            TryReadManagerCrs(simulationManager, ref coefX, ref coefY, ref offX, ref offY, ref offZ);
        }
        else
        {
            Debug.LogWarning("[GAMA][PREVIEW][BUILD] Aucun SimulationManager trouvé : CRS par défaut et overrides runtime ignorés.");
        }

        CoordinateConverter converter = new CoordinateConverter(
            parameters.precision,
            coefX,
            coefY,
            coefY,
            offX,
            offY,
            offZ);

        PolygonGenerator.DestroyInstance();
        PolygonGenerator polyGen = PolygonGenerator.GetInstance();
        polyGen.Init(converter);

        if (simulationManager != null)
        {
            simulationManager.ImportAgentProperties(allProperties.properties, parameters.precision);
            simulationManager.ImportPrefabProperties(allProperties.properties);
        }

        int precision = parameters.precision;
        int cptPrefab = 0;
        int cptGeom = 0;
        int builtAgents = 0;
        int skippedAgents = 0;
        int builtGeometries = 0;
        int skippedGeometries = 0;
        Dictionary<string, Transform> speciesParents = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

        int nameCount = hasAgents ? world.names.Count : 0;
        for (int i = 0; i < nameCount; i++)
        {
            try
            {
                string name = world.names[i] ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "unknown_agent_" + i;
                }

                if (world.propertyID == null || i >= world.propertyID.Count)
                {
                    skippedAgents++;
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=propertyID missing");
                    continue;
                }

                string propId = world.propertyID[i];
                PropertiesGAMA prop;
                if (string.IsNullOrEmpty(propId) || !propertyMap.TryGetValue(propId, out prop) || prop == null)
                {
                    skippedAgents++;
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=prop null propId=" + (propId ?? "<null>"));
                    continue;
                }

                Attributes attributes = null;
                try
                {
                    attributes = world.GetAttributesAt(i);
                }
                catch (Exception attrEx)
                {
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Attributes invalid for agent i=" + i + " reason=" + attrEx.Message);
                }

                bool hasVisualState = false;
                GamaAgentVisualState visualState = default;
                try
                {
                    visualState = simulationManager != null
                        ? simulationManager.ResolveVisualState(name, prop, attributes, precision)
                        : SimulationManager.CreateDefaultVisualState(prop, attributes, precision);
                    hasVisualState = true;
                }
                catch (Exception vsEx)
                {
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] ResolveVisualState failed for agent i=" + i + " reason=" + vsEx.Message);
                }
                if (!hasVisualState)
                {
                    try { visualState = SimulationManager.CreateDefaultVisualState(prop, attributes, precision); hasVisualState = true; }
                    catch { /* fallback failed */ }
                }
                if (!hasVisualState)
                {
                    skippedAgents++;
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=visualState failed");
                    continue;
                }

                string speciesKey = null;
                try { speciesKey = GamaEditorPreviewCapture.ResolveSpeciesKey(propId, propertyMap); }
                catch (Exception skEx) { Debug.LogWarning("[GAMA][PREVIEW][BUILD] ResolveSpeciesKey failed i=" + i + " reason=" + skEx.Message); }
                if (string.IsNullOrWhiteSpace(speciesKey)) speciesKey = name;
                if (string.IsNullOrWhiteSpace(speciesKey)) speciesKey = "unknown";

                if (VerbosePreviewBuildDebug && i < 5)
                {
                    Debug.Log(
                        "[GAMA][PREVIEW][BUILD][AGENTDBG] i=" + i +
                        " name=" + (name ?? "<null>") +
                        " propId=" + (propId ?? "<null>") +
                        " prop=" + (prop == null ? "null" : "ok") +
                        " attributes=" + (attributes == null ? "null" : "ok") +
                        " visualState=" + (hasVisualState ? "ok" : "failed") +
                        " speciesKey=" + (speciesKey ?? "<null>") +
                        " speciesOverrides=" + (speciesOverrides == null ? "null" : "ok"));
                }

                Transform speciesParent = GetOrCreateSpeciesParent(parent, speciesKey, speciesOverrides, speciesParents);
                if (speciesParent == null)
                {
                    skippedAgents++;
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=speciesParent null");
                    continue;
                }

                if (prop.hasPrefab)
                {
                    if (world.pointsLoc == null || cptPrefab >= world.pointsLoc.Count)
                    {
                        skippedAgents++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=pointsLoc insufficient");
                        continue;
                    }

                    List<int> pt = world.pointsLoc[cptPrefab] != null ? world.pointsLoc[cptPrefab].c : null;
                    if (pt == null || pt.Count < 3)
                    {
                        skippedAgents++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=invalid coordinates");
                        cptPrefab++;
                        continue;
                    }

                    GameObject obj = GamaVisualUtility.InstantiateVisual(name, prop, precision);
                    if (obj == null)
                    {
                        skippedAgents++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=InstantiateVisual returned null");
                        cptPrefab++;
                        continue;
                    }

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview agent");
                    obj.transform.SetParent(speciesParent, false);

                    Vector3 pos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                    pos.y += prop.yOffsetF;
                    pos += visualState.PositionOffset;
                    Quaternion rotation = ResolvePrefabRotation(prop, visualState, pt, obj, precision);
                    obj.transform.SetPositionAndRotation(pos, rotation);

                    ApplyPrefabVisualState(obj, prop, visualState, precision);
                    GamaPreviewObject marker = AddPreviewObjectIdentity(obj, speciesKey, name, BuildIntListHash(pt));
                    if (marker != null)
                    {
                        marker.SetVisualAnchorLocal(Vector3.zero);
                        marker.CaptureBaseTransformIfNeeded();
                    }
                    if (speciesOverrides != null) { ApplySpeciesOverrideIfAny(marker, speciesKey, speciesOverrides, modelPath, experimentName); }
                    builtAgents++;
                    cptPrefab++;
                }
                else
                {
                    if (world.pointsGeom == null || cptGeom >= world.pointsGeom.Count)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=pointsGeom insufficient");
                        continue;
                    }

                    List<int> rawGeom = world.pointsGeom[cptGeom] != null ? world.pointsGeom[cptGeom].c : null;
                    if (rawGeom == null || rawGeom.Count < 2)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=invalid geometry data");
                        cptGeom++;
                        continue;
                    }

                    int[] ptArr = rawGeom.ToArray();
                    if (ptArr == null || ptArr.Length == 0)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=empty points");
                        cptGeom++;
                        continue;
                    }

                    float yOffsetGeom = 0f;
                    if (world.offsetYGeom != null && cptGeom < world.offsetYGeom.Count)
                    {
                        yOffsetGeom = world.offsetYGeom[cptGeom] / (float)precision;
                    }

                    bool polygonInputValid = IsPreviewPolygonInputValid(rawGeom, converter);
                    if (polygonInputValid && polyGen == null)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=polyGen null");
                        cptGeom++;
                        continue;
                    }

                    Vector3 polygonBasePosition = new Vector3(0f, yOffsetGeom, 0f);
                    GameObject obj = polygonInputValid
                        ? polyGen.GeneratePolygons(true, name, ptArr, prop, precision)
                        : CreateInvalidGeometryFallbackObject(name, speciesKey, rawGeom, converter, yOffsetGeom, visualState);
                    if (obj == null)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=GeneratePolygons returned null");
                        cptGeom++;
                        continue;
                    }

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview geometry");
                    obj.transform.SetParent(speciesParent, false);

                    ApplyPolygonVisualState(obj, prop, visualState, polygonBasePosition);
                    GamaPreviewObject marker = AddPreviewObjectIdentity(obj, speciesKey, name, BuildIntListHash(rawGeom));
                    if (marker != null)
                    {
                        marker.SetVisualAnchorLocal(ResolvePreviewAnchorLocal(obj, rawGeom, converter, yOffsetGeom));
                        marker.CaptureBaseTransformIfNeeded();
                    }
                    if (speciesOverrides != null) { ApplySpeciesOverrideIfAny(marker, speciesKey, speciesOverrides, modelPath, experimentName); }

                    if (prop.hasCollider && obj.GetComponent<MeshCollider>() == null)
                    {
                        MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.sharedMesh != null)
                        {
                            MeshCollider meshCollider = Undo.AddComponent<MeshCollider>(obj);
                            meshCollider.sharedMesh = meshFilter.sharedMesh;
                            if (prop.isGrabable)
                            {
                                meshCollider.convex = true;
                            }
                        }
                    }

                    builtGeometries++;
                    cptGeom++;
                }
            }
            catch (Exception ex)
            {
                skippedAgents++;
                Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " reason=" + ex);
                continue;
            }
        }

        if (!hasAgents && hasGeometries)
        {
            for (int g = 0; g < world.pointsGeom.Count; g++)
            {
                try
                {
                    List<int> rawGeom = world.pointsGeom[g] != null ? world.pointsGeom[g].c : null;
                    if (rawGeom == null || rawGeom.Count < 2)
                    {
                        skippedGeometries++;
                        continue;
                    }

                    PropertiesGAMA prop = allProperties.properties.Count > 0 ? allProperties.properties[0] : null;
                    if (prop == null || prop.hasPrefab)
                    {
                        skippedGeometries++;
                        continue;
                    }

                    string geomName = "Preview_geom_" + g;
                    int[] ptArr = rawGeom.ToArray();
                    if (ptArr == null || ptArr.Length == 0)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=empty points");
                        continue;
                    }

                    float yOffsetGeom = 0f;
                    if (world.offsetYGeom != null && g < world.offsetYGeom.Count)
                    {
                        yOffsetGeom = world.offsetYGeom[g] / (float)precision;
                    }

                    if (polyGen == null)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=polyGen null");
                        continue;
                    }

                    Vector3 polygonBasePosition = new Vector3(0f, yOffsetGeom, 0f);
                    GameObject obj = polyGen.GeneratePolygons(true, geomName, ptArr, prop, precision);
                    if (obj == null)
                    {
                        skippedGeometries++;
                        Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=GeneratePolygons returned null");
                        continue;
                    }

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview geometry");
                    obj.transform.SetParent(parent, false);
                    GamaAgentVisualState defaultVisual = SimulationManager.CreateDefaultVisualState(prop, null, precision);
                    ApplyPolygonVisualState(obj, prop, defaultVisual, polygonBasePosition);
                    builtGeometries++;
                }
                catch (Exception ex)
                {
                    skippedGeometries++;
                    Debug.LogWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=" + ex);
                    continue;
                }
            }
        }

        if (world.position != null && world.position.Count > 2)
        {
            try
            {
                Vector3 playerPos = converter.fromGAMACRS(world.position[0], world.position[1], world.position[2]);
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = "Preview_PlayerSpawn";
                Undo.RegisterCreatedObjectUndo(marker, "GAMA static preview player");
                marker.transform.SetParent(parent, false);
                marker.transform.position = playerPos + Vector3.up * 0.05f;
                marker.transform.localScale = new Vector3(0.35f, 0.02f, 0.35f);
                Collider col = marker.GetComponent<Collider>();
                if (col != null)
                {
                    Undo.DestroyObjectImmediate(col);
                }
                GamaVisualUtility.ApplyColor(marker, new Color32(80, 200, 255, 255));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[GAMA][PREVIEW][BUILD] PlayerSpawn marker failed: " + ex.Message);
            }
        }

        prefabCount = builtAgents;
        geometryCount = builtGeometries;

        Debug.Log(
            "[GAMA][PREVIEW][BUILD] result builtAgents=" + builtAgents +
            " skippedAgents=" + skippedAgents +
            " builtGeometries=" + builtGeometries +
            " skippedGeometries=" + skippedGeometries);

        if (builtAgents == 0 && builtGeometries == 0)
        {
            error = "Aucun objet preview construit. skippedAgents=" + skippedAgents + ", skippedGeometries=" + skippedGeometries;
            return false;
        }

        return true;
    }

    private static void TryReadManagerCrs(
        SimulationManager simulationManager,
        ref float coefX,
        ref float coefY,
        ref float offX,
        ref float offY,
        ref float offZ)
    {
        try
        {
            SerializedObject managerSo = new SerializedObject(simulationManager);
            coefX = ReadFloatProperty(managerSo, "GamaCRSCoefX", coefX);
            coefY = ReadFloatProperty(managerSo, "GamaCRSCoefY", coefY);
            offX = ReadFloatProperty(managerSo, "GamaCRSOffsetX", offX);
            offY = ReadFloatProperty(managerSo, "GamaCRSOffsetY", offY);
            offZ = ReadFloatProperty(managerSo, "GamaCRSOffsetZ", offZ);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA][PREVIEW][BUILD] Lecture CRS SimulationManager impossible, valeurs par défaut utilisées : " + ex.Message);
        }
    }

    private static float ReadFloatProperty(SerializedObject serializedObject, string name, float fallback)
    {
        if (serializedObject == null)
        {
            return fallback;
        }

        SerializedProperty property = serializedObject.FindProperty(name);
        return property != null ? property.floatValue : fallback;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return "(null)";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static Transform GetOrCreateSpeciesParent(
        Transform previewRoot,
        string speciesKey,
        GamaSpeciesRenderOverrides speciesOverrides,
        Dictionary<string, Transform> cache)
    {
        if (previewRoot == null)
        {
            Debug.LogWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent: previewRoot is null");
            return null;
        }

        string key = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();
        if (cache != null && cache.TryGetValue(key, out Transform existing) && existing != null)
        {
            return existing;
        }

        try
        {
            GameObject rootChild = GamaSceneUtility.GetOrCreateChild(previewRoot.gameObject, "GAMA");
            if (rootChild == null)
            {
                Debug.LogWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent: GetOrCreateChild returned null for GAMA root");
                return null;
            }

            Transform gamaRoot = rootChild.transform;
            GameObject speciesGo = new GameObject(key);
            Undo.RegisterCreatedObjectUndo(speciesGo, "GAMA species parent");
            speciesGo.transform.SetParent(gamaRoot, false);
            using (GamaSpeciesWizard.SuppressAssetWrites())
            {
                GamaSpeciesWizard wizard = speciesGo.AddComponent<GamaSpeciesWizard>();
                if (wizard != null)
                {
                    wizard.speciesName = key;
                    wizard.overridesAsset = speciesOverrides;
                }
            }
            if (cache != null) cache[key] = speciesGo.transform;
            return speciesGo.transform;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent failed for key=" + key + " reason=" + ex.Message);
            return null;
        }
    }

    private static void ApplySpeciesOverrideIfAny(
        GamaPreviewObject marker,
        string speciesKey,
        GamaSpeciesRenderOverrides speciesOverrides,
        string modelPath,
        string experimentName)
    {
        if (marker == null || speciesOverrides == null || string.IsNullOrWhiteSpace(speciesKey))
        {
            return;
        }

        if (speciesOverrides.TryGetOverride(modelPath, experimentName, speciesKey, out GamaSpeciesRenderOverrideEntry entry, true) && entry != null)
        {
            LogPreviewOverridePickOnce(speciesKey, modelPath, experimentName, entry);
            marker.ApplySpeciesOverride(entry);
        }
    }

    private static void LogPreviewOverridePickOnce(
        string speciesKey,
        string modelPath,
        string experimentName,
        GamaSpeciesRenderOverrideEntry entry)
    {
        string logKey = GamaSpeciesRenderOverrides.NormalizeKey(modelPath) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(experimentName) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(speciesKey);
        if (!OverridePickLogKeys.Add(logKey))
        {
            return;
        }

        Debug.Log("[GAMA][PREVIEW][OVERRIDE_PICK] species=" + speciesKey +
                  " model=" + (modelPath ?? string.Empty) +
                  " experiment=" + (experimentName ?? string.Empty) +
                  " scale=" + (entry != null ? entry.GetEffectiveScaleMultiplier() : 1f));
    }

    private static GamaPreviewObject AddPreviewObjectIdentity(GameObject obj, string speciesKey, string agentId, string geometryHash)
    {
        if (obj == null)
        {
            return null;
        }

        GamaPreviewObject marker = obj.GetComponent<GamaPreviewObject>();
        if (marker == null)
        {
            marker = obj.AddComponent<GamaPreviewObject>();
        }

        marker.previewOnly = true;
        marker.canBeReusedAtRuntime = false;
        marker.speciesName = speciesKey ?? string.Empty;
        marker.agentId = agentId ?? string.Empty;
        marker.geometryHash = geometryHash ?? string.Empty;
        marker.sourceTick = -1;
        return marker;
    }

    private static Vector3 ResolvePreviewAnchorLocal(
        GameObject obj,
        IList<int> rawGeom,
        CoordinateConverter converter,
        float yOffset)
    {
        if (obj == null)
        {
            return Vector3.zero;
        }

        Vector3 anchor;
        if (TryGetRendererAnchorLocal(obj, out anchor))
        {
            return anchor;
        }

        if (TryGetMeshAnchorLocal(obj, out anchor))
        {
            return anchor;
        }

        if (TryGetRawGeometryAnchorLocal(obj, rawGeom, converter, yOffset, out anchor))
        {
            return anchor;
        }

        return Vector3.zero;
    }

    private static GameObject CreateInvalidGeometryFallbackObject(
        string name,
        string speciesKey,
        IList<int> rawGeom,
        CoordinateConverter converter,
        float yOffset,
        GamaAgentVisualState visualState)
    {
        GameObject root = new GameObject(string.IsNullOrWhiteSpace(name) ? "InvalidGeometryFallback" : name);
        GameObject fallback = GameObject.CreatePrimitive(ResolveFallbackPrimitive(speciesKey));
        fallback.name = "Visual";
        fallback.transform.SetParent(root.transform, false);
        fallback.transform.localPosition = ResolveRawGeometryAnchorLocal(rawGeom, converter, 0f);
        fallback.transform.localRotation = Quaternion.identity;
        fallback.transform.localScale = Vector3.one * 0.5f;

        Collider collider = fallback.GetComponent<Collider>();
        if (collider != null)
        {
            Undo.DestroyObjectImmediate(collider);
        }

        if (visualState.HasColor)
        {
            GamaVisualUtility.ApplyColor(fallback, visualState.Color);
        }

        SetRenderersEnabled(fallback, visualState.Visible);
        LogInvalidGeometryFallback(speciesKey);
        return root;
    }

    private static PrimitiveType ResolveFallbackPrimitive(string speciesKey)
    {
        if (!string.IsNullOrWhiteSpace(speciesKey))
        {
            string lower = speciesKey.ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(
                lower,
                @"predator|prey|people|pedestrian|person|walker|car|vehicle|voiture|human|agent"))
            {
                return PrimitiveType.Capsule;
            }
        }

        return PrimitiveType.Cube;
    }

    private static void LogInvalidGeometryFallback(string speciesKey)
    {
        string species = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();
        int count = 0;
        InvalidGeometryFallbackCounts.TryGetValue(species, out count);
        count++;
        InvalidGeometryFallbackCounts[species] = count;

        if (count == 1 || count == 10 || count % 100 == 0)
        {
            Debug.LogWarning(
                "[GAMA][PREVIEW][GEOMETRY] species=" + species +
                " invalidPolygonFallback=" + count);
        }
    }

    private static bool IsPreviewPolygonInputValid(IList<int> rawGeom, CoordinateConverter converter)
    {
        if (rawGeom == null || rawGeom.Count < 6)
        {
            return false;
        }

        int pointCount = rawGeom.Count / 2;
        if (pointCount < 3)
        {
            return false;
        }

        List<Vector2> cleaned = new List<Vector2>(pointCount);
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 point = converter != null
                ? converter.fromGAMACRS2D(rawGeom[i * 2], rawGeom[i * 2 + 1])
                : new Vector2(rawGeom[i * 2], rawGeom[i * 2 + 1]);

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

    private static Vector3 ResolveRawGeometryAnchorLocal(
        IList<int> rawGeom,
        CoordinateConverter converter,
        float yOffset)
    {
        if (rawGeom == null || rawGeom.Count < 2 || converter == null)
        {
            return Vector3.zero;
        }

        int pointCount = rawGeom.Count / 2;
        if (pointCount <= 0)
        {
            return Vector3.zero;
        }

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 pt = converter.fromGAMACRS2D(rawGeom[i * 2], rawGeom[i * 2 + 1]);
            sum += new Vector3(pt.x, yOffset, pt.y);
        }

        return sum / pointCount;
    }

    private static bool TryGetRendererAnchorLocal(GameObject obj, out Vector3 anchor)
    {
        anchor = Vector3.zero;
        if (obj == null)
        {
            return false;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            if (!hasBounds)
            {
                combined = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        anchor = obj.transform.InverseTransformPoint(combined.center);
        return true;
    }

    private static bool TryGetMeshAnchorLocal(GameObject obj, out Vector3 anchor)
    {
        anchor = Vector3.zero;
        if (obj == null)
        {
            return false;
        }

        MeshFilter[] meshFilters = obj.GetComponentsInChildren<MeshFilter>(true);
        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null || mesh.bounds.size.sqrMagnitude <= 0.000001f)
            {
                continue;
            }

            Bounds worldBounds = new Bounds(
                meshFilter.transform.TransformPoint(mesh.bounds.center),
                Vector3.zero);
            Vector3 ext = mesh.bounds.extents;
            worldBounds.Encapsulate(meshFilter.transform.TransformPoint(mesh.bounds.center + new Vector3(ext.x, ext.y, ext.z)));
            worldBounds.Encapsulate(meshFilter.transform.TransformPoint(mesh.bounds.center + new Vector3(-ext.x, -ext.y, -ext.z)));

            if (!hasBounds)
            {
                combined = worldBounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(worldBounds);
            }
        }

        if (!hasBounds)
        {
            return false;
        }

        anchor = obj.transform.InverseTransformPoint(combined.center);
        return true;
    }

    private static bool TryGetRawGeometryAnchorLocal(
        GameObject obj,
        IList<int> rawGeom,
        CoordinateConverter converter,
        float yOffset,
        out Vector3 anchor)
    {
        anchor = Vector3.zero;
        if (obj == null || rawGeom == null || rawGeom.Count < 2 || converter == null)
        {
            return false;
        }

        int pointCount = rawGeom.Count / 2;
        if (pointCount <= 0)
        {
            return false;
        }

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < pointCount; i++)
        {
            Vector2 pt = converter.fromGAMACRS2D(rawGeom[i * 2], rawGeom[i * 2 + 1]);
            sum += new Vector3(pt.x, yOffset, pt.y);
        }

        Vector3 worldCenter = sum / pointCount;
        anchor = obj.transform.InverseTransformPoint(worldCenter);
        return true;
    }

    private static string BuildIntListHash(IList<int> values)
    {
        if (values == null || values.Count == 0)
        {
            return string.Empty;
        }

        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < values.Count; i++)
            {
                hash ^= (uint)values[i];
                hash *= 16777619;
            }

            return hash.ToString("x8");
        }
    }

    private static Quaternion ResolvePrefabRotation(
        PropertiesGAMA prop,
        GamaAgentVisualState visualState,
        List<int> pointData,
        GameObject prefabInstance,
        int precision)
    {
        int rawHeading = pointData != null && pointData.Count > 3 ? pointData[3] : 0;
        float heading = rawHeading / (float)Mathf.Max(1, precision);
        float rotation = prop.rotationCoeffF * heading + prop.rotationOffsetF;
        Quaternion baseRotation = Quaternion.identity;
        if (prefabInstance != null)
        {
            GamaRuntimePrefabSignature marker = prefabInstance.GetComponent<GamaRuntimePrefabSignature>();
            if (marker != null)
            {
                baseRotation = marker.baseRotation;
            }
        }

        return Quaternion.AngleAxis(rotation, Vector3.up) *
               Quaternion.Euler(visualState.RotationOffsetEuler) *
               baseRotation;
    }

    private static void ApplyPrefabVisualState(
        GameObject obj,
        PropertiesGAMA prop,
        GamaAgentVisualState visualState,
        int precision)
    {
        if (obj == null)
        {
            return;
        }

        float baseScale = prop != null ? prop.GetUnityScale(precision) : 1f;
        float scale = Mathf.Max(0f, baseScale * visualState.ScaleMultiplier);
        obj.transform.localScale = new Vector3(scale, scale, scale);

        if (visualState.HasColor)
        {
            GamaVisualUtility.ApplyColor(obj, visualState.Color);
        }

        SetRenderersEnabled(obj, visualState.Visible);
    }

    private static void ApplyPolygonVisualState(
        GameObject obj,
        PropertiesGAMA prop,
        GamaAgentVisualState visualState,
        Vector3 polygonBasePosition)
    {
        if (obj == null)
        {
            return;
        }

        float scale = Mathf.Max(0f, visualState.ScaleMultiplier);
        obj.transform.localScale = new Vector3(scale, scale, scale);
        obj.transform.position = polygonBasePosition + visualState.PositionOffset;
        obj.transform.rotation = Quaternion.Euler(visualState.RotationOffsetEuler);

        if (visualState.HasColor)
        {
            GamaVisualUtility.ApplyColor(obj, visualState.Color);
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

    public static bool TryReadFile(string path, out string content, out string readError)
    {
        content = null;
        readError = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            readError = "Chemin vide.";
            return false;
        }

        string full = path.Trim().Trim('"');
        if (!File.Exists(full))
        {
            readError = "Fichier introuvable: " + full;
            return false;
        }

        try
        {
            content = File.ReadAllText(full);
            return true;
        }
        catch (System.Exception ex)
        {
            readError = ex.Message;
            return false;
        }
    }
}
