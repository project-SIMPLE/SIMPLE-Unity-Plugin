using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Vérifie qu'un modèle / expérience GAMA est compatible avec le plugin Unity (unity_linker, type unity).
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
            errorMessage = "Aucun fichier .gaml sélectionné.";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(modelPath.Trim());
        }
        catch (Exception ex)
        {
            errorMessage = "Chemin modèle invalide : " + ex.Message;
            return false;
        }

        if (!File.Exists(fullPath))
        {
            errorMessage = "Fichier .gaml introuvable : " + fullPath;
            return false;
        }

        if (string.IsNullOrWhiteSpace(experimentName))
        {
            errorMessage = "Aucune expérience sélectionnée.";
            return false;
        }

        string content;
        try
        {
            content = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            errorMessage = "Impossible de lire le modèle : " + ex.Message;
            return false;
        }

        if (!UnityLinkerSpeciesRegex.IsMatch(content) && !AbstractUnityLinkerRegex.IsMatch(content))
        {
            errorMessage =
                "Ce modèle n'est pas compatible Unity : aucune espèce « unity_linker » (parent abstract_unity_linker) dans « " +
                Path.GetFileName(fullPath) + " ».\n\n" +
                "Utilisez un modèle *-VR.gaml (ex. Traffic and Pollution-VR.gaml, experiment vr_xp) ou ajoutez unity_linker + experiment type: unity.";
            return false;
        }

        GamaPanelExperimentOption option = GamaPanelExperimentAnalyzer.FindExperimentsInFile(fullPath)
            .FirstOrDefault(o => string.Equals(o.Name, experimentName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (option == null)
        {
            errorMessage = "Expérience « " + experimentName + " » introuvable dans " + Path.GetFileName(fullPath) + ".";
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
                "L'expérience « " + experimentName + " » n'est pas une expérience Unity (attendu « type: unity » ou action create_player).\n\n" +
                "Exemple : experiment vr_xp type: unity dans un modèle *-VR.gaml.\n" +
                "Les modèles batch / 2D (ex. reproductibilité_parallélisme.gaml) ne peuvent pas être capturés pour la prévisualisation Unity.";
            return false;
        }

        return true;
    }
}
