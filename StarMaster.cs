using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
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

// embedded version info -> Task Manager shows the name (FileDescription/Product) + Publisher (Company). Bump AssemblyFileVersion with the release.
[assembly: System.Reflection.AssemblyTitle("StarMaster")]
[assembly: System.Reflection.AssemblyProduct("StarMaster")]
[assembly: System.Reflection.AssemblyDescription("Star Citizen Toolkit")]
[assembly: System.Reflection.AssemblyCompany("Elliot Borst")]
[assembly: System.Reflection.AssemblyCopyright("Elliot Borst")]
[assembly: System.Reflection.AssemblyFileVersion("34.0.0.0")]
[assembly: System.Reflection.AssemblyVersion("34.0.0.0")]

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

    public class Cmd { public string Label = "Command"; public bool Shift, Ctrl, Alt; public string Key = ""; public int Interval = 600; public bool Enabled = true; public DateTime LastFire = DateTime.MinValue; public bool Locked; }

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

    // ===== hardware sampling (driver-free, no game injection): CPU via NtQuerySystemInformation, RAM via GlobalMemoryStatusEx, GPU via NVIDIA NVML =====
    public static class SysMon {
        // ---- CPU per-core (NtQuerySystemInformation: SystemProcessorPerformanceInformation = 8) ----
        [StructLayout(LayoutKind.Sequential)]
        struct PerfInfo { public long Idle; public long Kernel; public long User; public long Dpc; public long Interrupt; public uint InterruptCount; }
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int infoClass, IntPtr buf, int len, out int ret);
        // ---- RAM ----
        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus { public uint Length; public uint MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirt, AvailVirt, AvailExt; }
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GlobalMemoryStatusEx(ref MemStatus s);
        // ---- NVML (ships with the NVIDIA driver; queries the GPU, never touches the game) ----
        [StructLayout(LayoutKind.Sequential)] struct NvUtil { public uint Gpu, Mem; }
        [StructLayout(LayoutKind.Sequential)] struct NvMem { public ulong Total, Free, Used; }
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")] static extern int NvInit();
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern int NvHandle(uint i, out IntPtr dev);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")] static extern int NvUtilRates(IntPtr dev, out NvUtil u);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")] static extern int NvMemInfo(IntPtr dev, out NvMem m);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")] static extern int NvTemp(IntPtr dev, int sensor, out uint t);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")] static extern int NvPower(IntPtr dev, out uint mw);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")] static extern int NvClock(IntPtr dev, int type, out uint mhz);
        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")] static extern int NvName(IntPtr dev, StringBuilder name, uint len);

        public class Sample {
            public double CpuTotal; public double[] Cores = new double[0]; public string CpuName = "CPU"; public int CpuMhz; public int CpuTempC = -1, CpuPowerW = -1;
            public double RamUsedGB, RamTotalGB; public int RamPct;
            public bool GpuOk; public string GpuName = "GPU";
            public int GpuPct, GpuTempC, GpuPowerW, GpuCoreMhz, GpuMemMhz, VramPct;
            public double VramUsedGB, VramTotalGB;
            public int Fps = -1;   // -1 = FPS overlay off / hidden
        }

        static int cores;
        static long[] pIdle, pKernel, pUser;   // previous raw tick counts, for per-tick deltas
        static bool gpuReady; static IntPtr gpuDev = IntPtr.Zero; static string gpuName = "GPU";
        static string cpuName = "CPU";
        // live CPU frequency = base MHz x (% Processor Performance / 100) - captures turbo, driver-free
        static System.Diagnostics.PerformanceCounter perfFreq, perfPerf; static int cpuBaseMhz;

        public static void Init() {
            cores = Environment.ProcessorCount;
            pIdle = new long[cores]; pKernel = new long[cores]; pUser = new long[cores];
            try { object n = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null); if (n != null) cpuName = n.ToString().Trim(); } catch { }
            try {
                perfFreq = new System.Diagnostics.PerformanceCounter("Processor Information", "Processor Frequency", "_Total");
                perfPerf = new System.Diagnostics.PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                cpuBaseMhz = (int)perfFreq.NextValue(); perfPerf.NextValue();   // prime (first read is 0)
            } catch { perfPerf = null; }
            try {
                if (NvInit() == 0 && NvHandle(0, out gpuDev) == 0) {
                    gpuReady = true;
                    StringBuilder nm = new StringBuilder(96); if (NvName(gpuDev, nm, 96) == 0 && nm.Length > 0) gpuName = nm.ToString();
                }
            } catch { gpuReady = false; }
        }

        public static Sample Read() {
            Sample s = new Sample(); s.CpuName = cpuName;
            if (pIdle != null) ReadCpu(s);
            if (perfPerf != null) { try { if (cpuBaseMhz <= 0 && perfFreq != null) cpuBaseMhz = (int)perfFreq.NextValue(); s.CpuMhz = (int)(cpuBaseMhz * perfPerf.NextValue() / 100.0); } catch { } }
            ReadRam(s);
            if (gpuReady) ReadGpu(s);
            return s;
        }

        static void ReadCpu(Sample s) {
            try {
                int sz = Marshal.SizeOf(typeof(PerfInfo));
                IntPtr buf = Marshal.AllocHGlobal(sz * cores);
                try {
                    int ret;
                    if (NtQuerySystemInformation(8, buf, sz * cores, out ret) != 0) return;
                    double[] usage = new double[cores]; double sum = 0;
                    for (int i = 0; i < cores; i++) {
                        PerfInfo pi = (PerfInfo)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * sz), typeof(PerfInfo));
                        long idleD = pi.Idle - pIdle[i];
                        long totD = (pi.Kernel + pi.User) - (pKernel[i] + pUser[i]);
                        double u = totD > 0 ? (100.0 * (totD - idleD) / totD) : 0;
                        if (u < 0) u = 0; if (u > 100) u = 100;
                        usage[i] = u; sum += u;
                        pIdle[i] = pi.Idle; pKernel[i] = pi.Kernel; pUser[i] = pi.User;
                    }
                    s.Cores = usage; s.CpuTotal = cores > 0 ? sum / cores : 0;
                } finally { Marshal.FreeHGlobal(buf); }
            } catch { }
        }

        static void ReadRam(Sample s) {
            try {
                MemStatus m = new MemStatus(); m.Length = (uint)Marshal.SizeOf(typeof(MemStatus));
                if (!GlobalMemoryStatusEx(ref m)) return;
                s.RamTotalGB = m.TotalPhys / 1073741824.0;
                s.RamUsedGB = (m.TotalPhys - m.AvailPhys) / 1073741824.0;
                s.RamPct = (int)m.MemoryLoad;
            } catch { }
        }

        static void ReadGpu(Sample s) {
            try {
                NvUtil u; NvMem mem; uint t, mw, gc, mc;
                s.GpuName = gpuName;
                if (NvUtilRates(gpuDev, out u) == 0) s.GpuPct = (int)u.Gpu;
                if (NvMemInfo(gpuDev, out mem) == 0) { s.VramTotalGB = mem.Total / 1073741824.0; s.VramUsedGB = mem.Used / 1073741824.0; s.VramPct = mem.Total > 0 ? (int)(100UL * mem.Used / mem.Total) : 0; }
                if (NvTemp(gpuDev, 0, out t) == 0) s.GpuTempC = (int)t;
                if (NvPower(gpuDev, out mw) == 0) s.GpuPowerW = (int)(mw / 1000);
                if (NvClock(gpuDev, 0, out gc) == 0) s.GpuCoreMhz = (int)gc;
                if (NvClock(gpuDev, 2, out mc) == 0) s.GpuMemMhz = (int)mc;
                s.GpuOk = true;
            } catch { s.GpuOk = false; }
        }
    }

    // ===== FPS via Intel PresentMon (MIT, open-source). Injection-free (ETW) so it's anti-cheat-safe. =====
    // PresentMon self-elevates (--restart_as_admin = one UAC prompt); StarMaster stays non-elevated and tails the CSV it writes.
    public static class FpsMon {
        const string PmUrl = "https://github.com/GameTechDev/PresentMon/releases/download/v2.4.1/PresentMon-2.4.1-x64.exe";
        const string MsCol = "msBetweenPresents";
        static System.Threading.Thread reader; static volatile bool running; static volatile int fps; static System.Diagnostics.Process proc;
        public static volatile string Status = "";
        public static int Fps { get { return fps; } }
        public static bool Running { get { return running; } }
        public static string PmPath() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarMaster", "PresentMon.exe"); }

        static bool EnsurePm() {
            string p = PmPath();
            if (File.Exists(p)) return true;
            try {
                Status = "Downloading PresentMon...";
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient wc = new TimedWebClient()) { wc.Headers.Add("User-Agent", "StarMaster"); wc.DownloadFile(PmUrl, p); }
                return File.Exists(p);
            } catch (Exception ex) { Status = "PresentMon download failed: " + ex.Message; return false; }
        }

        // call on a background thread (download can block). PresentMon runs hidden (CreateNoWindow) and writes frame data to STDOUT,
        // which we read directly - avoids PresentMon's exclusive lock on --output_file. No elevation needed for a normal game process.
        public static void Start(string processName) {
            Stop();
            if (!EnsurePm()) return;
            try {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(PmPath());
                psi.Arguments = "--stop_existing_session --session_name StarMasterFPS --terminate_on_proc_exit --no_console_stats --v1_metrics --process_name " + processName + " --output_stdout";
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                proc = new System.Diagnostics.Process(); proc.StartInfo = psi;
                proc.Start();
                Status = "Waiting for frames (is Star Citizen running?)...";
                running = true; fps = 0;
                reader = new System.Threading.Thread(ReadLoop); reader.IsBackground = true; reader.Start();
            } catch (Exception ex) { Status = "FPS start failed: " + ex.Message; running = false; }
        }
        public static void Stop() {
            running = false; fps = 0; Status = "";
            try { if (proc != null && !proc.HasExited) proc.Kill(); } catch { }
            try { if (proc != null) proc.Dispose(); } catch { }
            proc = null;
        }

        static void ReadLoop() {
            int msCol = -1; List<double> win = new List<double>();
            try {
                System.IO.StreamReader sr = proc.StandardOutput;
                string line;
                while (running && (line = sr.ReadLine()) != null) {
                    if (line.Length == 0) continue;
                    if (msCol < 0) {
                        if (line.IndexOf(MsCol, StringComparison.OrdinalIgnoreCase) >= 0) { string[] h = line.Split(','); for (int j = 0; j < h.Length; j++) if (h[j].Trim().Equals(MsCol, StringComparison.OrdinalIgnoreCase)) { msCol = j; break; } }
                        continue;
                    }
                    string[] f = line.Split(','); double ms;
                    if (msCol < f.Length && double.TryParse(f[msCol], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out ms) && ms > 0.0001) {
                        win.Add(ms); while (win.Count > 60) win.RemoveAt(0);
                        double sum = 0; for (int i = 0; i < win.Count; i++) sum += win[i];
                        if (sum > 0) fps = (int)(1000.0 * win.Count / sum + 0.5);
                        Status = "running";
                    }
                }
            } catch { }
        }
    }

    // ===== HWiNFO integration: reads CPU temp/watts from HWiNFO's shared memory (it does the ring-0 work; we just read). Optional - blank when HWiNFO isn't running. =====
    public static class HwInfo {
        public const string DownloadUrl = "https://www.hwinfo.com/download/";
        public const int NotInstalled = 0, NotRunning = 1, NoSharedMem = 2, Connected = 3;
        public static int CpuTempC = -1, CpuPowerW = -1;
        public static int State = NotInstalled;
        public static bool Autorun, StartMin, SmEnabled;   // from HWiNFO64.INI
        static string exe; static bool located;

        public static string Exe() { if (!located) Locate(); return exe; }

        static void Locate() {
            located = true;
            try {
                string[] keys = { @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\HWiNFO64_is1" };
                foreach (string k in keys) {
                    object il = Microsoft.Win32.Registry.GetValue(k, "InstallLocation", null);
                    if (il != null) { string p = Path.Combine(il.ToString(), "HWiNFO64.exe"); if (File.Exists(p)) { exe = p; break; } }
                    object di = Microsoft.Win32.Registry.GetValue(k, "DisplayIcon", null);
                    if (di != null) { string p = di.ToString().Split(',')[0].Trim('"'); if (File.Exists(p)) { exe = p; break; } }
                }
                if (exe == null) {
                    string pf = Environment.GetEnvironmentVariable("ProgramW6432"); if (string.IsNullOrEmpty(pf)) pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string p = Path.Combine(pf, "HWiNFO64", "HWiNFO64.exe"); if (File.Exists(p)) exe = p;
                }
                if (exe != null) ReadIni();
            } catch { }
        }
        static void ReadIni() {
            try {
                string ini = Path.Combine(Path.GetDirectoryName(exe), "HWiNFO64.INI");
                if (!File.Exists(ini)) return;
                foreach (string line in File.ReadAllLines(ini)) {
                    string l = line.Trim();
                    if (l.StartsWith("SensorsSM=")) SmEnabled = l.EndsWith("1");
                    else if (l.StartsWith("Autorun=")) Autorun = l.EndsWith("1");
                    else if (l.StartsWith("MinimalizeSensors=")) StartMin = l.EndsWith("1");
                }
            } catch { }
        }
        static bool ProcRunning() {
            try { return System.Diagnostics.Process.GetProcessesByName("HWiNFO64").Length > 0 || System.Diagnostics.Process.GetProcessesByName("HWiNFO32").Length > 0; } catch { return false; }
        }

        // fast (call every tick): read CPU temp/watts from shared memory. Returns true if shared memory is live.
        public static bool ReadSensors() {
            CpuTempC = -1; CpuPowerW = -1;
            try {
                using (MemoryMappedFile mmf = MemoryMappedFile.OpenExisting("Global\\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read))
                using (MemoryMappedViewAccessor a = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)) {
                    int roff = a.ReadInt32(32), rsize = a.ReadInt32(36), rnum = a.ReadInt32(40);   // packed SM2 header
                    if (rsize < 200 || rsize > 8192 || rnum < 1 || rnum > 100000 || roff < 1) { roff = a.ReadInt32(36); rsize = a.ReadInt32(40); rnum = a.ReadInt32(44); }
                    if (rsize < 200 || rsize > 8192 || rnum < 1 || rnum > 100000 || roff < 1) return true;
                    byte[] lbl = new byte[128]; int tRank = 99, pRank = 99;
                    for (int i = 0; i < rnum; i++) {
                        long b = roff + (long)i * rsize;
                        int type = a.ReadInt32(b);                       // 1 = temperature, 5 = power
                        if (type != 1 && type != 5) continue;
                        a.ReadArray(b + 12, lbl, 0, 128);
                        int z = Array.IndexOf(lbl, (byte)0); if (z < 0) z = 128;
                        string lo = Encoding.Default.GetString(lbl, 0, z);
                        if (lo.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        double v = a.ReadDouble(b + 284);               // Value (current); szUnit[16]@268 precedes it
                        if (type == 1 && v > 5 && v < 150) { int r = lo.IndexOf("Tctl") >= 0 ? 0 : (lo.IndexOf("Die") >= 0 ? 1 : 2); if (r < tRank) { tRank = r; CpuTempC = (int)(v + 0.5); } }
                        else if (type == 5 && v > 0.3 && v < 600 && lo.IndexOf("Power") >= 0) { int r = lo.IndexOf("Package") >= 0 ? 0 : (lo.IndexOf("PPT") >= 0 ? 1 : 2); if (r < pRank) { pRank = r; CpuPowerW = (int)(v + 0.5); } }
                    }
                    return true;
                }
            } catch { return false; }   // shared memory not present => HWiNFO not running (or SM off)
        }

        // heavier (call occasionally): classify overall state for the status line + button
        public static void RefreshState(bool smActive) {
            if (!located) Locate();
            if (smActive) { State = Connected; return; }
            bool running = ProcRunning();
            if (exe == null && !running) State = NotInstalled;
            else if (!running) State = NotRunning;
            else State = NoSharedMem;
        }
    }

    // ===== Aurora theme + widgets =====
    static class Ui {
        public static SolidColorBrush B(string hex) { hex = hex.TrimStart('#'); SolidColorBrush b = new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16))); b.Freeze(); return b; }
        public static SolidColorBrush B2(string hex) { hex = hex.TrimStart('#'); SolidColorBrush b = new SolidColorBrush(Color.FromArgb(Convert.ToByte(hex.Substring(0, 2), 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16), Convert.ToByte(hex.Substring(6, 2), 16))); b.Freeze(); return b; }
        public static readonly Brush Warn = B("#ffd34d"), HotO = B("#ff9a3d"), Crit = B("#ff5d5d");   // load tiers
        public static Brush Load(double p) { if (p >= 90) return Crit; if (p >= 80) return HotO; if (p >= 60) return Warn; return Text; }   // usage/mem %: white -> yellow -> orange -> red
        public static Brush LoadT(double t) { if (t >= 85) return Crit; if (t >= 75) return HotO; if (t >= 65) return Warn; return Text; }   // temperature tiers
        public static readonly Brush Bg = B("#0b0e16"), Card = B("#141a2b"), Card2 = B("#171d33"), Line = B("#232a45"), Line2 = B("#323c5e"),
            Text = B("#e6ecfb"), Dim = B("#94a0c2"), Faint = B("#646f93"), Accent = B("#79b0ff"), Accent2 = B("#a9c8ff"),
            Good = B("#5fe0c0"), Ink = B("#0a1228"), Inset = B("#0f1322"), Danger = B("#2a1722"), DangerFg = B("#ff9bb8");
        public static LinearGradientBrush AccentGrad() { LinearGradientBrush g = new LinearGradientBrush(Color.FromRgb(0x22, 0xd3, 0xee), Color.FromRgb(0xa8, 0x55, 0xf7), 0); g.Freeze(); return g; }
        public static readonly FontFamily Font = new FontFamily("Segoe UI"), Mono = new FontFamily("Consolas");
    }

    // small modal to add / edit a keystroke
    public partial class MainWindow : Window {
        public const string Version = "34";
        public const string VersionDate = "2026-06-28";   // bump alongside Version at release time
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
        // system monitor (control card + the over-the-game OSD overlay)
        DispatcherTimer monTimer;
        MonBar monCpuBar, monRamBar, monGpuBar, monVramBar;
        TextBlock monCpuTxt, monRamTxt, monGpuTxt, monVramTxt, monGpuDetail;
        MonWindow monWin; bool monOverlayOn = false, monLocked = false; double monOvX = 60, monOvY = 60;
        int monOvAlpha = 85; string monOvColor = "0a0e18";   // overlay background opacity (0-100, 10% steps) + colour
        bool monFpsOn = false; TextBlock monFpsTxt;
        TextBlock monHwTxt, monHwTip; Border monHwBtn; int hwTick = 99;
        // starstrings
        TextBox ssRoot; Dropdown ssChannel; TextBlock ssInstalled, ssLatest, ssStatus; Border ssDot, ssUpdateBtn; TextBlock ssUpdateLbl;
        StarStrings.Info ssLatestInfo; string ssInstalledBuild = "", ssRootCfg = "", ssChannelCfg = "";
        // tray
        System.Windows.Forms.NotifyIcon trayIcon; bool exiting = false;
        // single-instance: a second launch signals this handle to surface the existing window
        System.Threading.EventWaitHandle singleInstanceEvent;
        // header update button (its own label doubles as the status)
        Border updBtn; TextBlock updBtnLbl; DispatcherTimer updRevertTimer;
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
            // never scrolls: the window resizes to fit and clamps at a minimum that keeps everything visible.
            // WIDE not tall (3-column layout) + capped to the screen work area so the title bar can never end up off-screen.
            Width = 1580; Height = 1000; MinWidth = 1340; MinHeight = 940;
            MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
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
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { SysMon.Init(); });   // NVML init is slow - do it off the UI thread
            monTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; monTimer.Tick += MonTick; monTimer.Start();
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
            if (monOverlayOn) Loaded += delegate { SetOverlay(true); };   // restore the over-the-game overlay if it was on last session
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
            s.Children.Add(FormLbl(edit != null && edit.Locked ? "Label  (built-in - fixed)" : "Label")); TextBox label = FormTb(edit != null ? edit.Label : ""); if (edit != null && edit.Locked) { label.IsReadOnly = true; label.Opacity = 0.6; } s.Children.Add(label);
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
            // 3 columns x 2 rows: the 2x2 of tools on the left, System Monitor as a tall full-height column on the right
            // (keeps the window WIDE not TALL so it always fits on screen). Equal star rows + stretched cards => no empty space.
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            FrameworkElement ka = KeepAliveCard(); Grid.SetRow(ka, 0); Grid.SetColumn(ka, 0); ka.Margin = new Thickness(0, 0, 9, 18); ka.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(ka);
            FrameworkElement bk = BackupCard(); Grid.SetRow(bk, 0); Grid.SetColumn(bk, 1); bk.Margin = new Thickness(9, 0, 9, 18); bk.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(bk);
            FrameworkElement ss = StarStringsCard(); Grid.SetRow(ss, 1); Grid.SetColumn(ss, 0); ss.Margin = new Thickness(0, 0, 9, 0); ss.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(ss);
            FrameworkElement sc = ShaderCacheCard(); Grid.SetRow(sc, 1); Grid.SetColumn(sc, 1); sc.Margin = new Thickness(9, 0, 9, 0); sc.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(sc);
            FrameworkElement mon = SystemMonitorCard(); Grid.SetRow(mon, 0); Grid.SetRowSpan(mon, 2); Grid.SetColumn(mon, 2); mon.Margin = new Thickness(9, 0, 0, 0); mon.VerticalAlignment = VerticalAlignment.Stretch; g.Children.Add(mon);
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

        // ---------- system monitor card (full-width; it's the control panel - the live numbers live in the over-the-game OSD overlay) ----------
        FrameworkElement SystemMonitorCard() {
            StackPanel body; DockPanel head; Border card = CardShell(out body, out head, "▦", "System Monitor", "live overlay over the game - CPU / RAM / GPU (no injection)");
            body.Children.Add(new TextBlock { Text = "The overlay sits on top of Star Citizen (borderless/windowed mode). Drag it where you want it, then turn on Lock Position so clicks pass through into the game. Below is a live preview.", Foreground = Ui.Dim, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 12) });
            // overlay controls (in the body - the narrow column header has no room for them)
            StackPanel ov = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            ov.Children.Add(Toggle(monOverlayOn, delegate (bool v) { monOverlayOn = v; SetOverlay(v); }));
            ov.Children.Add(new TextBlock { Text = "  Show Overlay", Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            body.Children.Add(ov);
            StackPanel lk = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            lk.Children.Add(Toggle(monLocked, delegate (bool v) { monLocked = v; if (monWin != null) monWin.SetLocked(v); }));
            lk.Children.Add(new TextBlock { Text = "  Lock Position", Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            body.Children.Add(lk);
            // FPS via PresentMon (self-elevates -> one UAC prompt). Off by default; not persisted (so no surprise UAC on launch).
            StackPanel fp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            fp.Children.Add(Toggle(monFpsOn, delegate (bool v) { ToggleFps(v); }));
            fp.Children.Add(new TextBlock { Text = "  Show FPS", Foreground = Ui.Text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            body.Children.Add(fp);
            monFpsTxt = new TextBlock { Text = "uses PresentMon - asks for admin once when turned on", Foreground = Ui.Faint, FontSize = 11, FontFamily = Ui.Mono, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
            body.Children.Add(monFpsTxt);
            // overlay background opacity (slider, 10% steps)
            StackPanel opR = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            opR.Children.Add(new TextBlock { Text = "Opacity", Width = 64, Foreground = Ui.Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            TextBlock opVal = new TextBlock { Text = monOvAlpha + "%", Width = 44, Foreground = Ui.Text, FontSize = 12.5, FontFamily = Ui.Mono, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            MonSlider sl = new MonSlider(monOvAlpha, 10, delegate (int v) { monOvAlpha = v; opVal.Text = v + "%"; SetOverlayStyle(); }); sl.VerticalAlignment = VerticalAlignment.Center;
            opR.Children.Add(sl); opR.Children.Add(Sp(10)); opR.Children.Add(opVal);
            body.Children.Add(opR);
            // overlay background colour (swatches)
            StackPanel coR = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            coR.Children.Add(new TextBlock { Text = "Colour", Width = 64, Foreground = Ui.Dim, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            string[] swatches = { "0a0e18", "000000", "10131c", "141a2b", "0a1a14", "1a0e12", "140a1e", "20242e" };
            List<Border> swBorders = new List<Border>();
            foreach (string hx in swatches) {
                string h = hx; bool selNow = string.Equals(h, monOvColor, StringComparison.OrdinalIgnoreCase);
                Border sw = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(6), Background = Ui.B(h), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, BorderBrush = selNow ? Ui.Accent : Ui.Line2, BorderThickness = new Thickness(selNow ? 2 : 1) };
                swBorders.Add(sw);
                sw.MouseLeftButtonUp += delegate { monOvColor = h; SetOverlayStyle(); foreach (Border b in swBorders) { b.BorderBrush = Ui.Line2; b.BorderThickness = new Thickness(1); } sw.BorderBrush = Ui.Accent; sw.BorderThickness = new Thickness(2); };
                coR.Children.Add(sw);
            }
            body.Children.Add(coR);

            body.Children.Add(MetricRow("CPU", out monCpuBar, out monCpuTxt));
            body.Children.Add(MetricRow("RAM", out monRamBar, out monRamTxt));
            body.Children.Add(MetricRow("GPU", out monGpuBar, out monGpuTxt));
            body.Children.Add(MetricRow("VRAM", out monVramBar, out monVramTxt));
            monGpuDetail = new TextBlock { Text = "", Foreground = Ui.Dim, FontSize = 12, FontFamily = Ui.Mono, Margin = new Thickness(46, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(monGpuDetail);
            // HWiNFO status (CPU temp/watts come from it). Shows install/run state + an action button.
            Border hwBox = new Border { Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(10), Background = Ui.Inset, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
            StackPanel hwIn = new StackPanel();
            DockPanel hwTop = new DockPanel { LastChildFill = false };
            monHwBtn = Btn("Get HWiNFO", Ui.Card2, Ui.Text, false, delegate { HwAction(); }); monHwBtn.Padding = new Thickness(12, 6, 12, 6); DockPanel.SetDock(monHwBtn, Dock.Right); hwTop.Children.Add(monHwBtn);
            monHwTxt = new TextBlock { Text = "Checking HWiNFO...", Foreground = Ui.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(monHwTxt, Dock.Left); hwTop.Children.Add(monHwTxt);
            hwIn.Children.Add(hwTop);
            monHwTip = new TextBlock { Text = "", Foreground = Ui.Faint, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            hwIn.Children.Add(monHwTip);
            hwBox.Child = hwIn; body.Children.Add(hwBox);
            return card;
        }
        FrameworkElement MetricRow(string label, out MonBar bar, out TextBlock val) {
            DockPanel d = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            TextBlock l = new TextBlock { Text = label, Foreground = Ui.Dim, FontSize = 12.5, FontFamily = Ui.Mono, Width = 46, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(l, Dock.Left); d.Children.Add(l);
            val = new TextBlock { Text = "--", Foreground = Ui.Text, FontSize = 12.5, FontFamily = Ui.Mono, MinWidth = 165, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(val, Dock.Right); d.Children.Add(val);
            bar = new MonBar(14) { VerticalAlignment = VerticalAlignment.Center };
            d.Children.Add(bar);   // LastChildFill: the bar fills the middle
            return d;
        }
        // show/hide the over-the-game OSD window
        void SetOverlay(bool on) {
            if (on) {
                if (monWin == null) { monWin = new MonWindow(); monWin.Left = monOvX; monWin.Top = monOvY; monWin.Closed += delegate { if (monWin != null) { monOvX = monWin.Left; monOvY = monWin.Top; } monWin = null; }; }
                monWin.Show(); monWin.SetLocked(monLocked); monWin.SetStyle(monOvAlpha, monOvColor);
            } else if (monWin != null) { monOvX = monWin.Left; monOvY = monWin.Top; monWin.Hide(); }
        }
        void SetOverlayStyle() { if (monWin != null) monWin.SetStyle(monOvAlpha, monOvColor); }
        // turn FPS on/off (PresentMon, off the UI thread - download + UAC can block)
        void ToggleFps(bool on) {
            monFpsOn = on;
            if (on) { if (monFpsTxt != null) monFpsTxt.Text = "starting..."; System.Threading.ThreadPool.QueueUserWorkItem(delegate { FpsMon.Start("StarCitizen.exe"); }); }
            else { FpsMon.Stop(); if (monFpsTxt != null) monFpsTxt.Text = "uses PresentMon - asks for admin once when turned on"; }
        }
        // launch/get HWiNFO depending on detected state
        void HwAction() {
            try {
                if (HwInfo.State == HwInfo.NotInstalled) System.Diagnostics.Process.Start(HwInfo.DownloadUrl);
                else { string p = HwInfo.Exe(); if (p != null) System.Diagnostics.Process.Start(p); }   // start it (or bring it up to enable Shared Memory)
            } catch { }
        }
        void UpdateHwUi() {
            if (monHwTxt == null) return;
            TextBlock lbl = (TextBlock)monHwBtn.Child;
            if (HwInfo.State == HwInfo.Connected) {
                monHwTxt.Text = "HWiNFO connected" + (HwInfo.CpuTempC >= 0 ? "  -  CPU " + HwInfo.CpuTempC + " °C" + (HwInfo.CpuPowerW >= 0 ? " · " + HwInfo.CpuPowerW + " W" : "") : "");
                monHwTxt.Foreground = Ui.Good; monHwBtn.Visibility = Visibility.Collapsed;
                bool tipNeeded = !HwInfo.Autorun || !HwInfo.StartMin;
                monHwTip.Text = tipNeeded ? "Tip: in HWiNFO enable Auto Start + Minimize on startup so it's always ready." : "";
                monHwTip.Visibility = tipNeeded ? Visibility.Visible : Visibility.Collapsed;
            } else {
                monHwBtn.Visibility = Visibility.Visible; monHwTxt.Foreground = Ui.Dim; monHwTip.Visibility = Visibility.Collapsed;
                if (HwInfo.State == HwInfo.NotInstalled) { monHwTxt.Text = "HWiNFO not installed (needed for CPU temp / watts)."; lbl.Text = "Get HWiNFO"; }
                else if (HwInfo.State == HwInfo.NotRunning) { monHwTxt.Text = "HWiNFO is installed but not running."; lbl.Text = "Start HWiNFO"; monHwTip.Text = (!HwInfo.Autorun || !HwInfo.StartMin) ? "Tip: enable Auto Start + Minimize on startup in HWiNFO." : ""; monHwTip.Visibility = monHwTip.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
                else { monHwTxt.Text = "HWiNFO running, but Shared Memory is off."; lbl.Text = "Open HWiNFO"; monHwTip.Text = "Enable 'Shared Memory Support' in HWiNFO Settings, then it'll connect."; monHwTip.Visibility = Visibility.Visible; }
            }
        }
        void MonTick(object s, EventArgs e) {
            bool overlay = monWin != null && monOverlayOn;
            if (!IsVisible && !overlay) return;   // nothing on screen - skip the sample
            SysMon.Sample smp = SysMon.Read();
            smp.Fps = monFpsOn ? FpsMon.Fps : -1;
            bool sm = HwInfo.ReadSensors(); smp.CpuTempC = HwInfo.CpuTempC; smp.CpuPowerW = HwInfo.CpuPowerW;
            if (++hwTick >= 5) { hwTick = 0; HwInfo.RefreshState(sm); }   // heavier state check every ~5s
            if (IsVisible) {
                if (hwTick == 0) UpdateHwUi();
                int cpu = (int)(smp.CpuTotal + 0.5);
                monCpuBar.Set(smp.CpuTotal); monCpuTxt.Text = cpu + " %" + (smp.CpuTempC >= 0 ? "   " + smp.CpuTempC + " °C" : "") + (smp.CpuPowerW >= 0 ? "   " + smp.CpuPowerW + " W" : "") + (smp.CpuMhz > 0 ? "   " + smp.CpuMhz + " MHz" : "");
                if (monFpsOn && FpsMon.Status.Length > 0) monFpsTxt.Text = FpsMon.Status + (FpsMon.Fps > 0 ? "  -  " + FpsMon.Fps + " FPS" : "");
                monRamBar.Set(smp.RamPct); monRamTxt.Text = smp.RamUsedGB.ToString("0.0") + " / " + smp.RamTotalGB.ToString("0") + " GB (" + smp.RamPct + "%)";
                if (smp.GpuOk) {
                    monGpuBar.Set(smp.GpuPct); monGpuTxt.Text = smp.GpuPct + " %";
                    monVramBar.Set(smp.VramPct); monVramTxt.Text = smp.VramUsedGB.ToString("0.0") + " / " + smp.VramTotalGB.ToString("0") + " GB (" + smp.VramPct + "%)";
                    monGpuDetail.Text = smp.GpuName + "   " + smp.GpuTempC + " °C   " + smp.GpuPowerW + " W   " + smp.GpuCoreMhz + " MHz core   " + smp.GpuMemMhz + " MHz mem";
                } else {
                    monGpuBar.Set(0); monGpuTxt.Text = "n/a"; monVramBar.Set(0); monVramTxt.Text = "n/a";
                    monGpuDetail.Text = "No NVIDIA GPU detected (NVML unavailable) - GPU stats need an NVIDIA card.";
                }
            }
            if (overlay) monWin.Update(smp);
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
            if (c.Locked) {
                // built-in default: no delete button, just a lock glyph so it's clear why
                right.Children.Add(new TextBlock { Text = "🔒", Foreground = Ui.Faint, FontSize = 12, Width = 22, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Built-in keystroke (can't be removed)" });
            } else {
                Border del = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Background = Ui.Card2, Child = new TextBlock { Text = "✕", Foreground = Ui.Dim, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                del.MouseLeftButtonUp += delegate { commands.Remove(cc); RefreshCommands(); }; right.Children.Add(del);
            }
            DockPanel.SetDock(right, Dock.Right); d.Children.Add(right);
            row.Child = d; return row;
        }
        void AddKey() { ShowKeyForm(null, delegate (Cmd r) { commands.Add(r); RefreshCommands(); }); }
        void EditKey(Cmd c) { ShowKeyForm(c, delegate (Cmd r) { int i = commands.IndexOf(c); r.Enabled = c.Enabled; r.LastFire = c.LastFire; r.Locked = c.Locked; if (c.Locked) r.Label = c.Label; commands[i] = r; RefreshCommands(); }); }
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
            StackPanel r2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            cpFrom = new Dropdown(new string[] { "LIVE (current)" }, "LIVE (current)", 150); r2.Children.Add(cpFrom);
            r2.Children.Add(new TextBlock { Text = "  →  ", Foreground = Ui.Accent, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            cpTo = new Dropdown(new string[] { "HOTFIX" }, "HOTFIX", 110); r2.Children.Add(cpTo);
            body.Children.Add(r2);
            // button on its own line so it never overflows the (now narrower) card
            Border cp = Btn("Copy / Restore", Ui.Card2, Ui.Text, false, delegate { DoCopy(); }); cp.Padding = new Thickness(15, 8, 15, 8); cp.HorizontalAlignment = HorizontalAlignment.Left; cp.Margin = new Thickness(0, 0, 0, 12); body.Children.Add(cp);
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
        // after a successful "up to date" check, revert the label to the default prompt after 30 s
        void RevertUpdBtnAfter(int seconds) {
            if (updRevertTimer != null) updRevertTimer.Stop();
            updRevertTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            updRevertTimer.Tick += delegate { updRevertTimer.Stop(); SetUpdBtn("↻  Check for updates", Ui.Text); };
            updRevertTimer.Start();
        }
        void CheckUpdate(bool auto) {
            // auto (launch) checks back off if one ran in the last 5 minutes - avoids hammering GitHub's anon rate limit
            if (auto && (DateTime.Now - lastUpdateCheck).TotalMinutes < 5) return;
            if (updRevertTimer != null) updRevertTimer.Stop();
            SetUpdBtn("↻  Checking...", Ui.Dim);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                Updater.Info info = Updater.CheckLatest();
                lastUpdateCheck = DateTime.Now;
                Dispatcher.BeginInvoke(new Action(delegate {
                    if (info == null) SetUpdBtn("⚠  " + (Updater.LastError.Length > 0 ? Updater.LastError : "Check failed"), Ui.DangerFg);
                    else if (Updater.Compare(info.Version, CurrentVer) > 0) { SetUpdBtn("↑  Update available", Ui.Accent); ShowUpdateBanner(info); }
                    else { SetUpdBtn("✓  Up to date", Ui.Good); RevertUpdBtnAfter(30); }
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
            if (!exiting && trayIcon != null && trayIcon.Icon != null && trayIcon.Visible) { e.Cancel = true; Hide(); return; }   // overlay keeps running while we're in the tray
            timer.Stop(); if (monTimer != null) monTimer.Stop(); FpsMon.Stop(); if (monWin != null) { monWin.Close(); monWin = null; }
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
        }

        // ---------- config ----------
        void LoadConfig() {
            commands = new List<Cmd>(); autostart = false; focusGuard = true; startMinimized = false; winTitleField = "Star Citizen";
            try {
                if (File.Exists(cfgPath)) foreach (string line in File.ReadAllLines(cfgPath)) {
                    string ln = line.Trim(); if (ln.Length == 0 || ln.StartsWith("#")) continue;
                    if (ln.IndexOf('|') >= 0) { string[] f = ln.Split('|'); if (f.Length >= 7) { Cmd c = new Cmd(); c.Label = f[0]; c.Shift = f[1] == "1"; c.Ctrl = f[2] == "1"; c.Alt = f[3] == "1"; c.Key = f[4]; int iv; int.TryParse(f[5], out iv); c.Interval = iv < 1 ? 1 : (iv > 3600 ? 3600 : iv); c.Enabled = f[6] == "1"; commands.Add(c); } }
                    else if (ln.IndexOf('=') > 0) { string[] kv = ln.Split(new char[] { '=' }, 2); string k = kv[0].Trim().ToLower(), v = kv[1].Trim(); if (k == "autostart") autostart = v == "1"; else if (k == "focusguard") focusGuard = v == "1"; else if (k == "startminimized") startMinimized = v == "1"; else if (k == "wintitle") winTitleField = v; else if (k == "starstrings_build") ssInstalledBuild = v; else if (k == "starstrings_root") ssRootCfg = v; else if (k == "starstrings_channel") ssChannelCfg = v; else if (k == "lastcheck") { long t; if (long.TryParse(v, out t) && t > 0 && t <= DateTime.MaxValue.Ticks) lastUpdateCheck = new DateTime(t); } else if (k == "mon_overlay") monOverlayOn = v == "1"; else if (k == "mon_lock") monLocked = v == "1"; else if (k == "mon_ovx") { int x; if (int.TryParse(v, out x)) monOvX = x; } else if (k == "mon_ovy") { int y; if (int.TryParse(v, out y)) monOvY = y; } else if (k == "mon_ovalpha") { int a; if (int.TryParse(v, out a)) monOvAlpha = a; } else if (k == "mon_ovcolor") { if (v.Length > 0) monOvColor = v; } }
                }
            } catch { }
            // Always-present locked defaults: back-fill any that are missing (incl. for users upgrading from a pre-v4 config that only had Wipe Visor) and mark existing ones locked so they can't be deleted.
            foreach (Cmd def in DefaultCmds()) {
                Cmd existing = null;
                foreach (Cmd c in commands) if (string.Equals(c.Label, def.Label, StringComparison.OrdinalIgnoreCase)) { existing = c; break; }
                if (existing == null) commands.Add(def); else existing.Locked = true;
            }
        }
        // The built-in keystrokes that every user gets and cannot delete. Enabled-state and key/interval stay user-editable; the label is fixed (so back-fill matching stays stable).
        static Cmd[] DefaultCmds() {
            return new Cmd[] {
                new Cmd { Label = "Wipe Visor", Alt = true, Key = "X", Interval = 600, Enabled = true, Locked = true },
                new Cmd { Label = "Auto Accept", Key = "[", Interval = 1, Enabled = false, Locked = true },
            };
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
                if (monWin != null) { monOvX = monWin.Left; monOvY = monWin.Top; }
                sb.AppendLine("mon_overlay=" + (monOverlayOn ? "1" : "0"));
                sb.AppendLine("mon_lock=" + (monLocked ? "1" : "0"));
                sb.AppendLine("mon_ovx=" + (int)monOvX);
                sb.AppendLine("mon_ovy=" + (int)monOvY);
                sb.AppendLine("mon_ovalpha=" + monOvAlpha);
                sb.AppendLine("mon_ovcolor=" + monOvColor);
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

    // horizontal load bar: a rounded track with a star-sized fill column (auto-scales to its container width)
    public class MonBar : Border {
        ColumnDefinition fillCol, restCol; Border fill;
        public MonBar(double height) {
            Height = height; CornerRadius = new CornerRadius(height / 2); Background = Ui.Inset; BorderBrush = Ui.Line; BorderThickness = new Thickness(1); ClipToBounds = true;
            Grid g = new Grid();
            fillCol = new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) };
            restCol = new ColumnDefinition { Width = new GridLength(100, GridUnitType.Star) };
            g.ColumnDefinitions.Add(fillCol); g.ColumnDefinitions.Add(restCol);
            fill = new Border { Background = Ui.Good, CornerRadius = new CornerRadius(height / 2) }; Grid.SetColumn(fill, 0); g.Children.Add(fill);
            Child = g;
        }
        public void Set(double pct) { if (pct < 0) pct = 0; if (pct > 100) pct = 100; fillCol.Width = new GridLength(pct, GridUnitType.Star); restCol.Width = new GridLength(100 - pct, GridUnitType.Star); fill.Background = Ui.Load(pct); }
    }

    // a small dark slider that snaps to `step`-sized increments (used for overlay opacity 0-100 in 10% steps)
    public class MonSlider : Canvas {
        Border fill, knob; double w = 170, h = 22; int step; Action<int> cb; bool drag; int val;
        public MonSlider(int initial, int step, Action<int> cb) {
            this.step = step; this.cb = cb; Width = w; Height = h; Background = Brushes.Transparent; Cursor = Cursors.Hand;
            Border track = new Border { Width = w, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Inset, BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
            Canvas.SetTop(track, (h - 6) / 2); Canvas.SetLeft(track, 0); Children.Add(track);
            fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Accent };
            Canvas.SetTop(fill, (h - 6) / 2); Canvas.SetLeft(fill, 0); Children.Add(fill);
            knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Ui.B("#cdd6de"), BorderBrush = Ui.Line2, BorderThickness = new Thickness(1) };
            Canvas.SetTop(knob, (h - 16) / 2); Children.Add(knob);
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { drag = true; CaptureMouse(); ApplyX(e.GetPosition(this).X); };
            MouseMove += delegate (object o, MouseEventArgs e) { if (drag) ApplyX(e.GetPosition(this).X); };
            MouseLeftButtonUp += delegate (object o, MouseButtonEventArgs e) { drag = false; ReleaseMouseCapture(); };
            Apply(initial, false);
        }
        void ApplyX(double x) { Apply((int)System.Math.Round(x / w * 100), true); }
        void Apply(int pct, bool fire) {
            if (pct < 0) pct = 0; if (pct > 100) pct = 100;
            val = (int)System.Math.Round(pct / (double)step) * step;
            double fw = w * val / 100.0;
            fill.Width = fw < 0.5 ? 0.5 : fw;
            double kx = fw - 8; if (kx < 0) kx = 0; if (kx > w - 16) kx = w - 16; Canvas.SetLeft(knob, kx);
            if (fire && cb != null) cb(val);
        }
        public int Val { get { return val; } }
    }

    // the over-the-game OSD: a borderless, always-on-top, transparent window. Draggable; "lock" makes it click-through (WS_EX_TRANSPARENT) so clicks pass to the game.
    public class MonWindow : Window {
        TextBlock fpsLine; TextBlock[] cpuC, gpuC; bool locked; Border box;
        public MonWindow() {
            Title = "StarMaster Overlay";   // so Task Manager shows a name for this window
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
            ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; WindowStartupLocation = WindowStartupLocation.Manual;
            box = new Border { Background = Ui.B2("#d80a0e18"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 11, 16, 12), BorderBrush = Ui.Line2, BorderThickness = new Thickness(1) };
            StackPanel s = new StackPanel();
            fpsLine = new TextBlock { FontFamily = Ui.Mono, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Ui.B("#ff5bd6"), Margin = new Thickness(0, 0, 0, 3), Visibility = Visibility.Collapsed };
            s.Children.Add(fpsLine);
            // 2 rows (CPU+RAM, GPU+VRAM) x 6 columns: Name | % | C | MEM | W | MHz. Auto columns => CPU and GPU line up; each cell coloured independently.
            Grid g = new Grid();
            for (int c = 0; c < 6; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition());
            cpuC = new TextBlock[6]; gpuC = new TextBlock[6];
            for (int c = 0; c < 6; c++) {
                cpuC[c] = Cell(c == 0); Grid.SetRow(cpuC[c], 0); Grid.SetColumn(cpuC[c], c); g.Children.Add(cpuC[c]);
                gpuC[c] = Cell(c == 0); Grid.SetRow(gpuC[c], 1); Grid.SetColumn(gpuC[c], c); g.Children.Add(gpuC[c]);
            }
            s.Children.Add(g);
            box.Child = s; Content = box;
            MouseLeftButtonDown += delegate (object o, MouseButtonEventArgs e) { if (!locked && e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } } };
            SourceInitialized += delegate { Apply(); };
        }
        static TextBlock Cell(bool name) {
            return new TextBlock { FontFamily = Ui.Mono, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Text,
                TextAlignment = name ? TextAlignment.Left : TextAlignment.Right, HorizontalAlignment = name ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                Margin = new Thickness(name ? 0 : 16, 1, 0, 1) };
        }
        public void Update(SysMon.Sample s) {
            if (s.Fps >= 0) { fpsLine.Text = "FPS  " + (s.Fps > 0 ? s.Fps.ToString() : "--"); fpsLine.Visibility = Visibility.Visible; } else fpsLine.Visibility = Visibility.Collapsed;
            Set(cpuC, CleanName(s.CpuName), (int)(s.CpuTotal + 0.5), s.CpuTempC, s.RamUsedGB, s.RamTotalGB, s.RamPct, s.CpuPowerW, s.CpuMhz);
            if (s.GpuOk) Set(gpuC, CleanName(s.GpuName), s.GpuPct, s.GpuTempC, s.VramUsedGB, s.VramTotalGB, s.VramPct, s.GpuPowerW, s.GpuCoreMhz);
            else Set(gpuC, CleanName(s.GpuName), -1, -1, 0, 0, -1, -1, 0);
        }
        // fill one row. Only %, temp and MEM change colour (load tiers); name/W/MHz stay white.
        static void Set(TextBlock[] c, string name, int pct, int temp, double memUsed, double memTot, int memPct, int watt, int mhz) {
            c[0].Text = name;
            c[1].Text = pct >= 0 ? pct + "%" : "n/a"; c[1].Foreground = pct >= 0 ? Ui.Load(pct) : Ui.Text;
            c[2].Text = temp >= 0 ? temp + "°C" : ""; c[2].Foreground = Ui.LoadT(temp);
            c[3].Text = memTot > 0 ? memUsed.ToString("0.0") + "/" + memTot.ToString("0") + "GB" : ""; c[3].Foreground = memPct >= 0 ? Ui.Load(memPct) : Ui.Text;
            c[4].Text = watt >= 0 ? watt + "W" : "";
            c[5].Text = mhz > 0 ? mhz + "MHz" : "";
        }
        // keep the brand, trim only redundant words: "NVIDIA GeForce RTX 5090" -> "NVIDIA RTX 5090", "AMD Ryzen 7 9800X3D 8-Core Processor" -> "AMD Ryzen 7 9800X3D"
        static string CleanName(string s) {
            if (s == null) return "";
            s = s.Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "").Replace("(tm)", "").Replace("GeForce ", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s*\\d+-Core Processor", "");
            s = s.Replace(" Processor", "").Replace(" CPU", "");
            return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
        }
        public void SetStyle(int alphaPct, string rgb) {
            try {
                if (alphaPct < 0) alphaPct = 0; if (alphaPct > 100) alphaPct = 100;
                byte a = (byte)(alphaPct * 255 / 100);
                byte hit = a < 1 ? (byte)1 : a;   // keep the background just barely opaque so it stays draggable at 0%
                box.Background = Ui.B2(hit.ToString("x2") + (string.IsNullOrEmpty(rgb) ? "0a0e18" : rgb.TrimStart('#')));
                Color lc = ((SolidColorBrush)Ui.Line2).Color;   // fade the outline with the same opacity
                box.BorderBrush = new SolidColorBrush(Color.FromArgb(a, lc.R, lc.G, lc.B));
            } catch { }
        }
        public void SetLocked(bool v) { locked = v; Apply(); }
        void Apply() {
            try {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return;
                int ex = GetWindowLong(h, -20);                       // GWL_EXSTYLE; toggle WS_EX_TRANSPARENT only (WPF manages WS_EX_LAYERED)
                if (locked) ex |= 0x20; else ex &= ~0x20;
                SetWindowLong(h, -20, ex);
            } catch { }
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr h, int n);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr h, int n, int v);
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
