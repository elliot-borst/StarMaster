using System;
using LibreHardwareMonitor.Hardware;

namespace StarMaster {
    // ===== CPU temperature + package watts via LibreHardwareMonitor (v93; replaces the HWiNFO shared-memory reader) =====
    // LibreHardwareMonitorLib 0.9.6 (MPL-2.0, vendored in lib\ - no NuGet at build time) reads the CPU through the
    // PawnIO kernel driver, so it only returns real values when StarMaster runs elevated AND PawnIO is installed;
    // otherwise its sensors read 0, which we treat as "no reading" (-1 -> "--" on the overlay).
    // CPU only: GPU/RAM/VRAM/CPU% keep their driver-free sources (NVML / Windows), and IsMemoryEnabled would add
    // ~5 s of SMBus probing of the RAM sticks' SPD chips for numbers we don't show.
    // status flags in an LHM-free class, so the UI can read them even when lib\ is missing (touching CpuSensors
    // would need LibreHardwareMonitorLib.dll loaded)
    public static class SensorState {
        public static volatile bool Ready, Failed;
        public static volatile string Error = "";
    }
    public static class CpuSensors {
        static readonly object gate = new object();
        static Computer pc;
        static IHardware cpu;


        // (the DLLs load from <exe dir>\lib via App.HookLibResolve, hooked first thing in Main - not here, so
        // that hooking it never makes the runtime load this class and its LHM-typed fields)

        // off the UI thread. Safe to call again (after PawnIO gets installed) - it reopens.
        public static void Init() {
            try { InitCore(); } catch (Exception ex) { SensorState.Failed = true; SensorState.Error = ex.GetType().Name + ": " + ex.Message; }
        }
        static void InitCore() {
            lock (gate) {
                SensorState.Ready = false; SensorState.Failed = false; SensorState.Error = "";
                if (pc != null) { try { pc.Close(); } catch { } pc = null; cpu = null; }
                Computer c = new Computer { IsCpuEnabled = true };
                c.Open();
                foreach (IHardware h in c.Hardware) if (h.HardwareType == HardwareType.Cpu) { cpu = h; break; }
                pc = c; SensorState.Ready = true;
            }
        }
        public static void Close() {
            lock (gate) { SensorState.Ready = false; if (pc != null) { try { pc.Close(); } catch { } pc = null; cpu = null; } }
        }

        // ~1.5 ms; once per monitor tick. -1 = no reading.
        public static void Read(out int tempC, out int watts) {
            tempC = -1; watts = -1;
            if (!SensorState.Ready) return;
            lock (gate) {
                if (cpu == null) return;
                try {
                    cpu.Update();
                    ISensor best = null, hottest = null, pkg = null;
                    foreach (ISensor s in cpu.Sensors) {
                        if (s.SensorType == SensorType.Temperature) {
                            if (s.Name == "Core (Tctl/Tdie)" || s.Name == "CPU Package") best = s;   // AMD / Intel
                            if (s.Value.HasValue && (hottest == null || s.Value.Value > hottest.Value.Value)) hottest = s;
                        } else if (s.SensorType == SensorType.Power && s.Name == "Package") pkg = s;
                    }
                    if (best == null || !best.Value.HasValue) best = hottest;
                    if (best != null && best.Value.HasValue && best.Value.Value > 1f && best.Value.Value < 150f) tempC = (int)(best.Value.Value + 0.5f);
                    if (pkg != null && pkg.Value.HasValue && pkg.Value.Value > 0.3f && pkg.Value.Value < 1000f) watts = (int)(pkg.Value.Value + 0.5f);
                } catch { }
            }
        }
    }
}
