using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Checks if a GAMA model / experiment is compatible with the Unity plugin (unity_linker, type unity).
/// </summary>
internal static class GamaEditorUnityModelValidation
{
    private static readonly Regex UnityLinkerSpeciesRegex = new Regex(
        @"\bspecies\s+unity_linker\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AbstractUnityLinkerRegex = new Regex(
        @"\babstract_unity_linker\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UnityExperimentTypeRegex = new Regex(
        @"\btype\s*:\s*unity\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CreatePlayerActionRegex = new Regex(
        @"\baction\s+create_player\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryValidateUnityCaptureTarget(
        string modelPath,
        string experimentName,
        out string errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            errorMessage = "No .gaml file selected.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(modelPath.Trim());
        }
        catch (Exception ex)
        {
            errorMessage = "Invalid model path: " + ex.Message;
            return false;
        }

        if (!File.Exists(fullPath))
        {
            errorMessage = ".gaml file not found: " + fullPath;
            return false;
        }

        if (string.IsNullOrWhiteSpace(experimentName))
        {
            errorMessage = "No experiment selected.";
            return false;
        }

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            errorMessage = "Cannot read model: " + ex.Message;
            return false;
        }

        if (!UnityLinkerSpeciesRegex.IsMatch(content) && !AbstractUnityLinkerRegex.IsMatch(content))
        {
            errorMessage =
                "This model is not Unity-compatible: no 'unity_linker' species (parent abstract_unity_linker) in '" +
                Path.GetFileName(fullPath) + "'.\n\n" +
                "Use a *-VR.gaml model (e.g., Traffic and Pollution-VR.gaml, experiment vr_xp) or add unity_linker + experiment type: unity.";
            return false;
        }

        GamaPanelExperimentOption option = GamaPanelExperimentAnalyzer.FindExperimentsInFile(fullPath)
            .FirstOrDefault(o => string.Equals(o.Name, experimentName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            errorMessage = "Experiment '" + experimentName + "' not found in " + Path.GetFileName(fullPath) + ".";
            return false;
        }

        string experimentBlock = content.Substring(
            option.DeclarationIndex,
            Math.Max(0, option.NextDeclarationIndex - option.DeclarationIndex));

        bool unityExperiment = UnityExperimentTypeRegex.IsMatch(experimentBlock) ||
                               CreatePlayerActionRegex.IsMatch(experimentBlock);
        if (!unityExperiment)
        {
            errorMessage =
                "Experiment '" + experimentName + "' is not a Unity experiment (expected 'type: unity' or create_player action).\n\n" +
                "Example: experiment vr_xp type: unity in a *-VR.gaml model.\n" +
                "Batch / 2D models (e.g. reproducibility_parallelism.gaml) cannot be captured for Unity preview.";
            return false;
        }

        return true;
    }
}
