using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;


[System.Serializable]
public class Attributes
{
    public int type;

    [NonSerialized] private Dictionary<string, JToken> values;

    public static List<Attributes> FromJsonArray(JArray array)
    {
        List<Attributes> result = new List<Attributes>();
        if (array == null)
        {
            return result;
        }

        for (int i = 0; i < array.Count; i++)
        {
            result.Add(FromToken(array[i]));
        }

        return result;
    }

    public static Attributes FromToken(JToken token)
    {
        Attributes attributes = new Attributes();
        JObject obj = token as JObject;
        if (obj == null)
        {
            return attributes;
        }

        attributes.EnsureValues();
        foreach (JProperty property in obj.Properties())
        {
            attributes.values[property.Name] = property.Value;

            if (property.Name.Equals("type", StringComparison.OrdinalIgnoreCase))
            {
                int parsedType;
                if (TryReadInt(property.Value, out parsedType))
                {
                    attributes.type = parsedType;
                }
            }
        }

        return attributes;
    }

    public bool TryGetValue(string key, out JToken value)
    {
        EnsureValues();
        return values.TryGetValue(key, out value);
    }

    public bool TryGetFloat(out float value, params string[] keys)
    {
        EnsureValues();
        for (int i = 0; i < keys.Length; i++)
        {
            JToken token;
            double number;
            if (values.TryGetValue(keys[i], out token) && TryReadDouble(token, out number))
            {
                value = (float)number;
                return true;
            }
        }

        value = 0f;
        return false;
    }

    public bool TryGetBool(out bool value, params string[] keys)
    {
        EnsureValues();
        for (int i = 0; i < keys.Length; i++)
        {
            JToken token;
            if (values.TryGetValue(keys[i], out token) && TryReadBool(token, out value))
            {
                return true;
            }
        }

        value = false;
        return false;
    }

