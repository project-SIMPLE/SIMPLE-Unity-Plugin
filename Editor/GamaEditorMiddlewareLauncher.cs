using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Génère un script de démarrage Node pour simple.webplatform avec LEARNING_PACKAGE_PATH
/// pointant vers le package Unity (sans modifier le dépôt middleware).
/// </summary>
internal static class GamaEditorMiddlewareLauncher
{
    private const string LauncherFileName = "GamaUnityStartMiddleware.bat";

    public static bool TryResolveWebplatformRoot(out string webplatformRoot)
    {
        webplatformRoot = string.Empty;
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "simple.webplatform"),
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "simple.webplatform")),
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "simple.webplatform"))
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string candidate = candidates[i];
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "package.json")) &&
                File.Exists(Path.Combine(candidate, "src", "api", "index.ts")))
            {
                webplatformRoot = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool TryWriteLauncherScript(string learningPackageRoot, out string launcherPath, out string error)
    {
        launcherPath = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(learningPackageRoot) || !Directory.Exists(learningPackageRoot))
        {
            error = "Dossier learning package introuvable : " + learningPackageRoot;
            return false;
        }

        if (!TryResolveWebplatformRoot(out string webplatformRoot))
        {
            error = "Dépôt simple.webplatform introuvable (attendu sur le Bureau ou à côté du projet Unity).";
            return false;
        }

        try
        {
            string launcherDir = Path.Combine(Application.temporaryCachePath, "GamaMiddlewareLauncher");
            Directory.CreateDirectory(launcherDir);
            launcherPath = Path.Combine(launcherDir, LauncherFileName);
            string learningFull = Path.GetFullPath(learningPackageRoot);
            string webFull = Path.GetFullPath(webplatformRoot);

            StringBuilder bat = new StringBuilder();
            bat.AppendLine("@echo off");
            bat.AppendLine("setlocal");
            bat.AppendLine("cd /d \"" + webFull + "\"");
            bat.AppendLine("set \"LEARNING_PACKAGE_PATH=" + learningFull + "\"");
            bat.AppendLine("set \"EXTRA_LEARNING_PACKAGE_PATH=\"");
            bat.AppendLine("echo [GAMA][MW] WorkingDirectory=%CD%");
            bat.AppendLine("echo [GAMA][MW] LEARNING_PACKAGE_PATH=%LEARNING_PACKAGE_PATH%");
            bat.AppendLine("echo [GAMA][MW] EXTRA_LEARNING_PACKAGE_PATH=%EXTRA_LEARNING_PACKAGE_PATH%");
            bat.AppendLine("echo [GAMA][MW] Starting simple.webplatform API (monitor 8001, player 8080)...");
            bat.AppendLine("npx tsx src/api/index.ts");
            File.WriteAllText(launcherPath, bat.ToString(), Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            error = "Impossible d'écrire le script de lancement : " + ex.Message;
            return false;
        }
    }

    public static string BuildManualRestartHint(string learningPackageRoot)
    {
        if (!TryResolveWebplatformRoot(out string webplatformRoot))
        {
            return "Fermez le middleware actuel, puis relancez Node avec LEARNING_PACKAGE_PATH=\"" +
                   (learningPackageRoot ?? "?") + "\".";
        }

        return "1) Fermez le terminal/processus Node qui écoute sur le port 8001.\n" +
               "2) Ouvrez PowerShell dans : " + webplatformRoot + "\n" +
               "3) Exécutez :\n" +
               "   $env:LEARNING_PACKAGE_PATH=\"" + Path.GetFullPath(learningPackageRoot ?? string.Empty) + "\"\n" +
               "   $env:EXTRA_LEARNING_PACKAGE_PATH=\"\"\n" +
               "   npx tsx src/api/index.ts\n" +
               "4) Dans Unity, « Diagnostiquer catalogue middleware » doit afficher le .gaml et l'expérience sélectionnés.";
    }
}
