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
            Check(mips != null && mips.Min == 0 && mips.Max == 2 && mips.Def == 2 && !mips.Toggle, "catalog: DesiredMips is 0-2, game default 2, numeric");
            Check(pre != null && pre.Min == 0 && pre.Max == 1 && pre.Def == 0 && pre.Toggle, "catalog: PreLoading is 0/1, game default 0, toggle");
            Check(CVars.Find("no_such_cvar") == null, "catalog: unknown name -> null");
            Check(CVars.Clamp(mips, -3) == 0 && CVars.Clamp(mips, 9) == 2 && CVars.Clamp(mips, 1) == 1, "clamp: DesiredMips clamps to 0-2");
            Check(CVars.Clamp(pre, 7) == 1 && CVars.Clamp(pre, -1) == 0, "clamp: PreLoading clamps to 0/1");
            // value labels (v71): mips choices show resolution names; parse recovers the number
            Check(CVars.ValueLabel(mips, 0) == "0 - Full resolution" && CVars.ValueLabel(mips, 1) == "1 - Half resolution" && CVars.ValueLabel(mips, 2) == "2 - Quarter resolution", "labels: mips values name their resolution");
            Check(CVars.ValueLabel(mips, 9) == "2 - Quarter resolution" && CVars.ValueLabel(pre, 1) == "1", "labels: out-of-range clamps; unlabelled defs show the bare number");
            Check(CVars.ParseChoice("1 - Half resolution", 9) == 1 && CVars.ParseChoice("2", 9) == 2, "labels: ParseChoice recovers the leading value");
            Check(CVars.ParseChoice("garbage", 2) == 2 && CVars.ParseChoice(null, 1) == 1, "labels: ParseChoice falls back on junk/null");

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

            // ----- LocOverrides (v77): ship-name overrides on global.ini -----
            string ini = "﻿a_key=Alpha\nvehicle_NameRSI_Constellation_Mk5_Andromeda=RSI Constellation Mk V Centaurus\nvehicle_NameRSI_Constellation_Mk5_Andromeda_short=Constelltn. MkV Cntr\nvehicle_NameAEGS_Tiburon_short,P=Tiburon\nvehicle_NameAEGS_Firebird_Collector_Milt,P=Aegis Sabre Firebird Wikelo War Special\nvehicle_NameEmpty=\nvehicle_DescX=desc\n";
            Check(LocOverrides.InGroup("Ships", "vehicle_NameDRAK_Cutlass_Black"), "ovr: vehicle_Name is a ship");
            Check(!LocOverrides.InGroup("Ships", "vehicle_NameDRAK_Cutlass_Black_short"), "ovr: _short excluded");
            Check(!LocOverrides.InGroup("Ships", "vehicle_NameAEGS_Tiburon_short,P"), "ovr: _short,P excluded");
            Check(LocOverrides.InGroup("Ships", "vehicle_NameAEGS_Firebird_Collector_Milt,P"), "ovr: ,P full name included");
            Check(!LocOverrides.InGroup("Ships", "vehicle_DescX"), "ovr: descriptions excluded");
            System.Collections.Generic.List<LocEntry> ships = LocOverrides.List(ini, "Ships");
            Check(ships.Count == 2 && ships[0].Value.StartsWith("Aegis") && ships[1].Key == "vehicle_NameRSI_Constellation_Mk5_Andromeda", "ovr: list = full names only, sorted, blanks skipped");
            System.Collections.Generic.List<LocOverride> ol = new System.Collections.Generic.List<LocOverride>();
            ol.Add(new LocOverride { Group = "Ships", Key = "vehicle_NameRSI_Constellation_Mk5_Andromeda", Original = "RSI Constellation Mk V Centaurus", Name = "Connie Centaurus" });
            int nch; string applied = LocOverrides.Apply(ini, ol, out nch);
            string got; Check(nch == 1 && LocOverrides.TryGet(applied, ol[0].Key, out got) && got == "Connie Centaurus", "ovr: apply renames the key");
            Check(ini.Replace("RSI Constellation Mk V Centaurus", "Connie Centaurus") == applied, "ovr: apply leaves every other byte alone");
            Check(LocOverrides.TryGet(applied, "vehicle_NameRSI_Constellation_Mk5_Andromeda_short", out got) && got == "Constelltn. MkV Cntr", "ovr: _short key untouched");
            LocOverrides.Apply(applied, ol, out nch); Check(nch == 0, "ovr: re-apply is a no-op (no needless write)");
            string crlf = LocOverrides.Apply("x=1\r\nvehicle_NameRSI_Constellation_Mk5_Andromeda=Old\r\ny=2\r\n", ol, out nch);
            Check(crlf == "x=1\r\nvehicle_NameRSI_Constellation_Mk5_Andromeda=Connie Centaurus\r\ny=2\r\n", "ovr: CRLF endings preserved");
            Check(!LocOverrides.Reconcile(applied, ol), "ovr: reconcile - our own value isn't a new original");
            string fresh = ini.Replace("RSI Constellation Mk V Centaurus", "RSI Constellation Centaurus Mk V");
            Check(LocOverrides.Reconcile(fresh, ol) && ol[0].Original == "RSI Constellation Centaurus Mk V", "ovr: reconcile picks up a StarStrings rename as the new original");
            LocOverride rt = LocOverrides.Decode(LocOverrides.Encode(new LocOverride { Group = "Ships", Key = "k,P", Original = "A|B", Name = "N\tx" }));
            Check(rt != null && rt.Key == "k,P" && rt.Original == "A|B" && rt.Name == "N x", "ovr: config encode/decode round-trips (tabs cleaned)");
            Check(LocOverrides.Decode("garbage") == null, "ovr: bad config line ignored");

            Console.WriteLine(failed == 0 ? "OK  " + passed + " tests passed" : "" + failed + " FAILED, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
