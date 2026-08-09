using System;

namespace StarMaster {

    // Dependency-free console test runner (no NuGet / no test framework by design).
    // NOT part of the app build - compiled with WholeVersion.cs + CVars.cs into dist\StarMaster.Tests.exe
    // and run by build-installer.ps1, which fails the release build on a non-zero exit code.
    static class Tests {
        static int failed = 0, passed = 0;

        static void Check(bool ok, string name) {
            if (ok) { passed++; return; }
            failed++; Console.WriteLine("FAIL  " + name);
        }
        static string Fmt(int[] v) {
            string[] s = new string[v.Length];
            for (int i = 0; i < v.Length; i++) s[i] = v[i].ToString();
            return "[" + string.Join(",", s) + "]";
        }
        static void ParseIs(string tag, int[] expected) {
            int[] got = WholeVersion.Parse(tag);
            bool ok = got.Length == expected.Length;
            if (ok) for (int i = 0; i < got.Length; i++) if (got[i] != expected[i]) { ok = false; break; }
            Check(ok, "Parse(" + (tag == null ? "null" : "\"" + tag + "\"") + ") = " + Fmt(got) + ", expected " + Fmt(expected));
        }
        static void CompareIs(string a, string b, int expected) {
            int got = WholeVersion.Compare(WholeVersion.Parse(a), WholeVersion.Parse(b));
            Check(got == expected, "Compare(\"" + a + "\", \"" + b + "\") = " + got + ", expected " + expected);
        }

