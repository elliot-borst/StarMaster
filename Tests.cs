using System;

namespace StarMaster {

    // Dependency-free console test runner (no NuGet / no test framework by design).
    // NOT part of the app build - compiled with WholeVersion.cs into dist\StarMaster.Tests.exe
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

            Console.WriteLine(failed == 0 ? "OK  " + passed + " tests passed" : "" + failed + " FAILED, " + passed + " passed");
            return failed == 0 ? 0 : 1;
        }
    }
}
