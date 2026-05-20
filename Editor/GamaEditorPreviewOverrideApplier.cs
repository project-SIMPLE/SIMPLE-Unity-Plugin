using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class GamaEditorPreviewOverrideApplier
{
    [InitializeOnLoadMethod]
    private static void Init()
    {
        GamaSpeciesWizard.OnWizardSettingsChanged += HandleWizardSettingsChanged;
        GamaSpeciesWizard.GetDefaultOverridesAsset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset;
    }

    private static void HandleWizardSettingsChanged()
    {
        ScheduleApplyOverridesToCurrentPreview();
    }

    private static bool isUpdateQueued = false;

    public static void ScheduleApplyOverridesToCurrentPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Debug.Log("[GAMA][PREVIEW][AUTO] Schedule requested");
        if (!isUpdateQueued)
        {
            isUpdateQueued = true;
            EditorApplication.delayCall += () =>
            {
                isUpdateQueued = false;
                ApplyOverridesToCurrentPreview();
            };
        }
    }

    public static void ApplyOverridesToCurrentPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        Debug.Log("[GAMA][PREVIEW][AUTO] Apply called");

        GameObject root = GameObject.Find("[GAMA] Static Experiment Preview");
        if (root == null)
        {
            Debug.LogWarning("[GAMA][PREVIEW][AUTO] no preview root found");
            return;
        }

        Debug.Log("[GAMA][PREVIEW][AUTO] root found = " + root.name);

        GamaPreviewSession session = root.GetComponent<GamaPreviewSession>();
        GamaSpeciesRenderOverrides asset = session != null ? session.speciesOverrides : null;

        if (asset == null)
        {
            asset = GamaSpeciesRenderOverridesEditorStore.GetOrCreateDefaultAsset();
        }

        GamaPreviewObject[] previewObjects = root.GetComponentsInChildren<GamaPreviewObject>(true);
        Debug.Log("[GAMA][PREVIEW][AUTO] previewObjects found = " + previewObjects.Length);

        // Group by species
        Dictionary<string, List<GamaPreviewObject>> objectsBySpecies = new Dictionary<string, List<GamaPreviewObject>>();
        foreach (var obj in previewObjects)
        {
            if (string.IsNullOrWhiteSpace(obj.speciesName)) continue;
            if (!objectsBySpecies.ContainsKey(obj.speciesName))
            {
                objectsBySpecies[obj.speciesName] = new List<GamaPreviewObject>();
            }
            objectsBySpecies[obj.speciesName].Add(obj);
        }

        Transform gamaFolder = root.transform.Find("GAMA");
        if (gamaFolder == null) gamaFolder = root.transform;

        int totalUpdated = 0;

        foreach (var entry in asset.entries)
        {
            string speciesName = entry.speciesName;
            if (string.IsNullOrWhiteSpace(speciesName)) continue;

            int updatedObjects = 0;
            int updatedRenderers = 0;

            if (objectsBySpecies.TryGetValue(speciesName, out List<GamaPreviewObject> list) && list.Count > 0)
            {
                // Normal path
                Undo.RecordObjects(list.ToArray(), "Apply Overrides to GamaPreviewObjects");
                foreach (var obj in list)
                {
                    obj.ApplySpeciesOverride(entry);
                    EditorUtility.SetDirty(obj);
                    updatedObjects++;
                    updatedRenderers += obj.GetComponentsInChildren<Renderer>(true).Length;
                }
            }
            else
            {
                // Fallback path
                Transform speciesFolder = gamaFolder.Find(speciesName);
                if (speciesFolder != null)
                {
                    Renderer[] renderers = speciesFolder.GetComponentsInChildren<Renderer>(true);
                    Undo.RecordObjects(renderers, "Apply Overrides Renderers");

                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    foreach (var r in renderers)
                    {
                        r.GetPropertyBlock(block);
                        if (entry.overrideColor)
                        {
                            block.SetColor("_BaseColor", entry.color);
                            block.SetColor("_Color", entry.color);
                        }
                        r.SetPropertyBlock(block);
                        EditorUtility.SetDirty(r);
                        updatedRenderers++;

                        Transform t = r.transform;
                        Undo.RecordObject(t, "Apply Overrides Transform");

                        // Try to find the immediate child of speciesFolder to scale/offset
                        Transform childRoot = t;
                        while (childRoot.parent != null && childRoot.parent != speciesFolder)
                        {
                            childRoot = childRoot.parent;
                        }

                        if (childRoot != null && childRoot.parent == speciesFolder)
                        {
                            Undo.RecordObject(childRoot, "Apply Overrides Transform Root");
                            childRoot.localScale = Vector3.one * entry.scaleMultiplier;
                            childRoot.localRotation = Quaternion.Euler(entry.rotationOffsetEuler);
                            childRoot.localPosition = entry.positionOffset;
                            EditorUtility.SetDirty(childRoot);
                            updatedObjects++; // Roughly counts the objects
                        }
                    }
                }
                else
                {
                    Debug.Log("[GAMA][PREVIEW][AUTO] no override found for species=" + speciesName + " (no objects and no folder)");
                }
            }

            if (updatedObjects > 0 || updatedRenderers > 0)
            {
                Debug.Log($"[GAMA][PREVIEW][AUTO] species={speciesName} updated objects={updatedObjects} renderers={updatedRenderers}");
                totalUpdated += updatedObjects;
            }
        }

        if (totalUpdated > 0)
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
