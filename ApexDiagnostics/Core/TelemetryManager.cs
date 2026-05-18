using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.Core
{
    public class SensorData : ViewModelBase
    {
        private string _value = "";
        private bool _isAlert;

        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get => _value; set => SetProperty(ref _value, value); }
        public string Unit { get; set; } = "";
        public bool IsAlert { get => _isAlert; set => SetProperty(ref _isAlert, value); }
    }

    /// <summary>
    /// Live hardware telemetry poller. Fires PropertyChanged-compatible events so
    /// WPF ViewModels can subscribe and dispatch to the UI thread.
    /// </summary>
    public class TelemetryManager : IDisposable
    {
        public ObservableCollection<SensorData> LiveSensors { get; } = new();

        // ── Static hardware info ──
        public string  CpuName        { get; private set; } = "Unknown CPU";
        public string  CpuStepping    { get; private set; } = "";
        public int     PhysicalCores  { get; private set; }
        public int     LogicalCores   { get; private set; }
        public double  TotalRamGB     { get; private set; }
        public string  CpuArch        { get; private set; } = "x64";
        public string  CpuSocket      { get; private set; } = "";
        public string  L2CacheKB      { get; private set; } = "";
        public string  L3CacheMB      { get; private set; } = "";

        // ── Live telemetry (thread-safe via volatile) ──
        public double CpuUsage           { get; private set; }
        public double RamUsagePercent    { get; private set; }
        public double RamAvailableMB     { get; private set; }
        public double CpuTemperature     { get; private set; }
        public double MaxDiskTemperature { get; private set; }

        // ── Adjustable Safety Thresholds ──
        public double CpuCriticalTempC   { get; set; } = 95.0;
        public double DiskCriticalTempC  { get; set; } = 70.0;
        public double NetInKbps          { get; private set; }
        public double NetOutKbps         { get; private set; }

        // ── Per-core usage ──
        public double[] PerCoreUsage { get; private set; } = Array.Empty<double>();

        // ── Sparkline histories ──
        private readonly object _historyLock = new();
        public IEnumerable<double> CpuHistory  { get { lock(_historyLock) return _cpuHistory.ToList(); } }
        public IEnumerable<double> TempHistory { get { lock(_historyLock) return _tempHistory.ToList(); } }
        public IEnumerable<double> RamHistory  { get { lock(_historyLock) return _ramHistory.ToList(); } }
        public IEnumerable<double> DiskHistory { get { lock(_historyLock) return _diskHistory.ToList(); } }

        // ── HardwareMonitor (LHM) ──
        public HardwareMonitor HwMonitor { get; }

        // ── Events ──
        public bool                   IsInitialized { get; private set; }
        public event Action?          OnInitialized;
        public event Action<string>?  OnThermalLimitExceeded;
        public event Action?          OnEmergencyShutdown;
        public event Action?          OnTelemetryUpdated;   // fired after each poll cycle

        private PerformanceCounter?   _cpuCounter;
        private PerformanceCounter?   _ramCounter;
        private PerformanceCounter?   _diskCounter;
        private PerformanceCounter?   _netInCounter;
        private PerformanceCounter?   _netOutCounter;
        private PerformanceCounter?[] _coreCounters = Array.Empty<PerformanceCounter?>();
        private CancellationTokenSource? _cts;
        private Task? _task;

        private readonly List<double> _cpuHistory  = new();
        private readonly List<double> _tempHistory  = new();
        private readonly List<double> _ramHistory   = new();
        private readonly List<double> _diskHistory  = new();
        private const int HistorySize = 60;

        public TelemetryManager()
        {
            HwMonitor = new HardwareMonitor();
            // Start hardware detection in background to avoid blocking UI during startup
            Task.Run(InitializeAsync);
        }

        private async Task InitializeAsync()
        {
            try
            {
                await Task.Run(() => 
                {
                    InitHardwareInfo();
                    InitPerfCounters();
                    HwMonitor.Initialize();
                });

                _cts = new CancellationTokenSource();
                _task = Task.Run(() => PollLoop(_cts.Token));
                
                IsInitialized = true;
                Logger.Log("TelemetryManager initialized and started in background.");

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OnInitialized?.Invoke();
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"TelemetryManager critical init failure: {ex.Message}", "FATAL");
            }
        }

        public void Start()
        {
            // Already started in InitializeAsync
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _task?.Wait(3000); } catch { }
        }

        private void InitHardwareInfo()
        {
            try
            {
                using var s = new ManagementObjectSearcher(
                    "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,Architecture," +
                    "Stepping,SocketDesignation,L2CacheSize,L3CacheSize FROM Win32_Processor");
                foreach (var item in s.Get())
                {
                    CpuName       = item["Name"]?.ToString()?.Trim() ?? "Unknown";
                    CpuStepping   = item["Stepping"]?.ToString() ?? "";
                    CpuSocket     = item["SocketDesignation"]?.ToString() ?? "";
                    PhysicalCores = Convert.ToInt32(item["NumberOfCores"] ?? Environment.ProcessorCount / 2);
                    LogicalCores  = Convert.ToInt32(item["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                    int arch      = Convert.ToInt32(item["Architecture"] ?? 9);
                    CpuArch       = arch switch { 0=>"x86",5=>"ARM",9=>"x64",12=>"ARM64",_=>"x64" };
                    uint l2kb     = Convert.ToUInt32(item["L2CacheSize"] ?? 0);
                    uint l3kb     = Convert.ToUInt32(item["L3CacheSize"] ?? 0);
                    L2CacheKB     = l2kb > 0 ? $"{l2kb} KB" : "N/A";
                    L3CacheMB     = l3kb > 0 ? $"{l3kb / 1024.0:F1} MB" : "N/A";
                    break;
                }

                using var ram = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (var item in ram.Get())
                {
                    TotalRamGB = Math.Round(Convert.ToDouble(item["TotalVisibleMemorySize"]) / (1024 * 1024), 2);
                    break;
                }
            }
            catch (Exception ex) { Logger.Log($"HW info error: {ex.Message}", "WARN"); }

            PerCoreUsage = new double[LogicalCores];
        }

        private void InitPerfCounters()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                _ramCounter = new PerformanceCounter("Memory", "Available MBytes", true);
                _diskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total", true);
                _cpuCounter.NextValue();  // prime
                _diskCounter.NextValue(); 

                try
                {
                    var cat = new PerformanceCounterCategory("Network Interface");
                    var instances = cat.GetInstanceNames();
                    if (instances.Length > 0)
                    {
                        // Use the first one or try to find one with traffic? 
                        // For simplicity in WinPE, we'll take the first non-loopback.
                        string inst = instances.FirstOrDefault(i => !i.Contains("Loopback")) ?? instances[0];
                        _netInCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", inst);
                        _netOutCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", inst);
                    }
                }
                catch { }

                _coreCounters = new PerformanceCounter?[LogicalCores];
                for (int i = 0; i < LogicalCores; i++)
                {
                    try
                    {
                        _coreCounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString(), true);
                        _coreCounters[i]!.NextValue();
                    }
                    catch { _coreCounters[i] = null; }
                }
            }
            catch (Exception ex) { Logger.Log($"PerfCounter init failed: {ex.Message}", "WARN"); }
        }

        private void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_cpuCounter != null)
                        CpuUsage = Math.Round(_cpuCounter.NextValue(), 1);

                    if (_ramCounter != null)
                    {
                        RamAvailableMB  = _ramCounter.NextValue();
                        double totalMB  = TotalRamGB * 1024;
                        RamUsagePercent = totalMB > 0
                            ? Math.Round((totalMB - RamAvailableMB) / totalMB * 100, 1)
                            : 0;
                    }

                    for (int i = 0; i < _coreCounters.Length; i++)
                    {
                        if (_coreCounters[i] != null)
                            try { PerCoreUsage[i] = Math.Round(_coreCounters[i]!.NextValue(), 1); } catch { }
                    }

                    HwMonitor.Update();
                    CpuTemperature     = HwMonitor.CpuPackageTemp;
                    MaxDiskTemperature = HwMonitor.DiskTemps.Length > 0 ? HwMonitor.DiskTemps.Max() : 0;

                    Append(_cpuHistory,  CpuUsage);
                    Append(_tempHistory, CpuTemperature);
                    Append(_ramHistory,  RamUsagePercent);
                    Append(_diskHistory, _diskCounter?.NextValue() ?? 0);

                    // Convert bytes to Kbps (bits / 1024)
                    NetInKbps  = (_netInCounter?.NextValue() ?? 0) * 8 / 1024.0;
                    NetOutKbps = (_netOutCounter?.NextValue() ?? 0) * 8 / 1024.0;

                    UpdateLiveSensors();
                    CheckSafety();
                    OnTelemetryUpdated?.Invoke();
                }
                catch { }

                Thread.Sleep(500);
            }
        }

        private long _lastSensorUpdate;

        private void UpdateLiveSensors()
        {
            long now = DateTime.Now.Ticks;
            if (now - _lastSensorUpdate < 10000000) return; // 1s throttle
            _lastSensorUpdate = now;

            var list = new List<SensorData>();

            // CPU
            list.Add(new SensorData { Category = "CPU", Name = "Package Temp", Value = CpuTemperature.ToString("F1"), Unit = "°C", IsAlert = CpuTemperature > 90 });
            list.Add(new SensorData { Category = "CPU", Name = "Package Power", Value = HwMonitor.CpuPackageWatts.ToString("F1"), Unit = "W" });
            if (HwMonitor.CpuCoreClocks.Length > 0)
                list.Add(new SensorData { Category = "CPU", Name = "Avg Clock", Value = HwMonitor.CpuCoreClocks.Average().ToString("F0"), Unit = "MHz" });

            // RAM
            list.Add(new SensorData { Category = "RAM", Name = "Usage", Value = RamUsagePercent.ToString("F1"), Unit = "%" });
            list.Add(new SensorData { Category = "RAM", Name = "Available", Value = (RamAvailableMB / 1024).ToString("F1"), Unit = "GB" });

            // Disk
            list.Add(new SensorData { Category = "Disk", Name = "Active Time", Value = (_diskHistory.LastOrDefault()).ToString("F1"), Unit = "%" });
            for (int i = 0; i < HwMonitor.DiskTemps.Length; i++)
            {
                list.Add(new SensorData { Category = "Storage", Name = HwMonitor.DiskTempNames[i], Value = HwMonitor.DiskTemps[i].ToString("F1"), Unit = "°C", IsAlert = HwMonitor.DiskTemps[i] > 65 });
            }

            // Fans
            for (int i = 0; i < HwMonitor.FanRpms.Length; i++)
            {
                list.Add(new SensorData { Category = "Cooling", Name = HwMonitor.FanNames[i], Value = HwMonitor.FanRpms[i].ToString("F0"), Unit = "RPM" });
            }

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Simple sync to avoid full reset if count matches
                if (LiveSensors.Count == list.Count)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        LiveSensors[i].Value = list[i].Value;
                        LiveSensors[i].IsAlert = list[i].IsAlert;
                    }
                }
                else
                {
                    LiveSensors.Clear();
                    foreach (var s in list) LiveSensors.Add(s);
                }
            });
        }

        private void CheckSafety()
        {
            if (CpuTemperature > 105)
            {
                Logger.Log("EMERGENCY THERMAL SPIKE — initiating shutdown.", "FATAL");
                OnEmergencyShutdown?.Invoke();
                return;
            }
            if (CpuTemperature >= CpuCriticalTempC)     OnThermalLimitExceeded?.Invoke("CPU");
            if (MaxDiskTemperature >= DiskCriticalTempC) OnThermalLimitExceeded?.Invoke("Disk");
        }

        private void Append(List<double> list, double value)
        {
            lock (_historyLock)
            {
                list.Add(value);
                while (list.Count > HistorySize) list.RemoveAt(0);
            }
        }

        public void Dispose()
        {
            Stop();
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            foreach (var c in _coreCounters) c?.Dispose();
            HwMonitor.Dispose();
        }
    }
}
