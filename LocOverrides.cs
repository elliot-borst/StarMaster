using System;
using System.Collections.Generic;
using System.Text;

namespace StarMaster {

    // One user rename on top of StarStrings' global.ini (v77). Original is what the file said before we
    // touched it - remembered here because once applied, the file itself only holds the override.
    public class LocOverride {
        public string Group = "", Key = "", Original = "", Name = "";
    }
    public class LocEntry {
        public string Key, Value;
    }

    // Name overrides for the game's localization file (data\Localization\english\global.ini, plain
    // `key=value` lines). UI-free in its own file so Tests.cs can compile it without WPF refs.
    // Groups are the pickable categories; only "Ships" for now, built so more (weapons, ...) slot in.
    public static class LocOverrides {
        public static readonly string[] Groups = { "Ships" };

        // Ships = the vehicle_Name* keys, minus the *_short variants (a separate, already-abbreviated
        // HUD label). Keys may carry a ",P" suffix (vehicle_NameX,P=...), so _short is tested before it.
        public static bool InGroup(string group, string key) {
            if (key == null) return false;
            if (group == "Ships") {
                if (!key.StartsWith("vehicle_Name", StringComparison.OrdinalIgnoreCase)) return false;
                string b = key; int c = b.IndexOf(','); if (c >= 0) b = b.Substring(0, c);
                return !b.EndsWith("_short", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // every entry of a group, sorted by display name (blank values skipped)
        public static List<LocEntry> List(string text, string group) {
            List<LocEntry> r = new List<LocEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in Lines(text)) {
                string k, v; if (!Split(raw, out k, out v)) continue;
                if (!InGroup(group, k) || v.Trim().Length == 0 || !seen.Add(k)) continue;
                r.Add(new LocEntry { Key = k, Value = v });
            }
            r.Sort(delegate (LocEntry a, LocEntry b) { int c = string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase); return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        public static bool TryGet(string text, string key, out string value) {
            value = null;
            foreach (string raw in Lines(text)) { string k, v; if (Split(raw, out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) value = v; }
            return value != null;
        }

        // After StarStrings (or anything else) rewrote the file, a key's value is no longer our override:
        // whatever it says now is the fresh original. Returns true if any Original changed.
        public static bool Reconcile(string text, IList<LocOverride> list) {
            bool changed = false;
            foreach (LocOverride o in list) {
                string v; if (!TryGet(text, o.Key, out v)) continue;
                if (v != o.Name && v != o.Original) { o.Original = v; changed = true; }
            }
            return changed;
        }

        // Rewrite the value of every overridden key (every occurrence). Everything else - other lines,
        // line endings, order - is kept byte-for-byte. `changed` = lines actually altered.
        public static string Apply(string text, IList<LocOverride> list, out int changed) {
            changed = 0;
            if (text == null) text = "";
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (LocOverride o in list) if (o.Key.Length > 0) map[o.Key] = o.Name;
            if (map.Count == 0) return text;
            string[] parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++) {
                string line = parts[i]; bool cr = line.EndsWith("\r"); if (cr) line = line.Substring(0, line.Length - 1);
                string k, v; if (!Split(line, out k, out v)) continue;
                string nv; if (!map.TryGetValue(k, out nv) || nv == v) continue;
                parts[i] = k + "=" + nv + (cr ? "\r" : ""); changed++;
            }
            return changed == 0 ? text : string.Join("\n", parts);
        }

        // config line: Group<TAB>Key<TAB>Original<TAB>Name
        public static string Encode(LocOverride o) { return Clean(o.Group) + "\t" + Clean(o.Key) + "\t" + Clean(o.Original) + "\t" + Clean(o.Name); }
        public static LocOverride Decode(string s) {
            if (s == null) return null;
            string[] f = s.Split('\t'); if (f.Length < 4 || f[1].Length == 0) return null;
            return new LocOverride { Group = f[0], Key = f[1], Original = f[2], Name = f[3] };
        }
        // names live on one ini line: no tabs / line breaks
        public static string Clean(string s) { if (s == null) return ""; return s.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ").Trim(); }

        static IEnumerable<string> Lines(string text) {
            if (text == null) yield break;
            foreach (string l in text.Split('\n')) yield return l.TrimEnd('\r');
        }
        static bool Split(string line, out string key, out string value) {
            key = value = null;
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') return false;
            int eq = line.IndexOf('='); if (eq <= 0) return false;
            key = line.Substring(0, eq); if (key.Length > 0 && key[0] == '﻿') key = key.Substring(1);
            value = line.Substring(eq + 1); return true;
        }
    }
}
