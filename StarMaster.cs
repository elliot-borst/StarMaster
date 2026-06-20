using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.IO.Path;

namespace StarMaster {

    // ===== reused logic (UI-agnostic) =====
    static class Native {
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        const uint KEYUP = 0x2, SCANCODE = 0x8, EXTENDED = 0x1;
        public static string ActiveTitle() { StringBuilder sb = new StringBuilder(256); GetWindowText(GetForegroundWindow(), sb, 256); return sb.ToString(); }
        static void Key(byte vk, bool up) {
            uint sc = MapVirtualKey(vk, 0);
            uint flags = SCANCODE | (up ? KEYUP : 0);
            if (vk == 0xA5 || vk == 0xA3 || (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x5C) flags |= EXTENDED;
            keybd_event(vk, (byte)sc, flags, UIntPtr.Zero);
        }
        public static void Press(byte[] mods, byte key) {
            foreach (byte m in mods) Key(m, false);
            Key(key, false); System.Threading.Thread.Sleep(40); Key(key, true);
            for (int i = mods.Length - 1; i >= 0; i--) Key(mods[i], true);
        }
    }

    static class Vk {
        public static byte Map(string k) {
            if (string.IsNullOrEmpty(k)) return 0; k = k.Trim().ToUpper();
            if (k.Length == 1) { char c = k[0]; if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z')) return (byte)c; }
            switch (k) {
                case "SPACE": return 0x20; case "ENTER": return 0x0D; case "TAB": return 0x09; case "ESC": return 0x1B;
                case "F1": return 0x70; case "F2": return 0x71; case "F3": return 0x72; case "F4": return 0x73;
                case "F5": return 0x74; case "F6": return 0x75; case "F7": return 0x76; case "F8": return 0x77;
                case "F9": return 0x78; case "F10": return 0x79; case "F11": return 0x7A; case "F12": return 0x7B;
                case "[": return 0xDB; case "]": return 0xDD;
            }
            return 0;
        }
    }

    public class Cmd { public string Label = "Command"; public bool Shift, Ctrl, Alt; public string Key = ""; public int Interval = 600; public bool Enabled = true; public DateTime LastFire = DateTime.MinValue; }

    class TimedWebClient : WebClient {
        protected override WebRequest GetWebRequest(Uri address) {
            WebRequest r = base.GetWebRequest(address);
            HttpWebRequest h = r as HttpWebRequest;
            if (h != null) { h.Timeout = 30000; h.ReadWriteTimeout = 30000; }
            return r;
        }
    }

