using System;

/// <summary>
/// Builds identities that are safe to compare between a captured editor preview
/// and the live GAMA data received in Play mode. Only semantic source identity is
/// used; cache paths, timestamps and capture signatures are deliberately excluded.
/// </summary>
public static class GamaPreviewReuseIdentity
{
    private static readonly string[] StableIdAttributeNames =
    {
        "id",
        "gama_id",
        "agent_id",
        "uid",
        "uuid"
    };

    public static string NormalizeModelPath(string modelPath)
    {
        return GamaSpeciesRenderOverrides.NormalizeModelPath(modelPath);
    }

    public static string NormalizeExperimentName(string experimentName)
    {
        return GamaSpeciesRenderOverrides.NormalizeKey(experimentName);
    }

    public static bool TryBuildStableExperimentKey(
        string modelPath,
        string experimentName,
        bool activeSelection,
        string monitorExperimentId,
        out string key)
    {
        key = string.Empty;
        string normalizedModel = NormalizeModelPath(modelPath);
        string normalizedExperiment = NormalizeExperimentName(experimentName);
        if (string.IsNullOrEmpty(normalizedModel) || string.IsNullOrEmpty(normalizedExperiment))
        {
            return false;
        }

        string normalizedMonitorId = NormalizeIdentityPart(monitorExperimentId);
        if (activeSelection && string.IsNullOrEmpty(normalizedMonitorId))
        {
            return false;
        }

        key = "model=" + EncodePart(normalizedModel) +
              "|experiment=" + EncodePart(normalizedExperiment) +
              "|selection=" + (activeSelection ? "active" : "unity");
        if (activeSelection)
        {
            key += "|monitor=" + EncodePart(normalizedMonitorId);
        }

        return true;
    }

    public static bool TryBuildStableAgentKey(
        string speciesName,
        string worldName,
        Attributes attributes,
        out string key,
        out string sourceId)
    {
        key = string.Empty;
        sourceId = string.Empty;

        string normalizedSpecies = NormalizeIdentityPart(speciesName);
        if (string.IsNullOrEmpty(normalizedSpecies))
        {
            return false;
        }

        string candidate;
        if (attributes != null)
        {
            for (int i = 0; i < StableIdAttributeNames.Length; i++)
            {
                if (attributes.TryGetString(out candidate, StableIdAttributeNames[i]) &&
                    TryAcceptAgentId(candidate, out sourceId))
                {
                    key = BuildAgentKey(normalizedSpecies, sourceId);
                    return true;
                }
            }
        }

        if (!TryAcceptAgentId(worldName, out sourceId))
        {
            return false;
        }

        key = BuildAgentKey(normalizedSpecies, sourceId);
        return true;
    }

    public static bool IsSyntheticAgentName(string value)
    {
        string normalized = NormalizeIdentityPart(value);
        if (string.IsNullOrEmpty(normalized) ||
            normalized == "unknown" ||
            normalized == "agent" ||
            normalized == "unknown_agent")
        {
            return true;
        }

        const string agentPrefix = "agent_";
        const string unknownAgentPrefix = "unknown_agent_";
        string suffix = null;
        if (normalized.StartsWith(unknownAgentPrefix, StringComparison.Ordinal))
        {
            suffix = normalized.Substring(unknownAgentPrefix.Length);
        }
        else if (normalized.StartsWith(agentPrefix, StringComparison.Ordinal))
        {
            suffix = normalized.Substring(agentPrefix.Length);
        }

        if (suffix == null)
        {
            return false;
        }

        if (suffix == "i" || suffix.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < suffix.Length; i++)
        {
            if (!char.IsDigit(suffix[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static string NormalizeIdentityPart(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('\\', '/').ToLowerInvariant();
    }

    private static bool TryAcceptAgentId(string candidate, out string sourceId)
    {
        sourceId = NormalizeAgentId(candidate);
        if (string.IsNullOrEmpty(sourceId) || IsSyntheticAgentName(sourceId))
        {
            sourceId = string.Empty;
            return false;
        }

        return true;
    }

    private static string BuildAgentKey(string normalizedSpecies, string sourceId)
    {
        return EncodePart(normalizedSpecies) + "::" +
               EncodePart(NormalizeAgentId(sourceId));
    }

    private static string NormalizeAgentId(string value)
    {
        // GAMA IDs are opaque values. Their case can carry identity and must not
        // be folded even though species/model matching is case-insensitive.
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string EncodePart(string value)
    {
        return (value ?? string.Empty)
            .Replace("%", "%25")
            .Replace("|", "%7c")
            .Replace("=", "%3d")
            .Replace(":", "%3a");
    }
}
