using UnityEditor;
using UnityEngine;
using System.IO;

[InitializeOnLoad]
public class GamaPrefabAutoCopier
{
    private const string TARGET_DIR = "Assets/SIMPLE_Prefabs";
    private const string PREFS_KEY = "GamaPlugin_SpecificPrefabsCopied_v2";

    private static readonly string[] filesToCopy = new string[]
    {
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Prefabs/Visual Prefabs/Character/Boy.prefab",
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Prefabs/Visual Prefabs/Basic shape/Cube.prefab",
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Prefabs/Visual Prefabs/City/Vehicles/Car.prefab",
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Prefabs/Visual Prefabs/City/Vehicles/Scooter.prefab",
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Prefabs/Visual Prefabs/Character/Ghost.prefab",
        "Packages/com.project-simple.unity-plugin/Runtime/Resources/Imported/Little_Ghost2/Mesh/Little_Ghost_2.fbx"
    };

    static GamaPrefabAutoCopier()
    {
        EditorApplication.delayCall += CheckAndCopyPrefabs;
    }

    private static void CheckAndCopyPrefabs()
    {
        if (EditorPrefs.GetBool(PREFS_KEY + Application.dataPath, false))
        {
            return;
        }

        bool allExist = true;
        foreach (string file in filesToCopy)
        {
            string targetPath = TARGET_DIR + "/" + Path.GetFileName(file);
            if (AssetDatabase.GetMainAssetTypeAtPath(targetPath) == null)
            {
                allExist = false;
                break;
            }
        }

        if (allExist)
        {
            EditorPrefs.SetBool(PREFS_KEY + Application.dataPath, true);
            return;
        }

        bool doCopy = EditorUtility.DisplayDialog(
            "SIMPLE Unity Plugin",
            "Do you want to import the default prefabs from the package into this Unity project?\n\nThis will copy them to the 'Assets/SIMPLE_Prefabs' folder so you can easily assign or modify them.",
            "Import",
            "No thanks"
        );

        if (doCopy)
        {
            PerformCopy();
        }

        EditorPrefs.SetBool(PREFS_KEY + Application.dataPath, true);
    }

    [MenuItem("GAMA/Import Default Prefabs", false, 20)]
    public static void ManualImportPrefabs()
    {
        PerformCopy();
        EditorUtility.DisplayDialog("SIMPLE Unity Plugin", "Prefabs successfully imported into 'Assets/SIMPLE_Prefabs'.", "OK");
    }

    private static void PerformCopy()
    {
        if (!AssetDatabase.IsValidFolder(TARGET_DIR))
        {
            AssetDatabase.CreateFolder("Assets", "SIMPLE_Prefabs");
        }

        foreach (string file in filesToCopy)
        {
            string targetPath = TARGET_DIR + "/" + Path.GetFileName(file);
            
            // If the source file exists in the package
            if (AssetDatabase.GetMainAssetTypeAtPath(file) != null)
            {
                if (AssetDatabase.GetMainAssetTypeAtPath(targetPath) == null)
                {
                    FileUtil.CopyFileOrDirectory(file, targetPath);
                }
            }
        }
        
        AssetDatabase.Refresh();
        GamaLog.Dev("[GAMA] Successfully copied prefabs to " + TARGET_DIR);
    }
}
