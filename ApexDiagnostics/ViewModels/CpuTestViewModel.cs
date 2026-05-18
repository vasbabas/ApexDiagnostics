using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ApexDiagnostics.Core;
using ApexDiagnostics.Engines;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class CoreTelemetry : ViewModelBase
    {
        private double _usage;
        public double Usage { get => _usage; set => SetProperty(ref _usage, value); }

        private double _temp;
        public double Temp { get => _temp; set => SetProperty(ref _temp, value); }

        private double _clock;
        public double Clock { get => _clock; set => SetProperty(ref _clock, value); }

        public int Index { get; set; }
    }

    public class CpuTestViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private readonly CpuEngine _engine;
        private readonly DispatcherTimer _uiTimer;
        private bool _wasThrottling;
        private bool _wasTempHigh;

        public ObservableCollection<CoreTelemetry> Cores { get; } = new();

        public double CpuUsage => _telemetry.CpuUsage;
        public double CpuTemp => _telemetry.CpuTemperature;
        public double CpuPackageWatts => _telemetry.HwMonitor.CpuPackageWatts;
        public string InstructionSets => _telemetry.HwMonitor.InstructionSetsString;
        public bool IsThrottling => _telemetry.HwMonitor.IsThrottling;
        public string ThrottlingReason => _telemetry.HwMonitor.ThrottlingReason;
        public ObservableCollection<SensorData> Sensors => _telemetry.LiveSensors;
        public string EngineStatus => _engine.IsRunning ? "STRESS TESTING" : "IDLE";
        public string ElapsedTime => _engine.ElapsedTime.ToString(@"hh\:mm\:ss");
        public long TotalOps => _engine.TotalOperationsCalculated;

        public System.Collections.Generic.IEnumerable<double> CpuHistory => _telemetry.CpuHistory;
        public System.Collections.Generic.IEnumerable<double> TempHistory => _telemetry.TempHistory;

        public ObservableCollection<string> ActivityLog { get; } = new();

        public double CpuCriticalTempC
        {
            get => _telemetry.CpuCriticalTempC;
            set
            {
                if (_telemetry.CpuCriticalTempC != value)
                {
                    _telemetry.CpuCriticalTempC = value;
                    OnPropertyChanged(nameof(CpuCriticalTempC));
                    LogActivity($"Thermal safety watchdog ceiling set to {value:F0}°C");
                }
            }
        }

        private void LogActivity(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                ActivityLog.Insert(0, $"[{timestamp}] {message}");
                while (ActivityLog.Count > 100) ActivityLog.RemoveAt(ActivityLog.Count - 1);
            });
        }

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        public CpuTestViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            _engine = new CpuEngine();

            if (_telemetry.IsInitialized)
            {
                PopulateCores();
            }
            else
            {
                _telemetry.OnInitialized += PopulateCores;
            }

            LogActivity("Telemetry manager initialized. System status: NOMINAL.");

            StartCommand = new RelayCommand(() => 
            {
                _engine.Start();
                LogActivity("Stress engine started. Torturing AVX/SSE workloads.");
                LogActivity($"Active Instruction Sets: {InstructionSets}");
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }, () => !_engine.IsRunning);

            StopCommand = new RelayCommand(() => 
            {
                _engine.Stop();
                LogActivity("Stress engine stopped by user.");
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }, () => _engine.IsRunning);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, e) => UpdateUI();
            _uiTimer.Start();
        }

        private void PopulateCores()
        {
            Cores.Clear();
            for (int i = 0; i < _telemetry.LogicalCores; i++)
            {
                Cores.Add(new CoreTelemetry { Index = i });
            }
        }

        private void UpdateUI()
        {
            OnPropertyChanged(nameof(CpuUsage));
            OnPropertyChanged(nameof(CpuTemp));
            OnPropertyChanged(nameof(CpuPackageWatts));
            OnPropertyChanged(nameof(IsThrottling));
            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(ElapsedTime));
            OnPropertyChanged(nameof(TotalOps));
            OnPropertyChanged(nameof(CpuHistory));
            OnPropertyChanged(nameof(TempHistory));
            OnPropertyChanged(nameof(CpuCriticalTempC));

            if (IsThrottling && !_wasThrottling)
            {
                LogActivity($"CRITICAL: Thermal/Power Throttling Triggered! Reason: {ThrottlingReason}");
                _wasThrottling = true;
            }
            else if (!IsThrottling && _wasThrottling)
            {
                LogActivity("Nominal status: CPU Throttling cleared.");
                _wasThrottling = false;
            }

            if (CpuTemp > 85 && !_wasTempHigh)
            {
                LogActivity($"WARNING: Processor core temperature elevated ({CpuTemp:F0}°C)!");
                _wasTempHigh = true;
            }
            else if (CpuTemp < 78 && _wasTempHigh)
            {
                LogActivity($"Info: Processor core temperature stabilized ({CpuTemp:F0}°C).");
                _wasTempHigh = false;
            }

            // Self-healing check if logical core count is initialized in background
            if (Cores.Count != _telemetry.LogicalCores && _telemetry.LogicalCores > 0)
            {
                PopulateCores();
            }

            for (int i = 0; i < Cores.Count; i++)
            {
                if (i < _telemetry.PerCoreUsage.Length)
                    Cores[i].Usage = _telemetry.PerCoreUsage[i];
                
                // Fallback to global package temperature for each core if per-core sensors aren't active
                if (i < _telemetry.HwMonitor.CpuCoreTemps.Length)
                    Cores[i].Temp = _telemetry.HwMonitor.CpuCoreTemps[i];
                else
                    Cores[i].Temp = _telemetry.CpuTemperature;

                // Fallback to single WMI current clock speed or max boost if per-core sensors aren't active
                if (i < _telemetry.HwMonitor.CpuCoreClocks.Length)
                    Cores[i].Clock = _telemetry.HwMonitor.CpuCoreClocks[i];
                else if (_telemetry.HwMonitor.CpuCoreClocks.Length == 1)
                    Cores[i].Clock = _telemetry.HwMonitor.CpuCoreClocks[0];
                else
                    Cores[i].Clock = _telemetry.HwMonitor.MaxBoostClock;
            }
        }

        public void Cleanup()
        {
            _uiTimer.Stop();
            _engine.Stop();
        }
    }
}
