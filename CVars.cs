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
    }

    public static class CVars {
        // The catalog - shown as the "VFX streaming" section of the Shader Cache (graphics) card.
        public static CVarDef[] Catalog() {
            return new CVarDef[] {
                // NB the value's direction is NOT officially documented (mip index vs resident-mip
                // count), so the label/tooltip stay neutral - no "lower = better" anywhere.
                new CVarDef { Name = "r_texturesStreamingVFXDesiredMips", Label = "VFX texture mip floor", Min = 0, Max = 8, Def = 2, Toggle = false,
                    Tip = "Minimum resolution floor for particle/VFX textures kept in VRAM. Game default 2. Likely lower = sharper floor at higher VRAM cost - direction not officially documented, test in-game." },
                new CVarDef { Name = "e_ParticleTexturePreLoading", Label = "Pre-load particle textures", Min = 0, Max = 1, Def = 0, Toggle = true,
                    Tip = "Pre-load particle textures at startup. Sharper effects on first use; longer loads and higher VRAM use. Recommended for 16GB+ GPUs." },
            };
        }
        public static CVarDef Find(string name) { foreach (CVarDef d in Catalog()) if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) return d; return null; }

        public static int Clamp(CVarDef d, int v) { return v < d.Min ? d.Min : (v > d.Max ? d.Max : v); }

        // The key of a "name = value" line, or null for blanks / comments / non-assignments.
        static string KeyOf(string line) {
            string ln = line.Trim();
            if (ln.Length == 0 || ln.StartsWith(";") || ln.StartsWith("#") || ln.StartsWith("--")) return null;
            int eq = ln.IndexOf('=');
            if (eq <= 0) return null;
            return ln.Substring(0, eq).Trim();
        }

        // Reads a CVar's integer value from user.cfg text. The LAST assignment wins (matches the
        // game's read order); surrounding quotes tolerated. False when absent or not an integer.
        public static bool TryRead(string cfgText, string name, out int value) {
            value = 0; bool found = false;
            if (cfgText == null) return false;
            foreach (string raw in cfgText.Replace("\r\n", "\n").Split('\n')) {
                string key = KeyOf(raw);
                if (key == null || !string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
                string v = raw.Substring(raw.IndexOf('=') + 1).Trim().Trim('"', '\'');
                int parsed; if (int.TryParse(v, out parsed)) { value = parsed; found = true; }
            }
            return found;
        }

        // Merges "name = value" into user.cfg text: replaces the first existing assignment in place
        // (case-insensitive), drops any later duplicates (the file is last-wins, so a stale duplicate
        // below would defeat the write), appends when absent. Every other line is kept as-is; line
        // endings are normalised to the platform's (same format EnsureLanguageLine writes).
        public static string Merge(string cfgText, string name, int value) {
            string line = name + " = " + value;
            StringBuilder sb = new StringBuilder(); bool replaced = false;
            string[] lines = (cfgText == null ? "" : cfgText).Replace("\r\n", "\n").Split('\n');
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
