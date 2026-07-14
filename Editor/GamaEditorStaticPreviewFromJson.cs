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
    private const float PreviewSpreadWarnRatio = 1.35f;
    private const float PreviewReferenceOverflowRatio = 0.12f;
    private const float PreviewSpreadEpsilon = 0.0001f;
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

        GamaLog.Dev("[GAMA][PREVIEW][BUILD] simulationManager=" + (simulationManager == null ? "null" : "ok"));
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] precisionJson length=" + (precisionJson == null ? -1 : precisionJson.Length));
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] propertiesJson length=" + (propertiesJson == null ? -1 : propertiesJson.Length));
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] worldJson length=" + (worldJson == null ? -1 : worldJson.Length));
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] parent=" + (parent == null ? "null" : GetHierarchyPath(parent)));
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] speciesOverrides=" + (speciesOverrides == null ? "null" : "ok"));

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
            error = "Exception while building the static preview: " + ex.Message;
            GamaLog.Error("[GAMA][PREVIEW][BUILD] Exception: " + ex);
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
            error = "The preview parent is null; the hierarchy cannot be built.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(precisionJson))
        {
            error = "precisionJson is empty; the preview cannot be built.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(propertiesJson))
        {
            error = "propertiesJson is empty; the preview cannot be built.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(worldJson))
        {
            error = "worldJson is empty; the preview cannot be built.";
            return false;
        }

        ConnectionParameter parameters = ConnectionParameter.CreateFromJSON(precisionJson);
        if (parameters == null || parameters.precision <= 0)
        {
            error = "The precision JSON is invalid or precision is less than or equal to zero.";
            return false;
        }

        AllProperties allProperties = AllProperties.CreateFromJSON(propertiesJson);
        if (allProperties == null || allProperties.properties == null || allProperties.properties.Count == 0)
        {
            error = "The properties JSON is invalid or its list is empty.";
            return false;
        }

        WorldJSONInfo world = WorldJSONInfo.CreateFromJSON(worldJson);
        bool hasAgents = world != null && world.names != null && world.names.Count > 0;
        bool hasGeometries = world != null && world.pointsGeom != null && world.pointsGeom.Count > 0;
        GamaLog.Dev("[GAMA][PREVIEW][BUILD] world agents=" + (world != null && world.names != null ? world.names.Count : 0) +
                  " geometries=" + (world != null && world.pointsGeom != null ? world.pointsGeom.Count : 0));
        if (world == null || (!hasAgents && !hasGeometries))
        {
            error = "The world JSON (pointsLoc/world) is invalid or empty (no agents or geometries). Try a later tick with the slider.";
            return false;
        }

        if (!hasAgents)
        {
            GamaLog.DevWarning("[GAMA] Static preview: this tick contains no agents; only geometries will be displayed.");
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
            GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] No SimulationManager found; using the default CRS and ignoring runtime overrides.");
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
        Dictionary<string, PreviewSpreadProbe> spreadProbes =
            new Dictionary<string, PreviewSpreadProbe>(StringComparer.OrdinalIgnoreCase);

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
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=propertyID missing");
                    continue;
                }

                string propId = world.propertyID[i];
                PropertiesGAMA prop;
                if (string.IsNullOrEmpty(propId) || !propertyMap.TryGetValue(propId, out prop) || prop == null)
                {
                    skippedAgents++;
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=prop null propId=" + (propId ?? "<null>"));
                    continue;
                }

                Attributes attributes = null;
                try
                {
                    attributes = world.GetAttributesAt(i);
                }
                catch (Exception attrEx)
                {
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Attributes invalid for agent i=" + i + " reason=" + attrEx.Message);
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
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] ResolveVisualState failed for agent i=" + i + " reason=" + vsEx.Message);
                }
                if (!hasVisualState)
                {
                    try { visualState = SimulationManager.CreateDefaultVisualState(prop, attributes, precision); hasVisualState = true; }
                    catch { /* fallback failed */ }
                }
                if (!hasVisualState)
                {
                    skippedAgents++;
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=visualState failed");
                    continue;
                }

                string speciesKey = null;
                try { speciesKey = GamaEditorPreviewCapture.ResolveSpeciesKey(propId, propertyMap); }
                catch (Exception skEx) { GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] ResolveSpeciesKey failed i=" + i + " reason=" + skEx.Message); }
                if (string.IsNullOrWhiteSpace(speciesKey)) speciesKey = name;
                if (string.IsNullOrWhiteSpace(speciesKey)) speciesKey = "unknown";

                if (VerbosePreviewBuildDebug && i < 5)
                {
                    GamaLog.Dev(
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
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=speciesParent null");
                    continue;
                }

                if (prop.hasPrefab)
                {
                    if (world.pointsLoc == null || cptPrefab >= world.pointsLoc.Count)
                    {
                        skippedAgents++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=pointsLoc insufficient");
                        continue;
                    }

                    List<int> pt = world.pointsLoc[cptPrefab] != null ? world.pointsLoc[cptPrefab].c : null;
                    if (pt == null || pt.Count < 3)
                    {
                        skippedAgents++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=invalid coordinates");
                        cptPrefab++;
                        continue;
                    }

                    GameObject obj = GamaVisualUtility.InstantiateVisual(name, prop, precision);
                    if (obj == null)
                    {
                        skippedAgents++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " name=" + name + " reason=InstantiateVisual returned null");
                        cptPrefab++;
                        continue;
                    }

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview agent");
                    obj.transform.SetParent(speciesParent, false);

                    Vector3 pos = converter.fromGAMACRS(pt[0], pt[1], pt[2]);
                    pos.y += prop.yOffsetF;
                    pos += visualState.PositionOffset;
                    GetSpreadProbe(spreadProbes, speciesKey).AddExpected(pos);
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
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=pointsGeom insufficient");
                        continue;
                    }

                    List<int> rawGeom = world.pointsGeom[cptGeom] != null ? world.pointsGeom[cptGeom].c : null;
                    if (rawGeom == null || rawGeom.Count < 2)
                    {
                        skippedGeometries++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=invalid geometry data");
                        cptGeom++;
                        continue;
                    }

                    int[] ptArr = rawGeom.ToArray();
                    if (ptArr == null || ptArr.Length == 0)
                    {
                        skippedGeometries++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=empty points");
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
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=polyGen null");
                        cptGeom++;
                        continue;
                    }

                    Vector3 polygonWorldAnchor = ResolveRawGeometryAnchorLocal(rawGeom, converter, yOffsetGeom);
                    Vector3 polygonBasePosition = polygonWorldAnchor;
                    GameObject obj = polygonInputValid
                        ? polyGen.GeneratePolygons(true, name, ptArr, prop, precision)
                        : CreateInvalidGeometryFallbackObject(name, speciesKey, rawGeom, converter, yOffsetGeom, visualState);
                    if (obj == null)
                    {
                        skippedGeometries++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip geometry i=" + i + " name=" + name + " reason=GeneratePolygons returned null");
                        cptGeom++;
                        continue;
                    }

                    if (polygonInputValid)
                    {
                        RecenterPolygonMeshForStableScale(obj, polygonWorldAnchor);
                    }

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview geometry");
                    obj.transform.SetParent(speciesParent, false);

                    ApplyPolygonVisualState(obj, prop, visualState, polygonBasePosition);
                    GetSpreadProbe(spreadProbes, speciesKey).AddExpected(polygonBasePosition + visualState.PositionOffset);
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
                GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip agent i=" + i + " reason=" + ex);
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
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=empty points");
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
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=polyGen null");
                        continue;
                    }

                    Vector3 polygonWorldAnchor = ResolveRawGeometryAnchorLocal(rawGeom, converter, yOffsetGeom);
                    Vector3 polygonBasePosition = polygonWorldAnchor;
                    GameObject obj = polyGen.GeneratePolygons(true, geomName, ptArr, prop, precision);
                    if (obj == null)
                    {
                        skippedGeometries++;
                        GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=GeneratePolygons returned null");
                        continue;
                    }

                    RecenterPolygonMeshForStableScale(obj, polygonWorldAnchor);

                    Undo.RegisterCreatedObjectUndo(obj, "GAMA static preview geometry");
                    obj.transform.SetParent(parent, false);
                    GamaAgentVisualState defaultVisual = SimulationManager.CreateDefaultVisualState(prop, null, precision);
                    ApplyPolygonVisualState(obj, prop, defaultVisual, polygonBasePosition);
                    builtGeometries++;
                }
                catch (Exception ex)
                {
                    skippedGeometries++;
                    GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Skip standalone geometry g=" + g + " reason=" + ex);
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
                GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] PlayerSpawn marker failed: " + ex.Message);
            }
        }

        prefabCount = builtAgents;
        geometryCount = builtGeometries;

        GamaLog.Dev(
            "[GAMA][PREVIEW][BUILD] result builtAgents=" + builtAgents +
            " skippedAgents=" + skippedAgents +
            " builtGeometries=" + builtGeometries +
            " skippedGeometries=" + skippedGeometries);

        if (builtAgents == 0 && builtGeometries == 0)
        {
            error = "No preview objects were built. skippedAgents=" + skippedAgents + ", skippedGeometries=" + skippedGeometries;
            return false;
        }

        return true;
    }

    private static PreviewSpreadProbe GetSpreadProbe(
        Dictionary<string, PreviewSpreadProbe> probes,
        string speciesKey)
    {
        string key = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey.Trim();
        if (!probes.TryGetValue(key, out PreviewSpreadProbe probe) || probe == null)
        {
            probe = new PreviewSpreadProbe(key);
            probes[key] = probe;
        }

        return probe;
    }

    private static void RunPreviewSpreadDiagnostics(
        Transform previewRoot,
        Dictionary<string, PreviewSpreadProbe> probes)
    {
        if (previewRoot == null || probes == null || probes.Count == 0)
        {
            return;
        }

        GamaPreviewObject[] previewObjects = previewRoot.GetComponentsInChildren<GamaPreviewObject>(true);
        for (int i = 0; i < previewObjects.Length; i++)
        {
            GamaPreviewObject previewObject = previewObjects[i];
            if (previewObject == null)
            {
                continue;
            }

            PreviewSpreadProbe probe = GetSpreadProbe(probes, previewObject.speciesName);
            Bounds renderedBounds;
            if (TryGetDiagnosticRenderedBounds(previewObject, out renderedBounds))
            {
                probe.AddActualBounds(renderedBounds, previewObject.transform.localScale);
            }
            else
            {
                probe.AddActual(previewObject.transform.position, previewObject.transform.localScale);
            }
            if (HasScaledContainerBetween(previewObject.transform, previewRoot))
            {
                probe.ScaledContainerObjectCount++;
            }
        }

        PreviewSpreadProbe reference = ResolveReferenceSpreadProbe(probes);
        string referenceName = reference != null ? reference.SpeciesKey : "none";
        foreach (KeyValuePair<string, PreviewSpreadProbe> pair in probes)
        {
            PreviewSpreadProbe probe = pair.Value;
            if (probe == null || probe.ActualCount <= 0)
            {
                continue;
            }

            float expectedDiag = probe.ExpectedDiagonalXZ;
            float actualDiag = probe.ActualDiagonalXZ;
            float spreadRatio = expectedDiag > PreviewSpreadEpsilon
                ? actualDiag / expectedDiag
                : (actualDiag > PreviewSpreadEpsilon ? float.PositiveInfinity : 1f);
            float referenceOverflow = reference != null && reference != probe && reference.HasActual
                ? ComputeReferenceOverflowRatio(probe.ActualBounds, reference.ActualBounds)
                : 0f;

            string line = "[GAMA][PREVIEW][SPREAD] species=" + probe.SpeciesKey +
                          " expectedCount=" + probe.ExpectedCount +
                          " actualCount=" + probe.ActualCount +
                          " expectedXZ=" + FormatFloat(expectedDiag) +
                          " actualXZ=" + FormatFloat(actualDiag) +
                          " ratio=" + FormatFloat(spreadRatio) +
                          " reference=" + referenceName +
                          " referenceOverflow=" + FormatFloat(referenceOverflow) +
                          " scaleRange=" + FormatFloat(probe.MinObservedScale) + ".." + FormatFloat(probe.MaxObservedScale) +
                          " scaledContainerObjects=" + probe.ScaledContainerObjectCount;
            GamaLog.Dev(line);

            bool countMismatch = probe.ExpectedCount > 0 && probe.ActualCount != probe.ExpectedCount;
            bool inflatedAgainstSource = expectedDiag > PreviewSpreadEpsilon &&
                spreadRatio > PreviewSpreadWarnRatio &&
                actualDiag - expectedDiag > 0.5f;
            bool outsideReference = reference != null &&
                reference != probe &&
                referenceOverflow > PreviewReferenceOverflowRatio &&
                probe.ActualCount > 1;
            bool parentScaled = probe.ScaledContainerObjectCount > 0;

            if (countMismatch || inflatedAgainstSource || outsideReference || parentScaled)
            {
                GamaLog.DevWarning("[GAMA][PREVIEW][SPREAD][WARN] species=" + probe.SpeciesKey +
                                 " countMismatch=" + countMismatch +
                                 " inflatedAgainstSource=" + inflatedAgainstSource +
                                 " outsideReference=" + outsideReference +
                                 " parentScaled=" + parentScaled +
                                 " details={" + line + "}");
            }
        }
    }

    private static PreviewSpreadProbe ResolveReferenceSpreadProbe(Dictionary<string, PreviewSpreadProbe> probes)
    {
        PreviewSpreadProbe bestNamed = null;
        PreviewSpreadProbe bestCount = null;
        foreach (KeyValuePair<string, PreviewSpreadProbe> pair in probes)
        {
            PreviewSpreadProbe probe = pair.Value;
            if (probe == null || !probe.HasActual || probe.ActualCount <= 0)
            {
                continue;
            }

            if (IsReferenceSpeciesName(probe.SpeciesKey) &&
                (bestNamed == null || probe.ActualCount > bestNamed.ActualCount))
            {
                bestNamed = probe;
            }

            if (bestCount == null || probe.ActualCount > bestCount.ActualCount)
            {
                bestCount = probe;
            }
        }

        return bestNamed ?? bestCount;
    }

    private static bool IsReferenceSpeciesName(string speciesKey)
    {
        if (string.IsNullOrWhiteSpace(speciesKey))
        {
            return false;
        }

        string lower = speciesKey.ToLowerInvariant();
        return lower.Contains("vegetation") ||
               lower.Contains("cell") ||
               lower.Contains("terrain") ||
               lower.Contains("ground") ||
               lower.Contains("grid") ||
               lower.Contains("patch") ||
               lower.Contains("field") ||
               lower.Contains("zone");
    }

    private static bool HasScaledContainerBetween(Transform leaf, Transform previewRoot)
    {
        Transform current = leaf != null ? leaf.parent : null;
        while (current != null)
        {
            if ((current.localScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                return true;
            }

            if (current == previewRoot)
            {
                return false;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool TryGetDiagnosticRenderedBounds(GamaPreviewObject previewObject, out Bounds bounds)
    {
        bounds = default;
        if (previewObject == null)
        {
            return false;
        }

        Renderer[] renderers = previewObject.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude <= 0.000001f)
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

        return hasBounds;
    }

    private static float ComputeReferenceOverflowRatio(Bounds candidate, Bounds reference)
    {
        float overflow = 0f;
        overflow = Mathf.Max(overflow, reference.min.x - candidate.min.x);
        overflow = Mathf.Max(overflow, candidate.max.x - reference.max.x);
        overflow = Mathf.Max(overflow, reference.min.z - candidate.min.z);
        overflow = Mathf.Max(overflow, candidate.max.z - reference.max.z);
        float referenceDiag = BoundsDiagonalXZ(reference);
        return referenceDiag > PreviewSpreadEpsilon ? Mathf.Max(0f, overflow) / referenceDiag : 0f;
    }

    private static float BoundsDiagonalXZ(Bounds bounds)
    {
        Vector3 size = bounds.size;
        return Mathf.Sqrt(size.x * size.x + size.z * size.z);
    }

    private static string FormatFloat(float value)
    {
        if (float.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        if (float.IsNaN(value))
        {
            return "nan";
        }

        return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class PreviewSpreadProbe
    {
        public readonly string SpeciesKey;
        public int ExpectedCount;
        public int ActualCount;
        public Bounds ExpectedBounds;
        public Bounds ActualBounds;
        public bool HasExpected;
        public bool HasActual;
        public float MinObservedScale = float.PositiveInfinity;
        public float MaxObservedScale = 0f;
        public int ScaledContainerObjectCount;

        public PreviewSpreadProbe(string speciesKey)
        {
            SpeciesKey = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey;
        }

        public float ExpectedDiagonalXZ
        {
            get { return HasExpected ? BoundsDiagonalXZ(ExpectedBounds) : 0f; }
        }

        public float ActualDiagonalXZ
        {
            get { return HasActual ? BoundsDiagonalXZ(ActualBounds) : 0f; }
        }

        public void AddExpected(Vector3 point)
        {
            AddPoint(ref ExpectedBounds, ref HasExpected, point);
            ExpectedCount++;
        }

        public void AddActual(Vector3 point, Vector3 localScale)
        {
            AddPoint(ref ActualBounds, ref HasActual, point);
            ActualCount++;
            ObserveScale(localScale);
        }

        public void AddActualBounds(Bounds bounds, Vector3 localScale)
        {
            AddBounds(ref ActualBounds, ref HasActual, bounds);
            ActualCount++;
            ObserveScale(localScale);
        }

        private void ObserveScale(Vector3 localScale)
        {
            float scale = Mathf.Max(Mathf.Abs(localScale.x), Mathf.Abs(localScale.y), Mathf.Abs(localScale.z));
            MinObservedScale = Mathf.Min(MinObservedScale, scale);
            MaxObservedScale = Mathf.Max(MaxObservedScale, scale);
        }

        private static void AddPoint(ref Bounds bounds, ref bool hasBounds, Vector3 point)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(point);
        }

        private static void AddBounds(ref Bounds current, ref bool hasBounds, Bounds bounds)
        {
            if (!hasBounds)
            {
                current = bounds;
                hasBounds = true;
                return;
            }

            current.Encapsulate(bounds);
        }
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
            GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] Could not read the SimulationManager CRS; using default values: " + ex.Message);
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
            GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent: previewRoot is null");
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
                GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent: GetOrCreateChild returned null for GAMA root");
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
            GamaLog.DevWarning("[GAMA][PREVIEW][BUILD] GetOrCreateSpeciesParent failed for key=" + key + " reason=" + ex.Message);
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
        string logKey = GamaSpeciesRenderOverrides.NormalizeModelPath(modelPath) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(experimentName) + "|" +
            GamaSpeciesRenderOverrides.NormalizeKey(speciesKey);
        if (!OverridePickLogKeys.Add(logKey))
        {
            return;
        }

        GamaLog.Dev("[GAMA][PREVIEW][OVERRIDE_PICK] species=" + speciesKey +
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
        fallback.transform.localPosition = Vector3.zero;
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
            GamaLog.DevWarning(
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
            if (mesh == null || mesh.vertexCount == 0)
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
            readError = "Path is empty.";
            return false;
        }

        string full = path.Trim().Trim('"');
        if (!File.Exists(full))
        {
            readError = "File not found: " + full;
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
