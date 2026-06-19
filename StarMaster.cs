using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace StarMaster {

    static class Native {
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        const uint UP = 0x2;
        public static string ActiveTitle() {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(GetForegroundWindow(), sb, 256);
            return sb.ToString();
        }
        public static void Press(byte[] mods, byte key) {
            foreach (byte m in mods) keybd_event(m, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(40);
            keybd_event(key, 0, UP, UIntPtr.Zero);
            for (int i = mods.Length - 1; i >= 0; i--) keybd_event(mods[i], 0, UP, UIntPtr.Zero);
        }
    }

    static class Vk {
        public static byte Map(string k) {
            if (string.IsNullOrEmpty(k)) return 0;
            k = k.Trim().ToUpper();
            if (k.Length == 1) {
                char c = k[0];
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) return (byte)c;
            }
            switch (k) {
                case "SPACE": return 0x20;
                case "ENTER": return 0x0D;
                case "TAB": return 0x09;
                case "ESC": return 0x1B;
                case "F1": return 0x70; case "F2": return 0x71; case "F3": return 0x72; case "F4": return 0x73;
                case "F5": return 0x74; case "F6": return 0x75; case "F7": return 0x76; case "F8": return 0x77;
                case "F9": return 0x78; case "F10": return 0x79; case "F11": return 0x7A; case "F12": return 0x7B;
                default: return 0;
            }
        }
    }

    class Cmd {
        public string Label = "Command";
        public bool Shift, Ctrl, Alt;
        public string Key = "";
        public int Interval = 120;
        public bool Enabled = true;
        public DateTime LastFire = DateTime.MinValue;
    }

    public class MainForm : Form {
        TextBox txtLabel, txtKey, txtTitle, txtLog;
        CheckBox chkShift, chkCtrl, chkAlt, chkFocus, chkAuto;
        NumericUpDown numInt;
        Button btnAdd, btnRemove, btnStart, btnBackup;
        ListBox lst;
        Label lblStatus;
        Timer timer;
        List<Cmd> commands = new List<Cmd>();
        string cfgPath;

        public const string Version = "1";   // bump per release; shown in the title bar and matches the GitHub Release tag (vN)

        Label NewLabel(string t, int x, int y, int w) {
            Label l = new Label();
            l.Text = t; l.SetBounds(x, y, w, 20);
            Controls.Add(l); return l;
        }

        public MainForm() {
            cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            bool autostart, focusguard; string wintitle;
            LoadConfig(out autostart, out focusguard, out wintitle);

            Text = "StarMaster v" + Version;
            ClientSize = new Size(500, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            NewLabel("Label:", 10, 12, 42);
            txtLabel = new TextBox(); txtLabel.SetBounds(54, 9, 120, 22); Controls.Add(txtLabel);
            chkShift = new CheckBox(); chkShift.Text = "Shift"; chkShift.SetBounds(182, 10, 55, 20); Controls.Add(chkShift);
            chkCtrl = new CheckBox(); chkCtrl.Text = "Ctrl"; chkCtrl.SetBounds(238, 10, 48, 20); Controls.Add(chkCtrl);
            chkAlt = new CheckBox(); chkAlt.Text = "Alt"; chkAlt.SetBounds(288, 10, 44, 20); Controls.Add(chkAlt);
            NewLabel("Key:", 336, 12, 30);
            txtKey = new TextBox(); txtKey.SetBounds(368, 9, 40, 22); Controls.Add(txtKey);

            NewLabel("Every (sec):", 10, 44, 72);
            numInt = new NumericUpDown(); numInt.SetBounds(86, 42, 60, 22); numInt.Minimum = 5; numInt.Maximum = 3600; numInt.Value = 120; Controls.Add(numInt);
            btnAdd = new Button(); btnAdd.Text = "Add / Update"; btnAdd.SetBounds(160, 40, 110, 26); Controls.Add(btnAdd);
            btnRemove = new Button(); btnRemove.Text = "Remove"; btnRemove.SetBounds(278, 40, 90, 26); Controls.Add(btnRemove);

            lst = new ListBox(); lst.SetBounds(10, 78, 472, 150); Controls.Add(lst);
            NewLabel("Tip: double-click a row to toggle ON/off; click a row + Add/Update to overwrite it.", 10, 232, 480);

            chkFocus = new CheckBox(); chkFocus.Text = "Only send while active window contains:"; chkFocus.SetBounds(10, 258, 248, 20); Controls.Add(chkFocus);
            txtTitle = new TextBox(); txtTitle.SetBounds(260, 256, 150, 22); Controls.Add(txtTitle);
            chkAuto = new CheckBox(); chkAuto.Text = "Auto-start when launched (use this for run-on-boot)"; chkAuto.SetBounds(10, 284, 400, 20); Controls.Add(chkAuto);

            btnStart = new Button(); btnStart.Text = "Start"; btnStart.SetBounds(10, 312, 110, 34); Controls.Add(btnStart);
            lblStatus = new Label(); lblStatus.Text = "Stopped"; lblStatus.ForeColor = Color.Firebrick; lblStatus.SetBounds(128, 320, 150, 20); Controls.Add(lblStatus);
            btnBackup = new Button(); btnBackup.Text = "Backup / Restore..."; btnBackup.SetBounds(286, 312, 164, 34); Controls.Add(btnBackup);
            btnBackup.Click += delegate { using (BackupForm bf = new BackupForm()) { bf.ShowDialog(this); } };

            NewLabel("Log:", 10, 356, 40);
            txtLog = new TextBox(); txtLog.SetBounds(10, 376, 472, 128); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical; Controls.Add(txtLog);

            timer = new Timer(); timer.Interval = 1000; timer.Tick += Timer_Tick;

            btnAdd.Click += BtnAdd_Click;
            btnRemove.Click += BtnRemove_Click;
            btnStart.Click += delegate { ToggleStart(); };
            lst.DoubleClick += delegate { int i = lst.SelectedIndex; if (i >= 0) { commands[i].Enabled = !commands[i].Enabled; RefreshList(); } };
            lst.SelectedIndexChanged += delegate {
                int i = lst.SelectedIndex;
                if (i >= 0 && i < commands.Count) {
                    Cmd c = commands[i];
                    txtLabel.Text = c.Label; chkShift.Checked = c.Shift; chkCtrl.Checked = c.Ctrl; chkAlt.Checked = c.Alt;
                    txtKey.Text = c.Key; numInt.Value = Math.Max(numInt.Minimum, Math.Min(numInt.Maximum, c.Interval));
                }
            };

            RefreshList();
            chkAuto.Checked = autostart; chkFocus.Checked = focusguard; txtTitle.Text = wintitle;
            FormClosing += delegate { timer.Stop(); SaveConfig(); };
            if (autostart) ToggleStart();
        }

        void BtnAdd_Click(object s, EventArgs e) {
            string key = txtKey.Text.Trim();
            if (key.Length == 0) { Log("! enter a Key first"); return; }
            if (Vk.Map(key) == 0) { Log("! '" + key + "' not recognized (A-Z, 0-9, F1-F12, Space, Enter, Tab, Esc)"); return; }
            Cmd c = new Cmd();
            c.Label = txtLabel.Text.Trim().Length > 0 ? txtLabel.Text.Trim() : "Command";
            c.Shift = chkShift.Checked; c.Ctrl = chkCtrl.Checked; c.Alt = chkAlt.Checked;
            c.Key = key; c.Interval = (int)numInt.Value; c.Enabled = true;
            int i = lst.SelectedIndex;
            if (i >= 0 && i < commands.Count) commands[i] = c; else commands.Add(c);
            RefreshList();
        }

        void BtnRemove_Click(object s, EventArgs e) {
            int i = lst.SelectedIndex;
            if (i >= 0 && i < commands.Count) { commands.RemoveAt(i); RefreshList(); }
        }

        string Combo(Cmd c) {
            List<string> p = new List<string>();
            if (c.Shift) p.Add("Shift");
            if (c.Ctrl) p.Add("Ctrl");
            if (c.Alt) p.Add("Alt");
            p.Add(c.Key);
            return string.Join("+", p.ToArray());
        }

        void RefreshList() {
            lst.Items.Clear();
            foreach (Cmd c in commands)
                lst.Items.Add("[" + (c.Enabled ? "ON " : "off") + "] " + c.Label + "  -  " + Combo(c) + "  every " + c.Interval + "s");
        }

        void Log(string m) { txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + m + "\r\n"); }

        void Timer_Tick(object s, EventArgs e) {
            foreach (Cmd c in commands) {
                if (!c.Enabled) continue;
                if ((DateTime.Now - c.LastFire).TotalSeconds < c.Interval) continue;
                if (chkFocus.Checked) {
                    // Fail CLOSED: if the guard is on but no title is set, send nothing
                    // (rather than firing keystrokes into whatever window is focused).
                    string t = txtTitle.Text.Trim().ToLower();
                    if (t.Length == 0 || !Native.ActiveTitle().ToLower().Contains(t)) continue;
                }
                c.LastFire = DateTime.Now;
                List<byte> mods = new List<byte>();
                if (c.Shift) mods.Add(0xA0);
                if (c.Ctrl) mods.Add(0xA2);
                if (c.Alt) mods.Add(0xA4);
                byte vk = Vk.Map(c.Key);
                if (vk == 0) { Log("! bad key for " + c.Label); continue; }
                Native.Press(mods.ToArray(), vk);
                Log("sent " + c.Label + " (" + Combo(c) + ")");
            }
        }

        void ToggleStart() {
            if (timer.Enabled) {
                timer.Stop(); btnStart.Text = "Start"; lblStatus.Text = "Stopped"; lblStatus.ForeColor = Color.Firebrick; Log("stopped");
            } else {
                foreach (Cmd c in commands) c.LastFire = DateTime.Now;
                timer.Start(); btnStart.Text = "Stop"; lblStatus.Text = "Running"; lblStatus.ForeColor = Color.ForestGreen; Log("started");
            }
        }

        void LoadConfig(out bool autostart, out bool focusguard, out string wintitle) {
            autostart = false; focusguard = true; wintitle = "Star Citizen";
            commands = new List<Cmd>();
            try {
                if (File.Exists(cfgPath)) {
                    foreach (string line in File.ReadAllLines(cfgPath)) {
                        string ln = line.Trim();
                        if (ln.Length == 0 || ln.StartsWith("#")) continue;
                        if (ln.IndexOf('|') >= 0) {
                            string[] f = ln.Split('|');
                            if (f.Length >= 7) {
                                Cmd c = new Cmd();
                                c.Label = f[0]; c.Shift = f[1] == "1"; c.Ctrl = f[2] == "1"; c.Alt = f[3] == "1"; c.Key = f[4];
                                int iv; int.TryParse(f[5], out iv); c.Interval = iv < 5 ? 5 : (iv > 3600 ? 3600 : iv);
                                c.Enabled = f[6] == "1";
                                commands.Add(c);
                            }
                        } else if (ln.IndexOf('=') > 0) {
                            string[] kv = ln.Split(new char[] { '=' }, 2);
                            string k = kv[0].Trim().ToLower(); string v = kv[1].Trim();
                            if (k == "autostart") autostart = v == "1";
                            else if (k == "focusguard") focusguard = v == "1";
                            else if (k == "wintitle") wintitle = v;
                        }
                    }
                }
            } catch { }
            if (commands.Count == 0) {
                Cmd c = new Cmd(); c.Label = "Wipe Visor"; c.Alt = true; c.Key = "X"; c.Interval = 120; c.Enabled = true;
                commands.Add(c);
            }
        }

        void SaveConfig() {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# StarMaster config");
                sb.AppendLine("autostart=" + (chkAuto.Checked ? "1" : "0"));
                sb.AppendLine("focusguard=" + (chkFocus.Checked ? "1" : "0"));
                sb.AppendLine("wintitle=" + txtTitle.Text);
                sb.AppendLine("# commands: Label|Shift|Ctrl|Alt|Key|Interval|Enabled");
                foreach (Cmd c in commands)
                    sb.AppendLine(c.Label.Replace("|", "/") + "|" + (c.Shift ? "1" : "0") + "|" + (c.Ctrl ? "1" : "0") + "|" + (c.Alt ? "1" : "0") + "|" + c.Key + "|" + c.Interval + "|" + (c.Enabled ? "1" : "0"));
                File.WriteAllText(cfgPath, sb.ToString());
            } catch { }
        }

        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
