using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using ApexDiagnostics.Core;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private readonly SafetyWatchdog _watchdog;

        private object _currentView = null!;
        public object CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        private string _criticalAlertMessage = "";
        public string CriticalAlertMessage
        {
            get => _criticalAlertMessage;
            set => SetProperty(ref _criticalAlertMessage, value);
        }

        private bool _isAlertVisible;
        public bool IsAlertVisible
        {
            get => _isAlertVisible;
            set => SetProperty(ref _isAlertVisible, value);
        }

        public ICommand NavigateCommand { get; }
        public ICommand DismissAlertCommand { get; }

        private readonly DashboardViewModel _dashboardVM;
        private readonly CpuTestViewModel _cpuTestVM;
        private readonly RamTestViewModel _ramTestVM;
        private readonly DiskScanViewModel _diskScanVM;
        private readonly SystemInfoViewModel _systemInfoVM;
        private readonly ExplorerViewModel _explorerVM;
        private readonly CloneViewModel _cloneVM;
        private readonly PatchNotesViewModel _patchNotesVM;

        public MainViewModel()
        {
            _telemetry = new TelemetryManager();
            _watchdog = new SafetyWatchdog(_telemetry);
            
            _watchdog.OnCriticalEvent += OnCriticalEvent;
            _telemetry.Start();

            // Pre-initialize viewmodels to keep them alive and stateful
            _dashboardVM = new DashboardViewModel(_telemetry);
            _cpuTestVM = new CpuTestViewModel(_telemetry);
            _ramTestVM = new RamTestViewModel(_telemetry);
            _diskScanVM = new DiskScanViewModel(_telemetry);
            _systemInfoVM = new SystemInfoViewModel(_telemetry);
            _explorerVM = new ExplorerViewModel(_telemetry);
            _cloneVM = new CloneViewModel(_telemetry);
            _patchNotesVM = new PatchNotesViewModel();

            NavigateCommand = new RelayCommand(ExecuteNavigate);
            DismissAlertCommand = new RelayCommand(() => IsAlertVisible = false);

            // Set initial view
            CurrentView = _dashboardVM;
        }

        private void ExecuteNavigate(object? parameter)
        {
            if (parameter is string viewName)
            {
                CurrentView = viewName switch
                {
                    "Dashboard" => _dashboardVM,
                    "CpuTest" => _cpuTestVM,
                    "RamTest" => _ramTestVM,
                    "DiskScan" => _diskScanVM,
                    "SystemInfo" => _systemInfoVM,
                    "Explorer" => _explorerVM,
                    "Clone" => _cloneVM,
                    "PatchNotes" => _patchNotesVM,
                    _ => CurrentView
                };
            }
        }

        private void OnCriticalEvent(SafetyEvent evt)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                CriticalAlertMessage = evt.Reason;
                IsAlertVisible = true;
            });
        }
    }
}
