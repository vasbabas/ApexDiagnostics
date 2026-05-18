using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32.SafeHandles;
using ApexDiagnostics.Core;
using ApexDiagnostics.Helpers;
using System.Management;

namespace ApexDiagnostics.ViewModels
{
    public class DiskInfo
    {
        public int Index { get; set; }
        public string Model { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public long SizeBytes { get; set; }
        public string DisplaySize => $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        public string DisplayName => $"Drive #{Index} - {Model} ({DisplaySize})";
    }

    public class CloneViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;

        public ObservableCollection<DiskInfo> Disks { get; } = new();

        private DiskInfo? _selectedSource;
        public DiskInfo? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private DiskInfo? _selectedDest;
        public DiskInfo? SelectedDest
        {
            get => _selectedDest;
            set
            {
                if (SetProperty(ref _selectedDest, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private bool _isWarningVisible;
        public bool IsWarningVisible
        {
            get => _isWarningVisible;
            set => SetProperty(ref _isWarningVisible, value);
        }

        private bool _isCloning;
        public bool IsCloning
        {
            get => _isCloning;
            set => SetProperty(ref _isCloning, value);
        }

        private bool _isClonePaused;
        public bool IsClonePaused
        {
            get => _isClonePaused;
            set => SetProperty(ref _isClonePaused, value);
        }

        private bool _isCloneCancelled;
        public bool IsCloneCancelled
        {
            get => _isCloneCancelled;
            set => SetProperty(ref _isCloneCancelled, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private string GetTranslation(string key, string fallback)
        {
            try
            {
                if (Application.Current == null) return fallback;
                return Application.Current.Dispatcher.Invoke(() => {
                    return Application.Current.TryFindResource(key)?.ToString() ?? fallback;
                });
            }
            catch { return fallback; }
        }

        private double _speedMBs;
        public double SpeedMBs
        {
            get => _speedMBs;
            set => SetProperty(ref _speedMBs, value);
        }

        private string _timeRemaining = "N/A";
        public string TimeRemaining
        {
            get => _timeRemaining;
            set => SetProperty(ref _timeRemaining, value);
        }

        private string _confirmText = "";
        public string ConfirmText
        {
            get => _confirmText;
            set
            {
                if (SetProperty(ref _confirmText, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ICommand RefreshDisksCommand { get; }
        public ICommand ShowWarningCommand { get; }
        public ICommand CancelWarningCommand { get; }
        public ICommand StartCloneCommand { get; }
        public ICommand PauseCloneCommand { get; }
        public ICommand StopCloneCommand { get; }

        public CloneViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;

            RefreshDisksCommand = new RelayCommand(RefreshDisks, () => !IsCloning);
            ShowWarningCommand = new RelayCommand(ExecuteShowWarning, CanShowWarning);
            CancelWarningCommand = new RelayCommand(() => IsWarningVisible = false, () => !IsCloning);
            StartCloneCommand = new RelayCommand(ExecuteStartClone, CanStartClone);
            PauseCloneCommand = new RelayCommand(ExecutePauseClone, () => IsCloning);
            StopCloneCommand = new RelayCommand(ExecuteStopClone, () => IsCloning);

            Status = GetTranslation("CloneStatusSelect", "Select Source and Target drives to begin cloning.");

            LanguageManager.LanguageChanged += () => {
                if (!IsCloning)
                {
                    Status = GetTranslation("CloneStatusSelect", "Select Source and Target drives to begin cloning.");
                }
            };

            RefreshDisks();
        }

        private void RefreshDisks()
        {
            Disks.Clear();
            SelectedSource = null;
            SelectedDest = null;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Index, Model, Size, SerialNumber FROM Win32_DiskDrive");
                foreach (var item in searcher.Get())
                {
                    Disks.Add(new DiskInfo
                    {
                        Index = Convert.ToInt32(item["Index"] ?? 0),
                        Model = item["Model"]?.ToString() ?? "Generic Disk",
                        SerialNumber = item["SerialNumber"]?.ToString()?.Trim() ?? "N/A",
                        SizeBytes = Convert.ToInt64(item["Size"] ?? 0)
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error listing physical disks: {ex.Message}", "ERROR");
            }
        }

        private bool CanShowWarning()
        {
            return SelectedSource != null && SelectedDest != null && SelectedSource.Index != SelectedDest.Index && !IsCloning;
        }

        private void ExecuteShowWarning()
        {
            ConfirmText = "";
            IsWarningVisible = true;
        }

        private bool CanStartClone()
        {
            return ConfirmText.Equals("CLONE", StringComparison.OrdinalIgnoreCase) && !IsCloning;
        }

        private void ExecuteStartClone()
        {
            IsWarningVisible = false;
            IsCloning = true;
            IsClonePaused = false;
            IsCloneCancelled = false;
            Progress = 0;
            SpeedMBs = 0;
            TimeRemaining = GetTranslation("CloneStatusCalculating", "Calculating...");
            Status = GetTranslation("CloneStatusInitializing", "Initializing low-level sector cloning session...");

            var srcDisk = SelectedSource!;
            var dstDisk = SelectedDest!;

            System.Threading.Tasks.Task.Run(() =>
            {
                SafeFileHandle? hSource = null;
                SafeFileHandle? hDest = null;
                
                try
                {
                    // Create handle for physical drives
                    string srcPath = $"\\\\.\\PhysicalDrive{srcDisk.Index}";
                    string dstPath = $"\\\\.\\PhysicalDrive{dstDisk.Index}";

                    hSource = CreateFile(srcPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hSource.IsInvalid)
                    {
                        throw new IOException($"Could not open source physical drive {srcDisk.Index}. Error Code: {Marshal.GetLastWin32Error()}");
                    }

                    hDest = CreateFile(dstPath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                    if (hDest.IsInvalid)
                    {
                        throw new IOException($"Could not open destination physical drive {dstDisk.Index}. Error Code: {Marshal.GetLastWin32Error()}");
                    }

                    using var fsSource = new FileStream(hSource, FileAccess.Read);
                    using var fsDest = new FileStream(hDest, FileAccess.Write);

                    long totalBytes = srcDisk.SizeBytes;
                    long bytesCloned = 0;
                    int bufferSize = 4 * 1024 * 1024; // 4MB buffer chunks for fast copying
                    byte[] buffer = new byte[bufferSize];

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    long lastBytes = 0;
                    var speedTimer = System.Diagnostics.Stopwatch.StartNew();

                    while (bytesCloned < totalBytes)
                    {
                        if (IsCloneCancelled) break;
                        while (IsClonePaused && !IsCloneCancelled)
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                        if (IsCloneCancelled) break;

                        int bytesToRead = (int)Math.Min(bufferSize, totalBytes - bytesCloned);
                        int bytesRead = fsSource.Read(buffer, 0, bytesToRead);
                        if (bytesRead <= 0) break;

                        fsDest.Write(buffer, 0, bytesRead);
                        bytesCloned += bytesRead;

                        // UI Telemetry updates every 150ms
                        if (speedTimer.ElapsedMilliseconds > 150)
                        {
                            long elapsedMs = stopwatch.ElapsedMilliseconds;
                            double currentSpeed = (bytesCloned - lastBytes) / (speedTimer.ElapsedMilliseconds / 1000.0) / (1024.0 * 1024.0);
                            lastBytes = bytesCloned;
                            speedTimer.Restart();

                            double progressVal = (double)bytesCloned / totalBytes * 100.0;
                            double averageSpeed = bytesCloned / (elapsedMs / 1000.0) / (1024.0 * 1024.0);

                            long bytesRemaining = totalBytes - bytesCloned;
                            double remainingSeconds = averageSpeed > 0 ? (bytesRemaining / (averageSpeed * 1024.0 * 1024.0)) : 0;
                            TimeSpan t = TimeSpan.FromSeconds(remainingSeconds);
                            string timeStr = t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";

                            App.Current.Dispatcher.Invoke(() =>
                            {
                                Progress = progressVal;
                                SpeedMBs = currentSpeed;
                                TimeRemaining = timeStr;
                                string clonedLabel = GetTranslation("LabelCloned", "Cloned");
                                Status = $"{clonedLabel} {bytesCloned / (1024 * 1024 * 1024.0):F1} GB / {totalBytes / (1024 * 1024 * 1024.0):F1} GB...";
                            });
                        }
                    }

                    fsDest.Flush();
                    stopwatch.Stop();

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        IsCloning = false;
                        Progress = IsCloneCancelled ? Progress : 100;
                        SpeedMBs = 0;
                        TimeRemaining = IsCloneCancelled ? "Aborted" : "0s";
                        if (IsCloneCancelled)
                        {
                            Status = "Cloning session aborted by user.";
                            MessageBox.Show("Disk cloning has been aborted. The target disk contains incomplete partition data.", "Cloning Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            Status = GetTranslation("CloneStatusCompleted", "Drive cloning completed successfully!");
                            MessageBox.Show($"Physical Drive #{srcDisk.Index} has been successfully cloned to Drive #{dstDisk.Index}!\nTotal Time: {TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds):hh\\:mm\\:ss}", "Cloning Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log($"Cloning failed: {ex.Message}", "ERROR");
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        Status = $"Error: {ex.Message}";
                        IsCloning = false;
                        MessageBox.Show($"Cloning failed:\n{ex.Message}", "Cloning Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    hSource?.Dispose();
                    hDest?.Dispose();
                }
            });
        }

        private void ExecutePauseClone()
        {
            IsClonePaused = !IsClonePaused;
            Status = IsClonePaused ? "Cloning session PAUSED..." : "Resuming cloning session...";
        }

        private void ExecuteStopClone()
        {
            var res = MessageBox.Show("Are you sure you want to abort the raw sector cloning process? This may leave the destination disk in an incomplete, corrupted state.", "Abort Disk Cloning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                IsCloneCancelled = true;
            }
        }

        // Native imports for raw physical disk access
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
    }
}
