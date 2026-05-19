using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

public static class GamaSpeciesRenderOverridesEditorStore
{
    public const string DefaultAssetPath = "Assets/GAMA/Config/GamaSpeciesRenderOverrides.asset";

    public static GamaSpeciesRenderOverrides GetOrCreateDefaultAsset()
    {
#if UNITY_EDITOR
        GamaSpeciesRenderOverrides asset = AssetDatabase.LoadAssetAtPath<GamaSpeciesRenderOverrides>(DefaultAssetPath);
        if (asset != null)
        {
            return asset;
        }

        string folder = Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
        {
            EnsureFolderHierarchy(folder);
        }

        GamaSpeciesRenderOverridesAsset created = ScriptableObject.CreateInstance<GamaSpeciesRenderOverridesAsset>();
        AssetDatabase.CreateAsset(created, DefaultAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[GAMA][WIZARD] Created default overrides asset: " + DefaultAssetPath);
        return created;
#else
        return null;
#endif
    }

#if UNITY_EDITOR
    private static void EnsureFolderHierarchy(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
#endif
}
