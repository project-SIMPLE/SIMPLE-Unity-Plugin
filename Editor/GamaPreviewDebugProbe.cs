#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class GamaPreviewDebugProbe
{
    [MenuItem("GAMA/Debug/Probe Static Preview")]
    public static void ProbeStaticPreview()
    {
        var root = FindPreviewRoot();

        Debug.Log("[GAMA][PROBE] Preview root = " + (root ? GetPath(root.transform) : "NULL"));

        if (!root)
            return;

        var gama = root.transform.Find("GAMA");
        Debug.Log("[GAMA][PROBE] GAMA child = " + (gama ? GetPath(gama) : "NULL"));

        if (gama != null)
        {
            Debug.Log("[GAMA][PROBE] Children under GAMA:");

            foreach (Transform child in gama)
            {
                PrintSpecies(child);
            }
        }

        var pedestrian = root.transform.Find("GAMA/pedestrian");
        Debug.Log("[GAMA][PROBE] Direct path GAMA/pedestrian = " + (pedestrian ? GetPath(pedestrian) : "NULL"));

        if (pedestrian != null)
            PrintSpecies(pedestrian);

        SceneView.RepaintAll();
    }

    [MenuItem("GAMA/Debug/Force pedestrian red x10")]
    public static void ForcePedestrianRedX10()
    {
        var root = FindPreviewRoot();

        if (!root)
        {
            Debug.LogWarning("[GAMA][PROBE] Cannot force: preview root not found");
            return;
        }

        var pedestrian = root.transform.Find("GAMA/pedestrian");

        if (!pedestrian)
        {
            Debug.LogWarning("[GAMA][PROBE] Cannot force: GAMA/pedestrian not found");
            return;
        }

        pedestrian.localScale = Vector3.one * 10f;

        var renderers = pedestrian.GetComponentsInChildren<Renderer>(true);
        var block = new MaterialPropertyBlock();

        foreach (var renderer in renderers)
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", Color.red);
            block.SetColor("_Color", Color.red);
            renderer.SetPropertyBlock(block);
            EditorUtility.SetDirty(renderer);
        }

        EditorUtility.SetDirty(pedestrian);
        SceneView.RepaintAll();

        Debug.Log("[GAMA][PROBE] Forced pedestrian red x10. Renderers found = " + renderers.Length);
    }

    private static void PrintSpecies(Transform species)
    {
        var renderers = species.GetComponentsInChildren<Renderer>(true);

        Debug.Log(
            "[GAMA][PROBE] species=" + species.name +
            " path=" + GetPath(species) +
            " active=" + species.gameObject.activeInHierarchy +
            " childCount=" + species.childCount +
            " renderers=" + renderers.Length +
            " localScale=" + species.localScale
        );

        int max = Mathf.Min(renderers.Length, 5);

        for (int i = 0; i < max; i++)
        {
            Debug.Log(
                "[GAMA][PROBE] renderer[" + i + "]=" +
                GetPath(renderers[i].transform) +
                " material=" + (renderers[i].sharedMaterial ? renderers[i].sharedMaterial.name : "NULL")
            );
        }
    }

    private static GameObject FindPreviewRoot()
    {
        var root = GameObject.Find("[GAMA] Static Experiment Preview");

        if (root)
            return root;

        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (go.name == "[GAMA] Static Experiment Preview" && !EditorUtility.IsPersistent(go))
                return go;
        }

        return null;
    }

    private static string GetPath(Transform t)
    {
        if (!t)
            return "NULL";

        string path = t.name;

        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
#endif
