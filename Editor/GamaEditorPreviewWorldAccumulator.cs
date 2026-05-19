using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Accumulates partial GAMA world/json_output chunks into one preview snapshot.
/// A later chunk that omits a species does not remove it from the preview cache.
/// </summary>
internal sealed class GamaEditorPreviewWorldAccumulator
{
    private sealed class AgentEntry
    {
        public string Key;
        public string SpeciesKey;
        public string Name;
        public string KeepName;
        public string PropertyId;
        public bool UsesPrefab;
        public JToken PointLoc;
        public JToken PointGeom;
        public JToken OffsetYGeom;
        public JToken Attributes;
        public JToken Ranking;
    }

    internal sealed class MergeResult
    {
        public int ChunkAgentCount;
        public int ChunkGeometryCount;
        public int CacheAgentCount;
        public int NewAgentCount;
        public int UpdatedAgentCount;
        public int NewSpeciesCount;
        public int DynamicCacheAgentCount;
        public bool ExplicitReset;
        public Dictionary<string, int> ChunkSpeciesCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> CacheSpeciesCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public bool CacheGrew => NewAgentCount > 0 || NewSpeciesCount > 0;
    }

    private readonly Dictionary<string, AgentEntry> entriesByKey =
        new Dictionary<string, AgentEntry>(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> entryOrder = new List<string>();
    private readonly HashSet<string> knownSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private JObject latestMetadata = new JObject();

    public int Count => entryOrder.Count;

    public MergeResult Merge(
        JObject contents,
        int tickIndex,
        Dictionary<string, PropertiesGAMA> propertyMap,
        Regex dynamicRegex)
    {
        MergeResult result = new MergeResult();
        if (contents == null)
        {
            result.CacheSpeciesCounts = GetCacheSpeciesCounts();
            result.CacheAgentCount = Count;
            result.DynamicCacheAgentCount = CountDynamicCacheAgents(propertyMap, dynamicRegex);
            return result;
        }

        if (HasExplicitReset(contents))
        {
            entriesByKey.Clear();
            entryOrder.Clear();
            knownSpecies.Clear();
            result.ExplicitReset = true;
        }

        StoreMetadata(contents);

        JArray names = contents["names"] as JArray;
        JArray keepNames = contents["keepNames"] as JArray;
        JArray propertyIds = contents["propertyID"] as JArray;
        JArray pointsLoc = contents["pointsLoc"] as JArray;
        JArray pointsGeom = contents["pointsGeom"] as JArray;
        JArray offsetYGeom = contents["offsetYGeom"] as JArray;
        JArray attributes = contents["attributes"] as JArray;
        JArray ranking = contents["ranking"] as JArray;

        int locCursor = 0;
        int geomCursor = 0;
        int agentCount = ResolveAgentCount(names, propertyIds, pointsLoc, pointsGeom);
        result.ChunkAgentCount = agentCount;
        result.ChunkGeometryCount = pointsGeom != null ? pointsGeom.Count : 0;

        for (int i = 0; i < agentCount; i++)
        {
            string propertyId = ReadString(propertyIds, i);
            PropertiesGAMA prop = TryGetProperty(propertyId, propertyMap);
            if (prop == null && string.IsNullOrWhiteSpace(propertyId))
            {
                prop = TryGetFallbackProperty(propertyMap, pointsLoc, pointsGeom);
                if (prop != null)
                {
                    propertyId = prop.id;
                }
            }

            string speciesKey = GamaEditorPreviewCapture.ResolveSpeciesKey(propertyId, propertyMap);
            bool usesPrefab = ResolveUsesPrefab(prop, pointsLoc, pointsGeom, ref locCursor, ref geomCursor);

            JToken pointLoc = null;
            JToken pointGeom = null;
            JToken offset = null;
            if (usesPrefab)
            {
                pointLoc = ReadClone(pointsLoc, locCursor);
                locCursor++;
            }
            else
            {
                pointGeom = ReadClone(pointsGeom, geomCursor);
                offset = ReadClone(offsetYGeom, geomCursor) ?? new JValue(0);
                geomCursor++;
            }

            string name = ReadString(names, i);
            string keepName = ReadString(keepNames, i);
            JToken attr = ReadClone(attributes, i) ?? new JObject();
            JToken rank = ReadClone(ranking, i) ?? new JValue(0);
            string key = BuildStableKey(speciesKey, name, attr, pointLoc, pointGeom, i, usesPrefab);

            Increment(result.ChunkSpeciesCounts, speciesKey);

            if (!entriesByKey.TryGetValue(key, out AgentEntry entry))
            {
                entry = new AgentEntry { Key = key };
                entriesByKey[key] = entry;
                entryOrder.Add(key);
                result.NewAgentCount++;
            }
            else
            {
                result.UpdatedAgentCount++;
            }

            entry.SpeciesKey = speciesKey;
            entry.Name = string.IsNullOrWhiteSpace(name) ? key : name;
            entry.KeepName = string.IsNullOrWhiteSpace(keepName) ? entry.Name : keepName;
            entry.PropertyId = propertyId ?? string.Empty;
            entry.UsesPrefab = usesPrefab;
            entry.PointLoc = pointLoc;
            entry.PointGeom = pointGeom;
            entry.OffsetYGeom = offset;
            entry.Attributes = attr;
            entry.Ranking = rank;
            if (knownSpecies.Add(speciesKey))
            {
                result.NewSpeciesCount++;
            }
        }

        result.CacheSpeciesCounts = GetCacheSpeciesCounts();
        result.CacheAgentCount = Count;
        result.DynamicCacheAgentCount = CountDynamicCacheAgents(propertyMap, dynamicRegex);
        return result;
    }

    public string ToWorldJson()
    {
        return BuildWorldObject().ToString(Formatting.Indented);
    }

    private JObject BuildWorldObject()
    {
        JObject world = new JObject();

        CopyMetadata(world, "position");

        JArray names = new JArray();
        JArray keepNames = new JArray();
        JArray propertyIds = new JArray();
        JArray pointsLoc = new JArray();
        JArray pointsGeom = new JArray();
        JArray offsetYGeom = new JArray();
        JArray attributes = new JArray();
        JArray ranking = new JArray();

        for (int i = 0; i < entryOrder.Count; i++)
        {
            if (!entriesByKey.TryGetValue(entryOrder[i], out AgentEntry entry) || entry == null)
            {
                continue;
            }

            names.Add(entry.Name ?? entry.Key);
            keepNames.Add(entry.KeepName ?? entry.Name ?? entry.Key);
            propertyIds.Add(entry.PropertyId ?? string.Empty);
            attributes.Add(entry.Attributes != null ? entry.Attributes.DeepClone() : new JObject());
            ranking.Add(entry.Ranking != null ? entry.Ranking.DeepClone() : new JValue(0));

            if (entry.UsesPrefab)
            {
                if (entry.PointLoc != null)
                {
                    pointsLoc.Add(entry.PointLoc.DeepClone());
                }
            }
            else if (entry.PointGeom != null)
            {
                pointsGeom.Add(entry.PointGeom.DeepClone());
                offsetYGeom.Add(entry.OffsetYGeom != null ? entry.OffsetYGeom.DeepClone() : new JValue(0));
            }
        }

        world["names"] = names;
        world["keepNames"] = keepNames;
        world["propertyID"] = propertyIds;
        world["pointsLoc"] = pointsLoc;
        world["attributes"] = attributes;
        world["offsetYGeom"] = offsetYGeom;
        world["pointsGeom"] = pointsGeom;
        world["ranking"] = ranking;

        CopyMetadata(world, "players");
        CopyMetadata(world, "numTokens");
        CopyMetadata(world, "isInit");

        return world;
    }

    private Dictionary<string, int> GetCacheSpeciesCounts()
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entryOrder.Count; i++)
        {
            if (!entriesByKey.TryGetValue(entryOrder[i], out AgentEntry entry) || entry == null)
            {
                continue;
            }

            Increment(counts, entry.SpeciesKey);
        }

