using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Résout le chemin absolu du lanceur <see cref="GamaUnityHeadlessRunner.bat"/> livré dans le package.
/// </summary>
internal static class GamaHeadlessPackagePaths
{
    private const string RunnerFileName = "GamaUnityHeadlessRunner.bat";

    public static bool TryGetBundledRunnerBat(out string absolutePath, out string error)
    {
        absolutePath = null;
        error = null;

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
        {
            error = "Impossible de déterminer la racine du projet Unity.";
            return false;
        }

        string[] packageRelative =
        {
            Path.Combine("Packages", "com.project-simple.unity-plugin", "Editor", "GamaHeadless", RunnerFileName),
            Path.Combine("Packages", "com.project-simple.gama-unity", "Editor", "GamaHeadless", RunnerFileName)
        };

        for (int i = 0; i < packageRelative.Length; i++)
        {
            string candidate = Path.GetFullPath(Path.Combine(projectRoot, packageRelative[i]));
            if (File.Exists(candidate))
            {
                absolutePath = candidate;
                return true;
            }
        }

        string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(RunnerFileName));
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(assetPath))
            {
                continue;
            }

            if (!assetPath.EndsWith(RunnerFileName, System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string full = Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(full))
            {
                absolutePath = full;
                return true;
            }
        }

        error = "Lanceur « " + RunnerFileName + " » introuvable dans le package. Réinstallez com.project-simple.unity-plugin.";
        return false;
    }
}
