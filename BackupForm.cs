using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SCKeepAlive {

    // Backup / restore the things a Star Citizen patch or channel-switch wipes:
    //   - user\                  (in-game settings + key bindings)
    //   - data\Localization\     (StarStrings text mod, e.g. global.ini)
    //   - user.cfg               (StarStrings language line)
    // Backups are copied to Documents\SC-KeepAlive\Backups\<channel>-<timestamp>\.
    // Copy/restore only ever writes those 3 known sub-paths, overwrites (never deletes), and asks first.
    public class BackupForm : Form {
        TextBox txtRoot, txtLog;
        CheckBox chkUser, chkLoc, chkCfg;
        ComboBox cmbBackupCh, cmbFrom, cmbTo;
        Button btnBackup, btnCopy, btnRefresh, btnOpen;
        ListBox lstBackups;

        string BackupsRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SC-KeepAlive", "Backups"); } }
        string ScRoot { get { return txtRoot.Text.Trim(); } }

        Label L(string t, int x, int y, int w) { Label l = new Label(); l.Text = t; l.SetBounds(x, y, w, 20); Controls.Add(l); return l; }

        public BackupForm() {
            Text = "SC Keep-Alive - Backup / Restore";
            ClientSize = new Size(544, 540);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            L("Star Citizen folder:", 12, 14, 128);
            txtRoot = new TextBox(); txtRoot.SetBounds(144, 11, 388, 22);
            txtRoot.Text = @"C:\Program Files\Roberts Space Industries\StarCitizen"; Controls.Add(txtRoot);

            L("What to copy:", 12, 44, 120);
            chkUser = new CheckBox(); chkUser.Text = "User settings & key bindings  (user\\)"; chkUser.SetBounds(24, 66, 340, 20); chkUser.Checked = true; Controls.Add(chkUser);
            chkLoc = new CheckBox(); chkLoc.Text = "StarStrings text mod  (data\\Localization\\)"; chkLoc.SetBounds(24, 88, 340, 20); chkLoc.Checked = true; Controls.Add(chkLoc);
            chkCfg = new CheckBox(); chkCfg.Text = "StarStrings language line  (user.cfg)"; chkCfg.SetBounds(24, 110, 340, 20); chkCfg.Checked = true; Controls.Add(chkCfg);

            L("1.  Back up a channel to a safe folder", 12, 144, 340);
            L("Channel:", 24, 170, 60);
            cmbBackupCh = new ComboBox(); cmbBackupCh.DropDownStyle = ComboBoxStyle.DropDownList; cmbBackupCh.SetBounds(88, 167, 130, 22); Controls.Add(cmbBackupCh);
            btnBackup = new Button(); btnBackup.Text = "Back up now"; btnBackup.SetBounds(228, 165, 120, 26); Controls.Add(btnBackup);

            L("2.  Copy / restore into a channel", 12, 202, 340);
            L("From:", 24, 228, 40);
            cmbFrom = new ComboBox(); cmbFrom.DropDownStyle = ComboBoxStyle.DropDownList; cmbFrom.SetBounds(66, 225, 300, 22); Controls.Add(cmbFrom);
            L("To:", 374, 228, 26);
            cmbTo = new ComboBox(); cmbTo.DropDownStyle = ComboBoxStyle.DropDownList; cmbTo.SetBounds(402, 225, 130, 22); Controls.Add(cmbTo);
            btnCopy = new Button(); btnCopy.Text = "Copy / restore  ->"; btnCopy.SetBounds(24, 254, 170, 28); Controls.Add(btnCopy);

            L("Existing backups:", 12, 294, 200);
            btnRefresh = new Button(); btnRefresh.Text = "Refresh"; btnRefresh.SetBounds(364, 290, 80, 24); Controls.Add(btnRefresh);
            btnOpen = new Button(); btnOpen.Text = "Open folder"; btnOpen.SetBounds(450, 290, 82, 24); Controls.Add(btnOpen);
            lstBackups = new ListBox(); lstBackups.SetBounds(12, 318, 520, 104); Controls.Add(lstBackups);

            L("Log:", 12, 428, 40);
            txtLog = new TextBox(); txtLog.SetBounds(12, 448, 520, 80); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; Controls.Add(txtLog);

            btnBackup.Click += delegate { DoBackup(); };
            btnCopy.Click += delegate { DoCopy(); };
            btnRefresh.Click += delegate { Populate(); };
            btnOpen.Click += delegate { try { Directory.CreateDirectory(BackupsRoot); System.Diagnostics.Process.Start("explorer.exe", "\"" + BackupsRoot + "\""); } catch (Exception ex) { Log("ERROR: " + ex.Message); } };
            txtRoot.Leave += delegate { Populate(); };

            Populate();
        }

        void Log(string m) { txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + m + "\r\n"); }

        string[] DetectChannels() {
            List<string> ch = new List<string>();
            try {
                if (Directory.Exists(ScRoot)) {
                    foreach (string d in Directory.GetDirectories(ScRoot)) {
                        if (Directory.Exists(Path.Combine(d, "user")) || Directory.Exists(Path.Combine(d, "data")))
                            ch.Add(Path.GetFileName(d));
                    }
                }
            } catch { }
            return ch.ToArray();
        }

        void Populate() {
            string[] chans = DetectChannels();
            cmbBackupCh.Items.Clear(); cmbTo.Items.Clear(); cmbFrom.Items.Clear(); lstBackups.Items.Clear();
            foreach (string c in chans) { cmbBackupCh.Items.Add(c); cmbTo.Items.Add(c); cmbFrom.Items.Add(c + " (current)"); }
            List<string> snaps = new List<string>();
            try { if (Directory.Exists(BackupsRoot)) foreach (string d in Directory.GetDirectories(BackupsRoot)) snaps.Add(Path.GetFileName(d)); } catch { }
            snaps.Sort(); snaps.Reverse();
            foreach (string s in snaps) { cmbFrom.Items.Add(s); lstBackups.Items.Add(s); }
            if (cmbBackupCh.Items.Count > 0) cmbBackupCh.SelectedIndex = Pick(cmbBackupCh, "LIVE");
            if (cmbTo.Items.Count > 0) cmbTo.SelectedIndex = Pick(cmbTo, "HOTFIX");
            if (cmbFrom.Items.Count > 0) cmbFrom.SelectedIndex = Pick(cmbFrom, "LIVE (current)");
        }

        int Pick(ComboBox cb, string val) { int i = cb.Items.IndexOf(val); return i >= 0 ? i : 0; }

        void DoBackup() {
            string ch = cmbBackupCh.SelectedItem as string;
            if (string.IsNullOrEmpty(ch)) { Log("pick a channel to back up"); return; }
            string src = Path.Combine(ScRoot, ch);
            if (!Directory.Exists(src)) { Log("channel not found: " + src); return; }
            string dst = Path.Combine(BackupsRoot, ch + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Log("backing up " + ch + " ...");
            int n = CopyItems(src, dst);
            Log(n > 0 ? ("backup complete (" + n + " file(s)) -> " + dst) : "nothing copied - check the checkboxes and source channel");
            Populate();
        }

        void DoCopy() {
            string from = cmbFrom.SelectedItem as string;
            string to = cmbTo.SelectedItem as string;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) { Log("pick both From and To"); return; }
            string srcBase = from.EndsWith(" (current)") ? Path.Combine(ScRoot, from.Substring(0, from.Length - 10)) : Path.Combine(BackupsRoot, from);
            string dstBase = Path.Combine(ScRoot, to);
            if (!Directory.Exists(srcBase)) { Log("source not found: " + srcBase); return; }
            try {
                if (string.Equals(Path.GetFullPath(srcBase).TrimEnd('\\'), Path.GetFullPath(dstBase).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) {
                    Log("source and target are the same - nothing to do"); return;
                }
            } catch { }
            DialogResult r = MessageBox.Show(
                "Copy the ticked items\n\nFROM:   " + from + "\nTO:        " + to + " channel\n\nExisting files in the target are overwritten (nothing is deleted).\nClose Star Citizen first. Proceed?",
                "Confirm copy / restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) { Log("cancelled"); return; }
            Log("copying " + from + " -> " + to + " ...");
            int n = CopyItems(srcBase, dstBase);
            Log(n > 0 ? ("done - copied " + n + " file(s) into " + to + ". Restart Star Citizen to apply.") : "nothing copied - check the checkboxes/source");
        }

        int CopyItems(string srcBase, string dstBase) {
            int n = 0;
            if (chkUser.Checked) n += CopyDir(Path.Combine(srcBase, "user"), Path.Combine(dstBase, "user"), "user\\");
            if (chkLoc.Checked) n += CopyDir(Path.Combine(srcBase, "data", "Localization"), Path.Combine(dstBase, "data", "Localization"), "data\\Localization\\");
            if (chkCfg.Checked) {
                string s = Path.Combine(srcBase, "user.cfg");
                if (File.Exists(s)) { try { Directory.CreateDirectory(dstBase); File.Copy(s, Path.Combine(dstBase, "user.cfg"), true); Log("  user.cfg"); n++; } catch (Exception ex) { Log("  ERROR user.cfg: " + ex.Message); } }
                else Log("  (no user.cfg in source)");
            }
            return n;
        }

        int CopyDir(string src, string dst, string label) {
            if (!Directory.Exists(src)) { Log("  (no " + label + " in source)"); return 0; }
            int failed = 0, copied = 0;
            try { copied = CopyTree(src, dst, ref failed); }
            catch (Exception ex) { Log("  ERROR " + label + ": " + ex.Message); return copied; }
            if (failed > 0) Log("  " + label + " (" + copied + " file(s) copied, " + failed + " LOCKED/skipped - close Star Citizen and run again)");
            else Log("  " + label + " (" + copied + " file(s))");
            return copied;
        }

        static int CopyTree(string src, string dst, ref int failed) {
            Directory.CreateDirectory(dst);
            int c = 0;
            foreach (string f in Directory.GetFiles(src)) {
                try { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); c++; }
                catch { failed++; }
            }
            foreach (string sub in Directory.GetDirectories(src)) c += CopyTree(sub, Path.Combine(dst, Path.GetFileName(sub)), ref failed);
            return c;
        }
    }
}
