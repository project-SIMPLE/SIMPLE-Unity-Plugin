using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;


[System.Serializable]
public class WorldJSONInfo
{

    public List<int> position;
    public List<string> names;
    public List<string> keepNames;
    public List<string> propertyID;
    public List<GAMAPoint> pointsLoc;

    public List<Attributes> attributes;


    public List<int> offsetYGeom;

    public List<GAMAPoint> pointsGeom;

    public List<int> ranking;
    public List<string> players;
    public int numTokens;
    public bool isInit;

    public static WorldJSONInfo CreateFromJSON(string jsonString)
    {
        WorldJSONInfo info = JsonUtility.FromJson<WorldJSONInfo>(jsonString);

        // Parse attributes via Newtonsoft (JsonUtility can't handle the dynamic Attributes type)
        // Wrapped in try-catch so a malformed attributes field cannot crash the entire message pipeline
        try
        {
            JObject root = JObject.Parse(jsonString);
            JArray attributesArray = root["attributes"] as JArray;
            if (attributesArray != null)
            {
                info.attributes = Attributes.FromJsonArray(attributesArray);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GAMA] Could not parse attributes from world JSON: " + ex.Message);
        }

        return info;
    }

    public object getAttributeValue(string name, string attribute)
    {
        if (attributes == null || names == null)
        {
            return null;
        }

        int index = names.IndexOf(name);
        if (index < 0 || index >= attributes.Count)
        {
            return null;
        }

        Newtonsoft.Json.Linq.JToken value;
        return attributes[index].TryGetValue(attribute, out value) ? value : null;
    }

} 


[System.Serializable]
public class GAMAPoint
{
    public List<int> c;
}