    static class Updater {
        const string Owner = "elliot-borst", Repo = "StarMaster";
        const string ApiUrl = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
        public const string ReleasesPage = "https://github.com/" + Owner + "/" + Repo + "/releases/latest";
        public class Info { public int[] Version; public string Tag; public string SetupUrl; public string PageUrl; }
        public static string LastError = "";   // human-readable reason set when CheckLatest returns null
        public static int[] ParseVer(string tag) {
            List<int> parts = new List<int>();
            if (!string.IsNullOrEmpty(tag)) foreach (Match m in Regex.Matches(tag, "[0-9]+")) { int v; if (int.TryParse(m.Value, out v)) parts.Add(v); }
            return parts.ToArray();
        }
        public static int Compare(int[] a, int[] b) {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++) { int ai = i < a.Length ? a[i] : 0; int bi = i < b.Length ? b[i] : 0; if (ai != bi) return ai < bi ? -1 : 1; }
            return 0;
        }
        public static Info CheckLatest() {
            LastError = "";
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiUrl);
                req.UserAgent = "StarMaster-Updater"; req.Accept = "application/vnd.github+json"; req.Timeout = 8000;
                string json; using (WebResponse resp = req.GetResponse()) using (StreamReader sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
                Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\""); if (!tag.Success) { LastError = "Check failed"; return null; }
                Info info = new Info(); info.Tag = tag.Groups[1].Value; info.Version = ParseVer(info.Tag);
                Match page = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\""); info.PageUrl = page.Success ? page.Groups[1].Value : ReleasesPage;
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) { string low = m.Groups[1].Value.ToLower(); if (low.EndsWith(".exe") && low.Contains("setup")) { info.SetupUrl = m.Groups[1].Value; break; } }
                return info;
            } catch (WebException wex) { LastError = ClassifyWebError(wex); return null; }
            catch { LastError = "Check failed"; return null; }
        }
        // turn a WebException into a short, user-facing reason
        static string ClassifyWebError(WebException wex) {
            HttpWebResponse hr = wex.Response as HttpWebResponse;
            if (hr != null && (int)hr.StatusCode == 403) return "Rate-limited - try later";   // GitHub anon cap is 60/hour per IP
            if (wex.Status == WebExceptionStatus.NameResolutionFailure || wex.Status == WebExceptionStatus.ConnectFailure
                || wex.Status == WebExceptionStatus.Timeout || wex.Status == WebExceptionStatus.SendFailure) return "No connection";
            return "Check failed";
        }
        public static string DownloadInstaller(string url) {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string tmp = Path.Combine(Path.GetTempPath(), "StarMaster-Setup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                using (WebClient wc = new TimedWebClient()) { wc.Headers.Add("User-Agent", "StarMaster-Updater"); wc.DownloadFile(url, tmp); }
                return tmp;
            } catch { return null; }
        }
    }

    static class StarStrings {
        const string ApiLatest = "https://api.github.com/repos/MrKraken/StarStrings/releases/latest";
        public const string RepoPage = "https://github.com/MrKraken/StarStrings";
        public class Info { public string Build; public string ZipUrl; }
        public static Info CheckLatest() {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(ApiLatest);
                req.UserAgent = "StarMaster"; req.Accept = "application/vnd.github+json"; req.Timeout = 8000;
                string json; using (WebResponse resp = req.GetResponse()) using (StreamReader sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
                Info i = new Info();
                Match name = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\""); Match pub = Regex.Match(json, "\"published_at\"\\s*:\\s*\"([^\"]+)\"");
                i.Build = name.Success ? name.Groups[1].Value : (pub.Success ? pub.Groups[1].Value : "latest");
                foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"")) { if (m.Groups[1].Value.ToLower().EndsWith(".zip")) { i.ZipUrl = m.Groups[1].Value; break; } }
                return (i.ZipUrl != null) ? i : null;
            } catch { return null; }
        }
        public static bool Install(string zipUrl, string channelRoot, out string msg) {
            string tmpZip = null, tmpDir = null;
            try {
                if (!Directory.Exists(channelRoot)) { msg = "channel folder not found: " + channelRoot; return false; }
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                tmpZip = Path.Combine(Path.GetTempPath(), "StarStrings-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".zip");
                using (WebClient wc = new TimedWebClient()) { wc.Headers.Add("User-Agent", "StarMaster"); wc.DownloadFile(zipUrl, tmpZip); }
                tmpDir = Path.Combine(Path.GetTempPath(), "StarStrings-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                ZipFile.ExtractToDirectory(tmpZip, tmpDir);
                string dataSrc = FindDir(tmpDir, "Data"); if (dataSrc == null) { msg = "no Data folder in the downloaded zip"; return false; }
                int files = CopyTree(dataSrc, Path.Combine(channelRoot, "data"));
                EnsureLanguageLine(Path.Combine(channelRoot, "user.cfg"));
                msg = "installed " + files + " file(s) into " + channelRoot; return files > 0;
            } catch (Exception ex) { msg = "install failed: " + ex.Message; return false; }
            finally { try { if (tmpZip != null) File.Delete(tmpZip); } catch { } try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { } }
        }
        static string FindDir(string root, string name) { try { string d = Path.Combine(root, name); if (Directory.Exists(d)) return d; foreach (string s in Directory.GetDirectories(root, name, SearchOption.AllDirectories)) return s; } catch { } return null; }
        static int CopyTree(string src, string dst) { Directory.CreateDirectory(dst); int c = 0; foreach (string f in Directory.GetFiles(src)) { File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true); c++; } foreach (string s in Directory.GetDirectories(src)) c += CopyTree(s, Path.Combine(dst, Path.GetFileName(s))); return c; }
        static void EnsureLanguageLine(string p) { try { const string line = "g_language = english"; if (File.Exists(p)) { string t = File.ReadAllText(p); if (t.IndexOf("g_language", StringComparison.OrdinalIgnoreCase) < 0) { if (t.Length > 0 && !t.EndsWith("\n")) t += Environment.NewLine; File.WriteAllText(p, t + line + Environment.NewLine); } } else File.WriteAllText(p, line + Environment.NewLine); } catch { } }
    }

    // ===== Aurora theme + widgets =====
    static class Ui {
        public static SolidColorBrush B(string hex) { hex = hex.TrimStart('#'); SolidColorBrush b = new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16))); b.Freeze(); return b; }
        public static readonly Brush Bg = B("#0b0e16"), Card = B("#141a2b"), Card2 = B("#171d33"), Line = B("#232a45"), Line2 = B("#323c5e"),
            Text = B("#e6ecfb"), Dim = B("#94a0c2"), Faint = B("#646f93"), Accent = B("#79b0ff"), Accent2 = B("#a9c8ff"),
            Good = B("#5fe0c0"), Ink = B("#0a1228"), Inset = B("#0f1322"), Danger = B("#2a1722"), DangerFg = B("#ff9bb8");
        public static LinearGradientBrush AccentGrad() { LinearGradientBrush g = new LinearGradientBrush(Color.FromRgb(0x22, 0xd3, 0xee), Color.FromRgb(0xa8, 0x55, 0xf7), 0); g.Freeze(); return g; }
        public static readonly FontFamily Font = new FontFamily("Segoe UI"), Mono = new FontFamily("Consolas");
    }

    // small modal to add / edit a keystroke
    public partial class MainWindow : Window {
        public const string Version = "21";
        public const string VersionDate = "2026-06-20";   // bump alongside Version at release time
        const string DefaultScRoot = @"C:\Program Files\Roberts Space Industries\StarCitizen";
        string cfgPath; int[] CurrentVer;

        // keep-alive
        List<Cmd> commands = new List<Cmd>();
        StackPanel cmdPanel; TextBlock statusText; Border statusDot; Border startBtn; TextBlock startLbl;
        bool running = false, focusGuard = true, autostart = false, startMinimized = false; TextBox winTitleBox;
        DispatcherTimer timer;
        // backup
        TextBox bkRoot; Dropdown bkChannel, cpFrom, cpTo; bool wUser = true, wLoc = true, wCfg = true; StackPanel bkChips; TextBlock bkStatus;
        // shader cache
        TextBlock shaderStatus;
        // starstrings
        TextBox ssRoot; Dropdown ssChannel; TextBlock ssInstalled, ssLatest, ssStatus; Border ssDot, ssUpdateBtn; TextBlock ssUpdateLbl;
        StarStrings.Info ssLatestInfo; string ssInstalledBuild = "", ssRootCfg = "", ssChannelCfg = "";
        // tray
        System.Windows.Forms.NotifyIcon trayIcon; bool exiting = false;
        // single-instance: a second launch signals this handle to surface the existing window
        System.Threading.EventWaitHandle singleInstanceEvent;
        // header update button (its own label doubles as the status)
        Border updBtn; TextBlock updBtnLbl;
        DateTime lastUpdateCheck = DateTime.MinValue;   // throttles the automatic launch check (persisted in config)
        // in-app "update available" notice that lives in the header top row (replaces the popup)
        StackPanel updateNotice; TextBlock updateNoticeText;
        // in-app modal overlay - the ONLY way to show dialogs/messages (no OS popups anywhere)
        Grid overlayHost; Border overlayCard;

        public MainWindow() {
            cfgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            CurrentVer = Updater.ParseVer(Version);
            LoadConfig();

            Title = "StarMaster v" + Version;
            // never scrolls: the window resizes to fit and clamps at a minimum that keeps everything visible
            Width = 1120; Height = 1040; MinWidth = 1080; MinHeight = 1000;   // min keeps each star row tall enough that no card clips
            WindowStartupLocation = WindowStartupLocation.CenterScreen; FontFamily = Ui.Font;
            Background = new LinearGradientBrush(Color.FromRgb(0x12, 0x16, 0x2a), Color.FromRgb(0x0b, 0x0e, 0x16), 90);

            Grid root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Header());
            FrameworkElement cards = (FrameworkElement)Cards(); cards.Margin = new Thickness(0, 18, 0, 0); Grid.SetRow(cards, 1); root.Children.Add(cards);
            Grid shell = new Grid(); shell.Children.Add(root); shell.Children.Add(BuildOverlay());
            Content = shell;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += Tick;
            // listen for a second launch wanting to bring us forward (e.g. user re-runs while we're in the tray)
            try {
                singleInstanceEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, App.ActivateEvent);
                System.Threading.ThreadPool.RegisterWaitForSingleObject(singleInstanceEvent, delegate { Dispatcher.BeginInvoke(new Action(delegate { Restore(); })); }, null, -1, false);
            } catch { }
            BuildTray();
            Closing += OnClosing;
            Loaded += delegate { CheckUpdate(true); SSCheck(false); };
            if (startMinimized) { ShowInTaskbar = false; Visibility = Visibility.Hidden; Loaded += delegate { Hide(); }; }   // launch straight to the tray
            if (autostart) ToggleRun();
        }

        // ---------- header ----------
        UIElement Header() {
            DockPanel d = new DockPanel { LastChildFill = false };
            Border logo = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Background = Ui.Card2, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            Polygon star = new Polygon { Fill = Ui.AccentGrad(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            star.Points = new PointCollection(new Point[] { new Point(12, 1), new Point(15.5, 8.5), new Point(23, 12), new Point(15.5, 15.5), new Point(12, 23), new Point(8.5, 15.5), new Point(1, 12), new Point(8.5, 8.5) });
            logo.Child = star; DockPanel.SetDock(logo, Dock.Left); d.Children.Add(logo);
            StackPanel ttl = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            ttl.Children.Add(new TextBlock { Text = "StarMaster", Foreground = Ui.Accent, FontSize = 23, FontWeight = FontWeights.Bold });
            ttl.Children.Add(new TextBlock { Text = "Star Citizen Toolkit", Foreground = Ui.Dim, FontSize = 12.5 });
            DockPanel.SetDock(ttl, Dock.Left); d.Children.Add(ttl);
            // left-aligned dashboard section: version + release date + the app-wide Start-minimised setting
            Border sec = new Border { Margin = new Thickness(26, 0, 0, 0), Padding = new Thickness(16, 9, 16, 9), CornerRadius = new CornerRadius(12), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            StackPanel secRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            StackPanel ver = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ver.Children.Add(new TextBlock { Text = "Version " + Version, Foreground = Ui.Accent2, FontSize = 13, FontWeight = FontWeights.SemiBold });
            ver.Children.Add(new TextBlock { Text = "Released " + VersionDate, Foreground = Ui.Faint, FontSize = 11 });
            secRow.Children.Add(ver);
            secRow.Children.Add(new Border { Width = 1, Background = Ui.Line2, Margin = new Thickness(16, 2, 16, 2) });
            secRow.Children.Add(Toggle(startMinimized, delegate (bool v) { startMinimized = v; }));
            secRow.Children.Add(new TextBlock { Text = "  Start minimised", Foreground = Ui.Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            sec.Child = secRow; DockPanel.SetDock(sec, Dock.Left); d.Children.Add(sec);
            updBtn = Btn("↻  Check for updates", Ui.Card2, Ui.Text, false, delegate { CheckUpdate(false); }); updBtn.VerticalAlignment = VerticalAlignment.Center; updBtnLbl = (TextBlock)updBtn.Child; DockPanel.SetDock(updBtn, Dock.Right); d.Children.Add(updBtn);
            // inline "update available" notice in the top row; shown instead of the Check button while an update is pending
            updateNotice = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            updateNotice.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = Ui.Good, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            updateNoticeText = new TextBlock { Text = "", Foreground = Ui.Text, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            updateNotice.Children.Add(updateNoticeText);
            Border dl = Btn("↓  Download & install", Ui.AccentGrad(), Ui.Ink, true, delegate { StartUpdate(updateNotice != null ? (Updater.Info)updateNotice.Tag : null); });
            dl.VerticalAlignment = VerticalAlignment.Center; dl.Margin = new Thickness(14, 0, 0, 0); updateNotice.Children.Add(dl);
            Border later = Btn("Later", Ui.Card2, Ui.Dim, false, delegate { DismissUpdateNotice(); });
            later.VerticalAlignment = VerticalAlignment.Center; later.Margin = new Thickness(8, 0, 0, 0); updateNotice.Children.Add(later);
            DockPanel.SetDock(updateNotice, Dock.Right); d.Children.Add(updateNotice);
            return d;
        }

        // ---------- in-app modal overlay (replaces every OS popup / MessageBox) ----------
        UIElement BuildOverlay() {
            overlayHost = new Grid { Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x06, 0x08, 0x0f)), Visibility = Visibility.Collapsed };
            overlayHost.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };   // modal: swallow background clicks
            overlayCard = new Border { Background = Ui.Card, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(22, 20, 22, 20), MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            overlayHost.Children.Add(overlayCard);
            return overlayHost;
        }
        void HideOverlay() { overlayHost.Visibility = Visibility.Collapsed; overlayCard.Child = null; }
        StackPanel DialogShell(string title, double width) {
            StackPanel s = new StackPanel(); if (width > 0) s.Width = width;
            s.Children.Add(new TextBlock { Text = title, Foreground = Ui.Text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            return s;
        }
        void ShowOverlay(UIElement content) { overlayCard.Child = content; overlayHost.Visibility = Visibility.Visible; }
        // a yes/no confirm, in-app
        void ShowConfirm(string title, string msg, string okText, Action onOk) {
            StackPanel s = DialogShell(title, 0);
            s.Children.Add(new TextBlock { Text = msg, Foreground = Ui.Dim, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Border cancel = Btn("Cancel", Ui.Card2, Ui.Text, false, delegate { HideOverlay(); }); cancel.Padding = new Thickness(16, 9, 16, 9); btns.Children.Add(cancel); btns.Children.Add(Sp(10));
            Border ok = Btn(okText, Ui.AccentGrad(), Ui.Ink, true, delegate { HideOverlay(); if (onOk != null) onOk(); }); ok.Padding = new Thickness(18, 9, 18, 9); btns.Children.Add(ok);
            s.Children.Add(btns); ShowOverlay(s);
        }
        // an OK-only message, in-app
        void ShowAlert(string title, string msg) {
            StackPanel s = DialogShell(title, 0);
            s.Children.Add(new TextBlock { Text = msg, Foreground = Ui.Dim, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            Border ok = Btn("OK", Ui.AccentGrad(), Ui.Ink, true, delegate { HideOverlay(); }); ok.Padding = new Thickness(18, 9, 18, 9); btns.Children.Add(ok);
            s.Children.Add(btns); ShowOverlay(s);
        }
        // the add/edit-keystroke form, in-app (was a separate AddKeyDialog window)
        void ShowKeyForm(Cmd edit, Action<Cmd> onSave) {
            StackPanel s = DialogShell(edit == null ? "Add keystroke" : "Edit keystroke", 360);
            s.Children.Add(FormLbl("Label")); TextBox label = FormTb(edit != null ? edit.Label : ""); s.Children.Add(label);
            bool[] sh = { edit != null && edit.Shift }, ct = { edit != null && edit.Ctrl }, al = { edit != null && edit.Alt };
            StackPanel mods = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            mods.Children.Add(ModBtn("Shift", sh)); mods.Children.Add(ModBtn("Ctrl", ct)); mods.Children.Add(ModBtn("Alt", al));
            s.Children.Add(mods);
            s.Children.Add(FormLbl("Key  (A-Z, 0-9, F1-F12, Space, Enter, Tab, Esc, [ ])")); TextBox key = FormTb(edit != null ? edit.Key : ""); s.Children.Add(key);
            s.Children.Add(FormLbl("Every (seconds)")); TextBox interval = FormTb(edit != null ? edit.Interval.ToString() : "600"); s.Children.Add(interval);
            TextBlock err = new TextBlock { Foreground = Ui.DangerFg, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            s.Children.Add(err);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            Border cancel = Btn("Cancel", Ui.Card2, Ui.Text, false, delegate { HideOverlay(); }); cancel.Padding = new Thickness(16, 8, 16, 8); btns.Children.Add(cancel); btns.Children.Add(Sp(10));
            Border save = Btn("Save", Ui.AccentGrad(), Ui.Ink, true, null); save.Padding = new Thickness(20, 8, 20, 8);
            save.MouseLeftButtonUp += delegate {
                string k = key.Text.Trim();
                if (Vk.Map(k) == 0) { err.Text = "Key '" + k + "' not recognized. Use A-Z, 0-9, F1-F12, Space, Enter, Tab, Esc, [ or ]."; err.Visibility = Visibility.Visible; return; }
                int iv; if (!int.TryParse(interval.Text.Trim(), out iv)) iv = 600; if (iv < 1) iv = 1; if (iv > 3600) iv = 3600;
                Cmd r = new Cmd { Label = label.Text.Trim().Length > 0 ? label.Text.Trim() : "Command", Shift = sh[0], Ctrl = ct[0], Alt = al[0], Key = k, Interval = iv, Enabled = true };
                HideOverlay(); if (onSave != null) onSave(r);
            };
            btns.Children.Add(save); s.Children.Add(btns);
            ShowOverlay(s);
        }
        TextBlock FormLbl(string t) { return new TextBlock { Text = t, Foreground = Ui.Dim, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 4) }; }
        TextBox FormTb(string v) { return new TextBox { Text = v, Background = Ui.Bg, Foreground = Ui.Text, CaretBrush = Ui.Accent, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(7, 5, 7, 5), FontSize = 13 }; }
        UIElement ModBtn(string t, bool[] state) { Border b = Btn(t, state[0] ? (Brush)Ui.AccentGrad() : Ui.Card2, state[0] ? Ui.Ink : Ui.Text, false, null); b.Margin = new Thickness(0, 0, 8, 0); b.Padding = new Thickness(14, 7, 14, 7); b.MouseLeftButtonUp += delegate { state[0] = !state[0]; b.Background = state[0] ? (Brush)Ui.AccentGrad() : Ui.Card2; ((TextBlock)b.Child).Foreground = state[0] ? Ui.Ink : Ui.Text; }; return b; }

        UIElement Cards() {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
            // equal star rows + stretched cards => the 2x2 tiles fill the whole window (no empty space when enlarged)
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            FrameworkElement ka = KeepAliveCard(); Grid.SetRow(ka, 0); Grid.SetColumn(ka, 0); ka.Margin = new Thickness(0, 0, 9, 18); ka.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(ka);
            FrameworkElement bk = BackupCard(); Grid.SetRow(bk, 0); Grid.SetColumn(bk, 1); bk.Margin = new Thickness(9, 0, 0, 18); bk.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(bk);
            FrameworkElement ss = StarStringsCard(); Grid.SetRow(ss, 1); Grid.SetColumn(ss, 0); ss.Margin = new Thickness(0, 0, 9, 0); ss.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(ss);
            FrameworkElement sc = ShaderCacheCard(); Grid.SetRow(sc, 1); Grid.SetColumn(sc, 1); sc.Margin = new Thickness(9, 0, 0, 0); sc.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(sc);
            return g;
        }
        // ---------- shader cache card (half-width tile) ----------
        FrameworkElement ShaderCacheCard() {
            StackPanel body; DockPanel head; Border card = CardShell(out body, out head, "♻", "Shader Cache", "fixes graphical glitches - rebuilt on next launch");
            body.Children.Add(new TextBlock { Text = "Deletes the Star Citizen shader cache at %LOCALAPPDATA%\\Star Citizen. Safe to do - the game rebuilds it automatically on next launch (the first load afterwards is a little slower). Close Star Citizen first.", Foreground = Ui.Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 14) });
            Border clr = Btn("♻  Clear shader cache", Ui.AccentGrad(), Ui.Ink, true, delegate { ClearShaderCache(); }); clr.Padding = new Thickness(18, 10, 18, 10); clr.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(clr);
            shaderStatus = new TextBlock { Text = "", Foreground = Ui.Dim, FontSize = 11.5, FontFamily = Ui.Mono, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(shaderStatus);
            return card;
        }

        Border CardShell(out StackPanel body, out DockPanel head, string icon, string title, string sub) {
            Border card = new Border { CornerRadius = new CornerRadius(15), Background = Ui.Card, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), Padding = new Thickness(20, 18, 20, 18), VerticalAlignment = VerticalAlignment.Top };
            StackPanel s = new StackPanel();
            head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 15) };
            Border ic = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Ui.B("#16203a"), BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), Child = new TextBlock { Text = icon, FontSize = 16, Foreground = Ui.Accent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(ic, Dock.Left); head.Children.Add(ic);
            StackPanel tt = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            tt.Children.Add(new TextBlock { Text = title, Foreground = Ui.Text, FontSize = 15.5, FontWeight = FontWeights.SemiBold });
            tt.Children.Add(new TextBlock { Text = sub, Foreground = Ui.Faint, FontSize = 11.5 });
            DockPanel.SetDock(tt, Dock.Left); head.Children.Add(tt);
            s.Children.Add(head); card.Child = s; body = s; return card;
        }
        StackPanel StatusBadge(out Border dot, out TextBlock txt, string text, Brush color) {
            StackPanel st = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            dot = new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(5), Background = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            txt = new TextBlock { Text = text, Foreground = color, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            st.Children.Add(dot); st.Children.Add(txt); return st;
        }

        // ---------- keep-alive card ----------
        FrameworkElement KeepAliveCard() {
            StackPanel body; DockPanel head; Border card = CardShell(out body, out head, "♥", "Keep-Alive", "anti-idle keystrokes");
            StackPanel kaBadge = StatusBadge(out statusDot, out statusText, "Stopped", Ui.Dim); statusDot.Background = Ui.Faint; kaBadge.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(kaBadge, Dock.Right); head.Children.Add(kaBadge);
            cmdPanel = new StackPanel(); body.Children.Add(cmdPanel); RefreshCommands();
            Border add = new Border { Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(10), CornerRadius = new CornerRadius(11), BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = new TextBlock { Text = "+  Add keystroke", Foreground = Ui.Dim, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center } };
            add.MouseLeftButtonUp += delegate { AddKey(); }; body.Children.Add(add);
            // focus guard
            StackPanel fg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            fg.Children.Add(Toggle(focusGuard, delegate (bool v) { focusGuard = v; }));
            fg.Children.Add(new TextBlock { Text = "  Only while window contains:", Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
            winTitleBox = new TextBox { Text = winTitle(), Width = 110, Background = Ui.Bg, Foreground = Ui.Text, CaretBrush = Ui.Accent, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(6, 4, 6, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            fg.Children.Add(winTitleBox); body.Children.Add(fg);
            StackPanel au = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            au.Children.Add(Toggle(autostart, delegate (bool v) { autostart = v; })); au.Children.Add(new TextBlock { Text = "  Auto-start on launch", Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            body.Children.Add(au);
            startBtn = Btn("▶  Start", Ui.AccentGrad(), Ui.Ink, true, delegate { ToggleRun(); }); startBtn.Margin = new Thickness(0, 16, 0, 0); startLbl = (TextBlock)startBtn.Child;
            body.Children.Add(startBtn);
            return card;
        }
        string winTitleField = "Star Citizen";
        string winTitle() { return winTitleField; }

        void RefreshCommands() {
            cmdPanel.Children.Clear();
            foreach (Cmd c in commands) cmdPanel.Children.Add(CmdRow(c));
        }
        UIElement CmdRow(Cmd c) {
            Cmd cc = c;
            Border row = new Border { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 9, 10, 9), CornerRadius = new CornerRadius(11), Background = Ui.Inset, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
            if (!c.Enabled) row.Opacity = 0.6;
            DockPanel d = new DockPanel { LastChildFill = false };
            StackPanel left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            left.Children.Add(new TextBlock { Text = c.Label, Foreground = Ui.Text, FontSize = 13.5, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = Combo(c) + " · every " + c.Interval + "s", Foreground = Ui.Dim, FontSize = 11.5, FontFamily = Ui.Mono });
            left.MouseLeftButtonUp += delegate { EditKey(cc); };
            DockPanel.SetDock(left, Dock.Left); d.Children.Add(left);
            StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Border tg = Toggle(c.Enabled, delegate (bool v) { cc.Enabled = v; row.Opacity = v ? 1.0 : 0.6; }); tg.Margin = new Thickness(0, 0, 10, 0); right.Children.Add(tg);
            Border del = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Ui.Card2, Child = new TextBlock { Text = "✕", Foreground = Ui.Dim, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            del.MouseLeftButtonUp += delegate { commands.Remove(cc); RefreshCommands(); }; right.Children.Add(del);
            DockPanel.SetDock(right, Dock.Right); d.Children.Add(right);
            row.Child = d; return row;
        }
        void AddKey() { ShowKeyForm(null, delegate (Cmd r) { commands.Add(r); RefreshCommands(); }); }
        void EditKey(Cmd c) { ShowKeyForm(c, delegate (Cmd r) { int i = commands.IndexOf(c); r.Enabled = c.Enabled; r.LastFire = c.LastFire; commands[i] = r; RefreshCommands(); }); }
        string Combo(Cmd c) { List<string> p = new List<string>(); if (c.Shift) p.Add("Shift"); if (c.Ctrl) p.Add("Ctrl"); if (c.Alt) p.Add("Alt"); p.Add(c.Key); return string.Join("+", p.ToArray()); }

        void ToggleRun() {
            running = !running;
            if (running) { foreach (Cmd c in commands) c.LastFire = DateTime.Now; timer.Start(); startLbl.Text = "■  Stop"; startBtn.Background = Ui.Danger; startLbl.Foreground = Ui.DangerFg; statusText.Text = "Running"; statusText.Foreground = Ui.Good; statusDot.Background = Ui.Good; }
            else { timer.Stop(); startLbl.Text = "▶  Start"; startBtn.Background = Ui.AccentGrad(); startLbl.Foreground = Ui.Ink; statusText.Text = "Stopped"; statusText.Foreground = Ui.Dim; statusDot.Background = Ui.Faint; }
        }
        void Tick(object s, EventArgs e) {
            foreach (Cmd c in commands) {
                if (!c.Enabled) continue;
                if ((DateTime.Now - c.LastFire).TotalSeconds < c.Interval) continue;
                if (focusGuard) { string t = winTitleBox.Text.Trim().ToLower(); if (t.Length == 0 || !Native.ActiveTitle().ToLower().Contains(t)) continue; }
                c.LastFire = DateTime.Now;
                List<byte> mods = new List<byte>(); if (c.Shift) mods.Add(0xA0); if (c.Ctrl) mods.Add(0xA2); if (c.Alt) mods.Add(0xA4);
                byte vk = Vk.Map(c.Key); if (vk == 0) continue;
                byte[] mm = mods.ToArray(); System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Press(mm, vk); });   // send off the UI thread (Press sleeps 40ms)
            }
        }

        // ---------- backup card ----------
        FrameworkElement BackupCard() {
            StackPanel body; DockPanel head; Border card = CardShell(out body, out head, "▤", "Backup / Restore", "settings · bindings · StarStrings");
            bkRoot = TextField(DefaultScRoot); body.Children.Add(LabeledField("SC folder", bkRoot));
            bkRoot.LostFocus += delegate { RefreshChannels(); };
            StackPanel checks = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
            checks.Children.Add(Check("User settings & bindings (user\\)", wUser, delegate (bool v) { wUser = v; }));
            checks.Children.Add(Check("StarStrings text mod (data\\Localization\\)", wLoc, delegate (bool v) { wLoc = v; }));
            checks.Children.Add(Check("StarStrings language line (user.cfg)", wCfg, delegate (bool v) { wCfg = v; }));
            body.Children.Add(checks);
            StackPanel r1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            bkChannel = new Dropdown(new string[] { "LIVE", "HOTFIX" }, "LIVE", 120); r1.Children.Add(bkChannel); r1.Children.Add(Sp(9));
            Border bk = Btn("Back up now", Ui.AccentGrad(), Ui.Ink, true, delegate { DoBackup(); }); bk.Padding = new Thickness(15, 8, 15, 8); r1.Children.Add(bk);
            body.Children.Add(r1);
            StackPanel r2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            cpFrom = new Dropdown(new string[] { "LIVE (current)" }, "LIVE (current)", 150); r2.Children.Add(cpFrom);
            r2.Children.Add(new TextBlock { Text = "  →  ", Foreground = Ui.Accent, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            cpTo = new Dropdown(new string[] { "HOTFIX" }, "HOTFIX", 110); r2.Children.Add(cpTo); r2.Children.Add(Sp(9));
            Border cp = Btn("Copy / Restore", Ui.Card2, Ui.Text, false, delegate { DoCopy(); }); cp.Padding = new Thickness(15, 8, 15, 8); r2.Children.Add(cp);
            body.Children.Add(r2);
            bkStatus = new TextBlock { Foreground = Ui.Dim, FontSize = 11.5, FontFamily = Ui.Mono, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }; body.Children.Add(bkStatus);
            DockPanel rb = new DockPanel { LastChildFill = false }; TextBlock rc = Caps("Recent backups"); DockPanel.SetDock(rc, Dock.Left); rb.Children.Add(rc);
            Border openFld = new Border { Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Right, Child = new TextBlock { Text = "Open folder ↗", Foreground = Ui.Accent2, FontSize = 11 } };
            openFld.MouseLeftButtonUp += delegate { try { Directory.CreateDirectory(BackupOps.BackupsRoot); System.Diagnostics.Process.Start("explorer.exe", "\"" + BackupOps.BackupsRoot + "\""); } catch { } };
            DockPanel.SetDock(openFld, Dock.Right); rb.Children.Add(openFld); body.Children.Add(rb);
            bkChips = new StackPanel { Margin = new Thickness(0, 6, 0, 0) }; body.Children.Add(bkChips);
            RefreshChannels();
            return card;
        }
        void RefreshChannels() {
            string root = bkRoot.Text.Trim();
            string[] chans = BackupOps.DetectChannels(root); if (chans.Length == 0) chans = new string[] { "LIVE", "HOTFIX" };
            bkChannel.SetItems(chans, Pick(chans, "LIVE")); cpTo.SetItems(chans, Pick(chans, "HOTFIX"));
            List<string> from = new List<string>(); foreach (string c in chans) from.Add(c + " (current)"); foreach (string s in BackupOps.Snapshots()) from.Add(s);
            cpFrom.SetItems(from.ToArray(), from.Count > 0 ? from[0] : "");
            if (ssChannel != null) ssChannel.SetItems(chans, Pick(chans, ssChannel.Value));
            bkChips.Children.Clear();
            string[] snaps = BackupOps.Snapshots(); int shown = 0;
            foreach (string s in snaps) { if (shown++ >= 4) break; bkChips.Children.Add(new TextBlock { Text = s, Foreground = Ui.Dim, FontSize = 11, FontFamily = Ui.Mono, Margin = new Thickness(0, 2, 0, 2) }); }
            if (snaps.Length == 0) bkChips.Children.Add(new TextBlock { Text = "(none yet)", Foreground = Ui.Faint, FontSize = 11 });
        }
        string Pick(string[] arr, string want) { foreach (string a in arr) if (a == want) return want; return arr.Length > 0 ? arr[0] : ""; }
        void DoBackup() {
            string root = bkRoot.Text.Trim(); string ch = bkChannel.Value;
            string src = Path.Combine(root, ch); if (!Directory.Exists(src)) { bkStatus.Text = "channel not found: " + src; return; }
            string dst = Path.Combine(BackupOps.BackupsRoot, ch + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            bkStatus.Text = "backing up " + ch + " ...";
            RunBg(delegate { int n = BackupOps.CopyItems(src, dst, wUser, wLoc, wCfg, BkLog); return n > 0 ? ("backup complete (" + n + " files) → " + Path.GetFileName(dst)) : "nothing copied - check the tickboxes/channel"; }, delegate { RefreshChannels(); });
        }
        void DoCopy() {
            string root = bkRoot.Text.Trim(); string from = cpFrom.Value, to = cpTo.Value;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) { bkStatus.Text = "pick From and To"; return; }
            string srcBase = from.EndsWith(" (current)") ? Path.Combine(root, from.Substring(0, from.Length - 10)) : Path.Combine(BackupOps.BackupsRoot, from);
            string dstBase = Path.Combine(root, to);
            if (!Directory.Exists(srcBase)) { bkStatus.Text = "source not found: " + srcBase; return; }
            try { if (string.Equals(Path.GetFullPath(srcBase).TrimEnd('\\'), Path.GetFullPath(dstBase).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { bkStatus.Text = "source and target are the same - nothing to do"; return; } } catch { }
            ShowConfirm("Confirm copy / restore", "Copy the ticked items\n\nFROM:  " + from + "\nTO:      " + to + " channel\n\nExisting files are overwritten (nothing deleted). Close Star Citizen first.", "Copy / Restore", delegate {
                bkStatus.Text = "copying " + from + " → " + to + " ...";
                RunBg(delegate { int n = BackupOps.CopyItems(srcBase, dstBase, wUser, wLoc, wCfg, BkLog); return n > 0 ? ("done - copied " + n + " files into " + to + ". Restart Star Citizen.") : "nothing copied"; }, null);
            });
        }
        // delete the SC shader cache (%LOCALAPPDATA%\Star Citizen) - it regenerates on next launch; fixes most graphical glitches
        void ClearShaderCache() {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Star Citizen");
            if (!Directory.Exists(dir)) { shaderStatus.Text = "no shader cache found at " + dir; shaderStatus.Foreground = Ui.Dim; return; }
            ShowConfirm("Clear shader cache", "Delete the Star Citizen shader cache?\n\n" + dir + "\n\nSafe to do - the game rebuilds it on next launch (the first load will be a bit slower). Close Star Citizen first.", "Clear cache", delegate {
                shaderStatus.Text = "clearing shader cache ..."; shaderStatus.Foreground = Ui.Dim;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                    string res; Brush fg;
                    try { Directory.Delete(dir, true); res = "shader cache cleared - it rebuilds on next launch"; fg = Ui.Good; }
                    catch (Exception ex) { res = "could not fully clear (files in use? close Star Citizen and retry): " + ex.Message; fg = Ui.DangerFg; }
                    Dispatcher.BeginInvoke(new Action(delegate { shaderStatus.Text = res; shaderStatus.Foreground = fg; }));
                });
            });
        }
        void BkLog(string m) { Dispatcher.BeginInvoke(new Action(delegate { bkStatus.Text = m; })); }
        void RunBg(Func<string> work, Action then) {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                string res; try { res = work(); } catch (Exception ex) { res = "error: " + ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate { bkStatus.Text = res; if (then != null) then(); }));
            });
        }

        // ---------- starstrings card (half-width tile: stacked vertically) ----------
        FrameworkElement StarStringsCard() {
            StackPanel body; DockPanel head; Border card = CardShell(out body, out head, "◉", "StarStrings", "MrKraken community localization");
            StackPanel ssBadge = StatusBadge(out ssDot, out ssStatus, "not checked", Ui.Dim); ssDot.Background = Ui.Faint; ssBadge.VerticalAlignment = VerticalAlignment.Top; DockPanel.SetDock(ssBadge, Dock.Right); head.Children.Add(ssBadge);
            ssRoot = TextField(ssRootCfg.Length > 0 ? ssRootCfg : DefaultScRoot); body.Children.Add(LabeledField("SC folder", ssRoot));
            body.Children.Add(BuildRow("Installed", out ssInstalled, ssInstalledBuild.Length > 0 ? ssInstalledBuild : "(not installed)", Ui.Text));
            body.Children.Add(BuildRow("Latest", out ssLatest, "(check)", Ui.Accent));
            StackPanel ctl = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            ssChannel = new Dropdown(new string[] { "LIVE", "HOTFIX" }, ssChannelCfg.Length > 0 ? ssChannelCfg : "LIVE", 100); ctl.Children.Add(ssChannel); ctl.Children.Add(Sp(9));
            Border ck = Btn("↻ Check", Ui.Card2, Ui.Text, false, delegate { SSCheck(true); }); ck.Padding = new Thickness(14, 9, 14, 9); ctl.Children.Add(ck); ctl.Children.Add(Sp(9));
            ssUpdateBtn = Btn("✓ Up to date", Ui.AccentGrad(), Ui.Ink, true, delegate { SSInstall(); }); ssUpdateBtn.Padding = new Thickness(14, 9, 14, 9); ssUpdateLbl = (TextBlock)ssUpdateBtn.Child; ctl.Children.Add(ssUpdateBtn);
            body.Children.Add(ctl);
            // full credit to MrKraken, the creator of StarStrings
            TextBlock credit = new TextBlock { Margin = new Thickness(0, 14, 0, 0), FontSize = 11.5, Foreground = Ui.Faint, TextWrapping = TextWrapping.Wrap, LineHeight = 16 };
            credit.Inlines.Add(new Run("Created and maintained by MrKraken — all credit to him."));
            credit.Inlines.Add(new LineBreak());
            credit.Inlines.Add(new Run("Source: "));
            Hyperlink link = new Hyperlink(new Run("github.com/MrKraken/StarStrings")) { Foreground = Ui.Accent, TextDecorations = null };
            try { link.NavigateUri = new Uri("https://github.com/MrKraken/StarStrings"); } catch { }
            link.RequestNavigate += delegate (object s, RequestNavigateEventArgs e) { try { System.Diagnostics.Process.Start(e.Uri.AbsoluteUri); } catch { } };
            link.Cursor = Cursors.Hand;
            credit.Inlines.Add(link);
            body.Children.Add(credit);
            return card;
        }
        // a stacked label + wrapping mono value (for the half-width tile)
        StackPanel BuildRow(string cap, out TextBlock val, string v, Brush fg) { StackPanel s = new StackPanel { Margin = new Thickness(0, 8, 0, 0) }; s.Children.Add(Caps(cap)); val = new TextBlock { Text = v, Foreground = fg, FontSize = 12.5, FontFamily = Ui.Mono, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap }; s.Children.Add(val); return s; }
        void SSCheck(bool announce) {
            if (announce) ssLatest.Text = "checking...";
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                StarStrings.Info info = StarStrings.CheckLatest();
                Dispatcher.BeginInvoke(new Action(delegate {
                    ssLatestInfo = info;
                    if (info == null) { ssLatest.Text = "(offline)"; ssStatus.Text = "offline"; ssStatus.Foreground = Ui.Dim; ssDot.Background = Ui.Faint; return; }
                    ssLatest.Text = info.Build;
                    bool cur = ssInstalledBuild.Length > 0 && ssInstalledBuild == info.Build;
                    if (ssInstalledBuild.Length == 0) { ssUpdateLbl.Text = "↓ Install"; ssStatus.Text = "not installed"; ssStatus.Foreground = Ui.Accent2; ssDot.Background = Ui.Accent; }
                    else if (cur) { ssUpdateLbl.Text = "✓ Re-install"; ssStatus.Text = "up to date"; ssStatus.Foreground = Ui.Good; ssDot.Background = Ui.Good; }
                    else { ssUpdateLbl.Text = "↓ Update"; ssStatus.Text = "update available"; ssStatus.Foreground = Ui.Accent2; ssDot.Background = Ui.Accent; }
                }));
            });
        }
        void SSInstall() {
            if (ssLatestInfo == null || string.IsNullOrEmpty(ssLatestInfo.ZipUrl)) { SSCheck(true); return; }
            string root = ssRoot.Text.Trim(); string ch = ssChannel.Value; string channelRoot = Path.Combine(root, ch);
            ShowConfirm("Install StarStrings", "Install StarStrings into:\n\n" + channelRoot + "\n\nCopies the data\\ folder and ensures user.cfg has 'g_language = english'. Close Star Citizen first.", "Install", delegate {
                ssStatus.Text = "installing..."; ssStatus.Foreground = Ui.Dim; string zip = ssLatestInfo.ZipUrl; string build = ssLatestInfo.Build;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                    string msg; bool ok = StarStrings.Install(zip, channelRoot, out msg);
                    Dispatcher.BeginInvoke(new Action(delegate {
                        if (ok) { ssInstalledBuild = build; ssInstalled.Text = build; ssStatus.Text = "installed - restart SC"; ssStatus.Foreground = Ui.Good; ssDot.Background = Ui.Good; ssUpdateLbl.Text = "✓ Re-install"; SaveConfig(); }
                        else { ssStatus.Text = "failed: " + msg; ssStatus.Foreground = Ui.DangerFg; }
                    }));
                });
            });
        }

        // ---------- self-update ----------
        void SetUpdBtn(string text, Brush fg) { if (updBtnLbl != null) { updBtnLbl.Text = text; updBtnLbl.Foreground = fg; } }
        void CheckUpdate(bool auto) {
            // auto (launch) checks back off if one ran in the last 5 minutes - avoids hammering GitHub's anon rate limit
            if (auto && (DateTime.Now - lastUpdateCheck).TotalMinutes < 5) return;
            SetUpdBtn("↻  Checking...", Ui.Dim);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                Updater.Info info = Updater.CheckLatest();
                lastUpdateCheck = DateTime.Now;
                Dispatcher.BeginInvoke(new Action(delegate {
                    if (info == null) SetUpdBtn("⚠  " + (Updater.LastError.Length > 0 ? Updater.LastError : "Check failed"), Ui.DangerFg);
                    else if (Updater.Compare(info.Version, CurrentVer) > 0) { SetUpdBtn("↑  Update available", Ui.Accent); ShowUpdateBanner(info); }
                    else SetUpdBtn("✓  Up to date", Ui.Good);
                    SaveConfig();   // persist lastUpdateCheck so rapid relaunches stay throttled too
                }));
            });
        }
        void StartUpdate(Updater.Info info) {
            if (info == null) return;
            if (info.SetupUrl != null && RunningFromInstallDir()) {
                if (updateNoticeText != null) updateNoticeText.Text = "Downloading " + info.Tag + "...";
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { string p = Updater.DownloadInstaller(info.SetupUrl); Dispatcher.BeginInvoke(new Action(delegate { if (p != null) { try { System.Diagnostics.Process.Start(p); exiting = true; System.Windows.Application.Current.Shutdown(); return; } catch { } } OpenPage(info); })); });
            } else OpenPage(info);
        }
        // show the notice in the top row, hiding the Check button to make room
        void ShowUpdateBanner(Updater.Info info) {
            if (updateNotice == null) return;
            updateNotice.Tag = info;
            if (updateNoticeText != null) updateNoticeText.Text = "StarMaster " + info.Tag + " available";
            updateNotice.Visibility = Visibility.Visible;
            if (updBtn != null) updBtn.Visibility = Visibility.Collapsed;
        }
        // dismiss the notice and bring the Check button back (still flagged that an update exists)
        void DismissUpdateNotice() {
            if (updateNotice != null) updateNotice.Visibility = Visibility.Collapsed;
            if (updBtn != null) updBtn.Visibility = Visibility.Visible;
            SetUpdBtn("↑  Update available", Ui.Accent);
        }
        void OpenPage(Updater.Info info) { try { System.Diagnostics.Process.Start(info != null && info.PageUrl != null ? info.PageUrl : Updater.ReleasesPage); } catch { } }
        static bool RunningFromInstallDir() {
            try { string i = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMaster"); string e = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); return string.Equals(Path.GetFullPath(e).TrimEnd('\\'), Path.GetFullPath(i).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }

    }

    // ===== shared widgets (static on MainWindow for AddKeyDialog reuse) =====
    public partial class MainWindow {
        public static Border Btn(string text, Brush bg, Brush fg, bool bold, Action onClick) {
            TextBlock tb = new TextBlock { Text = text, Foreground = fg, FontSize = 13.5, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, HorizontalAlignment = HorizontalAlignment.Center };
            Border b = new Border { Background = bg, CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 10, 13, 10), Cursor = Cursors.Hand, Child = tb, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (bg == Ui.Card2) { b.BorderBrush = Ui.Line2; b.BorderThickness = new Thickness(1); }
            if (onClick != null) b.MouseLeftButtonUp += delegate { onClick(); };
            return b;
        }
        Border Toggle(bool on, Action<bool> changed) {
            Border track = new Border { Width = 40, Height = 22, CornerRadius = new CornerRadius(11), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Background = on ? (Brush)Ui.AccentGrad() : Ui.Line2 };
            Ellipse knob = new Ellipse { Width = 16, Height = 16, Fill = Ui.B("#cdd6de"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0), HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left };
            track.Child = knob; bool[] st = { on };
            track.MouseLeftButtonUp += delegate { st[0] = !st[0]; track.Background = st[0] ? (Brush)Ui.AccentGrad() : Ui.Line2; knob.HorizontalAlignment = st[0] ? HorizontalAlignment.Right : HorizontalAlignment.Left; if (changed != null) changed(st[0]); };
            return track;
        }
        UIElement Check(string text, bool on, Action<bool> changed) {
            StackPanel s = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            s.Children.Add(Toggle(on, changed)); s.Children.Add(new TextBlock { Text = "  " + text, Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return s;
        }
        TextBox TextField(string v) { return new TextBox { Text = v, Background = Ui.Bg, Foreground = Ui.Text, CaretBrush = Ui.Accent, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), FontSize = 12, FontFamily = Ui.Mono }; }
        UIElement LabeledField(string k, TextBox box) {
            DockPanel d = new DockPanel { Margin = new Thickness(0, 0, 0, 11) };
            TextBlock kt = new TextBlock { Text = k, Foreground = Ui.Dim, FontSize = 12.5, Width = 72, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(kt, Dock.Left); d.Children.Add(kt);
            d.Children.Add(box); return d;
        }
        TextBlock Caps(string t) { return new TextBlock { Text = t.ToUpper(), Foreground = Ui.Faint, FontSize = 11 }; }
        FrameworkElement Sp(int w) { return new Border { Width = w }; }

        // ---------- tray + lifecycle ----------
        void BuildTray() {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            try { trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            if (trayIcon.Icon == null) trayIcon.Icon = System.Drawing.SystemIcons.Application;   // a Visible NotifyIcon with no icon never appears -> app would be unreachable
            trayIcon.Text = "StarMaster"; trayIcon.Visible = true;
            System.Windows.Forms.ContextMenuStrip m = new System.Windows.Forms.ContextMenuStrip();
            m.BackColor = System.Drawing.Color.FromArgb(23, 29, 51); m.ForeColor = System.Drawing.Color.FromArgb(230, 236, 251); m.ShowImageMargin = false;
            m.Items.Add("Open StarMaster", null, delegate { Restore(); });
            m.Items.Add("Exit StarMaster", null, delegate { exiting = true; Close(); });
            trayIcon.ContextMenuStrip = m;
            trayIcon.MouseClick += delegate (object s, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) Restore(); };   // single left-click opens; right-click still shows the menu
        }
        void Restore() { Dispatcher.BeginInvoke(new Action(delegate { ShowInTaskbar = true; Visibility = Visibility.Visible; Show(); WindowState = WindowState.Normal; Activate(); })); }
        void OnClosing(object s, System.ComponentModel.CancelEventArgs e) {
            SaveConfig();
            if (!exiting && trayIcon != null && trayIcon.Icon != null && trayIcon.Visible) { e.Cancel = true; Hide(); return; }
            timer.Stop(); if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
        }

        // ---------- config ----------
        void LoadConfig() {
            commands = new List<Cmd>(); autostart = false; focusGuard = true; startMinimized = false; winTitleField = "Star Citizen";
            try {
                if (File.Exists(cfgPath)) foreach (string line in File.ReadAllLines(cfgPath)) {
                    string ln = line.Trim(); if (ln.Length == 0 || ln.StartsWith("#")) continue;
                    if (ln.IndexOf('|') >= 0) { string[] f = ln.Split('|'); if (f.Length >= 7) { Cmd c = new Cmd(); c.Label = f[0]; c.Shift = f[1] == "1"; c.Ctrl = f[2] == "1"; c.Alt = f[3] == "1"; c.Key = f[4]; int iv; int.TryParse(f[5], out iv); c.Interval = iv < 1 ? 1 : (iv > 3600 ? 3600 : iv); c.Enabled = f[6] == "1"; commands.Add(c); } }
                    else if (ln.IndexOf('=') > 0) { string[] kv = ln.Split(new char[] { '=' }, 2); string k = kv[0].Trim().ToLower(), v = kv[1].Trim(); if (k == "autostart") autostart = v == "1"; else if (k == "focusguard") focusGuard = v == "1"; else if (k == "startminimized") startMinimized = v == "1"; else if (k == "wintitle") winTitleField = v; else if (k == "starstrings_build") ssInstalledBuild = v; else if (k == "starstrings_root") ssRootCfg = v; else if (k == "starstrings_channel") ssChannelCfg = v; else if (k == "lastcheck") { long t; if (long.TryParse(v, out t) && t > 0 && t <= DateTime.MaxValue.Ticks) lastUpdateCheck = new DateTime(t); } }
                }
            } catch { }
            if (commands.Count == 0) { commands.Add(new Cmd { Label = "Wipe Visor", Alt = true, Key = "X", Interval = 600, Enabled = true }); commands.Add(new Cmd { Label = "Auto Accept", Key = "[", Interval = 1, Enabled = false }); }
        }
        void SaveConfig() {
            try {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# StarMaster config");
                sb.AppendLine("autostart=" + (autostart ? "1" : "0"));
                sb.AppendLine("focusguard=" + (focusGuard ? "1" : "0"));
                sb.AppendLine("startminimized=" + (startMinimized ? "1" : "0"));
                sb.AppendLine("wintitle=" + (winTitleBox != null ? winTitleBox.Text : winTitleField));
                sb.AppendLine("starstrings_build=" + ssInstalledBuild);
                sb.AppendLine("starstrings_root=" + (ssRoot != null ? ssRoot.Text : ssRootCfg));
                sb.AppendLine("starstrings_channel=" + (ssChannel != null ? ssChannel.Value : ssChannelCfg));
                sb.AppendLine("lastcheck=" + lastUpdateCheck.Ticks);
                sb.AppendLine("# commands: Label|Shift|Ctrl|Alt|Key|Interval|Enabled");
                foreach (Cmd c in commands) sb.AppendLine(c.Label.Replace("|", "/") + "|" + (c.Shift ? "1" : "0") + "|" + (c.Ctrl ? "1" : "0") + "|" + (c.Alt ? "1" : "0") + "|" + c.Key + "|" + c.Interval + "|" + (c.Enabled ? "1" : "0"));
                File.WriteAllText(cfgPath, sb.ToString());
            } catch { }
        }
    }

    // custom dark dropdown
    public class Dropdown : Border {
        Popup popup; StackPanel list; TextBlock label; public string Value;
        public Dropdown(string[] items, string initial, double minW) {
            Background = Ui.Bg; BorderBrush = Ui.Line2; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8); Padding = new Thickness(11, 7, 9, 7); Cursor = Cursors.Hand; MinWidth = minW; VerticalAlignment = VerticalAlignment.Center;
            DockPanel dp = new DockPanel();
            TextBlock arr = new TextBlock { Text = " ▾", Foreground = Ui.Dim, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(arr, Dock.Right); dp.Children.Add(arr);
            label = new TextBlock { Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }; dp.Children.Add(label);
            Child = dp;
            list = new StackPanel();
            popup = new Popup { PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Child = new Border { Background = Ui.Card2, BorderBrush = Ui.Line2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = list, MinWidth = minW } };
            SetItems(items, initial);
            MouseLeftButtonUp += delegate { if (list.Children.Count > 0) popup.IsOpen = !popup.IsOpen; };
        }
        public void SetItems(string[] items, string initial) {
            list.Children.Clear();
            string sel = null;
            if (!string.IsNullOrEmpty(initial)) foreach (string mm in items) if (mm == initial) { sel = initial; break; }
            if (sel == null && items.Length > 0) sel = items[0];
            Value = sel != null ? sel : "";
            foreach (string it in items) {
                string cur = it;
                Border row = new Border { Padding = new Thickness(12, 8, 12, 8), Cursor = Cursors.Hand, Child = new TextBlock { Text = cur, Foreground = Ui.Text, FontSize = 12.5 } };
                row.MouseEnter += delegate { row.Background = Ui.B("#222c4a"); };
                row.MouseLeave += delegate { row.Background = Brushes.Transparent; };
                row.MouseLeftButtonUp += delegate { Value = cur; label.Text = cur; popup.IsOpen = false; };
                list.Children.Add(row);
            }
            label.Text = Value;
        }
    }

    public class App {
        // single-instance plumbing (shared by the installed and portable builds, per-user)
        public const string MutexName = "StarMaster.SingleInstance.v1";
        public const string ActivateEvent = "StarMaster.Activate.v1";
        static System.Threading.Mutex mtx;
        [STAThread]
        static void Main() {
            bool createdNew;
            mtx = new System.Threading.Mutex(true, MutexName, out createdNew);
            if (!createdNew) {                                  // already running: poke the live instance to surface, then bow out
                try { System.Threading.EventWaitHandle.OpenExisting(ActivateEvent).Set(); } catch { }
                return;
            }
            System.Windows.Application app = new System.Windows.Application();
            app.Run(new MainWindow());
            GC.KeepAlive(mtx);
        }
    }
}
