using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StarMaster {

    // Whole-number release versioning (v1, v2, v3, ...). Parse pulls the numeric runs out of a
    // tag ("v56" -> [56]); Compare is numeric segment-by-segment with missing segments as zero
    // ("v2.1" > "v2", "v10" > "v9"). Kept UI-free in its own file so Tests.cs can compile it
    // without the WPF references (see build-installer.ps1).
    static class WholeVersion {
        public static int[] Parse(string tag) {
            List<int> parts = new List<int>();
            if (!string.IsNullOrEmpty(tag)) foreach (Match m in Regex.Matches(tag, "[0-9]+")) { int v; if (int.TryParse(m.Value, out v)) parts.Add(v); }
            return parts.ToArray();
        }
        public static int Compare(int[] a, int[] b) {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++) { int ai = i < a.Length ? a[i] : 0; int bi = i < b.Length ? b[i] : 0; if (ai != bi) return ai < bi ? -1 : 1; }
            return 0;
        }
    }
}
