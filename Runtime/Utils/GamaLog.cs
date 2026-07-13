using UnityEngine;

public static class GamaLog
{
    private static volatile bool verboseEnabled = false;

    public static bool VerboseEnabled => verboseEnabled;

    public static void SetVerboseEnabled(bool enabled)
    {
        verboseEnabled = enabled;
    }

    public static void Info(string message)
    {
        Debug.Log(message);
    }

    public static void Dev(string message)
    {
        if (VerboseEnabled)
        {
            Debug.Log(message);
        }
    }

    public static void Warning(string message)
    {
        Debug.LogWarning(message);
    }

    public static void DevWarning(string message)
    {
        if (VerboseEnabled)
        {
            Debug.LogWarning(message);
        }
    }

    public static void Error(string message)
    {
        Debug.LogError(message);
    }
}