    public bool TryGetString(out string value, params string[] keys)
    {
        EnsureValues();
        for (int i = 0; i < keys.Length; i++)
        {
            JToken token;
            if (values.TryGetValue(keys[i], out token) && TryReadString(token, out value))
            {
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public bool TryGetVector3(out Vector3 value, params string[] keys)
    {
        EnsureValues();
        for (int i = 0; i < keys.Length; i++)
        {
            JToken token;
            if (values.TryGetValue(keys[i], out token) && TryReadVector3(token, out value))
            {
                return true;
            }
        }

        value = Vector3.zero;
        return false;
    }

    public bool TryGetColor(out Color32 color)
    {
        EnsureValues();
        string[] colorKeys =
        {
            "color",
            "colour",
            "rgb",
            "rgba",
            "shade",
            "tint",
            "fill",
            "fillColor",
            "materialColor",
            "display_color",
            "agent_color",
            "body_color",
            "gama_color"
        };

        for (int i = 0; i < colorKeys.Length; i++)
        {
            JToken token;
            if (values.TryGetValue(colorKeys[i], out token) && TryReadColor(token, out color))
            {
                return true;
            }
        }

        if (TryReadColorChannels("red", "green", "blue", "alpha", out color) ||
            TryReadColorChannels("r", "g", "b", "a", out color))
        {
            return true;
        }

        color = default(Color32);
        return false;
    }

    private void EnsureValues()
    {
        if (values == null)
        {
            values = new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool TryReadColorChannels(string redKey, string greenKey, string blueKey, string alphaKey, out Color32 color)
    {
        color = default(Color32);

        JToken redToken;
        JToken greenToken;
        JToken blueToken;
        if (!values.TryGetValue(redKey, out redToken) ||
            !values.TryGetValue(greenKey, out greenToken) ||
            !values.TryGetValue(blueKey, out blueToken))
        {
            return false;
        }

        double red;
        double green;
        double blue;
        if (!TryReadDouble(redToken, out red) ||
            !TryReadDouble(greenToken, out green) ||
            !TryReadDouble(blueToken, out blue))
        {
            return false;
        }

        double alpha = 255;
        JToken alphaToken;
        if (values.TryGetValue(alphaKey, out alphaToken))
        {
            TryReadDouble(alphaToken, out alpha);
        }

        color = ToColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryReadColor(JToken token, out Color32 color)
    {
        color = default(Color32);
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        JArray array = token as JArray;
        if (array != null)
        {
            return TryReadColorArray(array, out color);
        }

        JObject obj = token as JObject;
        if (obj != null)
        {
            return TryReadColorObject(obj, out color);
        }

        if (token.Type == JTokenType.String)
        {
            return TryReadColorString(token.Value<string>(), out color);
        }

        return false;
    }

    private static bool TryReadColorArray(JArray array, out Color32 color)
    {
        color = default(Color32);
        if (array.Count < 3)
        {
            return false;
        }

        double red;
        double green;
        double blue;
        if (!TryReadDouble(array[0], out red) ||
            !TryReadDouble(array[1], out green) ||
            !TryReadDouble(array[2], out blue))
        {
            return false;
        }

        double alpha = 255;
        if (array.Count > 3)
        {
            TryReadDouble(array[3], out alpha);
        }

        color = ToColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryReadColorObject(JObject obj, out Color32 color)
    {
        color = default(Color32);

        if (TryReadObjectChannels(obj, "red", "green", "blue", "alpha", out color) ||
            TryReadObjectChannels(obj, "r", "g", "b", "a", out color))
        {
            return true;
        }

        JToken nested;
        return (TryReadObjectValue(obj, "color", out nested) ||
                TryReadObjectValue(obj, "rgb", out nested) ||
                TryReadObjectValue(obj, "rgba", out nested) ||
                TryReadObjectValue(obj, "name", out nested) ||
                TryReadObjectValue(obj, "hex", out nested) ||
                TryReadObjectValue(obj, "value", out nested)) &&
               TryReadColor(nested, out color);
    }

    private static bool TryReadObjectChannels(
        JObject obj,
        string redKey,
        string greenKey,
        string blueKey,
        string alphaKey,
        out Color32 color)
    {
        color = default(Color32);

        JToken redToken;
        JToken greenToken;
        JToken blueToken;
        if (!TryReadObjectValue(obj, redKey, out redToken) ||
            !TryReadObjectValue(obj, greenKey, out greenToken) ||
            !TryReadObjectValue(obj, blueKey, out blueToken))
        {
            return false;
        }

        double red;
        double green;
        double blue;
        if (!TryReadDouble(redToken, out red) ||
            !TryReadDouble(greenToken, out green) ||
            !TryReadDouble(blueToken, out blue))
        {
            return false;
        }

        double alpha = 255;
        JToken alphaToken;
        if (TryReadObjectValue(obj, alphaKey, out alphaToken))
        {
            TryReadDouble(alphaToken, out alpha);
        }

        color = ToColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryReadColorString(string value, out Color32 color)
    {
        color = default(Color32);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            return TryReadHexColor(normalized, out color);
        }

        if (TryReadNamedColor(normalized, out color))
        {
            return true;
        }

        MatchCollection matches = Regex.Matches(normalized, @"-?\d+(?:[.,]\d+)?");
        if (matches.Count < 3)
        {
            return false;
        }

        double red;
        double green;
        double blue;
        if (!TryReadDouble(matches[0].Value, out red) ||
            !TryReadDouble(matches[1].Value, out green) ||
            !TryReadDouble(matches[2].Value, out blue))
        {
            return false;
        }

        double alpha = 255;
        if (matches.Count > 3)
        {
            TryReadDouble(matches[3].Value, out alpha);
        }

        color = ToColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryReadVector3(JToken token, out Vector3 value)
    {
        value = Vector3.zero;
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        JArray array = token as JArray;
        if (array != null)
        {
            if (array.Count < 3)
            {
                return false;
            }

            double x;
            double y;
            double z;
            if (!TryReadDouble(array[0], out x) ||
                !TryReadDouble(array[1], out y) ||
                !TryReadDouble(array[2], out z))
            {
                return false;
            }

            value = new Vector3((float)x, (float)y, (float)z);
            return true;
        }

        JObject obj = token as JObject;
        if (obj != null)
        {
            JToken xToken;
            JToken yToken;
            JToken zToken;
            double x;
            double y;
            double z;
            if ((TryReadObjectValue(obj, "x", out xToken) || TryReadObjectValue(obj, "X", out xToken)) &&
                (TryReadObjectValue(obj, "y", out yToken) || TryReadObjectValue(obj, "Y", out yToken)) &&
                (TryReadObjectValue(obj, "z", out zToken) || TryReadObjectValue(obj, "Z", out zToken)) &&
                TryReadDouble(xToken, out x) &&
                TryReadDouble(yToken, out y) &&
                TryReadDouble(zToken, out z))
            {
                value = new Vector3((float)x, (float)y, (float)z);
                return true;
            }
        }

        if (token.Type != JTokenType.String)
        {
            return false;
        }

        MatchCollection matches = Regex.Matches(token.Value<string>(), @"-?\d+(?:[.,]\d+)?");
        if (matches.Count < 3)
        {
            return false;
        }

        double sx;
        double sy;
        double sz;
        if (!TryReadDouble(matches[0].Value, out sx) ||
            !TryReadDouble(matches[1].Value, out sy) ||
            !TryReadDouble(matches[2].Value, out sz))
        {
            return false;
        }

        value = new Vector3((float)sx, (float)sy, (float)sz);
        return true;
    }

    private static bool TryReadString(JToken token, out string value)
    {
        value = string.Empty;
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        if (token.Type == JTokenType.String)
        {
            value = token.Value<string>().Trim();
            return !string.IsNullOrEmpty(value);
        }

        if (token.Type == JTokenType.Integer ||
            token.Type == JTokenType.Float ||
            token.Type == JTokenType.Boolean)
        {
            value = token.ToString().Trim();
            return !string.IsNullOrEmpty(value);
        }

        JArray array = token as JArray;
        if (array != null)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (TryReadString(array[i], out value))
                {
                    return true;
                }
            }

            return false;
        }

        JObject obj = token as JObject;
        if (obj != null)
        {
            JToken nested;
            if ((TryReadObjectValue(obj, "prefab", out nested) ||
                 TryReadObjectValue(obj, "model", out nested) ||
                 TryReadObjectValue(obj, "mesh", out nested) ||
                 TryReadObjectValue(obj, "asset", out nested) ||
                 TryReadObjectValue(obj, "path", out nested) ||
                 TryReadObjectValue(obj, "name", out nested) ||
                 TryReadObjectValue(obj, "value", out nested)) &&
                TryReadString(nested, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadHexColor(string value, out Color32 color)
    {
        color = default(Color32);
        string hex = value.Substring(1);

        if (hex.Length == 3)
        {
            hex = new string(new[]
            {
                hex[0], hex[0],
                hex[1], hex[1],
                hex[2], hex[2]
            });
        }

        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        int red;
        int green;
        int blue;
        if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out red) ||
            !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out green) ||
            !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out blue))
        {
            return false;
        }

        int alpha = 255;
        if (hex.Length == 8 &&
            !int.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out alpha))
        {
            return false;
        }

        color = new Color32((byte)red, (byte)green, (byte)blue, (byte)alpha);
        return true;
    }

    private static bool TryReadNamedColor(string value, out Color32 color)
    {
        color = default(Color32);
        switch (value.Trim().ToLowerInvariant())
        {
            case "red":
                color = new Color32(255, 0, 0, 255);
                return true;
            case "green":
                color = new Color32(0, 255, 0, 255);
                return true;
            case "blue":
                color = new Color32(0, 0, 255, 255);
                return true;
            case "yellow":
                color = new Color32(255, 255, 0, 255);
                return true;
            case "black":
                color = new Color32(0, 0, 0, 255);
                return true;
            case "white":
                color = new Color32(255, 255, 255, 255);
                return true;
            case "gray":
            case "grey":
                color = new Color32(128, 128, 128, 255);
                return true;
            case "orange":
                color = new Color32(255, 165, 0, 255);
                return true;
            case "purple":
                color = new Color32(128, 0, 128, 255);
                return true;
            case "cyan":
                color = new Color32(0, 255, 255, 255);
                return true;
            case "magenta":
                color = new Color32(255, 0, 255, 255);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadObjectValue(JObject obj, string key, out JToken value)
    {
        foreach (JProperty property in obj.Properties())
        {
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadInt(JToken token, out int value)
    {
        double number;
        if (TryReadDouble(token, out number))
        {
            value = (int)number;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryReadDouble(JToken token, out double value)
    {
        if (token == null)
        {
            value = 0;
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            value = token.Value<double>();
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            return TryReadDouble(token.Value<string>(), out value);
        }

        value = 0;
        return false;
    }

    private static bool TryReadBool(JToken token, out bool value)
    {
        value = false;
        if (token == null || token.Type == JTokenType.Null)
        {
            return false;
        }

        if (token.Type == JTokenType.Boolean)
        {
            value = token.Value<bool>();
            return true;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            value = Math.Abs(token.Value<double>()) > double.Epsilon;
            return true;
        }

        if (token.Type != JTokenType.String)
        {
            return false;
        }

        string normalized = token.Value<string>().Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "true":
            case "yes":
            case "on":
            case "1":
                value = true;
                return true;
            case "false":
            case "no":
            case "off":
            case "0":
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDouble(string raw, out double value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = 0;
            return false;
        }

        return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Color32 ToColor(double red, double green, double blue, double alpha)
    {
        return new Color32(ToByte(red), ToByte(green), ToByte(blue), ToByte(alpha));
    }

    private static byte ToByte(double value)
    {
        if (value >= 0d && value <= 1d)
        {
            value *= 255d;
        }

        return (byte)Mathf.Clamp(Mathf.RoundToInt((float)value), 0, 255);
    }
}
