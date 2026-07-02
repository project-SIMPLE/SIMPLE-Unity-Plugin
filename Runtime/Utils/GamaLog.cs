using UnityEngine;

public static class GamaLog
{
    public static bool VerboseEnabled
    {
        get
        {
#if SIMPLE_GAMA_VERBOSE_LOGS
            return true;
#else
            return false;
#endif
        }
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
