using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

public static class GamaVisualUtility
{
    private static readonly string[] PackagePaths =
    {
        "Packages/com.project-simple.gama-unity",
        "Packages/com.project-simple.unity-plugin"
    };

    private static readonly Color32 DefaultFallbackColor = new Color32(180, 180, 180, 255);
    private static readonly HashSet<string> MissingPrefabWarnings = new HashSet<string>();
    private static readonly Dictionary<string, GameObject> PrefabCache = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

    public static GameObject InstantiateVisual(string name, PropertiesGAMA prop, int precision)
    {
        if (prop == null)
        {
            GameObject empty = new GameObject(name);
            return empty;
        }

        GameObject prefab = prop.prefabObj != null ? prop.prefabObj : ResolvePrefab(prop.prefab);
        prop.prefabObj = prefab;

        GameObject obj;
        bool scaleAlreadyApplied;
        if (prefab != null)
        {
            obj = UnityEngine.Object.Instantiate(prefab);
            obj.name = name;
            scaleAlreadyApplied = false;
        }
        else
        {
            obj = CreateFallbackVisual(name, prop, precision, out scaleAlreadyApplied);
            WarnMissingPrefabOnce(prop.prefab, name, obj.name);
        }

        ApplyVisualProperties(obj, prop, precision, !scaleAlreadyApplied);
        obj.SetActive(true);
        return obj;
    }

    public static GameObject ResolvePrefab(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return null;
        }

        string cacheKey = NormalizePath(prefabPath);
        GameObject cachedPrefab;
        if (PrefabCache.TryGetValue(cacheKey, out cachedPrefab))
        {
            return cachedPrefab;
        }

