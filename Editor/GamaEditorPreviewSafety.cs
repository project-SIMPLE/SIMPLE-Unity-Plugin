using System;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

internal enum GamaEditorPlayExitDecision
{
    KeepPreview,
    SkipOnce,
    NeverAskAgain
}

internal static class GamaEditorPreviewSafetyText
{
    public const string ReplacePreviewTitle = "Replace GAMA Preview?";
    public const string ReplacePreviewMessage =
        "An editable preview is already loaded. Continuing will overwrite it. Do you want to proceed?";
    public const string KeepPlayPreviewTitle = "Save GAMA Preview?";
    public const string KeepPlayPreviewMessage =
        "No editable preview is currently loaded for this experiment. " +
        "Do you want to save it as a preview in Edit Mode?";
    public const string YesButton = "Yes";
    public const string NoButton = "No";
    public const string NeverButton = "Never";
}

internal static class GamaEditorPreviewSafetyPreferences
{
    private const string ProjectKeyPrefix = "ProjectSimple.GamaUnity.PreviewSafety";
    private const string AskToSavePlayPreviewSuffix = "AskToSavePlayPreview";

    public static bool AskToSavePlayPreviewOnExit
    {
        get { return EditorPrefs.GetBool(BuildProjectKey(AskToSavePlayPreviewSuffix), true); }
        set { EditorPrefs.SetBool(BuildProjectKey(AskToSavePlayPreviewSuffix), value); }
    }

    public static void ResetProjectChoices()
    {
        EditorPrefs.DeleteKey(BuildProjectKey(AskToSavePlayPreviewSuffix));
    }

    internal static string BuildProjectKey(string suffix)
    {
        string projectIdentity;
        try
        {
            projectIdentity = Application.dataPath ?? string.Empty;
        }
        catch
        {
            projectIdentity = string.Empty;
        }

        return BuildProjectKey(projectIdentity, suffix);
    }

    internal static string BuildProjectKey(string projectIdentity, string suffix)
    {
        string normalizedIdentity = (projectIdentity ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .ToLowerInvariant();

        unchecked
        {
            uint hash = 2166136261;
            for (int i = 0; i < normalizedIdentity.Length; i++)
            {
                hash ^= normalizedIdentity[i];
                hash *= 16777619;
            }

            return ProjectKeyPrefix + "." + hash.ToString("x8") + "." + (suffix ?? string.Empty);
        }
    }
}

[InitializeOnLoad]
internal static class GamaEditorPreviewSafetyDialogs
{
    internal delegate bool ConfirmDialogDelegate(
        string title,
        string message,
        string ok,
        string cancel);

    internal delegate int ComplexDialogDelegate(
        string title,
        string message,
        string ok,
        string cancel,
        string alternative);

    private static readonly int MainThreadId;

    internal static ConfirmDialogDelegate ConfirmDialog = EditorUtility.DisplayDialog;
    internal static ComplexDialogDelegate ComplexDialog = EditorUtility.DisplayDialogComplex;
    internal static Func<bool> ModalAvailabilityOverride;

