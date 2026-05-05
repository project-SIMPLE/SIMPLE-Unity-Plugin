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
        if (info == null)
        {
            return null;
        }

        try
        {
            JObject root = JObject.Parse(jsonString);
            JArray attributesArray = root["attributes"] as JArray;
            if (attributesArray != null)
            {
                info.attributes = Attributes.FromJsonArray(attributesArray);
            }
        }
        catch
        {
            // Keep the JsonUtility result when the optional dynamic attributes cannot be parsed.
        }

        return info;
    }

    public object getAttributeValue(string name, string attribute)
    {
        return attributes[names.IndexOf(name)];
    }

    public Attributes GetAttributesAt(int index)
    {
        if (attributes == null || index < 0 || index >= attributes.Count)
        {
            return null;
        }

        return attributes[index];
    }

} 


[System.Serializable]
public class GAMAPoint
{
    public List<int> c;
}

