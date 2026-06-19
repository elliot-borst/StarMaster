using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

    // Dark "HUD" theme (amber on near-black) - matches the app icon and the Star Citizen aesthetic.
    // Dependency-free: just colors, fonts and WinForms/GDI+ styling helpers.
    static class Theme {
        public static readonly Color Bg       = Color.FromArgb(14, 18, 22);
        public static readonly Color Surface  = Color.FromArgb(22, 28, 34);
        public static readonly Color Surface2 = Color.FromArgb(31, 39, 47);
        public static readonly Color Line     = Color.FromArgb(45, 56, 66);
        public static readonly Color Text      = Color.FromArgb(214, 224, 230);
        public static readonly Color TextDim   = Color.FromArgb(138, 152, 162);
        public static readonly Color Amber     = Color.FromArgb(255, 176, 0);
        public static readonly Color AmberDim  = Color.FromArgb(176, 122, 0);
        public static readonly Color Good       = Color.FromArgb(80, 200, 120);
        public static readonly Color Bad        = Color.FromArgb(228, 96, 84);

        public static readonly Font Ui     = new Font("Segoe UI", 9f);
        public static readonly Font UiBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Title  = new Font("Segoe UI", 15f, FontStyle.Bold);
        public static readonly Font Small  = new Font("Segoe UI", 8f);
        public static readonly Font Mono   = new Font("Consolas", 8.5f);

        public static void StyleButton(Button b, bool primary) {
            b.FlatStyle = FlatStyle.Flat;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderSize = 1;
            b.Cursor = Cursors.Hand;
            if (b.Font == null) b.Font = Ui;
            if (primary) {
                b.BackColor = Amber;
                b.ForeColor = Color.FromArgb(20, 18, 10);
                b.FlatAppearance.BorderColor = Amber;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 196, 64);
                b.FlatAppearance.MouseDownBackColor = AmberDim;
            } else {
                b.BackColor = Surface2;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Line;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 52, 62);
                b.FlatAppearance.MouseDownBackColor = Surface;
            }
        }

        public static void StyleCheck(CheckBox c) {
            c.FlatStyle = FlatStyle.Flat;
            c.ForeColor = Text;
            c.BackColor = Color.Transparent;
            c.FlatAppearance.BorderColor = Line;
        }

        // Recursively apply the theme to a container's controls (used by BackupForm).
        public static void Apply(Control root) {
            foreach (Control k in root.Controls) {
                if (k is TextBox)        { k.BackColor = Surface; k.ForeColor = Text; ((TextBox)k).BorderStyle = BorderStyle.FixedSingle; }
                else if (k is Button)    { StyleButton((Button)k, false); }
                else if (k is CheckBox)  { StyleCheck((CheckBox)k); }
                else if (k is ComboBox)  { k.BackColor = Surface; k.ForeColor = Text; ((ComboBox)k).FlatStyle = FlatStyle.Flat; }
                else if (k is ListBox)   { k.BackColor = Surface; k.ForeColor = Text; ((ListBox)k).BorderStyle = BorderStyle.FixedSingle; }
                else if (k is Label)     { k.ForeColor = Text; k.BackColor = Color.Transparent; }
                if (k.Controls.Count > 0) Apply(k);
            }
        }

        // DPI scale factor for the screen this control is on (1.0 at 96 DPI, 1.5 at 144, 2.0 at 192...).
        public static float DpiFactor(Control c) {
            using (Graphics g = c.CreateGraphics()) return g.DpiX / 96f;
        }

        // Multiply every child control's bounds (and owner-drawn ListBox row height) by f. Point-based
        // fonts already grow with DPI, so we scale only geometry here to match.
        public static void ScaleControls(Control parent, float f) {
            foreach (Control c in parent.Controls) {
                c.Bounds = new Rectangle((int)Math.Round(c.Left * f), (int)Math.Round(c.Top * f), (int)Math.Round(c.Width * f), (int)Math.Round(c.Height * f));
                ListBox lb = c as ListBox;
                if (lb != null && lb.DrawMode == DrawMode.OwnerDrawFixed) lb.ItemHeight = (int)Math.Round(lb.ItemHeight * f);
                if (c.HasChildren) ScaleControls(c, f);
            }
        }
    }

    // WebClient with a real timeout so a stalled asset download fails fast instead of hanging forever.
    class TimedWebClient : WebClient {
        protected override WebRequest GetWebRequest(Uri address) {
            WebRequest r = base.GetWebRequest(address);
            HttpWebRequest h = r as HttpWebRequest;
            if (h != null) { h.Timeout = 30000; h.ReadWriteTimeout = 30000; }
            return r;
        }
    }

    // Checks GitHub Releases for a newer version. Works against a PUBLIC repo (no token needed).
    static class Updater {
        const string Owner = "elliot-borst";
        const string Repo  = "StarMaster";
        const string ApiUrl     = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";

        public class Info { public int[] Version; public string Tag; public string SetupUrl; public string PageUrl; }

        // Parse every numeric component of a tag: "v2" -> {2}, "v2.1" -> {2,1}, "2025.06" -> {2025,6}; {} if none.
        public static int[] ParseVer(string tag) {
            List<int> parts = new List<int>();
            if (!string.IsNullOrEmpty(tag)) {
                foreach (Match m in Regex.Matches(tag, "[0-9]+")) {
                    int v; if (int.TryParse(m.Value, out v)) parts.Add(v);
                }
            }
            return parts.ToArray();
        }

        // Component-wise compare; >0 if a is newer than b. Missing components count as 0 (so "2" == "2.0").
        public static int Compare(int[] a, int[] b) {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++) {
                int ai = i < a.Length ? a[i] : 0;
                int bi = i < b.Length ? b[i] : 0;
                if (ai != bi) return ai < bi ? -1 : 1;
            }
            return 0;
        }

        // Returns info for the latest release, or null on any failure (no connection, private repo, etc).
        public static Info CheckLatest() {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; // GitHub requires TLS 1.2+
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.UserAgent = "StarMaster-Updater";          // GitHub API rejects requests with no User-Agent
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000;
                string json;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();

                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                if (!tag.Success) return null;
                Info info = new Info();
                info.Tag = tag.Groups[1].Value;
                info.Version = ParseVer(info.Tag);
                Match page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                info.PageUrl = page.Success ? page.Groups[1].Value : ReleasesPage;

                // Only a "*setup*.exe" asset counts as an installer; if a release has no setup asset,
                // SetupUrl stays null and the UI offers "Open download page" instead of a bogus install.
                info.SetupUrl = FindAsset(json, true);
                return info;
            } catch { return null; }
        }

        static string FindAsset(string json, bool requireSetup) {
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) {
                string url = m.Groups[1].Value;
                string low = url.ToLower();
                if (low.EndsWith(".exe") && (!requireSetup || low.Contains("setup"))) return url;
            }
            return null;
        }

        // Download an installer to a temp file. Returns the temp path, or null on failure.
        public static string DownloadInstaller(string url) {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string tmp = Path.Combine(Path.GetTempPath(), "StarMaster-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                using (WebClient wc = new TimedWebClient()) {
                    wc.Headers.Add("User-Agent", "StarMaster-Updater");
                    wc.DownloadFile(url, tmp);
                }
                return tmp;
            } catch { return null; }
        }
    }

    public class MainForm : Form {
        TextBox txtLabel, txtKey, txtTitle, txtLog;
        CheckBox chkShift, chkCtrl, chkAlt, chkFocus, chkAuto;
        NumericUpDown numInt;
        Button btnAdd, btnRemove, btnStart, btnBackup, btnUpdate, btnGet, btnDismiss;
        ListBox lst;
        Label lblStatus, lblBanner;
        Panel header, banner, content;
        Timer timer;
        List<Cmd> commands = new List<Cmd>();
        string cfgPath;
        int[] CurrentVer;
        Updater.Info pendingUpdate;

        public const string Version = "2";   // bump per release; shown in the title bar and matches the GitHub Release tag (vN)

        const int HeaderH = 58;
        const int ContentH = 520;
        const int BaseClientW = 500;
        const int BaseClientH = HeaderH + ContentH;
        const int BannerH = 40;

        Label NewLabel(string t, int x, int y, int w) {
            Label l = new Label();
            l.Text = t; l.SetBounds(x, y, w, 20);
            l.Font = Theme.Ui; l.ForeColor = Theme.Text; l.BackColor = Color.Transparent;
            content.Controls.Add(l); return l;
        }

        public MainForm() {
            cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            CurrentVer = Updater.ParseVer(Version);
            bool autostart, focusguard; string wintitle;
            LoadConfig(out autostart, out focusguard, out wintitle);

            Text = "StarMaster v" + Version;
            // Scale the hand-coded layout from its 96-DPI design size to the current display DPI.
            AutoScaleMode = AutoScaleMode.None;   // hand-coded layout is scaled manually in ScaleToDpi()
            ClientSize = new Size(BaseClientW, BaseClientH);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildHeader();
            BuildBanner();
            BuildContent();

            // Dock order: add the Fill panel first, then the top-docked banner, then the header last
            // so they stack top-to-bottom as header / banner / content.
            Controls.Add(content);
            Controls.Add(banner);
            Controls.Add(header);
            ScaleToDpi();

            timer = new Timer(); timer.Interval = 1000; timer.Tick += Timer_Tick;

            btnAdd.Click += BtnAdd_Click;
            btnRemove.Click += BtnRemove_Click;
            btnStart.Click += delegate { ToggleStart(); };
            btnBackup.Click += delegate { using (BackupForm bf = new BackupForm()) { bf.ShowDialog(this); } };
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
            Shown += delegate { CheckForUpdates(false); };
            if (autostart) ToggleStart();
        }

        void BuildHeader() {
            header = new Panel(); header.Dock = DockStyle.Top; header.Height = HeaderH; header.BackColor = Theme.Surface2;
            header.Paint += delegate(object s, PaintEventArgs e) {
                using (Pen p = new Pen(Theme.Amber)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };

            PictureBox pic = new PictureBox(); pic.SetBounds(14, 13, 32, 32); pic.SizeMode = PictureBoxSizeMode.StretchImage; pic.BackColor = Color.Transparent;
            try { if (Icon != null) { using (Icon ico = new Icon(Icon, 32, 32)) pic.Image = ico.ToBitmap(); } } catch { }
            header.Controls.Add(pic);

            Label t = new Label(); t.Text = "StarMaster"; t.Font = Theme.Title; t.ForeColor = Theme.Amber; t.BackColor = Color.Transparent; t.SetBounds(52, 8, 250, 28);
            header.Controls.Add(t);
            Label sub = new Label(); sub.Text = "Star Citizen toolkit - v" + Version; sub.Font = Theme.Small; sub.ForeColor = Theme.TextDim; sub.BackColor = Color.Transparent; sub.SetBounds(54, 35, 280, 16);
            header.Controls.Add(sub);

            btnUpdate = new Button(); btnUpdate.Text = "Check for updates"; btnUpdate.Font = Theme.Ui; btnUpdate.SetBounds(350, 16, 138, 26);
            Theme.StyleButton(btnUpdate, false);
            btnUpdate.Click += delegate { Log("checking for updates..."); CheckForUpdates(true); };
            header.Controls.Add(btnUpdate);
        }

        void BuildBanner() {
            banner = new Panel(); banner.Dock = DockStyle.Top; banner.Height = BannerH; banner.Visible = false; banner.BackColor = Color.FromArgb(38, 32, 16);
            banner.Paint += delegate(object s, PaintEventArgs e) {
                using (SolidBrush b = new SolidBrush(Theme.Amber)) e.Graphics.FillRectangle(b, 0, 0, 3, banner.Height);
            };
            lblBanner = new Label(); lblBanner.SetBounds(16, 11, 290, 18); lblBanner.Font = Theme.UiBold; lblBanner.ForeColor = Theme.Amber; lblBanner.BackColor = Color.Transparent;
            banner.Controls.Add(lblBanner);
            btnGet = new Button(); btnGet.Text = "Download & Install"; btnGet.Font = Theme.Ui; btnGet.SetBounds(312, 7, 150, 26);
            Theme.StyleButton(btnGet, true);
            btnGet.Click += delegate { DownloadUpdate(); };
            banner.Controls.Add(btnGet);
            btnDismiss = new Button(); btnDismiss.Text = "X"; btnDismiss.Font = Theme.Ui; btnDismiss.SetBounds(466, 7, 24, 26);
            Theme.StyleButton(btnDismiss, false);
            btnDismiss.Click += delegate { HideBanner(); };
            banner.Controls.Add(btnDismiss);
        }

        void BuildContent() {
            content = new Panel(); content.Dock = DockStyle.Fill; content.BackColor = Theme.Bg;

            NewLabel("Label:", 10, 12, 42);
            txtLabel = new TextBox(); txtLabel.SetBounds(54, 9, 120, 22); StyleInput(txtLabel); content.Controls.Add(txtLabel);
            chkShift = new CheckBox(); chkShift.Text = "Shift"; chkShift.SetBounds(182, 10, 55, 20); Theme.StyleCheck(chkShift); content.Controls.Add(chkShift);
            chkCtrl = new CheckBox(); chkCtrl.Text = "Ctrl"; chkCtrl.SetBounds(238, 10, 48, 20); Theme.StyleCheck(chkCtrl); content.Controls.Add(chkCtrl);
            chkAlt = new CheckBox(); chkAlt.Text = "Alt"; chkAlt.SetBounds(288, 10, 44, 20); Theme.StyleCheck(chkAlt); content.Controls.Add(chkAlt);
            NewLabel("Key:", 336, 12, 30);
            txtKey = new TextBox(); txtKey.SetBounds(368, 9, 40, 22); StyleInput(txtKey); content.Controls.Add(txtKey);

            NewLabel("Every (sec):", 10, 44, 72);
            numInt = new NumericUpDown(); numInt.SetBounds(86, 42, 60, 22); numInt.Minimum = 5; numInt.Maximum = 3600; numInt.Value = 120;
            numInt.BackColor = Theme.Surface; numInt.ForeColor = Theme.Text; numInt.BorderStyle = BorderStyle.FixedSingle; content.Controls.Add(numInt);
            btnAdd = new Button(); btnAdd.Text = "Add / Update"; btnAdd.SetBounds(160, 40, 110, 26); Theme.StyleButton(btnAdd, false); content.Controls.Add(btnAdd);
            btnRemove = new Button(); btnRemove.Text = "Remove"; btnRemove.SetBounds(278, 40, 90, 26); Theme.StyleButton(btnRemove, false); content.Controls.Add(btnRemove);

            lst = new ListBox(); lst.SetBounds(10, 78, 472, 150);
            lst.BackColor = Theme.Surface; lst.ForeColor = Theme.Text; lst.BorderStyle = BorderStyle.FixedSingle;
            lst.DrawMode = DrawMode.OwnerDrawFixed; lst.ItemHeight = 24; lst.IntegralHeight = false;
            lst.DrawItem += Lst_DrawItem;
            content.Controls.Add(lst);
            Label tip = NewLabel("Tip: double-click a row to toggle ON/off; click a row + Add/Update to overwrite it.", 10, 232, 480);
            tip.ForeColor = Theme.TextDim; tip.Font = Theme.Small;

            chkFocus = new CheckBox(); chkFocus.Text = "Only send while active window contains:"; chkFocus.SetBounds(10, 258, 248, 20); Theme.StyleCheck(chkFocus); content.Controls.Add(chkFocus);
            txtTitle = new TextBox(); txtTitle.SetBounds(260, 256, 150, 22); StyleInput(txtTitle); content.Controls.Add(txtTitle);
            chkAuto = new CheckBox(); chkAuto.Text = "Auto-start when launched (use this for run-on-boot)"; chkAuto.SetBounds(10, 284, 400, 20); Theme.StyleCheck(chkAuto); content.Controls.Add(chkAuto);

            btnStart = new Button(); btnStart.Text = "Start"; btnStart.SetBounds(10, 312, 110, 34); Theme.StyleButton(btnStart, true); btnStart.Font = Theme.UiBold; content.Controls.Add(btnStart);
            lblStatus = new Label(); lblStatus.Text = "Stopped"; lblStatus.Font = Theme.UiBold; lblStatus.ForeColor = Theme.Bad; lblStatus.BackColor = Color.Transparent; lblStatus.SetBounds(128, 320, 150, 20); content.Controls.Add(lblStatus);
            btnBackup = new Button(); btnBackup.Text = "Backup / Restore..."; btnBackup.SetBounds(286, 312, 164, 34); Theme.StyleButton(btnBackup, false); content.Controls.Add(btnBackup);

            NewLabel("Log:", 10, 356, 40);
            txtLog = new TextBox(); txtLog.SetBounds(10, 376, 472, 128); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Theme.Surface; txtLog.ForeColor = Theme.TextDim; txtLog.BorderStyle = BorderStyle.FixedSingle; txtLog.Font = Theme.Mono; content.Controls.Add(txtLog);
        }

        void StyleInput(TextBox t) { t.BackColor = Theme.Surface; t.ForeColor = Theme.Text; t.BorderStyle = BorderStyle.FixedSingle; }

        // Scale the hand-coded 96-DPI layout up to the current display DPI (the manifest makes us DPI-aware,
        // so DpiFactor reads the real DPI instead of a lied-to 96).
        void ScaleToDpi() {
            float f = Theme.DpiFactor(this);
            if (f <= 1.0f) return;
            Theme.ScaleControls(this, f);
            ClientSize = new Size((int)Math.Round(BaseClientW * f), (int)Math.Round(BaseClientH * f));
        }

        void Lst_DrawItem(object s, DrawItemEventArgs e) {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush bb = new SolidBrush(sel ? Theme.Surface2 : Theme.Surface)) e.Graphics.FillRectangle(bb, e.Bounds);
            if (sel) using (SolidBrush ab = new SolidBrush(Theme.Amber)) e.Graphics.FillRectangle(ab, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            bool enabled = (e.Index < commands.Count) ? commands[e.Index].Enabled : true;
            string text = (e.Index < lst.Items.Count) ? lst.Items[e.Index].ToString() : "";
            Color fg = enabled ? Theme.Text : Theme.TextDim;
            Rectangle r = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Theme.Ui, r, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        // ----- Update checking (notify + 1-click download) -----

        void CheckForUpdates(bool announce) {
            if (btnUpdate != null) btnUpdate.Enabled = false;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                Updater.Info info = Updater.CheckLatest();
                try {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate {
                        if (btnUpdate != null) btnUpdate.Enabled = true;
                        if (info != null && Updater.Compare(info.Version, CurrentVer) > 0) ShowUpdateBanner(info);
                        else if (announce) {
                            if (info == null) Log("update check failed - no connection or release feed unavailable");
                            else Log("you're on the latest version (v" + Version + ")");
                        }
                    });
                } catch { }
            });
        }

        void ShowUpdateBanner(Updater.Info info) {
            pendingUpdate = info;
            lblBanner.Text = "Update available - StarMaster " + info.Tag;
            btnGet.Enabled = true;
            // 1-click install only applies to an installed copy that has a real setup asset; otherwise offer the page.
            bool canInstall = info.SetupUrl != null && RunningFromInstallDir();
            btnGet.Text = canInstall ? "Download & Install" : "Open download page";
            if (!banner.Visible) { banner.Visible = true; ClientSize = new Size(LogicalToDeviceUnits(BaseClientW), LogicalToDeviceUnits(BaseClientH + BannerH)); }
            Log("update available: " + info.Tag + " (you have v" + Version + ")");
        }

        void HideBanner() {
            if (banner.Visible) { banner.Visible = false; ClientSize = new Size(LogicalToDeviceUnits(BaseClientW), LogicalToDeviceUnits(BaseClientH)); }
        }

        void DownloadUpdate() {
            if (pendingUpdate == null) return;
            // Without a real installer asset, or when running the portable exe (not the installed copy under
            // %localappdata%\StarMaster), an in-place install would silently diverge - just open the release page.
            if (string.IsNullOrEmpty(pendingUpdate.SetupUrl) || !RunningFromInstallDir()) { OpenReleasePage(); return; }
            btnGet.Enabled = false;
            lblBanner.Text = "Downloading " + pendingUpdate.Tag + "...";
            string url = pendingUpdate.SetupUrl;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                string path = Updater.DownloadInstaller(url);
                try {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate {
                        if (path != null) {
                            try { System.Diagnostics.Process.Start(path); Close(); return; } catch { }
                        }
                        btnGet.Enabled = true;
                        lblBanner.Text = "Download failed - opening download page";
                        OpenReleasePage();
                    });
                } catch { }
            });
        }

        void OpenReleasePage() {
            string u = (pendingUpdate != null && !string.IsNullOrEmpty(pendingUpdate.PageUrl)) ? pendingUpdate.PageUrl : Updater.ReleasesPage;
            try { System.Diagnostics.Process.Start(u); } catch { }
        }

        // True only when this exe runs from the installer's target dir (%localappdata%\StarMaster),
        // i.e. an installed copy where running the downloaded setup actually upgrades the right files.
        static bool RunningFromInstallDir() {
            try {
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMaster");
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                return string.Equals(Path.GetFullPath(exeDir).TrimEnd('\\'), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }

        // ----- Keystroke commands (unchanged behaviour) -----

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
                timer.Stop(); btnStart.Text = "Start"; lblStatus.Text = "Stopped"; lblStatus.ForeColor = Theme.Bad; Log("stopped");
            } else {
                foreach (Cmd c in commands) c.LastFire = DateTime.Now;
                timer.Start(); btnStart.Text = "Stop"; lblStatus.Text = "Running"; lblStatus.ForeColor = Theme.Good; Log("started");
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
            // DPI awareness is declared in app.manifest (embedded via /win32manifest) so it applies before
            // any window is created; AutoScaleMode.Dpi then scales the layout. See CLAUDE.md build command.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CleanupOldInstallers();
            Application.Run(new MainForm());
        }

        // Best-effort: remove installers left in %TEMP% by a previous in-app update (a running one stays locked, so it's skipped).
        static void CleanupOldInstallers() {
            try {
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), "StarMaster-Setup-*.exe")) {
                    try { File.Delete(f); } catch { }
                }
            } catch { }
        }
    }
}
