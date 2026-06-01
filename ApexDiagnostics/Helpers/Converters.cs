using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ApexDiagnostics.Helpers
{
    public class StatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? "";
            if (status.Contains("TESTING") || status.Contains("RUNNING") || status.Contains("ACTIVE"))
                return (Brush)Application.Current.FindResource("AccentGreenBrush");
            return (Brush)Application.Current.FindResource("TextSecondaryBrush");
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ThrottleBgConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (bool)value ? new SolidColorBrush(Color.FromRgb(62, 38, 38)) : new SolidColorBrush(Color.FromRgb(33, 38, 45));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ThrottleTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if ((bool)value)
                return Application.Current.TryFindResource("ThermalThrottlingActive")?.ToString() ?? "THERMAL THROTTLING DETECTED";
            return Application.Current.TryFindResource("ThermalStateNominal")?.ToString() ?? "THERMAL STATE NOMINAL";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value?.ToString() ?? "";
            switch (status.ToUpperInvariant())
            {
                case "IDLE":
                    return Application.Current.TryFindResource("StatusIdle")?.ToString() ?? "IDLE";
                case "STRESS TESTING":
                    return Application.Current.TryFindResource("StatusStressTesting")?.ToString() ?? "STRESS TESTING";
                case "TESTING RAM":
                    return Application.Current.TryFindResource("StatusTestingRam")?.ToString() ?? "TESTING RAM";
                case "SCANNING":
                    return Application.Current.TryFindResource("StatusScanning")?.ToString() ?? "SCANNING";
                case "COMPLETED":
                    return Application.Current.TryFindResource("StatusCompleted")?.ToString() ?? "COMPLETED";
                case "PAUSED":
                    return Application.Current.TryFindResource("StatusPaused")?.ToString() ?? "PAUSED";
                default:
                    return status;
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ErrorColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long errors = System.Convert.ToInt64(value);
            return errors > 0 ? (Brush)Application.Current.FindResource("AccentRedBrush") : (Brush)Application.Current.FindResource("AccentGreenBrush");
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
    }

    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long count = System.Convert.ToInt64(value);
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class ExpanderSymbolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (bool)value ? "▼" : "▲";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StartButtonVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString() == "IDLE" ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StopButtonVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value?.ToString() != "IDLE" ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StringStartsWithConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string val = value?.ToString() ?? "";
            string prefix = parameter?.ToString() ?? "";
            return !string.IsNullOrEmpty(prefix) && val.StartsWith(prefix);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class EqualityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return parameter?.ToString() ?? "";
        }
    }
}
