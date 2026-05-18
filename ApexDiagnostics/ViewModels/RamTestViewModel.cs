using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using ApexDiagnostics.Core;
using ApexDiagnostics.Engines;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class RamTestViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private RamDiagEngine? _engine;
        private readonly DispatcherTimer _uiTimer;

        private bool _isDeepMode = true;
        public bool IsDeepMode { get => _isDeepMode; set => SetProperty(ref _isDeepMode, value); }

        public string EngineStatus => (_engine?.IsRunning ?? false) ? "TESTING RAM" : "IDLE";
        public int CurrentPass => _engine?.CurrentPass ?? 0;
        public long TotalErrors => _engine?.TotalErrorsFound ?? 0;
        public string CurrentTestName => _engine?.CurrentTestName ?? "Ready";
        public double TestProgress => (_engine?.CurrentTestProgress ?? 0) * 100;
        public long AllocatedMB => _engine?.AllocatedMB ?? 0;
        public long TargetMB => _engine?.TargetMB ?? 0;
        public string ElapsedTime => (_engine?.ElapsedTime ?? TimeSpan.Zero).ToString(@"hh\:mm\:ss");
        
        // Visual Map Data Persistence
        public byte[] MapData { get; } = new byte[100];

        public event Action<long, byte>? OnBlockScanned;
        public event Action? RequestReset;

        public ObservableCollection<MemoryFault> Faults { get; } = new();

        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }

        public RamTestViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            
            StartCommand = new RelayCommand(StartTest, () => _engine == null || !_engine.IsRunning);
            StopCommand = new RelayCommand(StopTest, () => _engine != null && _engine.IsRunning);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (s, e) => UpdateUI();
            _uiTimer.Start();
        }

        private void StartTest()
        {
            double ramGb = _telemetry.TotalRamGB;
            if (ramGb <= 0)
            {
                try
                {
                    using var OSQuery = new System.Management.ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                    foreach (var item in OSQuery.Get())
                    {
                        ramGb = Math.Round(Convert.ToDouble(item["TotalVisibleMemorySize"]) / (1024 * 1024), 2);
                        break;
                    }
                }
                catch 
                { 
                    ramGb = 8.0; // Dynamic default fallback
                }
            }

            _engine = new RamDiagEngine(ramGb, IsDeepMode);
            _engine.OnBlockResult += (idx, status) => 
            {
                // Update persistent map data
                if (_engine.TargetMB > 0)
                {
                    int index = (int)((double)idx / _engine.TargetMB * 100);
                    if (index >= 0 && index < 100 && status > MapData[index])
                    {
                        MapData[index] = status;
                    }
                }
                OnBlockScanned?.Invoke(idx, status);
            };

            Array.Clear(MapData, 0, MapData.Length);
            Faults.Clear();
            RequestReset?.Invoke();
            _engine.Start();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private void StopTest()
        {
            _engine?.Stop();
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }

        private void UpdateUI()
        {
            OnPropertyChanged(nameof(EngineStatus));
            OnPropertyChanged(nameof(CurrentPass));
            OnPropertyChanged(nameof(TotalErrors));
            OnPropertyChanged(nameof(CurrentTestName));
            OnPropertyChanged(nameof(TestProgress));
            OnPropertyChanged(nameof(AllocatedMB));
            OnPropertyChanged(nameof(TargetMB));
            OnPropertyChanged(nameof(ElapsedTime));

            if (_engine != null)
            {
                // Sync faults list (limited to last 50 for UI performance)
                lock (_engine.DetectedFaults)
                {
                    if (_engine.DetectedFaults.Count > Faults.Count)
                    {
                        for (int i = Faults.Count; i < _engine.DetectedFaults.Count; i++)
                        {
                            if (Faults.Count > 50) break;
                            Faults.Insert(0, _engine.DetectedFaults[i]);
                        }
                    }
                }
            }
        }

        public void Cleanup()
        {
            _uiTimer.Stop();
            _engine?.Stop();
        }
    }
}