        GameObject prefab = ResolveFromResources<GameObject>(prefabPath);
        if (prefab != null)
        {
            PrefabCache[cacheKey] = prefab;
            return prefab;
        }

#if UNITY_EDITOR
        prefab = ResolveFromAssetDatabase<GameObject>(prefabPath, ".prefab", "t:Prefab");
        PrefabCache[cacheKey] = prefab;
        return prefab;
#else
        PrefabCache[cacheKey] = null;
        return null;
#endif
    }

    public static Material ResolveMaterial(string materialPath)
    {
        if (string.IsNullOrEmpty(materialPath))
        {
            return null;
        }

        string cacheKey = NormalizePath(materialPath);
        Material cachedMaterial;
        if (MaterialCache.TryGetValue(cacheKey, out cachedMaterial))
        {
            return cachedMaterial;
        }

        Material material = ResolveFromResources<Material>(materialPath);
        if (material != null)
        {
            MaterialCache[cacheKey] = material;
            return material;
        }

#if UNITY_EDITOR
        material = ResolveFromAssetDatabase<Material>(materialPath, ".mat", "t:Material");
        MaterialCache[cacheKey] = material;
        return material;
#else
        MaterialCache[cacheKey] = null;
        return null;
#endif
    }

    public static Color32 GetColor(PropertiesGAMA prop)
    {
        if (prop == null)
        {
            return Color.white;
        }

        if (!prop.visible)
        {
            return new Color32(0, 0, 0, 0);
        }

        Color32 color;
        if (TryGetColor(prop, out color))
        {
            return color;
        }

        return DefaultFallbackColor;
    }

    /// <summary>
    /// Vrai si les properties GAMA contiennent une couleur utilisable (liste rgb / champs red,green,blue, etc.).
    /// Les prefabs sans RGB dans le message retournent faux.
    /// </summary>
    public static bool PropertiesMessageIncludesExplicitTint(PropertiesGAMA prop)
    {
        Color32 color;
        return prop != null && prop.visible && TryGetColor(prop, out color);
    }

    public static Material CreateMaterial(PropertiesGAMA prop)
    {
        Material source = ResolveMaterial(prop != null ? prop.material : null);
        Material material = source != null ? new Material(source) : new Material(FindDefaultShader());
        Color32 color = GetColor(prop);

        ApplyColor(material, color);
        if (color.a < 255)
        {
            ConfigureTransparentMaterial(material);
        }

        return material;
    }

    public static void ApplyVisualProperties(GameObject obj, PropertiesGAMA prop, int precision, bool applyScale)
    {
        if (obj == null || prop == null)
        {
            return;
        }

        if (applyScale)
        {
            ApplyUniformScale(obj, prop, precision);
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }

        Color32 explicitTintIgnored;
        bool hasExplicitTint = TryGetColor(prop, out explicitTintIgnored);
        bool importedPrefab = prop.prefabObj != null;
        bool preservePrefabMaterials =
            prop.visible &&
            importedPrefab &&
            !hasExplicitTint;

        Material material = prop.visible && !preservePrefabMaterials ? CreateMaterial(prop) : null;

        if (preservePrefabMaterials)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = true;
            }

            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = prop.visible;
            if (prop.visible)
            {
                Material[] materials = renderers[i].sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderers[i].sharedMaterial = material;
                }
                else
                {
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                    {
                        materials[materialIndex] = material;
                    }

                    renderers[i].sharedMaterials = materials;
                }
            }
        }
    }

    public static void ApplyColor(GameObject obj, Color32 color)
    {
        if (obj == null)
        {
            return;
        }

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.enabled = color.a > 0;
            EnsureRendererHasMaterial(renderer);

            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            Color unityColor = color;
            propertyBlock.SetColor("_BaseColor", unityColor);
            propertyBlock.SetColor("_Color", unityColor);
            propertyBlock.SetColor("_MainColor", unityColor);
            propertyBlock.SetColor("_TintColor", unityColor);
            renderer.SetPropertyBlock(propertyBlock);

            Material[] materials = renderer.materials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                ApplyColor(materials[materialIndex], color);
            }
        }
    }

    public static float GetScaledValue(int value, int precision, float fallback)
    {
        if (value <= 0 || precision <= 0)
        {
            return fallback;
        }

        return (float)value / precision;
    }

    private static GameObject CreateFallbackVisual(string name, PropertiesGAMA prop, int precision, out bool scaleAlreadyApplied)
    {
        string prefabDescriptor = (prop.prefab ?? string.Empty).ToLowerInvariant();
        string descriptor = (prefabDescriptor + " " + (prop.tag ?? string.Empty) + " " + name).ToLowerInvariant();

        if (IsVehicleDescriptor(prefabDescriptor) || IsVehicleDescriptor(descriptor))
        {
            scaleAlreadyApplied = true;
            return CreateVehicleFallback(name, prop, precision);
        }

        if (descriptor.Contains("pedestrian") || descriptor.Contains("character") ||
            descriptor.Contains("person") || descriptor.Contains("people") ||
            descriptor.Contains("ghost"))
        {
            scaleAlreadyApplied = false;
            return CreateCharacterFallback(name);
        }

        if (descriptor.Contains("wall") || descriptor.Contains("obstacle"))
        {
            scaleAlreadyApplied = true;
            return CreateBoxFallback(name, prop, precision, true);
        }

        scaleAlreadyApplied = true;
        return CreateBoxFallback(name, prop, precision, false);
    }

    private static bool IsVehicleDescriptor(string descriptor)
    {
        return descriptor.Contains("vehicle") ||
               descriptor.Contains("car") ||
               descriptor.Contains("scooter") ||
               descriptor.Contains("moto") ||
               descriptor.Contains("motorcycle") ||
               descriptor.Contains("truck") ||
               descriptor.Contains("bus") ||
               descriptor.Contains("bike") ||
               descriptor.Contains("bicycle");
    }

    private static GameObject CreateCharacterFallback(string name)
    {
        GameObject root = new GameObject(name);

        GameObject body = CreatePrimitiveChild(root, "Body", PrimitiveType.Capsule);
        body.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        body.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);

        GameObject head = CreatePrimitiveChild(root, "Head", PrimitiveType.Sphere);
        head.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        head.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.center = new Vector3(0f, 0.75f, 0f);
        collider.height = 1.5f;
        collider.radius = 0.35f;

        return root;
    }

    private static GameObject CreateVehicleFallback(string name, PropertiesGAMA prop, int precision)
    {
        float length = GetScaledValue(prop.size, precision, 1f);
        float width = Mathf.Max(length * 0.45f, 0.25f);
        float bodyHeight = Mathf.Max(length * 0.22f, 0.2f);
        float cabinHeight = Mathf.Max(length * 0.16f, 0.15f);
        float wheelRadius = Mathf.Max(length * 0.12f, 0.08f);
        float wheelThickness = Mathf.Max(width * 0.18f, 0.05f);

        GameObject root = new GameObject(name);

        GameObject body = CreatePrimitiveChild(root, "Body", PrimitiveType.Cube);
        body.transform.localPosition = new Vector3(0f, wheelRadius + bodyHeight * 0.5f, 0f);
        body.transform.localScale = new Vector3(width, bodyHeight, length);

        GameObject cabin = CreatePrimitiveChild(root, "Cabin", PrimitiveType.Cube);
        cabin.transform.localPosition = new Vector3(0f, wheelRadius + bodyHeight + cabinHeight * 0.5f, -length * 0.08f);
        cabin.transform.localScale = new Vector3(width * 0.75f, cabinHeight, length * 0.38f);

        float wheelX = width * 0.55f;
        float wheelZ = length * 0.32f;
        CreateWheel(root, "FrontLeftWheel", -wheelX, wheelRadius, wheelZ, wheelRadius, wheelThickness);
        CreateWheel(root, "FrontRightWheel", wheelX, wheelRadius, wheelZ, wheelRadius, wheelThickness);
        CreateWheel(root, "RearLeftWheel", -wheelX, wheelRadius, -wheelZ, wheelRadius, wheelThickness);
        CreateWheel(root, "RearRightWheel", wheelX, wheelRadius, -wheelZ, wheelRadius, wheelThickness);

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, wheelRadius + (bodyHeight + cabinHeight) * 0.5f, 0f);
        collider.size = new Vector3(width, bodyHeight + cabinHeight + wheelRadius, length);

        return root;
    }

    private static void CreateWheel(
        GameObject parent,
        string wheelName,
        float x,
        float y,
        float z,
        float radius,
        float thickness)
    {
        GameObject wheel = CreatePrimitiveChild(parent, wheelName, PrimitiveType.Cylinder);
        wheel.transform.localPosition = new Vector3(x, y, z);
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheel.transform.localScale = new Vector3(radius * 2f, thickness, radius * 2f);
    }

    private static GameObject CreateBoxFallback(string name, PropertiesGAMA prop, int precision, bool wallLike)
    {
        float width = GetScaledValue(prop.size, precision, 1f);
        float height = GetScaledValue(prop.height, precision, wallLike ? 2f : width);
        float depth = width;

        GameObject root = new GameObject(name);
        GameObject cube = CreatePrimitiveChild(root, "Visual", PrimitiveType.Cube);
        cube.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
        cube.transform.localScale = new Vector3(width, height, depth);

        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = cube.transform.localPosition;
        collider.size = cube.transform.localScale;

        return root;
    }

    private static GameObject CreatePrimitiveChild(GameObject parent, string childName, PrimitiveType primitiveType)
    {
        GameObject child = GameObject.CreatePrimitive(primitiveType);
        child.name = childName;
        child.transform.SetParent(parent.transform, false);

        Collider collider = child.GetComponent<Collider>();
        if (collider != null)
        {
            DestroyComponent(collider);
        }

        return child;
    }

    private static void ApplyUniformScale(GameObject obj, PropertiesGAMA prop, int precision)
    {
        if (prop.size <= 0 || precision <= 0)
        {
            return;
        }

        float scale = (float)prop.size / precision;
        obj.transform.localScale = new Vector3(scale, scale, scale);
    }

    private static void EnsureRendererHasMaterial(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        Material[] materials = renderer.sharedMaterials;
        if (materials != null && materials.Length > 0)
        {
            return;
        }

        renderer.sharedMaterial = new Material(FindDefaultShader());
    }

    private static Shader FindDefaultShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            return shader;
        }

        shader = Shader.Find("Standard");
        if (shader != null)
        {
            return shader;
        }

        shader = Shader.Find("Diffuse");
        if (shader != null)
        {
            return shader;
        }

        shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            return shader;
        }

        return Shader.Find("Hidden/InternalErrorShader");
    }

    private static void ApplyColor(Material material, Color32 color)
    {
        if (material == null)
        {
            return;
        }

        Color unityColor = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", unityColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", unityColor);
        }

        if (material.HasProperty("_MainColor"))
        {
            material.SetColor("_MainColor", unityColor);
        }

        if (material.HasProperty("_TintColor"))
        {
            material.SetColor("_TintColor", unityColor);
        }
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static T ResolveFromResources<T>(string assetPath) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        foreach (string resourcePath in GetResourcePathCandidates(assetPath))
        {
            T asset = Resources.Load<T>(resourcePath);
            if (asset != null)
            {
                return asset;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetResourcePathCandidates(string assetPath)
    {
        string normalized = NormalizePath(assetPath);
        string withoutExtension = StripKnownExtension(normalized);

        yield return ToResourcePath(withoutExtension);
        yield return Path.GetFileNameWithoutExtension(withoutExtension);
    }

    private static string ToResourcePath(string assetPath)
    {
        string normalized = NormalizePath(assetPath);
        int resourcesIndex = normalized.LastIndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex >= 0)
        {
            return normalized.Substring(resourcesIndex + "/Resources/".Length);
        }

        if (normalized.StartsWith("Resources/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring("Resources/".Length);
        }

        if (normalized.StartsWith("Assets/Resources/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring("Assets/Resources/".Length);
        }

        if (normalized.StartsWith("Runtime/Resources/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring("Runtime/Resources/".Length);
        }

        return normalized;
    }

#if UNITY_EDITOR
    private static T ResolveFromAssetDatabase<T>(string assetPath, string extension, string typeFilter) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        string normalized = StripKnownExtension(NormalizePath(assetPath));
        List<string> candidates = new List<string>();

        AddAssetPathCandidates(candidates, normalized, extension);
        AddAssetPathCandidates(candidates, "Assets/" + normalized, extension);
        AddAssetPathCandidates(candidates, "Assets/Resources/" + normalized, extension);
        for (int i = 0; i < PackagePaths.Length; i++)
        {
            AddAssetPathCandidates(candidates, PackagePaths[i] + "/" + normalized, extension);
            AddAssetPathCandidates(candidates, PackagePaths[i] + "/Runtime/Resources/" + normalized, extension);
            AddAssetPathCandidates(candidates, PackagePaths[i] + "/Samples~/" + normalized, extension);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(candidates[i]);
            if (asset != null)
            {
                return asset;
            }
        }

        string fileName = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        string[] guids = AssetDatabase.FindAssets(fileName + " " + typeFilter);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!MatchesRequestedAsset(path, normalized))
            {
                continue;
            }

            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }
        }

        return null;
    }

    private static void AddAssetPathCandidates(List<string> candidates, string pathWithoutExtension, string extension)
    {
        if (string.IsNullOrEmpty(pathWithoutExtension))
        {
            return;
        }

        string normalized = NormalizePath(pathWithoutExtension);
        if (!candidates.Contains(normalized))
        {
            candidates.Add(normalized);
        }

        string withExtension = normalized + extension;
        if (!candidates.Contains(withExtension))
        {
            candidates.Add(withExtension);
        }
    }

    private static bool MatchesRequestedAsset(string assetPath, string requestedPath)
    {
        string normalizedAsset = StripKnownExtension(NormalizePath(assetPath)).ToLowerInvariant();
        string normalizedRequested = StripKnownExtension(NormalizePath(requestedPath)).ToLowerInvariant();
        string requestedFileName = Path.GetFileNameWithoutExtension(normalizedRequested);

        return normalizedAsset.EndsWith(normalizedRequested, StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileNameWithoutExtension(normalizedAsset).Equals(requestedFileName, StringComparison.OrdinalIgnoreCase);
    }
#endif

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').Trim().Trim('/');
    }

    private static string StripKnownExtension(string path)
    {
        string normalized = NormalizePath(path);
        string extension = Path.GetExtension(normalized);
        if (extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".mat", StringComparison.OrdinalIgnoreCase))
        {
            return normalized.Substring(0, normalized.Length - extension.Length);
        }

        return normalized;
    }

    private static bool TryGetColor(PropertiesGAMA prop, out Color32 color)
    {
        color = default(Color32);

        if (TryGetColorFromIntList(prop.color, out color) ||
            TryGetColorFromFloatList(prop.rgba, out color) ||
            TryGetColorFromFloatList(prop.rgb, out color))
        {
            return true;
        }

        if (IsMissingPrefabColor(prop) || prop.red < 0 || prop.green < 0 || prop.blue < 0)
        {
            return false;
        }

        int alpha = prop.alpha <= 0 ? 255 : prop.alpha;
        color = new Color32(
            ClampColor(prop.red),
            ClampColor(prop.green),
            ClampColor(prop.blue),
            ClampColor(alpha));
        return true;
    }

    private static bool IsMissingPrefabColor(PropertiesGAMA prop)
    {
        return prop.hasPrefab &&
               prop.red == 0 &&
               prop.green == 0 &&
               prop.blue == 0 &&
               prop.alpha == 0 &&
               (prop.color == null || prop.color.Count == 0) &&
               (prop.rgb == null || prop.rgb.Count == 0) &&
               (prop.rgba == null || prop.rgba.Count == 0);
    }

    private static bool TryGetColorFromIntList(List<int> values, out Color32 color)
    {
        color = default(Color32);
        if (values == null || values.Count < 3)
        {
            return false;
        }

        int alpha = values.Count > 3 ? values[3] : 255;
        color = new Color32(
            ClampColor(values[0]),
            ClampColor(values[1]),
            ClampColor(values[2]),
            ClampColor(alpha <= 0 ? 255 : alpha));
        return true;
    }

    private static bool TryGetColorFromFloatList(List<float> values, out Color32 color)
    {
        color = default(Color32);
        if (values == null || values.Count < 3)
        {
            return false;
        }

        float alpha = values.Count > 3 ? values[3] : 1f;
        color = new Color32(
            ClampColorComponent(values[0]),
            ClampColorComponent(values[1]),
            ClampColorComponent(values[2]),
            ClampColorComponent(alpha <= 0 ? 1f : alpha));
        return true;
    }

    private static byte ClampColorComponent(float value)
    {
        if (value >= 0f && value <= 1f)
        {
            value *= 255f;
        }

        return (byte)Mathf.Clamp(Mathf.RoundToInt(value), 0, 255);
    }

    private static byte ClampColor(int value)
    {
        return (byte)Mathf.Clamp(value, 0, 255);
    }

    private static void DestroyComponent(Component component)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEngine.Object.DestroyImmediate(component);
            return;
        }
#endif
        UnityEngine.Object.Destroy(component);
    }

    private static void WarnMissingPrefabOnce(string prefabPath, string objectName, string fallbackName)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            return;
        }

        if (MissingPrefabWarnings.Add(prefabPath))
        {
            Debug.LogWarning("[GAMA] Prefab '" + prefabPath + "' not found for '" + objectName +
                             "'. Generated procedural fallback '" + fallbackName + "' with GAMA visual properties.");
        }
    }
}
