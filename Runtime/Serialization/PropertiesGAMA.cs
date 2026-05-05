using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class AllProperties
{
   public List<PropertiesGAMA> properties;

    public static AllProperties CreateFromJSON(string jsonString)
    {
       return JsonUtility.FromJson<AllProperties>(jsonString);
    }
}

[System.Serializable]
public class PropertiesGAMA
{
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
    // public List<int> color;


    public string material;

    public int red;
    public int green;
    public int blue;
    public int alpha;

    public bool toFollow;
    public GameObject prefabObj = null;


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
        return new Color32(ToByte(red), ToByte(green), ToByte(blue), ToByte(alpha));
    }

    private static byte ToByte(int value)
    {
        return (byte)Mathf.Clamp(value, 0, 255);
    }

}
