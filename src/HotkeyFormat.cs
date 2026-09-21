using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace DieYing
{
    internal static class HotkeyFormat
    {
        internal const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWindows = 8;

        internal static bool TryParse(string text, out uint modifiers, out uint key)
        {
            modifiers = 0; key = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            Keys parsedKey = Keys.None;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= ModWindows;
                else if (parsedKey == Keys.None && TryKey(part, out parsedKey)) { }
                else return false;
            }
            key = (uint)(parsedKey & Keys.KeyCode);
            return modifiers != 0 && key != 0;
        }

        internal static bool TryFormat(Keys data, out string text)
        {
            text = "";
            Keys key = data & Keys.KeyCode;
            Keys modifiers = data & Keys.Modifiers;
            if (key == Keys.None || modifiers == Keys.None) return false;
            List<string> parts = new List<string>();
            if ((modifiers & Keys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Keys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & Keys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & Keys.LWin) != 0 || (modifiers & Keys.RWin) != 0) parts.Add("Win");
            parts.Add(KeyName(key));
            text = String.Join("+", parts.ToArray());
            return true;
        }

        private static bool TryKey(string value, out Keys key)
        {
            key = Keys.None;
            if (value.Equals("Esc", StringComparison.OrdinalIgnoreCase)) { key = Keys.Escape; return true; }
            if (value.Equals("Space", StringComparison.OrdinalIgnoreCase)) { key = Keys.Space; return true; }
            if (value.Equals("Enter", StringComparison.OrdinalIgnoreCase)) { key = Keys.Enter; return true; }
            if (value.Equals("Tab", StringComparison.OrdinalIgnoreCase)) { key = Keys.Tab; return true; }
            Keys parsed;
            if (!Enum.TryParse<Keys>(value, true, out parsed)) return false;
            parsed &= Keys.KeyCode;
            if (parsed == Keys.None || parsed == Keys.ControlKey || parsed == Keys.ShiftKey || parsed == Keys.Menu || parsed == Keys.LWin || parsed == Keys.RWin) return false;
            key = parsed; return true;
        }

        private static string KeyName(Keys key)
        {
            if (key == Keys.Escape) return "Esc";
            if (key == Keys.Space) return "Space";
            if (key == Keys.Enter) return "Enter";
            if (key == Keys.Tab) return "Tab";
            return key.ToString();
        }
    }
}
