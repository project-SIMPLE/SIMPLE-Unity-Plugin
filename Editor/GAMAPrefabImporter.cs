using UnityEngine;
using UnityEditor;
using System.IO;

public class GAMAPrefabImporter : EditorWindow
{
    private string sourceFolderPath = "";

    public static void ShowWindow()
    {
        GAMAPrefabImporter window = GetWindow<GAMAPrefabImporter>("GAMA Prefab Importer");
        window.minSize = new Vector2(400, 150);
        window.Show();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField("Import GAMA Prefabs into Unity", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Select the external folder containing your prefabs (e.g., from the simple.toolchain). " +
                                "They will be copied directly into the 'Assets/Resources' folder of this project so " +
                                "your GAML 'prefab_aspect' code can find them.", MessageType.Info);

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        sourceFolderPath = EditorGUILayout.TextField("Source Folder", sourceFolderPath);
        if (GUILayout.Button("Browse...", GUILayout.Width(80)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select External Prefabs Folder", "", "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                sourceFolderPath = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(20);

        EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(sourceFolderPath) || !Directory.Exists(sourceFolderPath));
        if (GUILayout.Button("Import to Resources", GUILayout.Height(30)))
        {
            ImportPrefabs();
        }
        EditorGUI.EndDisabledGroup();
    }

    private void ImportPrefabs()
    {
        if (string.IsNullOrEmpty(sourceFolderPath) || !Directory.Exists(sourceFolderPath))
        {
            EditorUtility.DisplayDialog("Error", "Invalid source folder path.", "OK");
            return;
        }

        string targetResourcesPath = Application.dataPath + "/Resources";
        
        // Ensure Resources folder exists
        if (!Directory.Exists(targetResourcesPath))
        {
            Directory.CreateDirectory(targetResourcesPath);
        }

        try
        {
            int copiedFiles = CopyDirectory(sourceFolderPath, targetResourcesPath);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Success", $"Successfully imported {copiedFiles} files into Assets/Resources.", "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[GAMA] Error importing prefabs: {e.Message}");
            EditorUtility.DisplayDialog("Error", $"Failed to import prefabs. See console for details.\n\n{e.Message}", "OK");
        }
    }

    private int CopyDirectory(string sourceDir, string targetDir)
    {
        int fileCount = 0;
        DirectoryInfo dir = new DirectoryInfo(sourceDir);

        if (!dir.Exists) return 0;

        // If the target directory doesn't exist, create it
        DirectoryInfo[] dirs = dir.GetDirectories();
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Get the files in the directory and copy them to the new location
        FileInfo[] files = dir.GetFiles();
        foreach (FileInfo file in files)
        {
            if (file.Extension.ToLowerInvariant() == ".meta")
            {
                continue;
            }

            string targetFilePath = Path.Combine(targetDir, file.Name);
            file.CopyTo(targetFilePath, true);
            fileCount++;
        }

        // Copy subdirectories and their contents to new location
        foreach (DirectoryInfo subdir in dirs)
        {
            string newTargetDir = Path.Combine(targetDir, subdir.Name);
            fileCount += CopyDirectory(subdir.FullName, newTargetDir);
        }

        return fileCount;
    }
}