    static GamaEditorPreviewSafetyDialogs()
    {
        MainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public static bool CanShowModal
    {
        get
        {
            if (ModalAvailabilityOverride != null)
            {
                return ModalAvailabilityOverride();
            }

            return !Application.isBatchMode &&
                   !AssetDatabase.IsAssetImportWorkerProcess() &&
                   Thread.CurrentThread.ManagedThreadId == MainThreadId;
        }
    }

    public static GamaEditorPlayExitDecision AskToSavePlayPreview()
    {
        if (!CanShowModal)
        {
            return GamaEditorPlayExitDecision.SkipOnce;
        }

        int choice = ComplexDialog(
            GamaEditorPreviewSafetyText.KeepPlayPreviewTitle,
            GamaEditorPreviewSafetyText.KeepPlayPreviewMessage,
            GamaEditorPreviewSafetyText.YesButton,
            GamaEditorPreviewSafetyText.NoButton,
            GamaEditorPreviewSafetyText.NeverButton);

        if (choice == 0)
        {
            return GamaEditorPlayExitDecision.KeepPreview;
        }

        if (choice == 2)
        {
            GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit = false;
            return GamaEditorPlayExitDecision.NeverAskAgain;
        }

        return GamaEditorPlayExitDecision.SkipOnce;
    }

    public static bool ConfirmPreviewReplacement()
    {
        if (!CanShowModal)
        {
            GamaLog.Warning(
                "[GAMA][PREVIEW][SAFETY] Preview replacement was cancelled because modal dialogs are unavailable.");
            return false;
        }

        return ConfirmDialog(
            GamaEditorPreviewSafetyText.ReplacePreviewTitle,
            GamaEditorPreviewSafetyText.ReplacePreviewMessage,
            GamaEditorPreviewSafetyText.YesButton,
            GamaEditorPreviewSafetyText.NoButton);
    }

    internal static void ResetTestHooks()
    {
        ConfirmDialog = EditorUtility.DisplayDialog;
        ComplexDialog = EditorUtility.DisplayDialogComplex;
        ModalAvailabilityOverride = null;
    }
}

internal static class GamaEditorPreviewSafety
{
    public const string StaticPreviewRootName = "[GAMA] Static Experiment Preview";
    public const string BuildingPreviewRootName = "[GAMA] Static Experiment Preview (Building)";

    public static bool TryFindEditablePreview(
        out GameObject previewRoot,
        out GamaPreviewSession previewSession)
    {
        previewRoot = null;
        previewSession = null;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            return false;
        }

        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null ||
                !string.Equals(root.name, StaticPreviewRootName, StringComparison.Ordinal))
            {
                continue;
            }

            previewRoot = root;
            previewSession = root.GetComponent<GamaPreviewSession>();
            return true;
        }

        return false;
    }

    public static bool TryApproveCurrentReplacement(
        GameObject alreadyApprovedRoot,
        out GameObject currentRoot)
    {
        if (!TryFindEditablePreview(out currentRoot, out _))
        {
            return true;
        }

        if (alreadyApprovedRoot != null && currentRoot == alreadyApprovedRoot)
        {
            return true;
        }

        return GamaEditorPreviewSafetyDialogs.ConfirmPreviewReplacement();
    }

    public static bool ShouldOfferPlayExitSave(
        bool hasCompleteRuntimeSnapshot,
        bool correspondingPreviewWasLoaded)
    {
        return hasCompleteRuntimeSnapshot &&
               !correspondingPreviewWasLoaded &&
               GamaEditorPreviewSafetyPreferences.AskToSavePlayPreviewOnExit;
    }

    public static bool TryApprovePlayExitSave(
        bool hasCompleteRuntimeSnapshot,
        bool correspondingPreviewWasLoaded)
    {
        if (!ShouldOfferPlayExitSave(hasCompleteRuntimeSnapshot, correspondingPreviewWasLoaded))
        {
            return false;
        }

        return GamaEditorPreviewSafetyDialogs.AskToSavePlayPreview() ==
               GamaEditorPlayExitDecision.KeepPreview;
    }

    public static void CommitReplacement(GameObject existingRoot, GameObject completedRoot)
    {
        if (completedRoot == null)
        {
            return;
        }

        string completedRootPreviousName = completedRoot.name;
        Undo.RecordObject(completedRoot, "Commit GAMA preview");
        completedRoot.name = StaticPreviewRootName;

        try
        {
            if (existingRoot != null && existingRoot != completedRoot)
            {
                Undo.DestroyObjectImmediate(existingRoot);
            }
        }
        catch
        {
            // Restore the temporary identity so the caller can safely clean up
            // the failed build while the previous preview remains available.
            if (completedRoot != null)
            {
                completedRoot.name = completedRootPreviousName;
            }

            throw;
        }
    }

    public static void ClearBuildingPreview()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !activeScene.isLoaded)
        {
            return;
        }

        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root != null &&
                string.Equals(root.name, BuildingPreviewRootName, StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
