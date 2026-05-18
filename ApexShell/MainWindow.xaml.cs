using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace ApexShell
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public MainWindow()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => ClockText.Text = DateTime.Now.ToString("hh:mm:ss tt  MMM dd");
            _timer.Start();
            ClockText.Text = DateTime.Now.ToString("hh:mm:ss tt  MMM dd");
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = StartMenuPopup.Visibility == Visibility.Visible 
                ? Visibility.Collapsed 
                : Visibility.Visible;
        }

        private void DesktopGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
        }

        private void LaunchDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ApexDiagnostics.exe");
            if (File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show($"Could not find Diagnostics App at:\n{appPath}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LaunchExplorer_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            string appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Explorer++.exe");
            if (File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = true });
            }
            else
            {
                // Fallback if Explorer++ is missing
                Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true });
            }
        }

        private void LaunchCmd_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true });
        }

        private void LaunchNotepad_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
        }

        private void LaunchDeviceManager_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("mmc.exe", "devmgmt.msc") { UseShellExecute = true });
        }

        private void LaunchRegedit_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }

        private void LaunchDiskMgmt_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("mmc.exe", "diskmgmt.msc") { UseShellExecute = true });
        }

        private void LaunchNetworkSet_Click(object sender, RoutedEventArgs e)
        {
            StartMenuPopup.Visibility = Visibility.Collapsed;
            Process.Start(new ProcessStartInfo("control.exe", "ncpa.cpl") { UseShellExecute = true });
        }

        private void Shutdown_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to shutdown the system?", "Shutdown", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("wpeutil", "shutdown") { UseShellExecute = true, CreateNoWindow = true });
            }
        }

        private void Reboot_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to reboot the system?", "Reboot", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("wpeutil", "reboot") { UseShellExecute = true, CreateNoWindow = true });
            }
        }
    }
}