        return counts;
    }

    private int CountDynamicCacheAgents(
        Dictionary<string, PropertiesGAMA> propertyMap,
        Regex dynamicRegex)
    {
        if (dynamicRegex == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < entryOrder.Count; i++)
        {
            if (!entriesByKey.TryGetValue(entryOrder[i], out AgentEntry entry) || entry == null)
            {
                continue;
            }

            PropertiesGAMA prop = TryGetProperty(entry.PropertyId, propertyMap);
            if (GamaEditorPreviewCapture.IsDynamicProperty(prop, entry.PropertyId, dynamicRegex) ||
                (!string.IsNullOrWhiteSpace(entry.SpeciesKey) && dynamicRegex.IsMatch(entry.SpeciesKey)))
            {
                count++;
            }
        }

        return count;
    }

    private void StoreMetadata(JObject contents)
    {
        if (contents == null)
        {
            return;
        }

        string[] keys = { "position", "players", "numTokens", "isInit" };
        for (int i = 0; i < keys.Length; i++)
        {
            JToken token = contents[keys[i]];
            if (token != null)
            {
                latestMetadata[keys[i]] = token.DeepClone();
            }
        }
    }

    private void CopyMetadata(JObject target, string key)
    {
        JToken token = latestMetadata[key];
        if (token != null)
        {
            target[key] = token.DeepClone();
        }
    }

    private static int ResolveAgentCount(JArray names, JArray propertyIds, JArray pointsLoc, JArray pointsGeom)
    {
        if (names != null && names.Count > 0)
        {
            return names.Count;
        }

        if (propertyIds != null && propertyIds.Count > 0)
        {
            return propertyIds.Count;
        }

        if (pointsLoc != null && pointsLoc.Count > 0)
        {
            return pointsLoc.Count;
        }

        return pointsGeom != null ? pointsGeom.Count : 0;
    }

    private static bool ResolveUsesPrefab(
        PropertiesGAMA prop,
        JArray pointsLoc,
        JArray pointsGeom,
        ref int locCursor,
        ref int geomCursor)
    {
        if (prop != null)
        {
            return prop.hasPrefab;
        }

        bool hasLoc = pointsLoc != null && locCursor < pointsLoc.Count;
        bool hasGeom = pointsGeom != null && geomCursor < pointsGeom.Count;
        if (hasLoc && !hasGeom)
        {
            return true;
        }

        if (!hasLoc && hasGeom)
        {
            return false;
        }

        return hasLoc;
    }

    private static string BuildStableKey(
        string speciesKey,
        string name,
        JToken attributes,
        JToken pointLoc,
        JToken pointGeom,
        int localIndex,
        bool usesPrefab)
    {
        string species = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey;
        string attrId = TryReadAttributeString(attributes, "id", "gama_id", "agent_id", "uid", "uuid");
        if (!string.IsNullOrWhiteSpace(attrId))
        {
            return species + "|id|" + attrId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return species + "|name|" + name.Trim();
        }

        if (!usesPrefab && pointGeom != null)
        {
            return species + "|geom|" + StableHash(pointGeom.ToString(Formatting.None)) + "|i|" + localIndex;
        }

        if (usesPrefab && pointLoc != null)
        {
            return species + "|locindex|" + localIndex;
        }

        return species + "|index|" + localIndex;
    }

    private static string TryReadAttributeString(JToken attributes, params string[] keys)
    {
        JObject obj = attributes as JObject;
        if (obj == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < keys.Length; i++)
        {
            JToken token = null;
            foreach (JProperty property in obj.Properties())
            {
                if (property.Name.Equals(keys[i], StringComparison.OrdinalIgnoreCase))
                {
                    token = property.Value;
                    break;
                }
            }

            if (token == null || token.Type == JTokenType.Null)
            {
                continue;
            }

            string value = token.Type == JTokenType.String
                ? token.Value<string>()
                : token.ToString(Formatting.None);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static PropertiesGAMA TryGetProperty(string propertyId, Dictionary<string, PropertiesGAMA> propertyMap)
    {
        if (string.IsNullOrEmpty(propertyId) || propertyMap == null)
        {
            return null;
        }

        propertyMap.TryGetValue(propertyId, out PropertiesGAMA prop);
        return prop;
    }

    private static PropertiesGAMA TryGetFallbackProperty(
        Dictionary<string, PropertiesGAMA> propertyMap,
        JArray pointsLoc,
        JArray pointsGeom)
    {
        if (propertyMap == null || propertyMap.Count == 0)
        {
            return null;
        }

        bool geometryOnly = (pointsGeom != null && pointsGeom.Count > 0) &&
                            (pointsLoc == null || pointsLoc.Count == 0);
        bool prefabOnly = (pointsLoc != null && pointsLoc.Count > 0) &&
                          (pointsGeom == null || pointsGeom.Count == 0);

        foreach (KeyValuePair<string, PropertiesGAMA> pair in propertyMap)
        {
            PropertiesGAMA prop = pair.Value;
            if (prop == null)
            {
                continue;
            }

            if (geometryOnly && !prop.hasPrefab)
            {
                return prop;
            }

            if (prefabOnly && prop.hasPrefab)
            {
                return prop;
            }
        }

        foreach (KeyValuePair<string, PropertiesGAMA> pair in propertyMap)
        {
            if (pair.Value != null)
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static JToken ReadClone(JArray array, int index)
    {
        return array != null && index >= 0 && index < array.Count ? array[index]?.DeepClone() : null;
    }

    private static string ReadString(JArray array, int index)
    {
        if (array == null || index < 0 || index >= array.Count || array[index] == null)
        {
            return string.Empty;
        }

        return array[index].ToString();
    }

    private static bool HasExplicitReset(JObject contents)
    {
        return IsTrue(contents["reset"]) ||
               IsTrue(contents["clear"]) ||
               IsTrue(contents["clear_scene"]) ||
               IsTrue(contents["full_snapshot_reset"]);
    }

    private static bool IsTrue(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        if (token.Type == JTokenType.Boolean)
        {
            return token.Value<bool>();
        }

        if (token.Type == JTokenType.Integer)
        {
            return token.Value<int>() != 0;
        }

        return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token.ToString(), "reset", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(token.ToString(), "clear", StringComparison.OrdinalIgnoreCase);
    }

    private static void Increment(Dictionary<string, int> counts, string speciesKey)
    {
        string key = string.IsNullOrWhiteSpace(speciesKey) ? "unknown" : speciesKey;
        if (!counts.ContainsKey(key))
        {
            counts[key] = 0;
        }

        counts[key]++;
    }

    private static string StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261;
            if (text != null)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619;
                }
            }

            return hash.ToString("x8");
        }
    }
}
