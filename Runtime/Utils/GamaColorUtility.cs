using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

public static class GamaColorUtility
{
    public static readonly Color32 DefaultVisibleColor = new Color32(0, 190, 255, 255);

    public static bool TryFromIntList(IList<int> values, out Color32 color)
    {
        color = default(Color32);
        if (values == null || values.Count < 3)
        {
            return false;
        }

        double alpha = values.Count > 3 ? values[3] : 255d;
        color = FromChannels(values[0], values[1], values[2], alpha);
        return true;
    }

    public static bool TryFromFloatList(IList<float> values, out Color32 color)
    {
        color = default(Color32);
        if (values == null || values.Count < 3)
        {
            return false;
        }

        double alpha = values.Count > 3 ? values[3] : 1d;
        color = FromChannels(values[0], values[1], values[2], alpha);
        return true;
    }

    public static bool TryFromPackedInt(int value, out Color32 color)
    {
        color = default(Color32);
        uint raw = unchecked((uint)value);
        if (raw == 0u)
        {
            return false;
        }

        byte a;
        byte r;
        byte g;
        byte b;
        if (raw <= 0xFFFFFFu)
        {
            a = 255;
            r = (byte)((raw >> 16) & 0xFF);
            g = (byte)((raw >> 8) & 0xFF);
            b = (byte)(raw & 0xFF);
        }
        else
        {
            a = (byte)((raw >> 24) & 0xFF);
            r = (byte)((raw >> 16) & 0xFF);
            g = (byte)((raw >> 8) & 0xFF);
            b = (byte)(raw & 0xFF);
            if (a == 0)
            {
                a = 255;
            }
        }

        color = new Color32(r, g, b, a);
        return true;
    }

    public static bool TryParseString(string value, out Color32 color)
    {
        color = default(Color32);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            return TryParseHex(normalized.Substring(1), out color);
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseHex(normalized.Substring(2), out color);
        }

        if (TryParseNamedColor(normalized, out color))
        {
            return true;
        }

        MatchCollection matches = Regex.Matches(normalized, @"-?\d+(?:[.,]\d+)?");
        if (matches.Count >= 3)
        {
            double red;
            double green;
            double blue;
            if (!TryReadDouble(matches[0].Value, out red) ||
                !TryReadDouble(matches[1].Value, out green) ||
                !TryReadDouble(matches[2].Value, out blue))
            {
                return false;
            }

            double alpha = matches.Count > 3 && TryReadDouble(matches[3].Value, out double parsedAlpha)
                ? parsedAlpha
                : 255d;
            color = FromChannels(red, green, blue, alpha);
            return true;
        }

        return false;
    }

    public static bool TryParseNamedColor(string value, out Color32 color)
    {
        color = default(Color32);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (value.Trim().ToLowerInvariant().Replace("_", "").Replace("-", ""))
        {
            case "black":
                color = new Color32(0, 0, 0, 255);
                return true;
            case "white":
                color = new Color32(255, 255, 255, 255);
                return true;
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
            case "gray":
            case "grey":
                color = new Color32(128, 128, 128, 255);
                return true;
            case "darkgray":
            case "darkgrey":
                color = new Color32(64, 64, 64, 255);
                return true;
            case "lightgray":
            case "lightgrey":
                color = new Color32(192, 192, 192, 255);
                return true;
            case "dodgerblue":
                color = new Color32(30, 144, 255, 255);
                return true;
            case "brown":
                color = new Color32(165, 42, 42, 255);
                return true;
            default:
                return false;
        }
    }

    public static Color32 FromChannels(double red, double green, double blue, double alpha)
    {
        if (alpha <= 0d && (Math.Abs(red) > double.Epsilon ||
                            Math.Abs(green) > double.Epsilon ||
                            Math.Abs(blue) > double.Epsilon))
        {
            alpha = 255d;
        }

        return new Color32(ToByte(red), ToByte(green), ToByte(blue), ToByte(alpha <= 0d ? 255d : alpha));
    }

    private static bool TryParseHex(string hex, out Color32 color)
    {
        color = default(Color32);
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        hex = hex.Trim();
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

        if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int red) ||
            !int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int green) ||
            !int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int blue))
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

    private static bool TryReadDouble(string raw, out double value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = 0d;
            return false;
        }

        return double.TryParse(raw.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
