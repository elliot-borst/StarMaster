using System;
using System.Text;

namespace StarMaster {

    // ===== Star Citizen user.cfg CVar catalog + line merge (UI-agnostic) =====
    // Kept out of StarMaster.cs (like WholeVersion) so Tests.cs can compile it without the WPF refs.
    // user.cfg lines are "name = value"; the game reads the file once at launch, later lines win.

    public class CVarDef {
        public string Name;    // exact CVar name as written to user.cfg
        public string Label;   // short UI label
        public string Tip;     // tooltip shown in the card
        public int Min, Max;   // integer range - values clamp to this
        public int Def;        // the game's own default (what an unset CVar behaves as)
        public bool Toggle;    // 0/1 - rendered as a switch instead of a number picker
        public string[] ValueLabels;   // optional per-value display names (index = value), e.g. 0 -> "Full resolution"
    }

    public static class CVars {
        // The catalog - shown as the "VFX streaming" section of the Shader Cache (graphics) card.
        public static CVarDef[] Catalog() {
            return new CVarDef[] {
                // NB the value's direction is NOT officially documented (mip index vs resident-mip
                // count). The value labels (v71, user-requested) assume the LIKELY mip-index reading
                // - each mip step halves resolution, so 0=full / 1=half / 2=quarter - and the tooltip
                // still says the direction is unverified; if in-game testing proves it backwards,
                // flip ValueLabels here. Range trimmed 0-8 -> 0-2 (deeper floors aren't useful).
                new CVarDef { Name = "r_texturesStreamingVFXDesiredMips", Label = "VFX texture mip floor", Min = 0, Max = 2, Def = 2, Toggle = false,
                    ValueLabels = new string[] { "Full resolution", "Half resolution", "Quarter resolution" },
                    Tip = "Minimum resolution floor for particle/VFX textures kept in VRAM. Game default 2 (quarter). Labels assume the likely reading (0 = sharpest floor, more VRAM) - the direction isn't officially documented, so test in-game." },
                new CVarDef { Name = "e_ParticleTexturePreLoading", Label = "Pre-load particle textures", Min = 0, Max = 1, Def = 0, Toggle = true,
                    Tip = "Pre-load particle textures at startup. Sharper effects on first use; longer loads and higher VRAM use. Recommended for 16GB+ GPUs." },
            };
        }
        public static CVarDef Find(string name) { foreach (CVarDef d in Catalog()) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d; return null; }

        public static int Clamp(CVarDef d, int v) { return v < d.Min ? d.Min : (v > d.Max ? d.Max : v); }

        // Display string for a value: "0 - Full resolution" when the def carries labels, else just "0".
        public static string ValueLabel(CVarDef d, int v) {
            v = Clamp(d, v);
            return d.ValueLabels != null && v >= 0 && v < d.ValueLabels.Length ? v + " - " + d.ValueLabels[v] : v.ToString();
        }
        // The value back out of a ValueLabel-style choice string (leading integer token); fallback when unparseable.
        public static int ParseChoice(string choice, int fallback) {
            if (choice == null) return fallback;
            string s = choice.Trim(); int sp = s.IndexOf(' '); if (sp > 0) s = s.Substring(0, sp);
            int v; return int.TryParse(s, out v) ? v : fallback;
        }

        // The key of a "name = value" line, or null for blanks / comments / non-assignments.
        static string KeyOf(string line) {
            string ln = line.Trim();
            if (ln.Length == 0 || ln.StartsWith(";") || ln.StartsWith("#") || ln.StartsWith("--")) return null;
            int eq = ln.IndexOf('=');
            if (eq <= 0) return null;
            return ln.Substring(0, eq).Trim();
        }

        // Reads a CVar's integer value from user.cfg text. The LAST assignment wins (matches the
        // game's read order); surrounding quotes tolerated; float-formatted values ("1.0" - a common
        // hand-written form) truncate to int. False when absent or not numeric.
        public static bool TryRead(string cfgText, string name, out int value) {
            value = 0; bool found = false;
            if (cfgText == null) return false;
            foreach (string raw in Normalize(cfgText).Split('\n')) {
                string key = KeyOf(raw);
                if (key == null || !string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                string v = raw.Substring(raw.IndexOf('=') + 1).Trim().Trim('"', '\'');
                int parsed; double d;
                if (int.TryParse(v, out parsed)) { value = parsed; found = true; }
                else if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) { value = (int)d; found = true; }
            }
            return found;
        }

        // CRLF and lone-CR endings both become LF, so a bare-\r file (old/foreign editors) can't glue
        // two settings into one "line" (which would make Merge silently drop the second one).
        static string Normalize(string text) { return text.Replace("\r\n", "\n").Replace('\r', '\n'); }

        // Merges "name = value" into user.cfg text: replaces the first existing assignment in place
        // (case-insensitive), drops any later duplicates (the file is last-wins, so a stale duplicate
        // below would defeat the write), appends when absent. Every other line is kept as-is; line
        // endings are normalised to the platform's (same format EnsureLanguageLine writes).
        public static string Merge(string cfgText, string name, int value) {
            string line = name + " = " + value;
            StringBuilder sb = new StringBuilder(); bool replaced = false;
            string[] lines = Normalize(cfgText == null ? "" : cfgText).Split('\n');
            int last = lines.Length; while (last > 0 && lines[last - 1].Length == 0) last--;   // don't grow trailing blank lines on every merge
            for (int i = 0; i < last; i++) {
                string key = KeyOf(lines[i]);
                if (key != null && string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) { if (!replaced) { sb.AppendLine(line); replaced = true; } }
                else sb.AppendLine(lines[i]);
            }
            if (!replaced) sb.AppendLine(line);
            return sb.ToString();
        }
    }
}
