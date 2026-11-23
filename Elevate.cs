using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace StarMaster {
    // ===== Admin rights without a UAC prompt on every start (v93) =====
    // CPU temperature (LibreHardwareMonitor -> the PawnIO driver) and FPS (PresentMon's ETW capture) both need
    // an elevated process. A one-time setup (one UAC prompt) installs PawnIO and registers a scheduled task that
    // runs StarMaster "with highest privileges". The task has NO trigger - it is only ever started on demand:
    // a normal launch of the installed exe (Start menu, the HKCU Run key, the updater's relaunch) runs the task
    // and exits, and the task starts the real, elevated instance. Windows lets the task's owner run it without
    // a prompt; creating it is the part that needs admin.
    //
    // UI-free on purpose (like WholeVersion.cs) so Tests.cs can cover the task XML.
    public static class Elevate {
        public const string TaskName = "StarMaster (admin)";
        public const string ViaTaskArg = "--via-task";
        public const string SetupArg = "--elevated-setup";
        // pinned, hash-checked PawnIO installer (official signed release; downloaded on demand, not redistributed)
        public const string PawnIoUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
        public const string PawnIoSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
        // helper exit codes (bit flags)
        public const int OkPawnIo = 1, OkTask = 2, ErrPawnIo = 4, ErrTask = 8, ErrOtherUser = 16;

        // a process's elevation never changes, so ask Windows once (the UI asks every second)
        static int isAdmin = -1;
        public static bool IsAdmin() {
            if (isAdmin < 0) { try { isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? 1 : 0; } catch { isAdmin = 0; } }
            return isAdmin == 1;
        }

        // PawnIO registers an uninstall key (LibreHardwareMonitor checks the same one) and a kernel service.
        public static bool PawnIoInstalled() {
            try {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO", "DisplayVersion", null);
                if (v != null) return true;
            } catch { }
            try { return Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\PawnIO", "ImagePath", null) != null; } catch { return false; }
        }

        static string Schtasks(string args, out int exit) {
            exit = -1;
            try {
                ProcessStartInfo psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), args);
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi)) {
                    string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } return o; }
                    exit = p.ExitCode; return o;
                }
            } catch (Exception ex) { return ex.Message; }
        }
        public static bool TaskExists() { int e; Schtasks("/query /tn \"" + TaskName + "\"", out e); return e == 0; }
        public static bool RunTask() { int e; Schtasks("/run /tn \"" + TaskName + "\"", out e); return e == 0; }

        // The task definition. No <Triggers> (on demand only). Priority 4 = normal: the scheduler's default (7)
        // is BELOW normal and the 1 s monitor tick would be starved under game load. ExecutionTimeLimit PT0S =
        // never kill it (the default kills a task after 72 hours, and StarMaster lives in the tray for days).
        // Parallel instances: the running elevated copy IS a task instance, so IgnoreNew would swallow the
        // "bring the window forward" relaunch.
        public static string TaskXml(string userSid, string exePath) {
            string dir = Path.GetDirectoryName(exePath);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Author>StarMaster</Author><Description>Starts StarMaster with admin rights (CPU temperature and FPS sensors) without a UAC prompt. Created by StarMaster's one-time sensor setup.</Description></RegistrationInfo>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(userSid) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>4</Priority><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape("\"" + exePath + "\"") + "</Command><Arguments>" + ViaTaskArg + "</Arguments><WorkingDirectory>" + SecurityElement.Escape(dir) + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        // must run elevated
        public static bool CreateTask(string userSid, string exePath) {
            string xml = Path.Combine(Path.GetTempPath(), "StarMaster-task-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".xml");
            try {
                File.WriteAllText(xml, TaskXml(userSid, exePath), Encoding.Unicode);   // schtasks wants UTF-16
                int e; Schtasks("/create /tn \"" + TaskName + "\" /xml \"" + xml + "\" /f", out e);
                return e == 0;
            } catch { return false; } finally { try { File.Delete(xml); } catch { } }
        }

        // ---- the PawnIO installer: downloaded on demand (non-elevated, before the UAC step), hash-checked ----
        public static string PawnIoSetupPath() { return Path.Combine(Path.GetTempPath(), "StarMaster-PawnIO_setup.exe"); }
        public static string LastError = "";
        public static string DownloadPawnIo() {
            string p = PawnIoSetupPath();
            try {
                if (File.Exists(p) && HashOk(p)) return p;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient wc = new WebClient()) { wc.Headers.Add("User-Agent", "StarMaster"); wc.DownloadFile(PawnIoUrl, p); }
                if (HashOk(p)) return p;
                LastError = "the PawnIO download didn't match its checksum"; try { File.Delete(p); } catch { }
            } catch (Exception ex) { LastError = "couldn't download PawnIO (" + ex.Message + ")"; }
            return null;
        }
        public static bool HashOk(string path) {
            try {
                using (SHA256 h = SHA256.Create()) using (FileStream fs = File.OpenRead(path)) {
                    string s = BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "");
                    return string.Equals(s, PawnIoSha256, StringComparison.OrdinalIgnoreCase);
                }
            } catch { return false; }
        }

        // ---- the elevated half of the one-time setup ----
        // Non-elevated side: StarMaster.exe --elevated-setup <user SID> [<PawnIO setup path>] via runas (one UAC).
        // Also callable in-process when we're already elevated.
        public static int RunSetup(string userSid, string pawnSetup, string exePath) {
            int r = 0;
            string me = "";
            try { me = WindowsIdentity.GetCurrent().User.Value; } catch { }
            // a standard user typing an admin's password would elevate as THAT admin: the task would belong to
            // the wrong account and "highest privileges" for the real user is still a limited token - refuse
            if (!string.IsNullOrEmpty(userSid) && !string.Equals(me, userSid, StringComparison.OrdinalIgnoreCase)) return ErrOtherUser;
            if (!PawnIoInstalled()) {
                if (!string.IsNullOrEmpty(pawnSetup) && File.Exists(pawnSetup) && HashOk(pawnSetup)) {
                    try {
                        ProcessStartInfo psi = new ProcessStartInfo(pawnSetup, "-install -silent");
                        psi.UseShellExecute = false; psi.CreateNoWindow = true;
                        using (Process p = Process.Start(psi)) p.WaitForExit(120000);
                    } catch { }
                }
                r |= PawnIoInstalled() ? OkPawnIo : ErrPawnIo;
            } else r |= OkPawnIo;
            r |= CreateTask(string.IsNullOrEmpty(userSid) ? me : userSid, exePath) ? OkTask : ErrTask;
            return r;
        }

        // Launch the elevated helper (one UAC prompt). Returns its exit code, or -1 if the prompt was declined.
        public static int RunSetupElevated(string exePath, string pawnSetup) {
            try {
                string sid = WindowsIdentity.GetCurrent().User.Value;
                ProcessStartInfo psi = new ProcessStartInfo(exePath, SetupArg + " " + sid + (pawnSetup != null ? " \"" + pawnSetup + "\"" : ""));
                psi.UseShellExecute = true; psi.Verb = "runas"; psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
            } catch { return -1; }
        }

        // ---- the hop: arguments for the elevated instance ride along in a small file (schtasks /run takes none) ----
        static string HopFile(string exeDir) { return Path.Combine(exeDir, "hop.txt"); }
        public static void WriteHop(string exeDir, string args) { try { File.WriteAllText(HopFile(exeDir), DateTime.UtcNow.Ticks + "\n" + args); } catch { } }
        // read + delete; only trusted while fresh (a stale file from a hop whose task never started is ignored)
        public static string ReadHop(string exeDir) {
            string f = HopFile(exeDir);
            try {
                if (!File.Exists(f)) return "";
                string[] t = File.ReadAllText(f).Split(new char[] { '\n' }, 2);
                File.Delete(f);
                long ticks; if (t.Length < 2 || !long.TryParse(t[0].Trim(), out ticks)) return "";
                return ParseHopAge(ticks, DateTime.UtcNow.Ticks) ? t[1].Trim() : "";
            } catch { return ""; }
        }
        public static bool ParseHopAge(long written, long now) { long age = now - written; return age >= 0 && age < TimeSpan.FromSeconds(60).Ticks; }

        // Open a URL / document. An elevated app must not start the browser elevated, so hand it to Explorer
        // (the shell runs at the user's normal integrity) instead of ShellExecute-ing it ourselves.
        public static void OpenUrl(string url) {
            try {
                if (IsAdmin()) Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "\"" + url + "\"");
                else Process.Start(url);
            } catch { }
        }
    }
}