        static int Main() {
            // Parse: whole-number tags, v-prefix, multi-segment, junk, empty
            ParseIs("v56", new int[] { 56 });
            ParseIs("56", new int[] { 56 });
            ParseIs("v2.1", new int[] { 2, 1 });
            ParseIs("release-3-hotfix-2", new int[] { 3, 2 });
            ParseIs("", new int[0]);
            ParseIs(null, new int[0]);
            ParseIs("vNext", new int[0]);

            // Compare: numeric (not lexicographic), missing segments count as zero
            CompareIs("v2", "v1", 1);
            CompareIs("v1", "v2", -1);
            CompareIs("v2", "v2", 0);
            CompareIs("v10", "v9", 1);      // "10" < "9" as strings - must compare numerically
            CompareIs("v56", "v100", -1);
            CompareIs("v2.1", "v2", 1);
            CompareIs("v2", "v2.0", 0);
            CompareIs("v2", "v2.1", -1);
            CompareIs("", "v1", -1);        // an unparseable tag never wins

            // ----- CVars: catalog (the user.cfg VFX texture-streaming tweaks) -----
            CVarDef mips = CVars.Find("r_texturesStreamingVFXDesiredMips"), pre = CVars.Find("e_ParticleTexturePreLoading");
            Check(mips != null && mips.Min == 0 && mips.Max == 8 && mips.Def == 2 && !mips.Toggle, "catalog: DesiredMips is 0-8, game default 2, numeric");
            Check(pre != null && pre.Min == 0 && pre.Max == 1 && pre.Def == 0 && pre.Toggle, "catalog: PreLoading is 0/1, game default 0, toggle");
            Check(CVars.Find("no_such_cvar") == null, "catalog: unknown name -> null");
            Check(CVars.Clamp(mips, -3) == 0 && CVars.Clamp(mips, 9) == 8 && CVars.Clamp(mips, 5) == 5, "clamp: DesiredMips clamps to 0-8");
            Check(CVars.Clamp(pre, 7) == 1 && CVars.Clamp(pre, -1) == 0, "clamp: PreLoading clamps to 0/1");

            // ----- CVars: TryRead ("name = value" lines; the game is last-assignment-wins) -----
            int v;
            Check(CVars.TryRead("r_texturesStreamingVFXDesiredMips = 4\r\n", mips.Name, out v) && v == 4, "read: 'name = value'");
            Check(CVars.TryRead("R_TEXTURESSTREAMINGVFXDESIREDMIPS=6", mips.Name, out v) && v == 6, "read: case-insensitive key, no spaces");
            Check(CVars.TryRead("e_ParticleTexturePreLoading = \"1\"", pre.Name, out v) && v == 1, "read: quoted value");
            Check(CVars.TryRead("e_ParticleTexturePreLoading = 0\ne_ParticleTexturePreLoading = 1\n", pre.Name, out v) && v == 1, "read: last assignment wins");
            Check(!CVars.TryRead("g_language = english\r\n", mips.Name, out v), "read: absent -> false");
            Check(!CVars.TryRead("-- r_texturesStreamingVFXDesiredMips = 4\n", mips.Name, out v), "read: commented-out line ignored");
            Check(!CVars.TryRead("r_texturesStreamingVFXDesiredMips = high\n", mips.Name, out v), "read: non-numeric value -> false");
            Check(!CVars.TryRead(null, pre.Name, out v), "read: null text -> false");
            Check(CVars.TryRead("e_ParticleTexturePreLoading = 1.0\n", pre.Name, out v) && v == 1, "read: float-formatted value truncates to int (v70)");
            Check(CVars.TryRead("r_texturesStreamingVFXDesiredMips = 2\nr_texturesStreamingVFXDesiredMips = 5.0\n", mips.Name, out v) && v == 5, "read: float re-assignment still last-wins (v70)");
            Check(CVars.TryRead("e_ParticleTexturePreLoading = 1\rg_language = english", pre.Name, out v) && v == 1, "read: lone-CR line endings split correctly (v70)");

            // ----- CVars: Merge (emit 'name = value'; replace in place, keep everything else) -----
            string m = CVars.Merge("", pre.Name, 1);
            Check(m == pre.Name + " = 1" + Environment.NewLine, "merge: empty file -> single 'name = value' line");
            m = CVars.Merge("g_language = english\r\n", mips.Name, 3);
            Check(m.StartsWith("g_language = english") && CVars.TryRead(m, mips.Name, out v) && v == 3, "merge: append keeps existing lines");
            m = CVars.Merge("a = 1\r\nr_texturesStreamingVFXDesiredMips = 7\r\nb = 2\r\n", mips.Name, 0);
            Check(CVars.TryRead(m, mips.Name, out v) && v == 0 && m.IndexOf("= 7") < 0, "merge: replaces the old value in place");
            Check(m.IndexOf("a = 1") == 0 && m.IndexOf(mips.Name) > m.IndexOf("a = 1") && m.IndexOf("b = 2") > m.IndexOf(mips.Name), "merge: neighbours + line order preserved");
            m = CVars.Merge("E_PARTICLETEXTUREPRELOADING = 0\r\n", pre.Name, 1);
            Check(CVars.TryRead(m, pre.Name, out v) && v == 1 && m.IndexOf("E_PARTICLETEXTUREPRELOADING") < 0, "merge: case-insensitive replace normalises the name");
            m = CVars.Merge("e_ParticleTexturePreLoading = 0\nx = 9\ne_ParticleTexturePreLoading = 1\n", pre.Name, 0);
            Check(m.IndexOf(pre.Name) == m.LastIndexOf(pre.Name) && CVars.TryRead(m, pre.Name, out v) && v == 0, "merge: duplicate lines collapse (a stale last-wins dup would defeat the write)");
            m = CVars.Merge(CVars.Merge("g_language = english\r\n", pre.Name, 1), pre.Name, 1);
            Check(m.IndexOf(pre.Name) == m.LastIndexOf(pre.Name) && m.StartsWith("g_language = english"), "merge: re-applying is idempotent (no line growth)");
            m = CVars.Merge("e_ParticleTexturePreLoading = 1\rg_language = english", pre.Name, 0);
            Check(m.IndexOf("g_language = english") >= 0 && CVars.TryRead(m, pre.Name, out v) && v == 0, "merge: lone-CR endings don't swallow the next setting (v70)");

            Console.WriteLine(failed == 0 ? "OK  " + passed + " tests passed" : "" + failed + " FAILED, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
