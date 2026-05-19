using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Résout modelPath + experiment pour Play / capture (Unity, pas l'app GAMA IDE).</summary>
internal static class GamaEditorPlayTargetResolver
{
    private const string PlayModelPathPrefKey = "ProjectSimple.GamaUnity.Play.ModelPath";
    private const string PlayExperimentPrefKey = "ProjectSimple.GamaUnity.Play.Experiment";

    public static bool TryResolve(out string modelPath, out string experimentName, out string source)
    {
        modelPath = string.Empty;
        experimentName = string.Empty;
        source = "aucune";

        GamaPreviewSession session = FindPreviewSession();
        if (session != null &&
            !string.IsNullOrWhiteSpace(session.modelPath) &&
            !string.IsNullOrWhiteSpace(session.experimentName) &&
            File.Exists(session.modelPath))
        {
            modelPath = session.modelPath;
            experimentName = session.experimentName;
            source = "aperçu statique en scène" + (session.stale ? " (stale)" : string.Empty);
            return true;
        }

        if (GamaEditorRuntimeSelectionStore.TryLoad(out modelPath, out experimentName))
        {
            source = "sélection Unity enregistrée";
            return true;
        }

        string playModel = EditorPrefs.GetString(PlayModelPathPrefKey, string.Empty);
        string playExp = EditorPrefs.GetString(PlayExperimentPrefKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(playModel) && File.Exists(playModel) && !string.IsNullOrWhiteSpace(playExp))
        {
            modelPath = playModel;
            experimentName = playExp;
            source = "dernier Play réussi";
            return true;
        }

        if (GamaEditorRuntimeSelectionStore.TryLoadFromGeneratedLearningPackage(out modelPath, out experimentName))
        {
            source = "dernier settings.json (capture middleware)";
            return true;
        }

        if (TryFromExperimentPathPref(out modelPath, out experimentName))
        {
            source = "Experiment Path (panneau GAMA)";
            return true;
        }

        GamaPanelWindow[] panels = Resources.FindObjectsOfTypeAll<GamaPanelWindow>();
        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null &&
                panels[i].TryGetOpenPanelSelection(out modelPath, out experimentName))
            {
                source = "panneau GAMA ouvert";
                return true;
            }
        }

        return false;
    }

    private static bool TryFromExperimentPathPref(out string modelPath, out string experimentName)
    {
        modelPath = string.Empty;
        experimentName = EditorPrefs.GetString("ProjectSimple.GamaUnity.Panel.GamaHeadlessBatchName", string.Empty);
        if (string.IsNullOrWhiteSpace(experimentName))
        {
            experimentName = "vr_xp";
        }

        string raw = EditorPrefs.GetString("ProjectSimple.GamaUnity.Panel.ExperimentPath", string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (File.Exists(raw) && raw.EndsWith(".gaml", StringComparison.OrdinalIgnoreCase))
        {
            modelPath = Path.GetFullPath(raw);
            return true;
        }

        if (!Directory.Exists(raw))
        {
            return false;
        }

        string[] candidates = Directory.GetFiles(raw, "*-VR.gaml", SearchOption.AllDirectories);
        if (candidates.Length == 0)
        {
            candidates = Directory.GetFiles(raw, "*.gaml", SearchOption.AllDirectories);
        }

        string preferred = candidates.FirstOrDefault(p =>
                              p.IndexOf("Traffic", StringComparison.OrdinalIgnoreCase) >= 0) ??
                          candidates.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return false;
        }

        modelPath = Path.GetFullPath(preferred);
        return true;
    }

    private static GamaPreviewSession FindPreviewSession()
    {
        GameObject root = GameObject.Find("[GAMA] Static Experiment Preview");
        return root != null ? root.GetComponent<GamaPreviewSession>() : null;
    }
}
