using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        const uint KEYUP = 0x2;
        const uint SCANCODE = 0x8;
        const uint EXTENDED = 0x1;
        public static string ActiveTitle() {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(GetForegroundWindow(), sb, 256);
            return sb.ToString();
        }
        // Send one key by HARDWARE SCAN CODE, not just the virtual key. Games like Star Citizen read raw
        // input / DirectInput and ignore vk-only synthetic keys (scan code 0) - so the old path did nothing
        // in-game. This is what VoiceAttack does too.
        static void Key(byte vk, bool up) {
            uint sc = MapVirtualKey(vk, 0);   // MAPVK_VK_TO_VSC
            uint flags = SCANCODE | (up ? KEYUP : 0);
            if (vk == 0xA5 || vk == 0xA3 || (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C)
                flags |= EXTENDED;            // right alt/ctrl, nav cluster, ins/del, win keys are "extended"
            keybd_event(vk, (byte)sc, flags, UIntPtr.Zero);
        }
        public static void Press(byte[] mods, byte key) {
            foreach (byte m in mods) Key(m, false);
            Key(key, false);
            System.Threading.Thread.Sleep(40);
            Key(key, true);
            for (int i = mods.Length - 1; i >= 0; i--) Key(mods[i], true);
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
                case "[": return 0xDB; case "]": return 0xDD;
                default: return 0;
            }
        }
    }

    class Cmd {
        public string Label = "Command";
        public bool Shift, Ctrl, Alt;
        public string Key = "";
        public int Interval = 600;
        public bool Enabled = true;
        public DateTime LastFire = DateTime.MinValue;
    }

    // Dark "HUD" theme (amber on near-black) - matches the app icon and the Star Citizen aesthetic.
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

        // DPI scale factor for the screen this control is on (1.0 at 96 DPI, 1.5 at 144...).
        public static float DpiFactor(Control c) {
            using (Graphics g = c.CreateGraphics()) return g.DpiX / 96f;
        }

        // Multiply every child control's bounds (and owner-drawn ListBox row height) by f. Point fonts
        // already grow with DPI, so we scale only geometry here to match.
        public static void ScaleControls(Control parent, float f) {
            foreach (Control c in parent.Controls) {
                c.Bounds = new Rectangle((int)Math.Round(c.Left * f), (int)Math.Round(c.Top * f), (int)Math.Round(c.Width * f), (int)Math.Round(c.Height * f));
                ListBox lb = c as ListBox;
                if (lb != null && lb.DrawMode == DrawMode.OwnerDrawFixed) lb.ItemHeight = (int)Math.Round(lb.ItemHeight * f);
                if (c.HasChildren) ScaleControls(c, f);
            }
        }
    }

    // Owner-drawn checkbox: a clear bordered box that fills amber with a dark check when ticked.
    // The default flat checkbox is nearly invisible on the dark theme.
    class ThemedCheckBox : CheckBox {
        public ThemedCheckBox() {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            CheckedChanged += delegate { Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e) {
            Graphics g = e.Graphics;
            Color bg = (Parent != null) ? Parent.BackColor : Theme.Bg;
            using (SolidBrush bb = new SolidBrush(bg)) g.FillRectangle(bb, ClientRectangle);
            int box = Math.Min(Height - 2, Font.Height);
            Rectangle r = new Rectangle(1, (Height - box) / 2, box, box);
            using (SolidBrush fill = new SolidBrush(Checked ? Theme.Amber : Theme.Surface)) g.FillRectangle(fill, r);
            using (Pen p = new Pen(Checked ? Theme.Amber : Theme.Line)) g.DrawRectangle(p, r);
            if (Checked) {
                System.Drawing.Drawing2D.SmoothingMode old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen cp = new Pen(Color.FromArgb(20, 18, 10), Math.Max(2f, box / 8f)))
                    g.DrawLines(cp, new Point[] {
                        new Point(r.X + box * 3 / 16, r.Y + box / 2),
                        new Point(r.X + box * 6 / 16, r.Y + box * 11 / 16),
                        new Point(r.X + box * 12 / 16, r.Y + box / 4) });
                g.SmoothingMode = old;
            }
            Rectangle tr = new Rectangle(r.Right + 6, 0, Width - r.Right - 6, Height);
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // WebClient with a real timeout so a stalled download fails fast instead of hanging forever.
    class TimedWebClient : WebClient {
        protected override WebRequest GetWebRequest(Uri address) {
            WebRequest r = base.GetWebRequest(address);
            HttpWebRequest h = r as HttpWebRequest;
            if (h != null) { h.Timeout = 30000; h.ReadWriteTimeout = 30000; }
            return r;
        }
    }

    // Checks GitHub Releases for a newer version of StarMaster itself. Works against a PUBLIC repo.
    static class Updater {
        const string Owner = "elliot-borst";
        const string Repo  = "StarMaster";
        const string ApiUrl     = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";

        public class Info { public int[] Version; public string Tag; public string SetupUrl; public string PageUrl; }

        // Parse every numeric component of a tag: "v2" -> {2}, "v2.1" -> {2,1}; {} if none.
        public static int[] ParseVer(string tag) {
            List<int> parts = new List<int>();
            if (!string.IsNullOrEmpty(tag)) {
                foreach (Match m in Regex.Matches(tag, "[0-9]+")) {
                    int v; if (int.TryParse(m.Value, out v)) parts.Add(v);
                }
            }
            return parts.ToArray();
        }

        // Component-wise compare; >0 if a is newer than b. Missing components count as 0.
        public static int Compare(int[] a, int[] b) {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++) {
                int ai = i < a.Length ? a[i] : 0;
                int bi = i < b.Length ? b[i] : 0;
                if (ai != bi) return ai < bi ? -1 : 1;
            }
            return 0;
        }

        public static Info CheckLatest() {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.UserAgent = "StarMaster-Updater";
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
                info.SetupUrl = FindAsset(json);
                return info;
            } catch { return null; }
        }

        static string FindAsset(string json) {
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) {
                string low = m.Groups[1].Value.ToLower();
                if (low.EndsWith(".exe") && low.Contains("setup")) return m.Groups[1].Value;
            }
            return null;
        }

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

    // StarStrings tool: keeps MrKraken's StarStrings community localization mod up to date.
    // It's a rolling "latest" release (StarStrings.zip) re-published every SC patch, so the "version"
    // is the release name (build date + short commit), not a clean vN.
    static class StarStrings {
        const string ApiLatest = "https://api.github.com/repos/MrKraken/StarStrings/releases/latest";
        public const string RepoPage = "https://github.com/MrKraken/StarStrings";

        public class Info { public string Build; public string ZipUrl; }

        // Latest release info, or null on failure.
        public static Info CheckLatest() {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiLatest);
                req.UserAgent = "StarMaster";
                req.Accept = "application/vnd.github+json";
                req.Timeout = 8000;
                string json;
                using (WebResponse resp = req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
                Info i = new Info();
                Match name = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");        // "Latest Build (release-2026-06-17-7caecaf)"
                Match pub  = Regex.Match(json, "\"published_at\"\\s*:\\s*\"([^\"]+)\"");
                i.Build = name.Success ? name.Groups[1].Value : (pub.Success ? pub.Groups[1].Value : "latest");
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) {
                    if (m.Groups[1].Value.ToLower().EndsWith(".zip")) { i.ZipUrl = m.Groups[1].Value; break; }
                }
                return (i.ZipUrl != null) ? i : null;
            } catch { return null; }
        }

        // Download the zip, extract, copy the Data\ folder into <channelRoot>\data and ensure user.cfg
        // has the g_language line. Returns true on success; status text via msg.
        public static bool Install(string zipUrl, string channelRoot, out string msg) {
            string tmpZip = null, tmpDir = null;
            try {
                if (!Directory.Exists(channelRoot)) { msg = "channel folder not found: " + channelRoot; return false; }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                tmpZip = Path.Combine(Path.GetTempPath(), "StarStrings-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
                using (WebClient wc = new TimedWebClient()) { wc.Headers.Add("User-Agent", "StarMaster"); wc.DownloadFile(zipUrl, tmpZip); }
                tmpDir = Path.Combine(Path.GetTempPath(), "StarStrings-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                ZipFile.ExtractToDirectory(tmpZip, tmpDir);

                string dataSrc = FindDir(tmpDir, "Data");
                if (dataSrc == null) { msg = "no Data folder in the downloaded zip"; return false; }
                int files = CopyTree(dataSrc, Path.Combine(channelRoot, "data"));
                EnsureLanguageLine(Path.Combine(channelRoot, "user.cfg"));
                msg = "installed " + files + " file(s) into " + channelRoot + " (data\\ + user.cfg)";
                return files > 0;
            } catch (Exception ex) {
                msg = "install failed: " + ex.Message; return false;
            } finally {
                try { if (tmpZip != null) File.Delete(tmpZip); } catch { }
                try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { }
            }
        }

        static string FindDir(string root, string name) {
            try {
                string direct = Path.Combine(root, name);
                if (Directory.Exists(direct)) return direct;
                foreach (string d in Directory.GetDirectories(root, name, SearchOption.AllDirectories)) return d;
            } catch { }
            return null;
        }

        static int CopyTree(string src, string dst) {
            Directory.CreateDirectory(dst);
            int c = 0;
            foreach (string f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); c++; }
            foreach (string sub in Directory.GetDirectories(src)) c += CopyTree(sub, Path.Combine(dst, Path.GetFileName(sub)));
            return c;
        }

        static void EnsureLanguageLine(string userCfg) {
            try {
                const string line = "g_language = english";
                if (File.Exists(userCfg)) {
                    string txt = File.ReadAllText(userCfg);
                    if (txt.IndexOf("g_language", StringComparison.OrdinalIgnoreCase) < 0) {
                        if (txt.Length > 0 && !txt.EndsWith("\n")) txt += Environment.NewLine;
                        File.WriteAllText(userCfg, txt + line + Environment.NewLine);
                    }
                } else {
                    File.WriteAllText(userCfg, line + Environment.NewLine);
                }
            } catch { }
        }
    }

    public class MainForm : Form {
        // keep-alive
        TextBox txtLabel, txtKey, txtTitle, txtLog;
        CheckBox chkShift, chkCtrl, chkAlt, chkFocus, chkAuto;
        NumericUpDown numInt;
        Button btnAdd, btnRemove, btnStart;
        ListBox lst;
        Label lblStatus;
        Timer timer;
        List<Cmd> commands = new List<Cmd>();
        // chrome / app self-update
        Button btnUpdate, btnGet, btnDismiss;
        Label lblBanner;
        Panel header, banner, content, paKeep, paStar;
        BackupControl backup;
        Updater.Info pendingUpdate;
        int[] CurrentVer;
        NotifyIcon trayIcon;
        bool exiting = false, trayHintShown = false;
        // StarStrings tool
        TextBox txtScRoot, txtSSLog;
        ComboBox cmbSSChannel;
        Label lblSSInstalled, lblSSAvailable, lblSSStatus;
        Button btnSSCheck, btnSSUpdate;
        StarStrings.Info ssLatest;
        // config
        string cfgPath;
        string ssInstalledBuild = "", ssRootCfg = "", ssChannelCfg = "";

        public const string Version = "3";   // bump per release; matches the GitHub Release tag (vN)
        const string DefaultScRoot = @"C:\Program Files\Roberts Space Industries\StarCitizen";

        const int HeaderH = 58;
        const int BaseClientW = 1100;            // wide 2-column fixed layout (no scrolling)
        const int BaseClientH = HeaderH + 844;
        const int BannerH = 40;

        Label NewLabel(Control parent, string t, int x, int y, int w) {
            Label l = new Label();
            l.Text = t; l.SetBounds(x, y, w, 20);
            l.Font = Theme.Ui; l.ForeColor = Theme.Text; l.BackColor = Color.Transparent;
            parent.Controls.Add(l); return l;
        }

        public MainForm() {
            cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            CurrentVer = Updater.ParseVer(Version);
            bool autostart, focusguard; string wintitle;
            LoadConfig(out autostart, out focusguard, out wintitle);

            Text = "StarMaster v" + Version;
            AutoScaleMode = AutoScaleMode.None;   // hand-coded layout is scaled manually in ScaleToDpi()
            ClientSize = new Size(BaseClientW, BaseClientH);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Ui;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildHeader();
            BuildBanner();
            BuildContent();
            Controls.Add(content);
            Controls.Add(banner);
            Controls.Add(header);
            ScaleToDpi();

            timer = new Timer(); timer.Interval = 1000; timer.Tick += Timer_Tick;
            WireKeepAlive();
            WireStarStrings();

            RefreshList();
            chkAuto.Checked = autostart; chkFocus.Checked = focusguard; txtTitle.Text = wintitle;
            if (ssRootCfg.Length > 0) txtScRoot.Text = ssRootCfg;
            PopulateSSChannels();
            lblSSInstalled.Text = ssInstalledBuild.Length > 0 ? ssInstalledBuild : "(not installed via StarMaster)";

            BuildTray();
            FormClosing += MainForm_FormClosing;
            Shown += delegate { CheckForUpdates(false); SSCheck(false); };
            if (autostart) ToggleStart();
        }

        // ---------- header / banner ----------

        void BuildHeader() {
            header = new Panel(); header.Dock = DockStyle.Top; header.Height = HeaderH; header.BackColor = Theme.Surface2;
            header.Paint += delegate(object s, PaintEventArgs e) {
                using (Pen p = new Pen(Theme.Amber)) e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1);
            };
            PictureBox pic = new PictureBox(); pic.SetBounds(14, 13, 32, 32); pic.SizeMode = PictureBoxSizeMode.StretchImage; pic.BackColor = Color.Transparent;
            try { if (Icon != null) { using (Icon ico = new Icon(Icon, 32, 32)) pic.Image = ico.ToBitmap(); } } catch { }
            header.Controls.Add(pic);
            Label t = new Label(); t.Text = "StarMaster"; t.Font = Theme.Title; t.ForeColor = Theme.Amber; t.BackColor = Color.Transparent; t.SetBounds(52, 8, 260, 28);
            header.Controls.Add(t);
            Label sub = new Label(); sub.Text = "Star Citizen toolkit - v" + Version; sub.Font = Theme.Small; sub.ForeColor = Theme.TextDim; sub.BackColor = Color.Transparent; sub.SetBounds(54, 35, 300, 16);
            header.Controls.Add(sub);
            btnUpdate = new Button(); btnUpdate.Text = "Check for updates"; btnUpdate.Font = Theme.Ui; btnUpdate.SetBounds(950, 16, 138, 26);
            Theme.StyleButton(btnUpdate, false);
            btnUpdate.Click += delegate { Log("checking for updates..."); CheckForUpdates(true); };
            header.Controls.Add(btnUpdate);
        }

        void BuildBanner() {
            banner = new Panel(); banner.Dock = DockStyle.Top; banner.Height = BannerH; banner.Visible = false; banner.BackColor = Color.FromArgb(38, 32, 16);
            banner.Paint += delegate(object s, PaintEventArgs e) {
                using (SolidBrush b = new SolidBrush(Theme.Amber)) e.Graphics.FillRectangle(b, 0, 0, 3, banner.Height);
            };
            lblBanner = new Label(); lblBanner.SetBounds(16, 11, 840, 18); lblBanner.Font = Theme.UiBold; lblBanner.ForeColor = Theme.Amber; lblBanner.BackColor = Color.Transparent;
            banner.Controls.Add(lblBanner);
            btnGet = new Button(); btnGet.Text = "Download & Install"; btnGet.Font = Theme.Ui; btnGet.SetBounds(904, 7, 150, 26);
            Theme.StyleButton(btnGet, true);
            btnGet.Click += delegate { DownloadUpdate(); };
            banner.Controls.Add(btnGet);
            btnDismiss = new Button(); btnDismiss.Text = "X"; btnDismiss.Font = Theme.Ui; btnDismiss.SetBounds(1064, 7, 24, 26);
            Theme.StyleButton(btnDismiss, false);
            btnDismiss.Click += delegate { HideBanner(); };
            banner.Controls.Add(btnDismiss);
        }

        // ---------- content: 3 stacked sections ----------

        int AddSection(string title, int x, int y, int w) {
            Label h = new Label(); h.Text = title; h.Font = Theme.UiBold; h.ForeColor = Theme.Amber; h.BackColor = Color.Transparent; h.SetBounds(x, y, w, 20);
            content.Controls.Add(h);
            Panel line = new Panel(); line.SetBounds(x, y + 21, w, 1); line.BackColor = Theme.Line;
            content.Controls.Add(line);
            return y + 30;
        }

        void BuildContent() {
            content = new Panel(); content.Dock = DockStyle.Fill; content.BackColor = Theme.Bg;   // fixed size, no scrolling

            int leftX = 12, leftW = 500;
            int rightX = 528, rightW = 548;
            int topY = 8;

            // Left column: Keep-Alive
            int ky = AddSection("KEEP-ALIVE", leftX, topY, leftW);
            paKeep = new Panel(); paKeep.SetBounds(leftX, ky, leftW, 482); paKeep.BackColor = Theme.Bg;
            BuildKeepAlive(paKeep);
            content.Controls.Add(paKeep);

            // Right column: Backup / Restore
            int by = AddSection("BACKUP / RESTORE", rightX, topY, rightW);
            backup = new BackupControl(); backup.Location = new Point(rightX, by);
            content.Controls.Add(backup);

            // Full-width row underneath: StarStrings
            int rowBottom = Math.Max(ky + 482, by + backup.Height);
            int sy = AddSection("STARSTRINGS  (MrKraken localization)", leftX, rowBottom + 16, BaseClientW - 24);
            paStar = new Panel(); paStar.SetBounds(leftX, sy, BaseClientW - 24, 210); paStar.BackColor = Theme.Bg;
            BuildStarStrings(paStar);
            content.Controls.Add(paStar);
        }

        void BuildKeepAlive(Panel p) {
            NewLabel(p, "Label:", 10, 12, 42);
            txtLabel = new TextBox(); txtLabel.SetBounds(54, 9, 120, 22); StyleInput(txtLabel); p.Controls.Add(txtLabel);
            chkShift = new ThemedCheckBox(); chkShift.Text = "Shift"; chkShift.SetBounds(182, 10, 55, 20); Theme.StyleCheck(chkShift); p.Controls.Add(chkShift);
            chkCtrl = new ThemedCheckBox(); chkCtrl.Text = "Ctrl"; chkCtrl.SetBounds(238, 10, 48, 20); Theme.StyleCheck(chkCtrl); p.Controls.Add(chkCtrl);
            chkAlt = new ThemedCheckBox(); chkAlt.Text = "Alt"; chkAlt.SetBounds(288, 10, 44, 20); Theme.StyleCheck(chkAlt); p.Controls.Add(chkAlt);
            NewLabel(p, "Key:", 336, 12, 30);
            txtKey = new TextBox(); txtKey.SetBounds(368, 9, 40, 22); StyleInput(txtKey); p.Controls.Add(txtKey);

            NewLabel(p, "Every (sec):", 10, 44, 72);
            numInt = new NumericUpDown(); numInt.SetBounds(86, 42, 60, 22); numInt.Minimum = 1; numInt.Maximum = 3600; numInt.Value = 600;
            numInt.BackColor = Theme.Surface; numInt.ForeColor = Theme.Text; numInt.BorderStyle = BorderStyle.FixedSingle; p.Controls.Add(numInt);
            btnAdd = new Button(); btnAdd.Text = "Add / Update"; btnAdd.SetBounds(160, 40, 110, 26); Theme.StyleButton(btnAdd, false); p.Controls.Add(btnAdd);
            btnRemove = new Button(); btnRemove.Text = "Remove"; btnRemove.SetBounds(278, 40, 90, 26); Theme.StyleButton(btnRemove, false); p.Controls.Add(btnRemove);

            lst = new ListBox(); lst.SetBounds(10, 78, 472, 132);
            lst.BackColor = Theme.Surface; lst.ForeColor = Theme.Text; lst.BorderStyle = BorderStyle.FixedSingle;
            lst.DrawMode = DrawMode.OwnerDrawFixed; lst.ItemHeight = 24; lst.IntegralHeight = false;
            lst.DrawItem += Lst_DrawItem;
            p.Controls.Add(lst);
            Label tip = NewLabel(p, "Tip: double-click a row to toggle ON/off; click a row + Add/Update to overwrite it.", 10, 214, 480);
            tip.ForeColor = Theme.TextDim; tip.Font = Theme.Small;

            chkFocus = new ThemedCheckBox(); chkFocus.Text = "Only send while active window contains:"; chkFocus.SetBounds(10, 240, 248, 20); Theme.StyleCheck(chkFocus); p.Controls.Add(chkFocus);
            txtTitle = new TextBox(); txtTitle.SetBounds(260, 238, 150, 22); StyleInput(txtTitle); p.Controls.Add(txtTitle);
            chkAuto = new ThemedCheckBox(); chkAuto.Text = "Auto-start when launched (use this for run-on-boot)"; chkAuto.SetBounds(10, 266, 400, 20); Theme.StyleCheck(chkAuto); p.Controls.Add(chkAuto);

            btnStart = new Button(); btnStart.Text = "Start"; btnStart.SetBounds(10, 294, 130, 34); Theme.StyleButton(btnStart, true); btnStart.Font = Theme.UiBold; p.Controls.Add(btnStart);
            lblStatus = new Label(); lblStatus.Text = "Stopped"; lblStatus.Font = Theme.UiBold; lblStatus.ForeColor = Theme.Bad; lblStatus.BackColor = Color.Transparent; lblStatus.SetBounds(150, 302, 150, 20); p.Controls.Add(lblStatus);

            NewLabel(p, "Log:", 10, 338, 40);
            txtLog = new TextBox(); txtLog.SetBounds(10, 358, 472, 120); txtLog.Multiline = true; txtLog.ReadOnly = true; txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Theme.Surface; txtLog.ForeColor = Theme.TextDim; txtLog.BorderStyle = BorderStyle.FixedSingle; txtLog.Font = Theme.Mono; p.Controls.Add(txtLog);
        }

        void BuildStarStrings(Panel p) {
            NewLabel(p, "Star Citizen folder:", 10, 10, 124);
            txtScRoot = new TextBox(); txtScRoot.SetBounds(138, 8, 410, 22); StyleInput(txtScRoot); txtScRoot.Text = DefaultScRoot; p.Controls.Add(txtScRoot);

            NewLabel(p, "Channel:", 10, 42, 56);
            cmbSSChannel = new ComboBox(); cmbSSChannel.DropDownStyle = ComboBoxStyle.DropDownList; cmbSSChannel.SetBounds(70, 39, 110, 22);
            cmbSSChannel.BackColor = Theme.Surface; cmbSSChannel.ForeColor = Theme.Text; cmbSSChannel.FlatStyle = FlatStyle.Flat; p.Controls.Add(cmbSSChannel);
            btnSSCheck = new Button(); btnSSCheck.Text = "Check for update"; btnSSCheck.SetBounds(196, 38, 150, 26); Theme.StyleButton(btnSSCheck, false); p.Controls.Add(btnSSCheck);

            NewLabel(p, "Installed build:", 10, 74, 100);
            lblSSInstalled = new Label(); lblSSInstalled.SetBounds(116, 74, 432, 18); lblSSInstalled.ForeColor = Theme.Text; lblSSInstalled.BackColor = Color.Transparent; lblSSInstalled.Font = Theme.Small; p.Controls.Add(lblSSInstalled);
            NewLabel(p, "Latest build:", 10, 96, 100);
            lblSSAvailable = new Label(); lblSSAvailable.SetBounds(116, 96, 432, 18); lblSSAvailable.ForeColor = Theme.Amber; lblSSAvailable.BackColor = Color.Transparent; lblSSAvailable.Font = Theme.Small; lblSSAvailable.Text = "(not checked yet)"; p.Controls.Add(lblSSAvailable);

            btnSSUpdate = new Button(); btnSSUpdate.Text = "Download & install update"; btnSSUpdate.SetBounds(10, 124, 220, 30); Theme.StyleButton(btnSSUpdate, true); btnSSUpdate.Enabled = false; p.Controls.Add(btnSSUpdate);
            lblSSStatus = new Label(); lblSSStatus.SetBounds(240, 130, 308, 18); lblSSStatus.ForeColor = Theme.TextDim; lblSSStatus.BackColor = Color.Transparent; p.Controls.Add(lblSSStatus);

            txtSSLog = new TextBox(); txtSSLog.SetBounds(10, 164, 1056, 40); txtSSLog.Multiline = true; txtSSLog.ReadOnly = true; txtSSLog.ScrollBars = ScrollBars.Vertical;
            txtSSLog.BackColor = Theme.Surface; txtSSLog.ForeColor = Theme.TextDim; txtSSLog.BorderStyle = BorderStyle.FixedSingle; txtSSLog.Font = Theme.Mono; p.Controls.Add(txtSSLog);
        }

        void StyleInput(TextBox t) { t.BackColor = Theme.Surface; t.ForeColor = Theme.Text; t.BorderStyle = BorderStyle.FixedSingle; }

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

        void WireKeepAlive() {
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
        }

        // ---------- app self-update ----------

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
            if (string.IsNullOrEmpty(pendingUpdate.SetupUrl) || !RunningFromInstallDir()) { OpenReleasePage(); return; }
            btnGet.Enabled = false;
            lblBanner.Text = "Downloading " + pendingUpdate.Tag + "...";
            string url = pendingUpdate.SetupUrl;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                string path = Updater.DownloadInstaller(url);
                try {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate {
                        if (path != null) { try { System.Diagnostics.Process.Start(path); Close(); return; } catch { } }
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

        static bool RunningFromInstallDir() {
            try {
                string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMaster");
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                return string.Equals(Path.GetFullPath(exeDir).TrimEnd('\\'), Path.GetFullPath(installDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            } catch { return false; }
        }

        // ---------- StarStrings tool ----------

        void WireStarStrings() {
            btnSSCheck.Click += delegate { SSCheck(true); };
            btnSSUpdate.Click += delegate { SSUpdate(); };
            txtScRoot.Leave += delegate { PopulateSSChannels(); };
        }

        void PopulateSSChannels() {
            string sel = cmbSSChannel.SelectedItem as string;
            cmbSSChannel.Items.Clear();
            try {
                string root = txtScRoot.Text.Trim();
                if (Directory.Exists(root)) {
                    foreach (string d in Directory.GetDirectories(root)) {
                        string n = Path.GetFileName(d);
                        if (Directory.Exists(Path.Combine(d, "data")) || Directory.Exists(Path.Combine(d, "user")) ||
                            n.Equals("LIVE", StringComparison.OrdinalIgnoreCase) || n.Equals("HOTFIX", StringComparison.OrdinalIgnoreCase))
                            cmbSSChannel.Items.Add(n);
                    }
                }
            } catch { }
            if (cmbSSChannel.Items.Count == 0) { cmbSSChannel.Items.Add("LIVE"); cmbSSChannel.Items.Add("HOTFIX"); }
            int idx = -1;
            if (sel != null) idx = cmbSSChannel.Items.IndexOf(sel);
            if (idx < 0 && ssChannelCfg.Length > 0) idx = cmbSSChannel.Items.IndexOf(ssChannelCfg);
            if (idx < 0) idx = cmbSSChannel.Items.IndexOf("LIVE");
            cmbSSChannel.SelectedIndex = idx >= 0 ? idx : 0;
        }

        void SSLog(string m) { txtSSLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + m + "\r\n"); }

        void SSCheck(bool announce) {
            btnSSCheck.Enabled = false;
            if (announce) lblSSAvailable.Text = "checking...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                StarStrings.Info info = StarStrings.CheckLatest();
                try {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate {
                        btnSSCheck.Enabled = true;
                        ssLatest = info;
                        if (info == null) {
                            lblSSAvailable.Text = "(offline or unavailable)";
                            btnSSUpdate.Enabled = false;
                            if (announce) SSLog("StarStrings check failed - no connection");
                            return;
                        }
                        lblSSAvailable.Text = info.Build;
                        btnSSUpdate.Enabled = true;
                        bool current = ssInstalledBuild.Length > 0 && ssInstalledBuild == info.Build;
                        if (ssInstalledBuild.Length == 0) { btnSSUpdate.Text = "Download & install"; lblSSStatus.Text = "not installed yet"; lblSSStatus.ForeColor = Theme.Amber; }
                        else if (current) { btnSSUpdate.Text = "Re-install (up to date)"; lblSSStatus.Text = "up to date"; lblSSStatus.ForeColor = Theme.Good; }
                        else { btnSSUpdate.Text = "Update now"; lblSSStatus.Text = "update available"; lblSSStatus.ForeColor = Theme.Amber; }
                    });
                } catch { }
            });
        }

        void SSUpdate() {
            if (ssLatest == null || string.IsNullOrEmpty(ssLatest.ZipUrl)) { SSLog("nothing to install - click Check first"); return; }
            string root = txtScRoot.Text.Trim();
            string ch = cmbSSChannel.SelectedItem as string;
            if (string.IsNullOrEmpty(ch)) { SSLog("pick a channel"); return; }
            string channelRoot = Path.Combine(root, ch);
            DialogResult r = MessageBox.Show(
                "Install MrKraken's StarStrings into:\n\n" + channelRoot + "\n\nThis copies the data\\ folder and ensures user.cfg has 'g_language = english'. Close Star Citizen first. Proceed?",
                "Install StarStrings", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) { SSLog("cancelled"); return; }
            btnSSUpdate.Enabled = false; lblSSStatus.Text = "downloading..."; lblSSStatus.ForeColor = Theme.TextDim;
            SSLog("downloading + installing into " + channelRoot + " ...");
            string zip = ssLatest.ZipUrl; string build = ssLatest.Build;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                string msg; bool ok = StarStrings.Install(zip, channelRoot, out msg);
                try {
                    if (!IsHandleCreated) return;
                    BeginInvoke((MethodInvoker)delegate {
                        btnSSUpdate.Enabled = true;
                        SSLog(msg);
                        if (ok) {
                            ssInstalledBuild = build;
                            lblSSInstalled.Text = build;
                            lblSSStatus.Text = "installed - restart Star Citizen"; lblSSStatus.ForeColor = Theme.Good;
                            btnSSUpdate.Text = "Re-install (up to date)";
                            SaveConfig();
                        } else {
                            lblSSStatus.Text = "failed"; lblSSStatus.ForeColor = Theme.Bad;
                        }
                    });
                } catch { }
            });
        }

        // ---------- keep-alive (unchanged behaviour) ----------

        void BtnAdd_Click(object s, EventArgs e) {
            string key = txtKey.Text.Trim();
            if (key.Length == 0) { Log("! enter a Key first"); return; }
            if (Vk.Map(key) == 0) { Log("! '" + key + "' not recognized (A-Z, 0-9, F1-F12, Space, Enter, Tab, Esc, [ ])"); return; }
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

        // ---------- tray / close-to-tray ----------

        void BuildTray() {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = Icon ?? System.Drawing.SystemIcons.Application;
            trayIcon.Text = "StarMaster";
            trayIcon.Visible = true;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open StarMaster", null, delegate { RestoreFromTray(); });
            menu.Items.Add("Exit", null, delegate { exiting = true; Close(); });
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { RestoreFromTray(); };
        }

        void RestoreFromTray() {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        // The X button hides to the tray (keep-alive keeps running). Real exit only on tray "Exit"
        // (sets `exiting`) or a non-user close like Windows shutdown.
        void MainForm_FormClosing(object s, FormClosingEventArgs e) {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) {
                e.Cancel = true;
                SaveConfig();
                Hide();
                if (!trayHintShown) {
                    trayHintShown = true;
                    try { trayIcon.ShowBalloonTip(2500, "StarMaster", "Still running in the tray - keep-alive stays active. Right-click the icon to Exit.", ToolTipIcon.Info); } catch { }
                }
                return;
            }
            timer.Stop();
            SaveConfig();
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
        }

        // ---------- config ----------

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
                                int iv; int.TryParse(f[5], out iv); c.Interval = iv < 1 ? 1 : (iv > 3600 ? 3600 : iv);
                                c.Enabled = f[6] == "1";
                                commands.Add(c);
                            }
                        } else if (ln.IndexOf('=') > 0) {
                            string[] kv = ln.Split(new char[] { '=' }, 2);
                            string k = kv[0].Trim().ToLower(); string v = kv[1].Trim();
                            if (k == "autostart") autostart = v == "1";
                            else if (k == "focusguard") focusguard = v == "1";
                            else if (k == "wintitle") wintitle = v;
                            else if (k == "starstrings_build") ssInstalledBuild = v;
                            else if (k == "starstrings_root") ssRootCfg = v;
                            else if (k == "starstrings_channel") ssChannelCfg = v;
                        }
                    }
                }
            } catch { }
            if (commands.Count == 0) {
                Cmd c = new Cmd(); c.Label = "Wipe Visor"; c.Alt = true; c.Key = "X"; c.Interval = 600; c.Enabled = true;
                commands.Add(c);
                Cmd a = new Cmd(); a.Label = "Auto Accept"; a.Key = "["; a.Interval = 1; a.Enabled = false;
                commands.Add(a);
            }
        }

        void SaveConfig() {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# StarMaster config");
                sb.AppendLine("autostart=" + (chkAuto.Checked ? "1" : "0"));
                sb.AppendLine("focusguard=" + (chkFocus.Checked ? "1" : "0"));
                sb.AppendLine("wintitle=" + txtTitle.Text);
                sb.AppendLine("starstrings_build=" + ssInstalledBuild);
                sb.AppendLine("starstrings_root=" + (txtScRoot != null ? txtScRoot.Text : ""));
                sb.AppendLine("starstrings_channel=" + (cmbSSChannel != null && cmbSSChannel.SelectedItem != null ? cmbSSChannel.SelectedItem.ToString() : ""));
                sb.AppendLine("# commands: Label|Shift|Ctrl|Alt|Key|Interval|Enabled");
                foreach (Cmd c in commands)
                    sb.AppendLine(c.Label.Replace("|", "/") + "|" + (c.Shift ? "1" : "0") + "|" + (c.Ctrl ? "1" : "0") + "|" + (c.Alt ? "1" : "0") + "|" + c.Key + "|" + c.Interval + "|" + (c.Enabled ? "1" : "0"));
                File.WriteAllText(cfgPath, sb.ToString());
            } catch { }
        }

        [STAThread]
        static void Main() {
            // DPI awareness is declared in app.manifest (embedded via /win32manifest) so it applies before
            // any window is created; ScaleToDpi() then scales the layout. See CLAUDE.md build command.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            CleanupOldInstallers();
            Application.Run(new MainForm());
        }

        static void CleanupOldInstallers() {
            try {
                foreach (string f in Directory.GetFiles(Path.GetTempPath(), "StarMaster-Setup-*.exe")) {
                    try { File.Delete(f); } catch { }
                }
            } catch { }
        }
    }
}
