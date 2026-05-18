using System;
using System.Windows;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Core
{
    /// <summary>
    /// Monitors thermal sensors and stops active engines when safety thresholds are breached.
    /// Shows a WPF dialog on the UI thread when triggered.
    /// </summary>
    public class SafetyWatchdog
    {
        public float CpuCriticalTempC
        {
            get => (float)_telemetry.CpuCriticalTempC;
            set => _telemetry.CpuCriticalTempC = value;
        }

        public float DiskCriticalTempC
        {
            get => (float)_telemetry.DiskCriticalTempC;
            set => _telemetry.DiskCriticalTempC = value;
        }

        public event Action<SafetyEvent>? OnCriticalEvent;

        private readonly TelemetryManager _telemetry;
        private bool _triggered;

        public SafetyWatchdog(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            _telemetry.OnThermalLimitExceeded += HandleThermalLimit;
            _telemetry.OnEmergencyShutdown    += HandleEmergency;
        }

        private void HandleThermalLimit(string component)
        {
            if (_triggered) return;
            _triggered = true;

            float temp = component == "CPU"
                ? (float)_telemetry.CpuTemperature
                : (float)_telemetry.MaxDiskTemperature;

            var evt = new SafetyEvent
            {
                Component   = component,
                Temperature = temp,
                Reason      = $"{component} temperature {temp:F0}°C exceeded critical threshold",
                IsEmergency = false
            };

            Logger.Log($"Safety stop triggered: {evt.Reason}", "WARN");
            OnCriticalEvent?.Invoke(evt);

            // Reset after 30s so it can trigger again
            System.Threading.Tasks.Task.Delay(30000).ContinueWith(_ => _triggered = false);
        }

        private void HandleEmergency()
        {
            var evt = new SafetyEvent
            {
                Component   = "SYSTEM",
                Temperature = (float)_telemetry.CpuTemperature,
                Reason      = $"EMERGENCY: CPU temperature {_telemetry.CpuTemperature:F0}°C — thermal runaway detected",
                IsEmergency = true
            };
            Logger.Log(evt.Reason, "FATAL");
            OnCriticalEvent?.Invoke(evt);
        }
    }

    public class SafetyEvent
    {
        public string  Component   { get; set; } = "";
        public float   Temperature { get; set; }
        public string  Reason      { get; set; } = "";
        public bool    IsEmergency { get; set; }
    }
}
