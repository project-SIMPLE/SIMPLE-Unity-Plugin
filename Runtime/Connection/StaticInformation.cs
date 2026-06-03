using System;

public static class StaticInformation
{
    public static string endOfGame { get; set; }
    private static string connectionId;

    public static void ResetSessionId(string prefix = "unity_play")
    {
        connectionId = BuildUniqueSessionId(prefix);
    }

    public static string CreateUniqueSessionId(string prefix)
    {
        return BuildUniqueSessionId(prefix);
    }

    public static void EnsureSessionIdPrefix(string prefix)
    {
        string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "unity_play" : prefix.Trim();
        if (string.IsNullOrWhiteSpace(connectionId) ||
            !connectionId.StartsWith(safePrefix + "_", StringComparison.OrdinalIgnoreCase))
        {
            connectionId = BuildUniqueSessionId(safePrefix);
        }
    }

    public static string getId()
    {
        if (connectionId == null || connectionId.Length == 0)
        {
            connectionId = BuildUniqueSessionId("unity_play");
        }

        return connectionId;
    }

    private static string BuildUniqueSessionId(string prefix)
    {
        string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "unity_play" : prefix.Trim();
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        return safePrefix + "_" + stamp + "_" + suffix;
    }
}
