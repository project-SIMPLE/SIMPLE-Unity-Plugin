using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;


[System.Serializable]
public class AllProperties
{
   public List<PropertiesGAMA> properties;

    public static AllProperties CreateFromJSON(string jsonString)
    {
       AllProperties parsed = JsonUtility.FromJson<AllProperties>(jsonString);
       if (parsed == null || parsed.properties == null)
       {
           return parsed;
       }

       // JsonUtility is strict and can ignore color fields when they are not
       // encoded as the expected static type. We parse again with Newtonsoft
       // to recover dynamic color formats (hex, named colors, nested objects, etc.).
       try
       {
           JObject root = JObject.Parse(jsonString);
           JArray propsArray = root["properties"] as JArray;
           if (propsArray == null)
           {
               return parsed;
           }

           int count = Mathf.Min(parsed.properties.Count, propsArray.Count);
           for (int i = 0; i < count; i++)
           {
               PropertiesGAMA p = parsed.properties[i];
               if (p == null)
               {
                   continue;
               }

               Attributes a = Attributes.FromToken(propsArray[i]);
               Color32 c;
               if (a != null && a.TryGetColor(out c))
               {
                   p.hasDynamicColor = true;
                   p.dynamicRed = c.r;
                   p.dynamicGreen = c.g;
                   p.dynamicBlue = c.b;
                   p.dynamicAlpha = c.a;
               }
           }
       }
       catch
       {
           // Keep default parsing path if dynamic color extraction fails.
       }

       return parsed;
    }
}

[System.Serializable]
public class PropertiesGAMA
{
    [System.Serializable]
    public class VisualRecipe
    {
        public string kind;
        public List<VisualPart> parts;
    }

    [System.Serializable]
    public class VisualPart
    {
        public string primitive;
        public List<float> size;
        public List<float> offset;
        public List<float> rotation;
        public bool scaleWithPrecision = true;
    }

    public string id;
    public bool hasCollider;
    public string tag;
    public List<bool> constraints;
  
    public bool isInteractable;
    public bool isGrabable;

    public bool hasPrefab;
    public string prefab;

    public int size;
    public int yOffset;
    public int rotationCoeff;
    public int rotationOffset;
    public bool visible = true;

    public float yOffsetF;
    public float rotationCoeffF;
    public float rotationOffsetF;

    public int height;
    public bool is3D;
    public List<int> color;
    public List<int> rgb;
    public List<int> rgba;
    public List<float> colorFloat;
    public List<float> rgbFloat;
    public List<float> rgbaFloat;

    public string material;
    public string hexColor;
    public string colorHex;
    public string colorString;
    public string colorName;
    public string colour;
    public int packedColor;
    public int colorInt;
    public int rgbInt;
    public int rgbaInt;

    public int red;
    public int green;
    public int blue;
    public int alpha;

    public bool toFollow;
    public GameObject prefabObj = null;
    public VisualRecipe visual;

    // Filled from dynamic Newtonsoft parsing in AllProperties.CreateFromJSON.
    public bool hasDynamicColor = false;
    public int dynamicRed = -1;
    public int dynamicGreen = -1;
    public int dynamicBlue = -1;
    public int dynamicAlpha = -1;


    public void loadPrefab(int precision)
    {
        PrepareRuntime(precision);
    }

    public void PrepareRuntime(int precision)
    {
        int safePrecision = Mathf.Max(precision, 1);
        yOffsetF = (0.0f + yOffset) / safePrecision;
        rotationCoeffF = (0.0f + rotationCoeff) / safePrecision;
        rotationOffsetF = (0.0f + rotationOffset) / safePrecision;

        if (prefabObj == null && prefab != null && !prefab.Equals(""))
        {
            prefabObj = Resources.Load(prefab) as GameObject;
        }
    }

    public float GetUnityScale(int precision)
    {
        return (0.0f + size) / Mathf.Max(precision, 1);
    }

    public Color32 GetUnityColor()
    {
        Color32 colorValue;
        return TryGetUnityColor(out colorValue) ? colorValue : GamaColorUtility.DefaultVisibleColor;
    }

    public bool TryGetUnityColor(out Color32 colorValue)
    {
        if (GamaColorUtility.TryFromIntList(rgba, out colorValue) ||
            GamaColorUtility.TryFromIntList(rgb, out colorValue) ||
            GamaColorUtility.TryFromIntList(color, out colorValue) ||
            GamaColorUtility.TryFromFloatList(rgbaFloat, out colorValue) ||
            GamaColorUtility.TryFromFloatList(rgbFloat, out colorValue) ||
            GamaColorUtility.TryFromFloatList(colorFloat, out colorValue) ||
            GamaColorUtility.TryParseString(hexColor, out colorValue) ||
            GamaColorUtility.TryParseString(colorHex, out colorValue) ||
            GamaColorUtility.TryParseString(colorString, out colorValue) ||
            GamaColorUtility.TryParseString(colorName, out colorValue) ||
            GamaColorUtility.TryParseString(colour, out colorValue) ||
            GamaColorUtility.TryFromPackedInt(packedColor, out colorValue) ||
            GamaColorUtility.TryFromPackedInt(colorInt, out colorValue) ||
            GamaColorUtility.TryFromPackedInt(rgbInt, out colorValue) ||
            GamaColorUtility.TryFromPackedInt(rgbaInt, out colorValue))
        {
            return true;
        }

        if (red == 0 && green == 0 && blue == 0 && alpha == 0)
        {
            colorValue = default(Color32);
            return false;
        }

        colorValue = GamaColorUtility.FromChannels(red, green, blue, alpha);
        return true;
    }
}
