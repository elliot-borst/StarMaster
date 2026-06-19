using System;
using System.Collections.Generic;
using System.IO;

namespace StarMaster {

    // Backup/restore file operations (UI-agnostic). Copies the 3 things a patch/channel-switch wipes:
    //   user\  ·  data\Localization\  ·  user.cfg  — overwrites, never deletes, skips locked files.
    static class BackupOps {
        public static string BackupsRoot {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarMaster", "Backups"); }
        }

        public static string[] DetectChannels(string root) {
            List<string> ch = new List<string>();
            try {
                if (Directory.Exists(root))
                    foreach (string d in Directory.GetDirectories(root))
                        if (Directory.Exists(Path.Combine(d, "user")) || Directory.Exists(Path.Combine(d, "data")))
                            ch.Add(Path.GetFileName(d));
            } catch { }
            return ch.ToArray();
        }

        public static string[] Snapshots() {
            List<string> s = new List<string>();
            try {
                if (Directory.Exists(BackupsRoot))
                    foreach (string d in Directory.GetDirectories(BackupsRoot)) s.Add(Path.GetFileName(d));
            } catch { }
            s.Sort(); s.Reverse(); return s.ToArray();
        }

        public static int CopyItems(string srcBase, string dstBase, bool user, bool loc, bool cfg, Action<string> log) {
            int n = 0;
            if (user) n += CopyDir(Path.Combine(srcBase, "user"), Path.Combine(dstBase, "user"), "user\\", log);
            if (loc)  n += CopyDir(Path.Combine(srcBase, "data", "Localization"), Path.Combine(dstBase, "data", "Localization"), "data\\Localization\\", log);
            if (cfg) {
                string s = Path.Combine(srcBase, "user.cfg");
                if (File.Exists(s)) { try { Directory.CreateDirectory(dstBase); File.Copy(s, Path.Combine(dstBase, "user.cfg"), true); log("  user.cfg"); n++; } catch (Exception ex) { log("  ERROR user.cfg: " + ex.Message); } }
                else log("  (no user.cfg in source)");
            }
            return n;
        }

        static int CopyDir(string src, string dst, string label, Action<string> log) {
            if (!Directory.Exists(src)) { log("  (no " + label + " in source)"); return 0; }
            int failed = 0, copied = 0;
            try { copied = CopyTree(src, dst, ref failed); }
            catch (Exception ex) { log("  ERROR " + label + ": " + ex.Message); return copied; }
            if (failed > 0) log("  " + label + " (" + copied + " copied, " + failed + " LOCKED/skipped - close Star Citizen)");
            else log("  " + label + " (" + copied + " file(s))");
            return copied;
        }

        static int CopyTree(string src, string dst, ref int failed) {
            Directory.CreateDirectory(dst);
            int c = 0;
            foreach (string f in Directory.GetFiles(src)) { try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); c++; } catch { failed++; } }
            foreach (string sub in Directory.GetDirectories(src)) c += CopyTree(sub, Path.Combine(dst, Path.GetFileName(sub)), ref failed);
            return c;
        }
    }
}
