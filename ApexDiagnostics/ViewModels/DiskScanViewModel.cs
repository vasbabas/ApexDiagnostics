using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows;
using ApexDiagnostics.Core;
using ApexDiagnostics.Engines;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class DiskDriveInfo : ViewModelBase
    {
        public int Index { get; set; }
        public string Name => $"DISK {Index}";
        public string Model { get; set; } = "";
        public long SizeGB { get; set; }
        public long Capacity => SizeGB; // WPF Binding Compatibility Fail-Safe
        public string Interface { get; set; } = "";
        public string Serial { get; set; } = "";
        public string DriveLetters { get; set; } = "";
        
        private string _status = "Healthy";
        public string Status { get => _status; set { _status = value; OnPropertyChanged(nameof(StatusColor)); } }

        private int _healthPercent = 100;
        public int HealthPercent { get => _healthPercent; set => SetProperty(ref _healthPercent, value); }

        private string _healthText = "Healthy";
        public string HealthText { get => _healthText; set => SetProperty(ref _healthText, value); }

        private int _powerOnHours = -1;
        public int PowerOnHours { get => _powerOnHours; set { if (SetProperty(ref _powerOnHours, value)) OnPropertyChanged(nameof(PowerOnHoursText)); } }
        public string PowerOnHoursText => PowerOnHours != -1 ? $"{PowerOnHours} Hours" : "N/A";

        private int _reallocatedSectors = 0;
        public int ReallocatedSectors { get => _reallocatedSectors; set { if (SetProperty(ref _reallocatedSectors, value)) OnPropertyChanged(nameof(ReallocatedSectorsText)); } }
        public string ReallocatedSectorsText => ReallocatedSectors > 0 ? $"{ReallocatedSectors} Bad Sectors" : "0 Bad Sectors";

        public string StatusColor => Status switch
        {
            "Healthy" => "#4CAF50",      // Green
            "Scanning" => "#2196F3",     // Blue
            "Slow" => "#FF9800",         // Orange
            "Warning" => "#FFEB3B",      // Yellow
            "Bad" => "#F44336",          // Red
            _ => "#9E9E9E"               // Gray
        };

        public override string ToString() => $"{Name}: {Model} ({SizeGB} GB)";
    }

    public class DiskScanState : ViewModelBase
    {
        public int Index { get; set; }
        public byte[] MapData { get; } = new byte[100];
        
        private string _status = "IDLE";
        public string Status { get => _status; set => SetProperty(ref _status, value); }
        
        private double _progress;
        public double Progress { get => _progress; set => SetProperty(ref _progress, value); }
        
        private string _speed = "0.0 MB/s";
        public string Speed { get => _speed; set => SetProperty(ref _speed, value); }
        
        private string _eta = "--:--:--";
        public string ETA { get => _eta; set => SetProperty(ref _eta, value); }
        
        private long _badSectors;
        public long BadSectors { get => _badSectors; set => SetProperty(ref _badSectors, value); }
        
        private long _goodSectors;
        public long GoodSectors { get => _goodSectors; set => SetProperty(ref _goodSectors, value); }
        
        private long _slowSectors;
        public long SlowSectors { get => _slowSectors; set => SetProperty(ref _slowSectors, value); }
        
        private long _delayedSectors;
        public long DelayedSectors { get => _delayedSectors; set => SetProperty(ref _delayedSectors, value); }
        
        private long _weakSectors;
        public long WeakSectors { get => _weakSectors; set => SetProperty(ref _weakSectors, value); }

        private long _timeoutSectors;
        public long TimeoutSectors { get => _timeoutSectors; set => SetProperty(ref _timeoutSectors, value); }
        
        private double _latency;
        public double Latency { get => _latency; set => SetProperty(ref _latency, value); }
        
        private string _sectorRange = "0 - 0";
        public string SectorRange { get => _sectorRange; set => SetProperty(ref _sectorRange, value); }

        public long TotalBytes { get; set; }
        public long ScannedBytes { get; set; }

        public long TotalScannedBlocks => GoodSectors + SlowSectors + DelayedSectors + WeakSectors + BadSectors + TimeoutSectors;
    }

    public class DiskScanViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private readonly DiskScanEngine _engine;
        private readonly DispatcherTimer _uiTimer;
        private readonly Dictionary<int, DiskScanState> _diskStates = new();

        public ObservableCollection<DiskDriveInfo> AvailableDisks { get; } = new();

        private DiskDriveInfo? _selectedDisk;
        public DiskDriveInfo? SelectedDisk 
        { 
            get => _selectedDisk; 
            set 
            { 
                if (SetProperty(ref _selectedDisk, value) && value != null)
                {
                    if (!_diskStates.ContainsKey(value.Index))
                        _diskStates[value.Index] = new DiskScanState { Index = value.Index };

                    OnPropertyChanged(nameof(CurrentState));
                    OnPropertyChanged(nameof(MapData));
                    OnPropertyChanged(nameof(EngineStatus));
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(ProgressValue));
                    OnPropertyChanged(nameof(Speed));
                    OnPropertyChanged(nameof(ETA));
                    OnPropertyChanged(nameof(BadSectors));
                    OnPropertyChanged(nameof(GoodSectors));
                    OnPropertyChanged(nameof(ScannedBytes));
                    OnPropertyChanged(nameof(TotalBytes));
                    OnPropertyChanged(nameof(SlowSectors));
                    OnPropertyChanged(nameof(DelayedSectors));
                    OnPropertyChanged(nameof(WeakSectors));
                    OnPropertyChanged(nameof(Latency));
                    OnPropertyChanged(nameof(SectorRange));
                    
                    if (!_engine.IsRunning)
                        _engine.SelectedDriveNumber = value.Index;

                    LoadDiskDetails(value);
                }
            }
        }

        public DiskScanState? CurrentState => SelectedDisk != null && _diskStates.ContainsKey(SelectedDisk.Index) 
            ? _diskStates[SelectedDisk.Index] : null;

        public byte[]? MapData => CurrentState?.MapData;

        // Proxy properties for UI binding to the CurrentState
        public string EngineStatus => CurrentState?.Status ?? "IDLE";
        public double ProgressValue => CurrentState?.Progress ?? 0;
        public string ProgressPercent => $"{ProgressValue:F1}%";
        public string Speed => CurrentState?.Speed ?? "0.0 MB/s";
        public string ETA => CurrentState?.ETA ?? "--:--:--";
        public long BadSectors => CurrentState?.BadSectors ?? 0;
        public long GoodSectors => CurrentState?.GoodSectors ?? 0;
        public long SlowSectors => CurrentState?.SlowSectors ?? 0;
        public long DelayedSectors => CurrentState?.DelayedSectors ?? 0;
        public long WeakSectors => CurrentState?.WeakSectors ?? 0;
        public long TimeoutSectors => CurrentState?.TimeoutSectors ?? 0;
        public double Latency => CurrentState?.Latency ?? 0;
        public string SectorRange => CurrentState?.SectorRange ?? "0 - 0";
        public long TotalBytes => CurrentState?.TotalBytes ?? 0;
        public long ScannedBytes => CurrentState?.ScannedBytes ?? 0;

        public double DiskCriticalTempC
        {
            get => _telemetry.DiskCriticalTempC;
            set
            {
                if (_telemetry.DiskCriticalTempC != value)
                {
                    _telemetry.DiskCriticalTempC = value;
                    OnPropertyChanged(nameof(DiskCriticalTempC));
                }
            }
        }

        // Command properties...
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RefreshDisksCommand { get; }

        public DiskScanViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            _engine = new DiskScanEngine();
            _engine.OnSectorScanned += (offset, status) => 
            {
                int driveIdx = _engine.SelectedDriveNumber;
                if (_diskStates.TryGetValue(driveIdx, out var state))
                {
                    if (_engine.TotalBytes > 0)
                    {
                        int mapIdx = (int)((double)offset / _engine.TotalBytes * 100);
                        if (mapIdx >= 0 && mapIdx < 100 && status > state.MapData[mapIdx])
                        {
                            state.MapData[mapIdx] = status;
                        }
                    }
                }
            };

            StartCommand = new RelayCommand(() => 
            { 
                if (SelectedDisk != null && _diskStates.TryGetValue(SelectedDisk.Index, out var state))
                {
                    Array.Clear(state.MapData, 0, state.MapData.Length);
                    state.Status = "SCANNING";
                    _engine.SelectedDriveNumber = SelectedDisk.Index;
                    _engine.Start(); 
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }
            }, () => !_engine.IsRunning && SelectedDisk != null);

            StopCommand = new RelayCommand(() => 
            {
                _engine.Stop();
                if (SelectedDisk != null && _diskStates.TryGetValue(SelectedDisk.Index, out var state))
                {
                    state.Status = "IDLE";
                }
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }, () => _engine.IsRunning);
            RefreshDisksCommand = new RelayCommand(LoadAvailableDisks);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, e) => UpdateUI();
            _uiTimer.Start();

            LoadAvailableDisks();
        }

        private bool _isRefreshing;
        public bool IsRefreshing { get => _isRefreshing; set => SetProperty(ref _isRefreshing, value); }

        private void LoadAvailableDisks()
        {
            if (IsRefreshing) return;
            IsRefreshing = true;

            Task.Run(() =>
            {
                var disks = new List<DiskDriveInfo>();
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Index, Model, Size, InterfaceType, SerialNumber, PNPDeviceID FROM Win32_DiskDrive");
                    searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                    
                    foreach (var item in searcher.Get())
                    {
                        try
                        {
                            int index = Convert.ToInt32(item["Index"] ?? 0);
                            var info = new DiskDriveInfo
                            {
                                Index = index,
                                Model = item["Model"]?.ToString() ?? "Unknown Disk",
                                SizeGB = Convert.ToInt64(item["Size"] ?? 0) / (1024 * 1024 * 1024),
                                Interface = item["InterfaceType"]?.ToString() ?? "N/A",
                                Serial = item["SerialNumber"]?.ToString()?.Trim() ?? "N/A"
                            };

                            // Populate SMART parameters from raw WMI
                            string pnpId = item["PNPDeviceID"]?.ToString() ?? "";
                            PopulateSmartData(info, pnpId);

                            // Drive letters
                            var letters = new List<string>();
                            try
                            {
                                string devId = $"\\\\\\\\.\\\\PHYSICALDRIVE{index}";
                                string partitionQuery = $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{devId}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition";
                                using var partSearcher = new ManagementObjectSearcher(partitionQuery);
                                foreach (var part in partSearcher.Get())
                                {
                                    string logicalQuery = $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass = Win32_LogicalDiskToPartition";
                                    using var logSearcher = new ManagementObjectSearcher(logicalQuery);
                                    foreach (var log in logSearcher.Get())
                                    {
                                        letters.Add(log["DeviceID"]?.ToString() ?? "");
                                    }
                                }
                            }
                            catch { /* Ignore association errors for failing disks */ }
                            
                            info.DriveLetters = letters.Count > 0 ? string.Join(", ", letters) : "No Partitions";
                            disks.Add(info);
                        }
                        catch (Exception ex) { Logger.Log($"Error parsing disk {item["Index"]}: {ex.Message}", "WARN"); }
                    }
                }
                catch (Exception ex) { Logger.Log($"Disk refresh error: {ex.Message}", "ERROR"); }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableDisks.Clear();
                    foreach (var d in disks) AvailableDisks.Add(d);
                    
                    if (SelectedDisk == null && AvailableDisks.Count > 0)
                        SelectedDisk = AvailableDisks[0];
                        
                    IsRefreshing = false;
                    Logger.Log($"Disk refresh complete. Found {disks.Count} drives.");
                });
            });
        }

        private void PopulateSmartData(DiskDriveInfo info, string pnpDeviceId)
        {
            if (string.IsNullOrEmpty(pnpDeviceId))
            {
                info.HealthPercent = 100;
                info.HealthText = "Healthy (No SMART)";
                return;
            }

            try
            {
                bool predictsFailure = false;
                string cleanedPnp = pnpDeviceId.ToLower().Replace("\\", "");

                // 1. Check WMI Failure Predict Status
                using (var statusSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictStatus"))
                {
                    statusSearcher.Options.Timeout = TimeSpan.FromSeconds(2);
                    foreach (var statusObj in statusSearcher.Get())
                    {
                        string instName = statusObj["InstanceName"]?.ToString().ToLower().Replace("\\", "") ?? "";
                        if (instName.Contains(cleanedPnp) || cleanedPnp.Contains(instName.Split('_')[0]))
                        {
                            predictsFailure = (bool)(statusObj["PredictFailure"] ?? false);
                            break;
                        }
                    }
                }

                if (predictsFailure)
                {
                    info.HealthPercent = 5;
                    info.HealthText = "Failing! (SMART Predict)";
                    info.Status = "Bad";
                    return;
                }

                // 2. Query Raw SMART Data attributes
                byte[] vendorSpecific = null;
                using (var dataSearcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM MSStorageDriver_FailurePredictData"))
                {
                    dataSearcher.Options.Timeout = TimeSpan.FromSeconds(2);
                    foreach (var dataObj in dataSearcher.Get())
                    {
                        string instName = dataObj["InstanceName"]?.ToString().ToLower().Replace("\\", "") ?? "";
                        if (instName.Contains(cleanedPnp) || cleanedPnp.Contains(instName.Split('_')[0]))
                        {
                            vendorSpecific = (byte[])dataObj["VendorSpecific"];
                            break;
                        }
                    }
                }

                if (vendorSpecific != null && vendorSpecific.Length >= 362)
                {
                    int healthScore = 100;
                    int reallocated = 0;
                    int hours = -1;
                    int wearLevel = -1;

                    // SMART telemetry payload starts at offset 2, 12 bytes per attribute block
                    for (int i = 2; i < 362; i += 12)
                    {
                        if (i + 12 > vendorSpecific.Length) break;
                        byte attrId = vendorSpecific[i];
                        if (attrId == 0) continue;

                        byte attrVal = vendorSpecific[i + 3];
                        int rawVal = BitConverter.ToInt32(vendorSpecific, i + 5) & 0x00FFFFFF;

                        if (attrId == 0x05) // Reallocated Sectors Count
                        {
                            reallocated = rawVal;
                            if (rawVal > 0)
                            {
                                healthScore -= Math.Min(60, rawVal * 5); // Reduce health proportionally
                            }
                        }
                        else if (attrId == 0x09) // Power-On Hours Count
                        {
                            hours = rawVal;
                        }
                        else if (attrId == 0xE7 || attrId == 0xAD || attrId == 0xB8) // Wear Range / SSD Endurance Life
                        {
                            wearLevel = attrVal;
                        }
                    }

                    if (wearLevel != -1 && wearLevel <= 100)
                    {
                        healthScore = Math.Min(healthScore, wearLevel);
                    }

                    info.HealthPercent = Math.Max(0, healthScore);
                    info.PowerOnHours = hours;
                    info.ReallocatedSectors = reallocated;

                    if (info.HealthPercent >= 90) info.HealthText = "Healthy";
                    else if (info.HealthPercent >= 50) info.HealthText = "Degraded";
                    else info.HealthText = "Warning";

                    if (info.HealthPercent < 50) info.Status = "Warning";
                    if (info.HealthPercent < 20) info.Status = "Bad";
                }
                else
                {
                    info.HealthPercent = 100;
                    info.HealthText = "Healthy (No SMART)";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SMART query failed for disk {info.Index}: {ex.Message}", "WARN");
                info.HealthPercent = 100;
                info.HealthText = "Healthy (No SMART)";
            }
        }

        // SMART / Detailed Info
        private string _smartStatus = "Unknown";
        public string SmartStatus { get => _smartStatus; set => SetProperty(ref _smartStatus, value); }

        private string _diskDetails = "";
        public string DiskDetails { get => _diskDetails; set => SetProperty(ref _diskDetails, value); }

        private void LoadDiskDetails(DiskDriveInfo disk)
        {
            SmartStatus = disk.HealthText;
            DiskDetails = $"Model: {disk.Model}\n" +
                          $"Serial: {disk.Serial}\n" +
                          $"Interface: {disk.Interface}\n" +
                          $"Capacity: {disk.SizeGB} GB\n" +
                          $"SMART Health: {disk.HealthPercent}%\n" +
                          $"Power-On Hours: {disk.PowerOnHoursText}\n" +
                          $"Reallocated: {disk.ReallocatedSectorsText}";
        }

        private void UpdateUI()
        {
            // Update the state of the DISK CURRENTLY BEING SCANNED by the engine
            if (_engine.IsRunning)
            {
                int activeIdx = _engine.SelectedDriveNumber;
                if (_diskStates.TryGetValue(activeIdx, out var state))
                {
                    state.Status = "SCANNING";
                    state.Progress = _engine.ProgressPercent;
                    state.Speed = $"{_engine.SpeedMBps:F1} MB/s";
                    state.ETA = _engine.EstimatedTimeRemaining;
                    state.BadSectors = _engine.BadSectors;
                    state.GoodSectors = _engine.GoodSectors;
                    state.SlowSectors = _engine.SlowSectors;
                    state.DelayedSectors = _engine.DelayedSectors;
                    state.WeakSectors = _engine.WeakSectors;
                    state.TimeoutSectors = _engine.TimeoutSectors;
                    state.Latency = _engine.LastReadLatencyMs;
                    state.SectorRange = _engine.CurrentSectorRange;
                    state.TotalBytes = _engine.TotalBytes;
                    state.ScannedBytes = _engine.ScannedBytes;
                }
            }
            else
            {
                // Engine is IDLE, sync states status back to IDLE
                foreach (var state in _diskStates.Values)
                {
                    if (state.Status == "SCANNING")
                    {
                        state.Status = "IDLE";
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    }
                }
            }

            // Notify UI that the proxy properties have changed
            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(Speed));
            OnPropertyChanged(nameof(ETA));
            OnPropertyChanged(nameof(BadSectors));
            OnPropertyChanged(nameof(GoodSectors));
            OnPropertyChanged(nameof(ScannedBytes));
            OnPropertyChanged(nameof(TotalBytes));
            OnPropertyChanged(nameof(SlowSectors));
            OnPropertyChanged(nameof(DelayedSectors));
            OnPropertyChanged(nameof(WeakSectors));
            OnPropertyChanged(nameof(TimeoutSectors));
            OnPropertyChanged(nameof(Latency));
            OnPropertyChanged(nameof(SectorRange));
            OnPropertyChanged(nameof(DiskCriticalTempC));
            OnPropertyChanged(nameof(MapData));
        }

        public void Cleanup()
        {
            _uiTimer.Stop();
            _engine.Stop();
        }
    }
}
