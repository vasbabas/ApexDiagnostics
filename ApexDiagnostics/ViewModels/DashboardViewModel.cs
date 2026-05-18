using System;
using System.Windows.Threading;
using ApexDiagnostics.Core;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private readonly DispatcherTimer _timer;

        public string CpuName => _telemetry.CpuName;
        public double CpuUsage => _telemetry.CpuUsage;
        public double CpuTemp => _telemetry.CpuTemperature;
        public double RamUsagePercent => _telemetry.RamUsagePercent;
        public double DiskTemp => _telemetry.MaxDiskTemperature;

        public System.Collections.Generic.IEnumerable<double> CpuHistory => _telemetry.CpuHistory;
        public System.Collections.Generic.IEnumerable<double> TempHistory => _telemetry.TempHistory;
        public System.Collections.Generic.IEnumerable<double> RamHistory => _telemetry.RamHistory;

        public string DownloadSpeed => FormatSpeed(_telemetry.NetInKbps);
        public string UploadSpeed   => FormatSpeed(_telemetry.NetOutKbps);
        public string PeakThroughput => FormatSpeed(Math.Max(_telemetry.NetInKbps, _telemetry.NetOutKbps));

        private string FormatSpeed(double kbps)
        {
            if (kbps < 1000) return $"{kbps:F1} Kbps";
            double mbps = kbps / 1024.0;
            if (mbps < 1000) return $"{mbps:F1} Mbps";
            double gbps = mbps / 1024.0;
            return $"{gbps:F1} Gbps";
        }

        public DashboardViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => 
            {
                OnPropertyChanged(nameof(CpuUsage));
                OnPropertyChanged(nameof(CpuTemp));
                OnPropertyChanged(nameof(RamUsagePercent));
                OnPropertyChanged(nameof(DiskTemp));
                OnPropertyChanged(nameof(CpuHistory));
                OnPropertyChanged(nameof(TempHistory));
                OnPropertyChanged(nameof(RamHistory));
                OnPropertyChanged(nameof(DownloadSpeed));
                OnPropertyChanged(nameof(UploadSpeed));
                OnPropertyChanged(nameof(PeakThroughput));
            };
            _timer.Start();

            if (!_telemetry.IsInitialized)
            {
                _telemetry.OnInitialized += () =>
                {
                    OnPropertyChanged(nameof(CpuName));
                };
            }
        }
    }
}
