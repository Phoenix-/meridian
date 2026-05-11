using Meridian.Models;
using Windows.UI;

namespace Meridian.Views;

internal static class EventColorPicker
{
    // Fallback palette indexed by account when no per-event/calendar color exists.
    private static readonly Color[] AccountColors =
    [
        Color.FromArgb(255, 26, 115, 232),   // Google blue
        Color.FromArgb(255, 52, 168, 83),    // Google green
        Color.FromArgb(255, 234, 67, 53),    // Google red
        Color.FromArgb(255, 251, 188, 4),    // Google yellow
        Color.FromArgb(255, 103, 58, 183),   // purple
    ];

    public static Color Pick(CalendarEvent ev, Dictionary<string, int> accountIndex)
    {
        if (TryParseHex(ev.CalendarColor, out var c)) return c;
        return AccountColor(ev.AccountEmail, accountIndex);
    }

    public static Color? PickText(CalendarEvent ev)
    {
        return TryParseHex(ev.CalendarTextColor, out var c) ? c : null;
    }

    // Black or white, chosen for contrast against bg. Used when a calendar
    // doesn't supply an explicit foregroundColor.
    public static Color PickReadable(Color bg)
    {
        // Perceived luminance (Rec. 709 weights), 0..255.
        double l = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B;
        return l > 140 ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 255, 255, 255);
    }

    public static Color AccountColor(string? email, Dictionary<string, int> index)
    {
        if (email == null) return AccountColors[0];
        if (!index.TryGetValue(email, out int i))
        {
            i = index.Count % AccountColors.Length;
            index[email] = i;
        }
        return AccountColors[i];
    }

    private static bool TryParseHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(hex)) return false;
        var s = hex.AsSpan();
        if (s[0] == '#') s = s[1..];
        if (s.Length != 6 && s.Length != 8) return false;

        byte a = 255, r, g, b;
        try
        {
            if (s.Length == 8)
            {
                a = byte.Parse(s[..2], System.Globalization.NumberStyles.HexNumber);
                s = s[2..];
            }
            r = byte.Parse(s[..2], System.Globalization.NumberStyles.HexNumber);
            g = byte.Parse(s.Slice(2, 2), System.Globalization.NumberStyles.HexNumber);
            b = byte.Parse(s.Slice(4, 2), System.Globalization.NumberStyles.HexNumber);
        }
        catch { return false; }

        color = Color.FromArgb(a, r, g, b);
        return true;
    }
}
