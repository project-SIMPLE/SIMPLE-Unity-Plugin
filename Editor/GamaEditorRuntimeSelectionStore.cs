using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Mémorise la dernière sélection Unity (modelPath + experiment) pour capture et Play.</summary>
internal static class GamaEditorRuntimeSelectionStore
{
    private const string ModelPathKey = "ProjectSimple.GamaUnity.Panel.LastRuntimeModelPath";
    private const string ExperimentKey = "ProjectSimple.GamaUnity.Panel.LastRuntimeExperimentName";

    public static void Save(string modelPath, string experimentName)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(experimentName))
        {
            return;
        }

        try
        {
            string full = Path.GetFullPath(modelPath.Trim());
            EditorPrefs.SetString(ModelPathKey, full);
            EditorPrefs.SetString(ExperimentKey, experimentName.Trim());
            Debug.Log("[GAMA][SYNC] Saved runtime selection model=" + full + " exp=" + experimentName.Trim());
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA][SYNC] Impossible d'enregistrer la sélection : " + ex.Message);
        }
    }

    public static bool TryLoad(out string modelPath, out string experimentName)
    {
        modelPath = EditorPrefs.GetString(ModelPathKey, string.Empty);
        experimentName = EditorPrefs.GetString(ExperimentKey, string.Empty);
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(experimentName))
        {
            return false;
        }

        if (!File.Exists(modelPath))
        {
            return false;
        }

        return true;
    }

    public static bool TryLoadFromGeneratedLearningPackage(out string modelPath, out string experimentName)
    {
        modelPath = string.Empty;
        experimentName = string.Empty;
        string root = Path.Combine(Application.temporaryCachePath, "GamaGeneratedLearningPackages");
        if (!Directory.Exists(root))
        {
            return false;
        }

        string newestSettings = null;
        DateTime newestUtc = DateTime.MinValue;
        foreach (string settingsPath in Directory.GetFiles(root, "settings.json", SearchOption.AllDirectories))
        {
            try
            {
                DateTime write = File.GetLastWriteTimeUtc(settingsPath);
                if (write >= newestUtc)
                {
                    newestUtc = write;
                    newestSettings = settingsPath;
                }
            }
            catch
            {
                // ignore
            }
        }

        if (string.IsNullOrWhiteSpace(newestSettings))
        {
            return false;
        }

        try
        {
            JObject json = JObject.Parse(File.ReadAllText(newestSettings));
            modelPath = json["model_file_path"]?.ToString() ?? string.Empty;
            experimentName = json["experiment_name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(experimentName);
        }
        catch
        {
            return false;
        }
    }
}
