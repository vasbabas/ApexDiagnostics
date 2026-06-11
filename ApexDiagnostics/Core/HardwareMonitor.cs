using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace ApexDiagnostics.Core
{
    /// <summary>
    /// Wraps LibreHardwareMonitor for real CPU temps, clocks, power, fan RPM, and disk temps.
    /// All sensor data is sourced exclusively from LHM — no fake WMI thermal estimations for primary data.
    /// Falls back to WMI only for clock speeds when LHM is unavailable (e.g., restricted WinPE environment).
    /// </summary>
    public class HardwareMonitor : IDisposable
    {
        private LibreHardwareMonitor.Hardware.Computer? _computer;
        private bool _initialized;
        private readonly object _lock = new();
        private int _throttleCount = 0;
        private const int ThrottleHysteresis = 3; // require 3 cycles to toggle
        private readonly Dictionary<string, List<double>> _wmiTempHistories = new();
        private readonly HashSet<string> _staticZonesLogged = new();

        // ── CPU Thermal ──
        public double CpuPackageTemp   { get; private set; }
        public double[] CpuCoreTemps   { get; private set; } = Array.Empty<double>();
        public double CpuPackageWatts  { get; private set; }
        public double CpuCoresWatts    { get; private set; }

        // ── CPU Clocks ──
        public double[] CpuCoreClocks  { get; private set; } = Array.Empty<double>();
        public double CpuBusClock      { get; private set; }
        public double MaxBoostClock    { get; private set; }
        public double CpuLoad          { get; private set; }
        public bool IsThrottling       { get; private set; }
        public string ThrottlingReason { get; private set; } = "NOMINAL";

        // ── Motherboard / Fans ──
        public double[] FanRpms        { get; private set; } = Array.Empty<double>();
        public string[] FanNames       { get; private set; } = Array.Empty<string>();
        public double[] MbTemps        { get; private set; } = Array.Empty<double>();
        public string[] MbTempNames    { get; private set; } = Array.Empty<string>();

        // ── Disk Temperatures (via LHM) ──
        public double[] DiskTemps      { get; private set; } = Array.Empty<double>();
        public string[] DiskTempNames  { get; private set; } = Array.Empty<string>();

        // ── Instruction sets (cached once) ──
        public bool HasAVX    { get; private set; }
        public bool HasAVX2   { get; private set; }
        public bool HasAVX512 { get; private set; }
        public bool HasSSE42  { get; private set; }
        public string InstructionSetsString { get; private set; } = "";

        public bool UsesLHM => _initialized;

        public HardwareMonitor() => DetectInstructionSets();

        public void Initialize()
        {
            try
            {
                _computer = new LibreHardwareMonitor.Hardware.Computer
                {
                    IsCpuEnabled         = true,
                    IsMotherboardEnabled  = true,
                    IsStorageEnabled      = true,   // disk temps via LHM
                    IsControllerEnabled   = true,
                };
                _computer.Open();
                _initialized = true;
                Logger.Log("LibreHardwareMonitor initialized (CPU+MB+Storage sensors active).");

                // Cache max boost from WMI once
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                    foreach (var item in s.Get())
                    { MaxBoostClock = Convert.ToDouble(item["MaxClockSpeed"] ?? 0); break; }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Logger.Log($"LHM init failed: {ex.Message}. Falling back to WMI for clocks.", "WARN");
                _initialized = false;
            }
        }

        public void Update()
        {
            if (_initialized && _computer != null)
                UpdateFromLHM();
            else
                UpdateWmiFallback();
        }

        private void UpdateFromLHM()
        {
            lock (_lock)
            {
                try
                {
                    var coreTemps  = new List<double>();
                    var coreClocks = new List<double>();
                    var fanRpms    = new List<double>();
                    var fanNames   = new List<string>();
                    var mbTemps    = new List<double>();
                    var mbNames    = new List<string>();
                    var diskTemps  = new List<double>();
                    var diskNames  = new List<string>();

                    double pkgTemp = 0, pkgW = 0, coreW = 0, bus = 0, pkgLoad = 0;

                    foreach (var hw in _computer!.Hardware)
                    {
                        hw.Update();
                        foreach (var sub in hw.SubHardware) sub.Update();

                        switch (hw.HardwareType)
                        {
                            case LibreHardwareMonitor.Hardware.HardwareType.Cpu:
                                foreach (var s in hw.Sensors)
                                {
                                    if (s.Value == null) continue;
                                    double v = (double)s.Value;
                                    switch (s.SensorType)
                                    {
                                        case LibreHardwareMonitor.Hardware.SensorType.Temperature:
                                            if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                                s.Name.Contains("Tctl",    StringComparison.OrdinalIgnoreCase) ||
                                                s.Name.Contains("Tdie",    StringComparison.OrdinalIgnoreCase))
                                                pkgTemp = v;
                                            else if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                                                     !s.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
                                                coreTemps.Add(v);
                                            break;
                                        case LibreHardwareMonitor.Hardware.SensorType.Power:
                                            if (s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase)) pkgW  = v;
                                            else if (s.Name.Contains("Cores", StringComparison.OrdinalIgnoreCase)) coreW = v;
                                            break;
                                        case LibreHardwareMonitor.Hardware.SensorType.Load:
                                            if (s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) pkgLoad = v;
                                            break;
                                        case LibreHardwareMonitor.Hardware.SensorType.Clock:
                                            if (s.Name.Contains("Bus",  StringComparison.OrdinalIgnoreCase)) bus = v;
                                            else if (s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) coreClocks.Add(v);
                                            break;
                                    }
                                }
                                break;

                            case LibreHardwareMonitor.Hardware.HardwareType.Motherboard:
                            case LibreHardwareMonitor.Hardware.HardwareType.SuperIO:
                                // Fans and MB temps live in sub-hardware
                                foreach (var sub in hw.SubHardware)
                                {
                                    sub.Update();
                                    foreach (var s in sub.Sensors)
                                    {
                                        if (s.Value == null) continue;
                                        if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan)
                                        { fanRpms.Add((double)s.Value); fanNames.Add(s.Name); }
                                        else if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature)
                                        { mbTemps.Add((double)s.Value); mbNames.Add(s.Name); }
                                    }
                                }
                                // Also check hardware-level sensors
                                foreach (var s in hw.Sensors)
                                {
                                    if (s.Value == null) continue;
                                    if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Fan)
                                    { fanRpms.Add((double)s.Value); fanNames.Add(s.Name); }
                                }
                                break;

                            case LibreHardwareMonitor.Hardware.HardwareType.Storage:
                                foreach (var s in hw.Sensors)
                                {
                                    if (s.SensorType == LibreHardwareMonitor.Hardware.SensorType.Temperature &&
                                        s.Value != null)
                                    { diskTemps.Add((double)s.Value); diskNames.Add(hw.Name); }
                                }
                                break;
                        }
                    }

                    if (pkgTemp <= 0 && coreTemps.Count > 0) pkgTemp = coreTemps.Max();

                    CpuPackageTemp  = Math.Round(pkgTemp, 1);
                    CpuCoreTemps    = coreTemps.Select(t => Math.Round(t, 1)).ToArray();
                    CpuPackageWatts = Math.Round(pkgW,  1);
                    CpuCoresWatts   = Math.Round(coreW, 1);
                    CpuCoreClocks   = coreClocks.Select(c => Math.Round(c, 0)).ToArray();
                    CpuBusClock     = Math.Round(bus, 1);
                    CpuLoad         = Math.Round(pkgLoad, 1);
                    FanRpms         = fanRpms.ToArray();
                    FanNames        = fanNames.ToArray();
                    MbTemps         = mbTemps.Select(t => Math.Round(t, 1)).ToArray();
                    MbTempNames     = mbNames.ToArray();
                    DiskTemps       = diskTemps.Select(t => Math.Round(t, 1)).ToArray();
                    DiskTempNames   = diskNames.ToArray();

                    if (MaxBoostClock > 0 && coreClocks.Count > 0)
                    {
                        double avgClock = coreClocks.Average();
                        bool thermalCritical = CpuPackageTemp > 95;
                        bool clockLow = avgClock < MaxBoostClock * 0.80;
                        bool underLoad = CpuLoad > 50; 

                        if (thermalCritical)
                        {
                            _throttleCount = Math.Min(ThrottleHysteresis, _throttleCount + 1);
                            ThrottlingReason = "CRITICAL TEMPERATURE (THROTTLING)";
                        }
                        else if (clockLow && underLoad)
                        {
                            _throttleCount = Math.Min(ThrottleHysteresis, _throttleCount + 1);
                            ThrottlingReason = "CLOCK FREQUENCY DROP (POWER/THERMAL)";
                        }
                        else if (clockLow && !underLoad)
                        {
                            _throttleCount = 0; 
                            ThrottlingReason = "POWER SAVING / IDLE SCALE";
                        }
                        else
                        {
                            _throttleCount = Math.Max(0, _throttleCount - 1);
                        }

                        IsThrottling = _throttleCount >= ThrottleHysteresis;
                        if (!IsThrottling && ThrottlingReason != "POWER SAVING / IDLE SCALE") 
                            ThrottlingReason = "NOMINAL";
                    }
                }
                catch (Exception ex) { Logger.Log($"LHM update error: {ex.Message}", "WARN"); }
            }
        }

        private void UpdateWmiFallback()
        {
            lock (_lock)
            {
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT CurrentClockSpeed,MaxClockSpeed,LoadPercentage FROM Win32_Processor");
                    foreach (var item in s.Get())
                    {
                        double cur = Convert.ToDouble(item["CurrentClockSpeed"] ?? 0);
                        MaxBoostClock = Convert.ToDouble(item["MaxClockSpeed"] ?? 0);
                        CpuLoad = Convert.ToDouble(item["LoadPercentage"] ?? 0);
                        CpuCoreClocks = new[] { cur };

                        if (MaxBoostClock > 0)
                        {
                            bool clockLow = cur < MaxBoostClock * 0.80;
                            bool underLoad = CpuLoad > 50;

                            if (clockLow && underLoad)
                            {
                                _throttleCount = Math.Min(ThrottleHysteresis, _throttleCount + 1);
                                ThrottlingReason = "CLOCK FREQUENCY DROP (WMI DETECTED)";
                            }
                            else if (clockLow && !underLoad)
                            {
                                _throttleCount = 0;
                                ThrottlingReason = "POWER SAVING / IDLE";
                            }
                            else
                            {
                                _throttleCount = Math.Max(0, _throttleCount - 1);
                            }
                            
                            IsThrottling = _throttleCount >= ThrottleHysteresis;
                            if (!IsThrottling && ThrottlingReason != "POWER SAVING / IDLE") 
                                ThrottlingReason = "NOMINAL";
                        }
                        break;
                    }

                    // MSAcpi Thermal Zone temperature fallback
                    try
                    {
                        using var s2 = new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                        double maxTemp = 0;
                        foreach (var item in s2.Get())
                        {
                            string name = item["InstanceName"]?.ToString() ?? "UnknownZone";
                            double kelvinDeci = Convert.ToDouble(item["CurrentTemperature"]);
                            double celsius = (kelvinDeci / 10.0) - 273.15;

                            // Track history to detect static zones
                            if (!_wmiTempHistories.TryGetValue(name, out var history))
                            {
                                history = new List<double>();
                                _wmiTempHistories[name] = history;
                            }
                            history.Add(celsius);
                            if (history.Count > 10) history.RemoveAt(0);

                            bool isStaticHigh = false;
                            if (history.Count >= 5)
                            {
                                double minVal = history.Min();
                                double maxVal = history.Max();
                                if (celsius >= 80.0 && Math.Abs(maxVal - minVal) < 0.01)
                                {
                                    isStaticHigh = true;
                                    if (!_staticZonesLogged.Contains(name))
                                    {
                                        _staticZonesLogged.Add(name);
                                        Logger.Log($"Ignoring static high WMI thermal zone '{name}' reporting {celsius:F1}°C with zero fluctuation.", "WARN");
                                    }
                                }
                            }

                            if (!isStaticHigh && celsius > maxTemp)
                                maxTemp = celsius;
                        }
                        if (maxTemp > 0) CpuPackageTemp = Math.Round(maxTemp, 1);
                    }
                    catch { }
                }
                catch { }
            }
        }

        private void DetectInstructionSets()
        {
            try
            {
                HasSSE42  = System.Runtime.Intrinsics.X86.Sse42.IsSupported;
                HasAVX    = System.Runtime.Intrinsics.X86.Avx.IsSupported;
                HasAVX2   = System.Runtime.Intrinsics.X86.Avx2.IsSupported;
                HasAVX512 = System.Runtime.Intrinsics.X86.Avx512F.IsSupported;
            }
            catch { }
            var sets = new List<string> { "SSE4.2" };
            if (HasAVX)    sets.Add("AVX");
            if (HasAVX2)   sets.Add("AVX2");
            if (HasAVX512) sets.Add("AVX-512");
            InstructionSetsString = string.Join(" │ ", sets);
        }

        public void Dispose()
        {
            try { _computer?.Close(); } catch { }
        }
    }
}
