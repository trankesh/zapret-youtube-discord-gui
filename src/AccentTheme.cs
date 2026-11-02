using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace ZapretDesktop
{
    internal static class AccentTheme
    {
        public const string Default = "#FFAD58";
        public static string Normalize(string value)
        {
            if (value == null) return null;
            value = value.Trim(); if (value.StartsWith("#")) value = value.Substring(1);
            return Regex.IsMatch(value, "^[0-9a-fA-F]{6}$") ? "#" + value.ToUpperInvariant() : null;
        }
        static Color Parse(string value) { return (Color)ColorConverter.ConvertFromString(value); }
        static string Blend(string hex, Color other, double amount)
        {
            var c = Parse(hex);
            return String.Format("#{0:X2}{1:X2}{2:X2}", (byte)Math.Round(c.R * (1 - amount) + other.R * amount), (byte)Math.Round(c.G * (1 - amount) + other.G * amount), (byte)Math.Round(c.B * (1 - amount) + other.B * amount));
        }
        static double Channel(byte value) { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Luminance(string hex) { var c = Parse(hex); return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B); }
        public static string TextColor(string hex) { return Luminance(hex) > 0.179 ? "#111114" : "#FFFFFF"; }
        public static string Readable(string hex) { return Luminance(hex) < 0.12 ? Blend(hex, Colors.White, 0.52) : hex; }
        public static string Markup(string source, string hex)
        {
            hex = Normalize(hex) ?? Default;
            string visible = Readable(hex), light = Blend(visible, Colors.White, 0.2), tint = Blend(hex, Color.FromRgb(20, 20, 23), 0.78);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                { "#FFAD58", hex }, { "#FFB667", light }, { "#FFB46C", light }, { "#FFBD78", light }, { "#FFC078", light },
                { "#2A190B", TextColor(hex) }, { "#53351C", tint }, { "#443327", tint }, { "#46372C", tint }, { "#FFD1A0", light }, { "#3E7158", tint }, { "#211A15", tint }
            };
            source = Regex.Replace(source, String.Join("|", values.Keys.Select(Regex.Escape)), m => values[m.Value], RegexOptions.IgnoreCase);
            // The inactive connection ring stays gray for every accent.
            source = source.Replace("Stroke='" + hex + "'", "Stroke='" + visible + "'");
            source = source.Replace("Property='Stroke' Value='" + hex + "'", "Property='Stroke' Value='" + visible + "'");
            return source.Replace("<Window.Resources>", "<Window.Resources><SolidColorBrush x:Key=\"AccentBrush\" Color=\"" + visible + "\"/><SolidColorBrush x:Key=\"AccentTextBrush\" Color=\"" + light + "\"/>");
        }
    }
}
