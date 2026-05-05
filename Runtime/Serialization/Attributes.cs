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

    private readonly Dictionary<string, JToken> values =
        new Dictionary<string, JToken>(StringComparer.OrdinalIgnoreCase);

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
        return values.TryGetValue(key, out value);
    }

    /// <summary>
    /// Vrai si l'objet d'attributes GAMA contient au moins une clé autre que <c>type</c>.
    /// Sert à ne pas écraser des agents avec une teinte inspector unique alors que GAMA a envoyé du contexte par ligne.
    /// </summary>
    public bool HasKeysOtherThanType()
    {
        foreach (string key in values.Keys)
        {
            if (!string.Equals(key, "type", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public string DebugKeysPreview(int maxKeys = 8)
    {
        if (values == null || values.Count == 0)
        {
            return "(no keys)";
        }

        int shown = 0;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (string key in values.Keys)
        {
            if (shown > 0)
            {
                sb.Append(", ");
            }

            sb.Append(key);
            shown++;
            if (shown >= maxKeys)
            {
                break;
            }
        }

        if (values.Count > shown)
        {
            sb.Append(", ...");
        }

        return sb.ToString();
    }

    public bool TryGetColor(out Color32 color)
    {
        string[] colorKeys =
        {
            "color",
            "colour",
            "rgb",
            "rgba",
            "shade",
            "tint",
            "hue",
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
            if (TryGetValue(colorKeys[i], out token) && TryReadColor(token, out color))
            {
                return true;
            }
        }

        if (TryReadColorChannels("red", "green", "blue", "alpha", out color) ||
            TryReadColorChannels("r", "g", "b", "a", out color))
        {
            return true;
        }

        foreach (KeyValuePair<string, JToken> pair in values)
        {
            if (!LooksLikeColorAttributeKey(pair.Key))
            {
                continue;
            }

            if (TryReadColor(pair.Value, out color))
            {
                return true;
            }
        }

        color = default(Color32);
        return false;
    }

    static bool LooksLikeColorAttributeKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        string k = key.ToLowerInvariant();

        if (k.Equals("type") ||
            k.Equals("id") ||
            k.Equals("name") ||
            k.Equals("location") ||
            k.Equals("position") ||
            k.Equals("point") ||
            k.Equals("target") ||
            k.Equals("heading") ||
            k.Equals("speed") ||
            k.Equals("direction") ||
            k.Equals("velocity") ||
            k.Equals("size") ||
            k.Equals("area") ||
            k.Equals("perimeter"))
        {
            return false;
        }

        if (k.Contains("colour") ||
            k.Contains("color") ||
            k.Equals("rgb") ||
            k.Equals("rgba") ||
            k.Contains("tint") ||
            k.Contains("shade") ||
            k.Contains("hue") ||
            k.Contains("pigment"))
        {
            return true;
        }

        return false;
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

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            long packed = token.Value<long>();
            return TryReadPackedColor(packed, out color);
        }

        return false;
    }

    private static bool TryReadPackedColor(long rawValue, out Color32 color)
    {
        color = default(Color32);

        // GAMA / middleware can encode colors as signed ints.
        // Normalize to unsigned representation first.
        uint packed = unchecked((uint)rawValue);
        if (rawValue >= 0 && rawValue <= int.MaxValue)
        {
            packed = (uint)rawValue;
        }

        // 0xRRGGBB
        if ((packed & 0xFF000000u) == 0u)
        {
            byte r = (byte)((packed >> 16) & 0xFF);
            byte g = (byte)((packed >> 8) & 0xFF);
            byte b = (byte)(packed & 0xFF);
            color = new Color32(r, g, b, 255);
            return true;
        }

        // 0xAARRGGBB
        byte aA = (byte)((packed >> 24) & 0xFF);
        byte aR = (byte)((packed >> 16) & 0xFF);
        byte aG = (byte)((packed >> 8) & 0xFF);
        byte aB = (byte)(packed & 0xFF);
        color = new Color32(aR, aG, aB, aA == 0 ? (byte)255 : aA);
        return true;
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

    private bool TryReadColorChannels(string redKey, string greenKey, string blueKey, string alphaKey, out Color32 color)
    {
        color = default(Color32);

        JToken redToken;
        JToken greenToken;
        JToken blueToken;
        if (!TryGetValue(redKey, out redToken) ||
            !TryGetValue(greenKey, out greenToken) ||
            !TryGetValue(blueKey, out blueToken))
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
        if (TryGetValue(alphaKey, out alphaToken))
        {
            TryReadDouble(alphaToken, out alpha);
        }

        color = ToColor(red, green, blue, alpha);
        return true;
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
            if (TryReadHexColor(normalized, out color))
            {
                return true;
            }

            if (TryReadNamedColor(normalized.Substring(1), out color))
            {
                return true;
            }
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
            case "blue":
                color = new Color32(0, 0, 255, 255);
                return true;
            case "green":
                color = new Color32(0, 255, 0, 255);
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
            case "cyan":
                color = new Color32(0, 255, 255, 255);
                return true;
            case "magenta":
                color = new Color32(255, 0, 255, 255);
                return true;
            case "purple":
                color = new Color32(128, 0, 128, 255);
                return true;
            case "dodgerblue":
                color = new Color32(30, 144, 255, 255);
                return true;
            case "darkgray":
            case "darkgrey":
                color = new Color32(169, 169, 169, 255);
                return true;
            case "lightgray":
            case "lightgrey":
                color = new Color32(211, 211, 211, 255);
                return true;
            case "slategray":
            case "slategrey":
                color = new Color32(112, 128, 144, 255);
                return true;
            case "steelblue":
                color = new Color32(70, 130, 180, 255);
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadObjectValue(JObject obj, string key, out JToken value)
    {
        value = null;
        foreach (JProperty property in obj.Properties())
        {
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadInt(JToken token, out int value)
    {
        value = 0;
        if (token == null)
        {
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            value = Mathf.RoundToInt(token.Value<float>());
            return true;
        }

        return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadDouble(JToken token, out double value)
    {
        value = 0;
        if (token == null)
        {
            return false;
        }

        if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
        {
            value = token.Value<double>();
            return true;
        }

        return TryReadDouble(token.ToString(), out value);
    }

    private static bool TryReadDouble(string raw, out double value)
    {
        return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Color32 ToColor(double red, double green, double blue, double alpha)
    {
        return new Color32(ToColorByte(red), ToColorByte(green), ToColorByte(blue), ToColorByte(alpha));
    }

    private static byte ToColorByte(double value)
    {
        if (value >= 0 && value <= 1)
        {
            value *= 255;
        }

        return (byte)Mathf.Clamp(Mathf.RoundToInt((float)value), 0, 255);
    }
}
