namespace Caelum.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Caelum.Models;

    /// <summary>
    /// Task 14 parity — recently used colors per palette, newest first,
    /// capped at <see cref="MaxRecentColors"/> entries. Operates on the
    /// "#RRGGBB" hex lists stored on <see cref="AppSettings"/>; the lists
    /// belong to a transient <see cref="AppSettingsService.Load"/> clone, so
    /// callers persist the mutated snapshot via
    /// <see cref="AppSettingsService.Save"/> (WPF RecordRecentColor parity).
    /// </summary>
    public static class RecentColors
    {
        /// <summary>Task 14: recently used colors, newest first, max 8 per palette.</summary>
        public const int MaxRecentColors = 8;

        /// <summary>
        /// Inserts (or moves) the "#RRGGBB" hex to the front of the list,
        /// de-duplicating case-insensitively and trimming to
        /// <see cref="MaxRecentColors"/>.
        /// </summary>
        public static void Record(List<string> list, string hex)
        {
            if (list == null || string.IsNullOrWhiteSpace(hex))
                return;

            list.RemoveAll(x => string.Equals(x, hex, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, hex);
            if (list.Count > MaxRecentColors)
                list.RemoveRange(MaxRecentColors, list.Count - MaxRecentColors);
        }

        /// <summary>
        /// Parses "#RRGGBB" (the recorded format) and tolerates "#AARRGGBB"
        /// for hand-edited settings.json files (WPF TryParseRecentColor
        /// parity). Malformed or hand-typed entries are skipped, never thrown.
        /// </summary>
        public static bool TryParse(string hex, out byte a, out byte r, out byte g, out byte b)
        {
            a = r = g = b = 0;
            if (string.IsNullOrWhiteSpace(hex))
                return false;

            var digits = hex.Trim().TrimStart('#');
            if (digits.Length != 6 && digits.Length != 8)
                return false;

            try
            {
                r = byte.Parse(digits.Substring(digits.Length - 6, 2), NumberStyles.HexNumber);
                g = byte.Parse(digits.Substring(digits.Length - 4, 2), NumberStyles.HexNumber);
                b = byte.Parse(digits.Substring(digits.Length - 2, 2), NumberStyles.HexNumber);
                a = digits.Length == 8
                    ? byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber)
                    : (byte)255;
                return true;
            }
            catch
            {
                a = r = g = b = 0;
                return false;
            }
        }
    }
}
