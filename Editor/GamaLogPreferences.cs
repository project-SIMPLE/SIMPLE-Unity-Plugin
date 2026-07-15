using UnityEditor;

[InitializeOnLoad]
internal static class GamaLogPreferences
{
    internal const string VerboseModePrefKey = "ProjectSimple.GamaUnity.Logging.VerboseMode";

    static GamaLogPreferences()
    {
        GamaLog.SetVerboseEnabled(EditorPrefs.GetBool(VerboseModePrefKey, false));
    }

    internal static bool VerboseEnabled
    {
        get => GamaLog.VerboseEnabled;
        set
        {
            GamaLog.SetVerboseEnabled(value);
            EditorPrefs.SetBool(VerboseModePrefKey, value);
        }
    }
}
