using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Comptage d'espèces par tick et sélection du meilleur frame d'aperçu (agents dynamiques tardifs).
/// </summary>
internal static class GamaEditorPreviewCapture
{
    public const string DefaultDynamicSpeciesRegex =
        @"car|vehicle|voiture|traffic|vehicule|pedestrian|pieton|piéton|walker|person|people|up_people|human|homme|citizen|population|passenger|worker";

    public static Regex CompileDynamicSpeciesRegex(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            pattern = DefaultDynamicSpeciesRegex;
        }

        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch
        {
            return new Regex(DefaultDynamicSpeciesRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
    }

    public static Dictionary<string, PropertiesGAMA> BuildPropertyMap(AllProperties allProperties)
    {
        Dictionary<string, PropertiesGAMA> map = new Dictionary<string, PropertiesGAMA>(StringComparer.OrdinalIgnoreCase);
        if (allProperties?.properties == null)
        {
            return map;
        }

        for (int i = 0; i < allProperties.properties.Count; i++)
        {
            PropertiesGAMA p = allProperties.properties[i];
            if (p == null || string.IsNullOrEmpty(p.id))
            {
                continue;
            }

            map[p.id] = p;
        }

        return map;
    }

    public static string ResolveSpeciesKey(string propertyId, Dictionary<string, PropertiesGAMA> propertyMap)
    {
        if (!string.IsNullOrEmpty(propertyId) &&
            propertyMap != null &&
            propertyMap.TryGetValue(propertyId, out PropertiesGAMA prop) &&
            prop != null)
        {
            if (!string.IsNullOrWhiteSpace(prop.tag))
            {
                return SanitizeSpeciesKey(prop.tag);
            }

            if (!string.IsNullOrWhiteSpace(prop.id))
            {
                return SanitizeSpeciesKey(prop.id);
            }
        }

        return string.IsNullOrWhiteSpace(propertyId) ? "unknown" : SanitizeSpeciesKey(propertyId);
    }

    public static string SanitizeSpeciesKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "unknown";
        }

        char[] chars = key.Trim().ToCharArray();
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

    public static Dictionary<string, int> CountSpeciesInFrame(
        JObject contents,
        Dictionary<string, PropertiesGAMA> propertyMap)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (contents == null)
        {
            return counts;
        }

        JArray names = contents["names"] as JArray;
        JArray propertyIds = contents["propertyID"] as JArray;
        int agentCount = names != null && names.Count > 0
            ? names.Count
            : contents["pointsLoc"] is JArray pointsLoc ? pointsLoc.Count : 0;

        for (int i = 0; i < agentCount; i++)
        {
            string propertyId = propertyIds != null && i < propertyIds.Count
                ? propertyIds[i]?.ToString()
                : string.Empty;
            string speciesKey = ResolveSpeciesKey(propertyId, propertyMap);
            if (!counts.ContainsKey(speciesKey))
            {
                counts[speciesKey] = 0;
            }

            counts[speciesKey]++;
        }

        return counts;
    }

    public static int CountDynamicAgentsInFrame(
        JObject contents,
        Dictionary<string, PropertiesGAMA> propertyMap,
        Regex dynamicRegex)
    {
        if (contents == null || dynamicRegex == null)
        {
            return 0;
        }

        JArray names = contents["names"] as JArray;
        JArray propertyIds = contents["propertyID"] as JArray;
        int agentCount = names != null && names.Count > 0
            ? names.Count
            : contents["pointsLoc"] is JArray pointsLoc ? pointsLoc.Count : 0;
        int dynamicCount = 0;

        for (int i = 0; i < agentCount; i++)
        {
            string propertyId = propertyIds != null && i < propertyIds.Count
                ? propertyIds[i]?.ToString()
                : string.Empty;
            PropertiesGAMA prop = null;
            if (!string.IsNullOrEmpty(propertyId) && propertyMap != null)
            {
                propertyMap.TryGetValue(propertyId, out prop);
            }

            if (IsDynamicProperty(prop, propertyId, dynamicRegex))
            {
                dynamicCount++;
            }
        }

        return dynamicCount;
    }

    public static bool IsDynamicProperty(PropertiesGAMA prop, string propertyId, Regex dynamicRegex)
    {
        if (dynamicRegex == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(propertyId) && dynamicRegex.IsMatch(propertyId))
        {
            return true;
        }

        if (prop == null)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(prop.id) && dynamicRegex.IsMatch(prop.id))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(prop.tag) && dynamicRegex.IsMatch(prop.tag))
        {
            return true;
        }

        return !string.IsNullOrEmpty(prop.prefab) && dynamicRegex.IsMatch(prop.prefab);
    }

    public static string FormatSpeciesCountsLine(int tickIndex, Dictionary<string, int> counts)
    {
        return FormatChunkSpeciesCountsLine(tickIndex, counts);
    }

    public static string FormatChunkSpeciesCountsLine(int tickIndex, Dictionary<string, int> counts)
    {
        return FormatSpeciesCountsLine("tick=" + tickIndex + " chunk", counts);
    }

    public static string FormatCacheSpeciesCountsLine(Dictionary<string, int> counts)
    {
        return FormatSpeciesCountsLine("cache", counts);
    }

    private static string FormatSpeciesCountsLine(string scope, Dictionary<string, int> counts)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("[GAMA][PREVIEW] ").Append(scope).Append(" species:");
        if (counts == null || counts.Count == 0)
        {
            sb.Append(" (no agents)");
            return sb.ToString();
        }

        List<string> keys = new List<string>(counts.Keys);
        keys.Sort(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(keys[i]).Append('=').Append(counts[keys[i]]);
        }

        return sb.ToString();
    }
}
