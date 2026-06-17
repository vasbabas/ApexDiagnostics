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

    public class PartitionInfo : ViewModelBase
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public int Index { get; set; }
        public string DeviceID { get; set; } = "";
        public string VolumeLetter { get; set; } = "";
        public string VolumeLabel { get; set; } = "";
        public long SizeBytes { get; set; }
        public long StartingOffset { get; set; }
        public string DisplaySize => $"{SizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        public string DisplayName => $"Partition #{Index} ({VolumeLetter} {VolumeLabel}) - {DisplaySize}";
    }

    public class CloneViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;

        public ObservableCollection<DiskInfo> Disks { get; } = new();

        private string _cloneType = "";
        public string CloneType
        {
            get => _cloneType;
            set
            {
                if (SetProperty(ref _cloneType, value))
                {
                    OnPropertyChanged(nameof(IsPartitionCloneSelected));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsPartitionCloneSelected => _cloneType == "Partition";

        private bool _fitPartitions = true;
        public bool FitPartitions
        {
            get => _fitPartitions;
            set => SetProperty(ref _fitPartitions, value);
        }

        private bool _sectorBySectorClone;
        public bool SectorBySectorClone
        {
            get => _sectorBySectorClone;
            set => SetProperty(ref _sectorBySectorClone, value);
        }

        private bool _skipBadSectors;
        public bool SkipBadSectors
        {
            get => _skipBadSectors;
            set => SetProperty(ref _skipBadSectors, value);
        }

        private bool _alignPartitions;
        public bool AlignPartitions
        {
            get => _alignPartitions;
            set => SetProperty(ref _alignPartitions, value);
        }

        private long _skippedBadSectors;
        public long SkippedBadSectors
        {
            get => _skippedBadSectors;
            set
            {
                if (SetProperty(ref _skippedBadSectors, value))
                {
                    OnPropertyChanged(nameof(IsSkippedBadSectorsVisible));
                    OnPropertyChanged(nameof(LocalizedSkippedSectorsLabel));
                }
            }
        }

        private string _cloneWarningMessage = "";
        public string CloneWarningMessage
        {
            get => _cloneWarningMessage;
            set
            {
                if (SetProperty(ref _cloneWarningMessage, value))
                {
                    OnPropertyChanged(nameof(IsCloneWarningActive));
                }
            }
        }

        public bool IsCloneWarningActive => !string.IsNullOrEmpty(_cloneWarningMessage);

        public Visibility IsSkippedBadSectorsVisible => SkippedBadSectors > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string LocalizedSkippedSectorsLabel => string.Format(GetTranslation("CloneBadSectorsSkippedLabel", "Skipped Bad Sectors: {0}"), SkippedBadSectors);

        public ObservableCollection<PartitionInfo> SourcePartitions { get; } = new();

        private DiskInfo? _selectedSource;
        public DiskInfo? SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value))
                {
                    LoadPartitions();
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
        public ICommand ClearWarningMessageCommand { get; }

        public CloneViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;

            RefreshDisksCommand = new RelayCommand(RefreshDisks, () => !IsCloning);
            ShowWarningCommand = new RelayCommand(ExecuteShowWarning, CanShowWarning);
            CancelWarningCommand = new RelayCommand(() => IsWarningVisible = false, () => !IsCloning);
            StartCloneCommand = new RelayCommand(ExecuteStartClone, CanStartClone);
            PauseCloneCommand = new RelayCommand(ExecutePauseClone, () => IsCloning);
            StopCloneCommand = new RelayCommand(ExecuteStopClone, () => IsCloning);
            ClearWarningMessageCommand = new RelayCommand(() => CloneWarningMessage = "");

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
            if (SelectedSource == null || SelectedDest == null || SelectedSource.Index == SelectedDest.Index || IsCloning)
                return false;

            if (string.IsNullOrEmpty(CloneType))
                return false;

            if (CloneType == "Partition" && !SourcePartitions.Any(p => p.IsSelected))
                return false;

            return true;
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
            SkippedBadSectors = 0;
            CloneWarningMessage = "";
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
                var lockedVolumeHandles = new System.Collections.Generic.List<SafeFileHandle>();
                bool cloneSuccessful = false;
                
                try
                {
                    // Dismount and lock all volumes on destination drive first to prevent ACCESS DENIED
                    var destLetters = GetVolumeLettersForDisk(dstDisk.Index);
                    foreach (var letter in destLetters)
                    {
                        string volumePath = $"\\\\.\\{letter}:";
                        var hVol = CreateFile(volumePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (!hVol.IsInvalid)
                        {
                            uint bytesReturned;
                            bool locked = DeviceIoControl(hVol, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                            if (locked)
                            {
                                bool dismounted = DeviceIoControl(hVol, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out bytesReturned, IntPtr.Zero);
                                lockedVolumeHandles.Add(hVol);
                                Logger.Log($"Successfully locked and dismounted volume {volumePath}", "INFO");
                            }
                            else
                            {
                                hVol.Dispose();
                                Logger.Log($"Failed to lock volume {volumePath}. Error Code: {Marshal.GetLastWin32Error()}", "WARN");
                            }
                        }
                        else
                        {
                            Logger.Log($"Could not open volume {volumePath} for locking. Error Code: {Marshal.GetLastWin32Error()}", "WARN");
                        }
                    }

                    // Set destination disk offline first to prevent ACCESS DENIED
                    SetDiskOfflineState(dstDisk.Index, true);

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

                    long totalBytes = 0;
                    if (CloneType == "Full")
                    {
                        totalBytes = srcDisk.SizeBytes;
                    }
                    else
                    {
                        totalBytes = SourcePartitions.Where(p => p.IsSelected).Sum(p => p.SizeBytes);
                    }

                    long bytesCloned = 0;
                    int bufferSize = 4 * 1024 * 1024; // 4MB buffer chunks for fast copying
                    byte[] buffer = new byte[bufferSize];

                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    long lastBytes = 0;
                    var speedTimer = System.Diagnostics.Stopwatch.StartNew();

                    if (CloneType == "Full")
                    {
                        while (bytesCloned < totalBytes)
                        {
                            if (IsCloneCancelled) break;
                            while (IsClonePaused && !IsCloneCancelled)
                            {
                                System.Threading.Thread.Sleep(100);
                            }
                            if (IsCloneCancelled) break;

                            int bytesToRead = (int)Math.Min(bufferSize, totalBytes - bytesCloned);
                            int bytesRead = 0;

                            try
                            {
                                bytesRead = fsSource.Read(buffer, 0, bytesToRead);
                            }
                            catch (IOException)
                            {
                                if (SkipBadSectors)
                                {
                                    fsSource.Position = bytesCloned;
                                    int sectorSize = 512;
                                    byte[] sectorBuffer = new byte[sectorSize];
                                    long blockBytesWritten = 0;
                                    int consecutiveBlockErrors = 0;

                                    while (blockBytesWritten < bytesToRead)
                                    {
                                        if (IsCloneCancelled) break;
                                        long currentTargetPos = bytesCloned + blockBytesWritten;
                                        int toRead = (int)Math.Min(sectorSize, bytesToRead - blockBytesWritten);

                                        try
                                        {
                                            fsSource.Position = currentTargetPos;
                                            int read = fsSource.Read(sectorBuffer, 0, toRead);
                                            if (read <= 0)
                                            {
                                                Array.Clear(sectorBuffer, 0, sectorBuffer.Length);
                                                SkippedBadSectors++;
                                            }
                                            else
                                            {
                                                consecutiveBlockErrors = 0;
                                            }
                                        }
                                        catch
                                        {
                                            Array.Clear(sectorBuffer, 0, sectorBuffer.Length);
                                            SkippedBadSectors++;
                                            consecutiveBlockErrors++;
                                            if (consecutiveBlockErrors > 100)
                                            {
                                                throw new IOException(string.Format(GetTranslation("CloneStatusFailedTooManyBadSectors", "Cloning aborted: Too many consecutive bad sectors ({0}) encountered. The disk may be completely disconnected or failing."), consecutiveBlockErrors));
                                            }
                                        }

                                        fsDest.Position = currentTargetPos;
                                        fsDest.Write(sectorBuffer, 0, toRead);
                                        blockBytesWritten += toRead;
                                    }

                                    bytesCloned += bytesToRead;
                                    continue;
                                }
                                else
                                {
                                    throw;
                                }
                            }

                            if (bytesRead <= 0) break;

                            fsDest.Write(buffer, 0, bytesRead);
                            bytesCloned += bytesRead;

                            UpdateTelemetry(bytesCloned, totalBytes, stopwatch, speedTimer, ref lastBytes);
                        }
                    }
                    else
                    {
                        var selectedParts = SourcePartitions.Where(p => p.IsSelected).OrderBy(p => p.StartingOffset).ToList();
                        foreach (var part in selectedParts)
                        {
                            if (IsCloneCancelled) break;

                            long partStartOffset = part.StartingOffset;
                            long partTargetOffset = part.StartingOffset;

                            if (AlignPartitions)
                            {
                                if (partTargetOffset % (1024 * 1024) != 0)
                                {
                                    partTargetOffset = ((partTargetOffset / (1024 * 1024)) + 1) * 1024 * 1024;
                                }
                            }

                            fsSource.Position = partStartOffset;
                            fsDest.Position = partTargetOffset;

                            long partBytesCloned = 0;
                            long partTotalBytes = part.SizeBytes;

                            while (partBytesCloned < partTotalBytes)
                            {
                                if (IsCloneCancelled) break;
                                while (IsClonePaused && !IsCloneCancelled)
                                {
                                    System.Threading.Thread.Sleep(100);
                                }
                                if (IsCloneCancelled) break;

                                int bytesToRead = (int)Math.Min(bufferSize, partTotalBytes - partBytesCloned);
                                int bytesRead = 0;

                                try
                                {
                                    bytesRead = fsSource.Read(buffer, 0, bytesToRead);
                                }
                                catch (IOException)
                                {
                                    if (SkipBadSectors)
                                    {
                                        fsSource.Position = partStartOffset + partBytesCloned;
                                        int sectorSize = 512;
                                        byte[] sectorBuffer = new byte[sectorSize];
                                        long blockBytesWritten = 0;
                                        int consecutiveBlockErrors = 0;

                                        while (blockBytesWritten < bytesToRead)
                                        {
                                            if (IsCloneCancelled) break;
                                            long currentSrcPos = partStartOffset + partBytesCloned + blockBytesWritten;
                                            long currentDstPos = partTargetOffset + partBytesCloned + blockBytesWritten;
                                            int toRead = (int)Math.Min(sectorSize, bytesToRead - blockBytesWritten);

                                            try
                                            {
                                                fsSource.Position = currentSrcPos;
                                                int read = fsSource.Read(sectorBuffer, 0, toRead);
                                                if (read <= 0)
                                                {
                                                    Array.Clear(sectorBuffer, 0, sectorBuffer.Length);
                                                    SkippedBadSectors++;
                                                }
                                                else
                                                {
                                                    consecutiveBlockErrors = 0;
                                                }
                                            }
                                            catch
                                            {
                                                Array.Clear(sectorBuffer, 0, sectorBuffer.Length);
                                                SkippedBadSectors++;
                                                consecutiveBlockErrors++;
                                                if (consecutiveBlockErrors > 100)
                                                {
                                                    throw new IOException(string.Format(GetTranslation("CloneStatusFailedTooManyBadSectors", "Cloning aborted: Too many consecutive bad sectors ({0}) encountered. The disk may be completely disconnected or failing."), consecutiveBlockErrors));
                                                }
                                            }

                                            fsDest.Position = currentDstPos;
                                            fsDest.Write(sectorBuffer, 0, toRead);
                                            blockBytesWritten += toRead;
                                        }

                                        partBytesCloned += bytesToRead;
                                        bytesCloned += bytesToRead;
                                        continue;
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }

                                if (bytesRead <= 0) break;

                                fsDest.Write(buffer, 0, bytesRead);
                                partBytesCloned += bytesRead;
                                bytesCloned += bytesRead;

                                UpdateTelemetry(bytesCloned, totalBytes, stopwatch, speedTimer, ref lastBytes);
                            }
                        }
                    }

                    fsDest.Flush();
                    stopwatch.Stop();
                    cloneSuccessful = !IsCloneCancelled;

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        IsCloning = false;
                        Progress = IsCloneCancelled ? Progress : 100;
                        SpeedMBs = 0;
                        TimeRemaining = IsCloneCancelled ? "Aborted" : "0s";
                        if (IsCloneCancelled)
                        {
                            Status = "Cloning session aborted by user.";
                            CloneWarningMessage = "Cloning aborted: The target disk contains incomplete partition data.";
                            MessageBox.Show("Disk cloning has been aborted. The target disk contains incomplete partition data.", "Cloning Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else if (SkippedBadSectors > 0)
                        {
                            string warningPattern = GetTranslation("CloneStatusCompletedWithWarnings", "Drive cloning completed with warnings. {0} bad sectors were skipped and zero-filled.");
                            Status = string.Format(warningPattern, SkippedBadSectors);
                            CloneWarningMessage = string.Format(warningPattern, SkippedBadSectors);

                            string title = GetTranslation("CloneBootingNoteWarningTitle", "Cloning Complete with Warnings");
                            string textPattern = GetTranslation("CloneBootingNoteWarningText", "Physical Drive #{0} has been cloned to Drive #{1}!\nTotal Time: {2}\n\nWARNING: {3} bad sectors were skipped and zero-filled. Check your source disk health.\n\nBOOTING NOTE: To boot from the cloned drive, the target drive has been left OFFLINE to prevent Windows from altering its disk signature (which breaks bootability).\n\nInstructions to boot:\n1. Shut down your PC.\n2. Disconnect the source drive (or change boot priority in BIOS).\n3. Turn on your PC. The new drive will boot and automatically come online.\n\nIf you just want to use it as data storage, you can manually online it via Windows Disk Management.");
                            string formattedText = string.Format(textPattern, srcDisk.Index, dstDisk.Index, TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).ToString(@"hh\:mm\:ss"), SkippedBadSectors);
                            MessageBox.Show(formattedText, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            Status = GetTranslation("CloneStatusCompleted", "Drive cloning completed successfully!");

                            string title = GetTranslation("CloneBootingNoteTitle", "Cloning Complete");
                            string textPattern = GetTranslation("CloneBootingNoteText", "Physical Drive #{0} has been successfully cloned to Drive #{1}!\nTotal Time: {2}\n\nBOOTING NOTE: To boot from the cloned drive, the target drive has been left OFFLINE to prevent Windows from altering its disk signature (which breaks bootability).\n\nInstructions to boot:\n1. Shut down your PC.\n2. Disconnect the source drive (or change boot priority in BIOS).\n3. Turn on your PC. The new drive will boot and automatically come online.\n\nIf you just want to use it as data storage, you can manually online it via Windows Disk Management.");
                            string formattedText = string.Format(textPattern, srcDisk.Index, dstDisk.Index, TimeSpan.FromMilliseconds(stopwatch.ElapsedMilliseconds).ToString(@"hh\:mm\:ss"));
                            MessageBox.Show(formattedText, title, MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.Log($"Cloning failed: {ex.Message}", "ERROR");
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        Status = $"Error: {ex.Message}";
                        CloneWarningMessage = $"FATAL ERROR: {ex.Message}. Cloning aborted.";
                        IsCloning = false;
                        MessageBox.Show($"Cloning failed:\n{ex.Message}", "Cloning Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    hSource?.Dispose();
                    hDest?.Dispose();
                    foreach (var hVol in lockedVolumeHandles)
                    {
                        hVol.Dispose();
                    }
                    // Bring destination disk back online only if the clone was not successful
                    if (!cloneSuccessful)
                    {
                        SetDiskOfflineState(dstDisk.Index, false);
                    }
                    else
                    {
                        Logger.Log($"Cloning completed successfully. Leaving destination disk {dstDisk.Index} offline to prevent OS signature collision alterations.", "INFO");
                    }
                }
            });
        }

        private void UpdateTelemetry(long bytesCloned, long totalBytes, System.Diagnostics.Stopwatch stopwatch, System.Diagnostics.Stopwatch speedTimer, ref long lastBytes)
        {
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

        private void LoadPartitions()
        {
            SourcePartitions.Clear();
            if (SelectedSource == null) return;

            int diskIndex = SelectedSource.Index;
            var logicalMap = new System.Collections.Generic.Dictionary<string, string>();

            try
            {
                using (var mapSearcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
                {
                    foreach (var mapping in mapSearcher.Get())
                    {
                        string antecedent = mapping["Antecedent"]?.ToString() ?? "";
                        string dependent = mapping["Dependent"]?.ToString() ?? "";

                        string partId = ExtractDeviceId(antecedent);
                        string driveLetter = ExtractDeviceId(dependent);

                        if (!string.IsNullOrEmpty(partId) && !string.IsNullOrEmpty(driveLetter))
                        {
                            logicalMap[partId] = driveLetter;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error mapping logical disks: {ex.Message}", "WARN");
            }

            try
            {
                using (var partSearcher = new ManagementObjectSearcher("SELECT DeviceID, Index, Size, StartingOffset FROM Win32_DiskPartition"))
                {
                    foreach (var partition in partSearcher.Get())
                    {
                        string deviceId = partition["DeviceID"]?.ToString() ?? "";
                        if (deviceId.StartsWith($"Disk #{diskIndex},", StringComparison.OrdinalIgnoreCase))
                        {
                            int index = Convert.ToInt32(partition["Index"] ?? 0);
                            long sizeBytes = Convert.ToInt64(partition["Size"] ?? 0);
                            long startingOffset = Convert.ToInt64(partition["StartingOffset"] ?? 0);

                            string volumeLetter = "";
                            string volumeLabel = "";

                            if (logicalMap.TryGetValue(deviceId, out var driveLetter))
                            {
                                volumeLetter = driveLetter;
                                try
                                {
                                    var driveInfo = new DriveInfo(driveLetter);
                                    if (driveInfo.IsReady)
                                    {
                                        volumeLabel = driveInfo.VolumeLabel;
                                    }
                                }
                                catch { }
                            }

                            SourcePartitions.Add(new PartitionInfo
                            {
                                IsSelected = true,
                                Index = index,
                                DeviceID = deviceId,
                                VolumeLetter = volumeLetter,
                                VolumeLabel = volumeLabel,
                                SizeBytes = sizeBytes,
                                StartingOffset = startingOffset
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading partitions: {ex.Message}", "ERROR");
            }
        }

        private string ExtractDeviceId(string wmiPath)
        {
            int idx = wmiPath.IndexOf("DeviceID=\"");
            if (idx != -1)
            {
                int start = idx + "DeviceID=\"".Length;
                int end = wmiPath.IndexOf("\"", start);
                if (end != -1)
                {
                    return wmiPath.Substring(start, end - start);
                }
            }
            return "";
        }

        private System.Collections.Generic.List<string> GetVolumeLettersForDisk(int diskIndex)
        {
            var letters = new System.Collections.Generic.List<string>();
            var logicalMap = new System.Collections.Generic.Dictionary<string, string>();

            try
            {
                using (var mapSearcher = new ManagementObjectSearcher("SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
                {
                    foreach (var mapping in mapSearcher.Get())
                    {
                        string antecedent = mapping["Antecedent"]?.ToString() ?? "";
                        string dependent = mapping["Dependent"]?.ToString() ?? "";

                        string partId = ExtractDeviceId(antecedent);
                        string driveLetter = ExtractDeviceId(dependent);

                        if (!string.IsNullOrEmpty(partId) && !string.IsNullOrEmpty(driveLetter))
                        {
                            logicalMap[partId] = driveLetter;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error mapping logical disks in GetVolumeLettersForDisk: {ex.Message}", "WARN");
            }

            try
            {
                using (var partSearcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskPartition"))
                {
                    foreach (var partition in partSearcher.Get())
                    {
                        string deviceId = partition["DeviceID"]?.ToString() ?? "";
                        if (deviceId.StartsWith($"Disk #{diskIndex},", StringComparison.OrdinalIgnoreCase))
                        {
                            if (logicalMap.TryGetValue(deviceId, out var driveLetter))
                            {
                                string cleanLetter = driveLetter.Replace("\\", "").Replace(":", "").Trim();
                                if (!string.IsNullOrEmpty(cleanLetter))
                                {
                                    letters.Add(cleanLetter);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error reading partitions for disk {diskIndex}: {ex.Message}", "ERROR");
            }

            return letters;
        }

        private void SetDiskOfflineState(int diskNumber, bool offline)
        {
            try
            {
                string scopePath = @"\\localhost\ROOT\Microsoft\Windows\Storage";
                string query = $"SELECT * FROM MSFT_Disk WHERE Number = {diskNumber}";
                using var searcher = new ManagementObjectSearcher(scopePath, query);
                foreach (ManagementObject disk in searcher.Get())
                {
                    string methodName = offline ? "Offline" : "Online";
                    disk.InvokeMethod(methodName, null);
                    Logger.Log($"Disk {diskNumber} has been set to {methodName} via WMI.", "INFO");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to set disk {diskNumber} offline state to {offline} via WMI: {ex.Message}. Trying diskpart fallback...", "WARN");
                SetDiskOfflineStateDiskpart(diskNumber, offline);
            }
        }

        private void SetDiskOfflineStateDiskpart(int diskNumber, bool offline)
        {
            try
            {
                var cmd = offline ? "offline disk" : "online disk";
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    using (var writer = process.StandardInput)
                    {
                        writer.WriteLine($"select disk {diskNumber}");
                        writer.WriteLine(cmd);
                        writer.WriteLine("exit");
                    }
                    process.WaitForExit(10000);
                    Logger.Log($"Disk {diskNumber} set to {cmd} via diskpart.", "INFO");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to set disk {diskNumber} offline state to {offline} via Diskpart: {ex.Message}", "ERROR");
            }
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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            uint nInBufferSize,
            IntPtr lpOutBuffer,
            uint nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    }
}
