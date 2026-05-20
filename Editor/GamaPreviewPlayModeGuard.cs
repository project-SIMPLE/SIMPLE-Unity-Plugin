using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class GamaPreviewPlayModeGuard
{
    private const string SessionStateKey = "GamaPreviewWasActiveBeforePlay";
    private const string AutoHidePreviewOnPlayPrefKey = "ProjectSimple.GamaUnity.Panel.AutoHidePreviewOnPlay";
    private const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";

    static GamaPreviewPlayModeGuard()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            GameObject root = FindPreviewRoot();
            if (root != null)
            {
                bool wasActive = root.activeSelf;
                SessionState.SetBool(SessionStateKey, wasActive);

                bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                if (autoHide && wasActive)
                {
                    root.SetActive(false);
                    Debug.Log("[GAMA][PREVIEW][PLAY] Static preview disabled for Play mode to avoid duplicates.");
                }
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            if (SessionState.GetBool(SessionStateKey, false))
            {
                GameObject root = FindPreviewRoot();
                if (root != null)
                {
                    bool autoHide = EditorPrefs.GetBool(AutoHidePreviewOnPlayPrefKey, true);
                    if (autoHide && !root.activeSelf)
                    {
                        root.SetActive(true);
                        Debug.Log("[GAMA][PREVIEW][PLAY] Static preview restored after Play mode.");
                    }
                }
            }
            SessionState.EraseBool(SessionStateKey);
        }
    }

    private static GameObject FindPreviewRoot()
    {
        GamaPreviewSession session = UnityEngine.Object.FindFirstObjectByType<GamaPreviewSession>(FindObjectsInactive.Include);
        if (session != null)
        {
            return session.gameObject;
        }

        return GameObject.Find(StaticPreviewRootName);
    }
}
