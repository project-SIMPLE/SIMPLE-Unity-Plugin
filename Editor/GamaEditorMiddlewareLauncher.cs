using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Generates a Node startup script for simple.webplatform with LEARNING_PACKAGE_PATH
/// pointing to the Unity package, without modifying the middleware repository.
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
            error = "Learning package folder not found: " + learningPackageRoot;
            return false;
        }

        if (!TryResolveWebplatformRoot(out string webplatformRoot))
        {
            error = "simple.webplatform repository not found (expected on the Desktop or next to the Unity project).";
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
            error = "Could not write the startup script: " + ex.Message;
            return false;
        }
    }

    public static string BuildManualRestartHint(string learningPackageRoot)
    {
        if (!TryResolveWebplatformRoot(out string webplatformRoot))
        {
            return "Close the current middleware, then restart Node with LEARNING_PACKAGE_PATH=\"" +
                   (learningPackageRoot ?? "?") + "\".";
        }

        return "1) Close the terminal or Node process listening on port 8001.\n" +
               "2) Open PowerShell in: " + webplatformRoot + "\n" +
               "3) Run:\n" +
               "   $env:LEARNING_PACKAGE_PATH=\"" + Path.GetFullPath(learningPackageRoot ?? string.Empty) + "\"\n" +
               "   $env:EXTRA_LEARNING_PACKAGE_PATH=\"\"\n" +
               "   npx tsx src/api/index.ts\n" +
               "4) In Unity, 'Diagnose Middleware Catalog' should display the selected .gaml file and experiment.";
    }
}
