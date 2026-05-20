using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using ApexDiagnostics.Helpers;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.ViewModels
{
    public class FileItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string Size { get; set; } = "";
        public string LastModified { get; set; } = "";
        public string Icon => IsDirectory ? "📁" : "📄";
        public string Color => IsDirectory ? "#58A6FF" : "#8B949E";
    }

    public class ExplorerViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        private readonly object _logLock = new();
        private readonly List<string> _pendingLogs = new();
        private readonly Stopwatch _uiUpdateStopwatch = new();

        private void LogAndStatusUpdate(string? newLogLine, string newStatus, bool force = false)
        {
            lock (_logLock)
            {
                if (newLogLine != null)
                {
                    _pendingLogs.Add(newLogLine);
                }
            }

            if (force || !_uiUpdateStopwatch.IsRunning || _uiUpdateStopwatch.ElapsedMilliseconds > 150)
            {
                if (!_uiUpdateStopwatch.IsRunning)
                {
                    _uiUpdateStopwatch.Start();
                }
                else
                {
                    _uiUpdateStopwatch.Restart();
                }

                List<string> logsToFlush;
                lock (_logLock)
                {
                    logsToFlush = new List<string>(_pendingLogs);
                    _pendingLogs.Clear();
                }

                App.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var log in logsToFlush)
                    {
                        if (BackupLogs.Count > 150) BackupLogs.RemoveAt(0);
                        BackupLogs.Add(log);
                    }
                    BackupStatus = newStatus;
                }));
            }
        }
        
        private string _currentDirectory = "";
        public string CurrentDirectory
        {
            get => _currentDirectory;
            set
            {
                if (SetProperty(ref _currentDirectory, value))
                {
                    LoadDirectory(value);
                }
            }
        }

        private FileItem? _selectedItem;
        public FileItem? SelectedItem
        {
            get => _selectedItem;
            set => SetProperty(ref _selectedItem, value);
        }

        private string _copiedPath = "";
        public string CopiedPath
        {
            get => _copiedPath;
            set => SetProperty(ref _copiedPath, value);
        }

        // Backup Wizard Properties
        private bool _isBackupWizardVisible;
        public bool IsBackupWizardVisible
        {
            get => _isBackupWizardVisible;
            set => SetProperty(ref _isBackupWizardVisible, value);
        }

        private bool _isBackingUp;
        public bool IsBackingUp
        {
            get => _isBackingUp;
            set => SetProperty(ref _isBackingUp, value);
        }

        private bool _isBackupPaused;
        public bool IsBackupPaused
        {
            get => _isBackupPaused;
            set => SetProperty(ref _isBackupPaused, value);
        }

        private bool _isBackupCancelled;
        public bool IsBackupCancelled
        {
            get => _isBackupCancelled;
            set => SetProperty(ref _isBackupCancelled, value);
        }

        private string _selectedUserProfile = "";
        public string SelectedUserProfile
        {
            get => _selectedUserProfile;
            set => SetProperty(ref _selectedUserProfile, value);
        }

        private string _selectedBackupDest = "";
        public string SelectedBackupDest
        {
            get => _selectedBackupDest;
            set => SetProperty(ref _selectedBackupDest, value);
        }

        private bool _backupCredentials = true;
        public bool BackupCredentials
        {
            get => _backupCredentials;
            set => SetProperty(ref _backupCredentials, value);
        }

        private bool _backupRootCustomFolders = true;
        public bool BackupRootCustomFolders
        {
            get => _backupRootCustomFolders;
            set => SetProperty(ref _backupRootCustomFolders, value);
        }

        private string _backupStatus = "Ready to start...";
        public string BackupStatus
        {
            get => _backupStatus;
            set => SetProperty(ref _backupStatus, value);
        }

        private double _backupProgress;
        public double BackupProgress
        {
            get => _backupProgress;
            set => SetProperty(ref _backupProgress, value);
        }

        private int _copiedFilesCount;
        public ObservableCollection<string> BackupLogs { get; } = new();
        public ObservableCollection<string> Drives { get; } = new();
        public ObservableCollection<FileItem> Items { get; } = new();
        public ObservableCollection<string> UserProfiles { get; } = new();

        public ICommand NavigateUpCommand { get; }
        public ICommand NavigateToFolderCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateFolderCommand { get; }
        public ICommand ShowBackupWizardCommand { get; }
        public ICommand CancelBackupWizardCommand { get; }
        public ICommand BrowseBackupDestCommand { get; }
        public ICommand StartWizardBackupCommand { get; }
        public ICommand PauseBackupCommand { get; }
        public ICommand StopBackupCommand { get; }

        public ExplorerViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            
            NavigateUpCommand = new RelayCommand(ExecuteNavigateUp, () => CanNavigateUp());
            NavigateToFolderCommand = new RelayCommand(ExecuteNavigateToFolder);
            CopyCommand = new RelayCommand(ExecuteCopy, () => SelectedItem != null);
            PasteCommand = new RelayCommand(ExecutePaste, () => !string.IsNullOrEmpty(CopiedPath));
            DeleteCommand = new RelayCommand(ExecuteDelete, () => SelectedItem != null);
            CreateFolderCommand = new RelayCommand(ExecuteCreateFolder);
            
            ShowBackupWizardCommand = new RelayCommand(ExecuteShowBackupWizard);
            CancelBackupWizardCommand = new RelayCommand(() => IsBackupWizardVisible = false, () => !IsBackingUp);
            BrowseBackupDestCommand = new RelayCommand(ExecuteBrowseBackupDest, () => !IsBackingUp);
            StartWizardBackupCommand = new RelayCommand(ExecuteStartWizardBackup, () => !IsBackingUp);
            PauseBackupCommand = new RelayCommand(ExecutePauseBackup, () => IsBackingUp);
            StopBackupCommand = new RelayCommand(ExecuteStopBackup, () => IsBackingUp);

            RefreshDrives();
        }

        public void RefreshDrives()
        {
            Drives.Clear();
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    Drives.Add(drive.Name);
                }
            }

            if (Drives.Count > 0)
            {
                CurrentDirectory = Drives[0];
            }
        }

        private void LoadDirectory(string path)
        {
            try
            {
                Items.Clear();
                
                // Add directories
                var dirs = Directory.GetDirectories(path);
                foreach (var dir in dirs)
                {
                    var di = new DirectoryInfo(dir);
                    if ((di.Attributes & FileAttributes.Hidden) != 0 || (di.Attributes & FileAttributes.System) != 0)
                        continue;

                    Items.Add(new FileItem
                    {
                        Name = di.Name,
                        FullPath = di.FullName,
                        IsDirectory = true,
                        Size = "<DIR>",
                        LastModified = di.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    });
                }

                // Add files
                var files = Directory.GetFiles(path);
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    if ((fi.Attributes & FileAttributes.Hidden) != 0 || (fi.Attributes & FileAttributes.System) != 0)
                        continue;

                    Items.Add(new FileItem
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        IsDirectory = false,
                        Size = FormatFileSize(fi.Length),
                        LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Access Denied / Read Error:\n{ex.Message}", "Explorer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{(bytes / 1073741824.0):F1} GB";
            if (bytes >= 1048576) return $"{(bytes / 1048576.0):F1} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }

        private bool CanNavigateUp()
        {
            if (string.IsNullOrEmpty(CurrentDirectory)) return false;
            var di = new DirectoryInfo(CurrentDirectory);
            return di.Parent != null;
        }

        private void ExecuteNavigateUp()
        {
            var di = new DirectoryInfo(CurrentDirectory);
            if (di.Parent != null)
            {
                CurrentDirectory = di.Parent.FullName;
            }
        }

        private void ExecuteNavigateToFolder(object? parameter)
        {
            if (parameter is FileItem item && item.IsDirectory)
            {
                CurrentDirectory = item.FullPath;
            }
        }

        private void ExecuteCopy()
        {
            if (SelectedItem != null)
            {
                CopiedPath = SelectedItem.FullPath;
            }
        }

        private void ExecutePaste()
        {
            if (string.IsNullOrEmpty(CopiedPath) || (!File.Exists(CopiedPath) && !Directory.Exists(CopiedPath))) return;

            try
            {
                string destName = Path.GetFileName(CopiedPath);
                string destPath = Path.Combine(CurrentDirectory, destName);

                if (File.Exists(CopiedPath))
                {
                    File.Copy(CopiedPath, destPath, true);
                }
                else if (Directory.Exists(CopiedPath))
                {
                    CopyDirectory(CopiedPath, destPath);
                }

                LoadDirectory(CurrentDirectory);
                MessageBox.Show("Transfer completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Paste failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyDirectory(string source, string dest)
        {
            if (IsBackupCancelled) return;
            try
            {
                Directory.CreateDirectory(dest);
                foreach (string file in Directory.GetFiles(source))
                {
                    if (IsBackupCancelled) return;
                    while (IsBackupPaused && !IsBackupCancelled)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    if (IsBackupCancelled) return;

                    string name = Path.GetFileName(file);
                    string destFile = Path.Combine(dest, name);
                    try
                    {
                        File.Copy(file, destFile, true);
                        _copiedFilesCount++;
                        LogAndStatusUpdate($"[+] Rescued: {name}", $"[{_copiedFilesCount} files rescued] Copying: {name}");
                    }
                    catch (Exception ex)
                    {
                        LogAndStatusUpdate($"[!] File Error ({name}): {ex.Message}", $"[{_copiedFilesCount} files rescued] Copying: {name}");
                    }
                }
                foreach (string folder in Directory.GetDirectories(source))
                {
                    if (IsBackupCancelled) return;
                    string name = Path.GetFileName(folder);
                    string destFolder = Path.Combine(dest, name);
                    LogAndStatusUpdate($"[📁] Directory: {name}", $"[{_copiedFilesCount} files rescued] Entering folder: {name}");
                    CopyDirectory(folder, destFolder);
                }
            }
            catch (Exception ex)
            {
                LogAndStatusUpdate($"[!] Dir Error: {ex.Message}", $"[{_copiedFilesCount} files rescued] Error in folder: {Path.GetFileName(source)}");
            }
        }

        private void ExecuteDelete()
        {
            if (SelectedItem == null) return;

            var result = MessageBox.Show($"Are you sure you want to permanently delete:\n{SelectedItem.Name}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (SelectedItem.IsDirectory)
                    {
                        Directory.Delete(SelectedItem.FullPath, true);
                    }
                    else
                    {
                        File.Delete(SelectedItem.FullPath);
                    }
                    LoadDirectory(CurrentDirectory);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Delete failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteCreateFolder()
        {
            string folderName = "New Folder";
            string path = Path.Combine(CurrentDirectory, folderName);
            int count = 1;
            while (Directory.Exists(path))
            {
                path = Path.Combine(CurrentDirectory, $"{folderName} ({count})");
                count++;
            }

            try
            {
                Directory.CreateDirectory(path);
                LoadDirectory(CurrentDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create folder:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteShowBackupWizard()
        {
            UserProfiles.Clear();
            string sourceUsers = @"C:\Users";
            if (Directory.Exists(sourceUsers))
            {
                foreach (var dir in Directory.GetDirectories(sourceUsers))
                {
                    string name = Path.GetFileName(dir);
                    if (name.Equals("All Users", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Default User", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("Public", StringComparison.OrdinalIgnoreCase))
                        continue;

                    UserProfiles.Add(name);
                }
            }

            if (UserProfiles.Count == 0)
            {
                MessageBox.Show("No user profiles found in C:\\Users! Make sure target OS has user profiles.", "No Profiles Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedUserProfile = UserProfiles[0];

            // Set default backup destination (first ready drive other than system/WinPE)
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.Name != "C:\\" && d.Name != "X:\\").ToList();
            if (drives.Count > 0)
            {
                SelectedBackupDest = drives[0].Name;
            }
            else
            {
                SelectedBackupDest = "";
            }

            BackupStatus = "Ready to start...";
            BackupProgress = 0;
            IsBackupWizardVisible = true;
        }

        private void ExecuteBrowseBackupDest()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Destination Backup Folder",
                InitialDirectory = string.IsNullOrEmpty(SelectedBackupDest) ? "D:\\" : SelectedBackupDest
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedBackupDest = dialog.FolderName;
            }
        }

        private void ExecuteStartWizardBackup()
        {
            if (string.IsNullOrEmpty(SelectedUserProfile))
            {
                MessageBox.Show("Please select a user profile to backup!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedBackupDest) || !Directory.Exists(SelectedBackupDest))
            {
                MessageBox.Show("Please select a valid backup destination directory!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsBackingUp = true;
            IsBackupPaused = false;
            IsBackupCancelled = false;
            BackupProgress = 0;
            _copiedFilesCount = 0;
            BackupLogs.Clear();
            BackupLogs.Add("=== APEX DATA RECOVERY TERMINAL ACTIVE ===");
            BackupLogs.Add($"[SYSTEM] Target profile: {SelectedUserProfile}");
            BackupLogs.Add($"[SYSTEM] Destination: {SelectedBackupDest}");
            BackupStatus = "Preparing data recovery session...";

            // Initialize stopwatch and list for throttled UI updates
            _uiUpdateStopwatch.Restart();
            lock (_logLock)
            {
                _pendingLogs.Clear();
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string sourceUserDir = Path.Combine(@"C:\Users", SelectedUserProfile);
                    string targetDir = Path.Combine(SelectedBackupDest, $"Backup_{SelectedUserProfile}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
                    Directory.CreateDirectory(targetDir);
                    
                    LogAndStatusUpdate($"[SYSTEM] Rescue folder created: {Path.GetFileName(targetDir)}", "Preparing data recovery session...", true);

                    string[] subFolders = { "Desktop", "Documents", "Pictures", "Downloads" };
                    double stepWeight = 100.0 / (subFolders.Length + (BackupCredentials ? 1 : 0) + (BackupRootCustomFolders ? 1 : 0));

                    foreach (var folder in subFolders)
                    {
                        LogAndStatusUpdate(null, $"Rescuing user {folder} folder...", true);
                        
                        string src = Path.Combine(sourceUserDir, folder);
                        string dst = Path.Combine(targetDir, folder);
                        
                        if (Directory.Exists(src))
                        {
                            CopyDirectory(src, dst);
                        }

                        LogAndStatusUpdate(null, $"Completed user {folder} folder.", true);
                        App.Current.Dispatcher.Invoke(() => BackupProgress += stepWeight);
                    }

                    if (BackupRootCustomFolders)
                    {
                        LogAndStatusUpdate(null, "Scanning C:\\ root for custom folders...", true);
                        
                        string[] standardSystemDirs = {
                            "windows", "program files", "program files (x86)", "programdata", "users",
                            "system volume information", "recovery", "$recycle.bin", "$winreagent",
                            "documents and settings", "perflogs", "esd", "$windows.~bt", "$windows.~ws",
                            "boot", "efi"
                        };

                        try
                        {
                            string targetRootRescueDir = Path.Combine(targetDir, "C_Root_Custom_Folders");
                            bool foundCustom = false;

                            if (Directory.Exists(@"C:\"))
                            {
                                foreach (var dir in Directory.GetDirectories(@"C:\"))
                                {
                                    string name = Path.GetFileName(dir);
                                    string nameLower = name.ToLowerInvariant();

                                    if (!standardSystemDirs.Contains(nameLower))
                                    {
                                        if (!foundCustom)
                                        {
                                            Directory.CreateDirectory(targetRootRescueDir);
                                            foundCustom = true;
                                        }

                                        LogAndStatusUpdate(null, $"Rescuing C:\\{name} custom folder...", true);
                                        CopyDirectory(dir, Path.Combine(targetRootRescueDir, name));
                                    }
                                }

                                string[] standardSystemFiles = {
                                    "pagefile.sys", "hiberfil.sys", "swapfile.sys", "dumpstack.log.tmp",
                                    "bootmgr", "bootsect.bak", "ntldr"
                                };

                                foreach (var file in Directory.GetFiles(@"C:\"))
                                {
                                    string name = Path.GetFileName(file);
                                    string nameLower = name.ToLowerInvariant();

                                    if (!standardSystemFiles.Contains(nameLower))
                                    {
                                        if (!foundCustom)
                                        {
                                            Directory.CreateDirectory(targetRootRescueDir);
                                            foundCustom = true;
                                        }

                                        LogAndStatusUpdate($"[+] Rescued loose file: {name}", $"Rescuing loose file C:\\{name}...");
                                        try
                                        {
                                            File.Copy(file, Path.Combine(targetRootRescueDir, name), true);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"Error scanning C:\\ root custom folders: {ex.Message}", "WARN");
                        }

                        LogAndStatusUpdate(null, "Completed scanning C:\\ root custom folders.", true);
                        App.Current.Dispatcher.Invoke(() => BackupProgress += stepWeight);
                    }

                    if (BackupCredentials)
                    {
                        LogAndStatusUpdate(null, "Backing up Windows Credentials & DPAPI Keys...", true);
                        string credentialsBackupPath = Path.Combine(targetDir, "Windows_Credentials");
                        Directory.CreateDirectory(credentialsBackupPath);

                        // 1. Copy Offline Vaults, Credentials, and DPAPI Protect directories
                        string localCreds = Path.Combine(sourceUserDir, @"AppData\Local\Microsoft\Credentials");
                        string roamCreds = Path.Combine(sourceUserDir, @"AppData\Roaming\Microsoft\Credentials");
                        string roamProtect = Path.Combine(sourceUserDir, @"AppData\Roaming\Microsoft\Protect");
                        string roamVault = Path.Combine(sourceUserDir, @"AppData\Roaming\Microsoft\Vault");

                        if (Directory.Exists(localCreds)) CopyDirectory(localCreds, Path.Combine(credentialsBackupPath, "Local_Credentials"));
                        if (Directory.Exists(roamCreds)) CopyDirectory(roamCreds, Path.Combine(credentialsBackupPath, "Roaming_Credentials"));
                        if (Directory.Exists(roamProtect)) CopyDirectory(roamProtect, Path.Combine(credentialsBackupPath, "Roaming_Protect"));
                        if (Directory.Exists(roamVault)) CopyDirectory(roamVault, Path.Combine(credentialsBackupPath, "Roaming_Vault"));

                        // 2. Export Live Credentials using vaultcmd if running on active OS (technician utility)
                        try
                        {
                            string backupFile = Path.Combine(credentialsBackupPath, "vault_backup.crdu");
                            var psi = new ProcessStartInfo("vaultcmd.exe", $"/backup:\"{backupFile}\" /password:123")
                            {
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            var process = Process.Start(psi);
                            process?.WaitForExit(3000);
                        }
                        catch { /* Silent skip if running in offline PE mode */ }

                        LogAndStatusUpdate(null, "Windows Credentials backup complete.", true);
                        App.Current.Dispatcher.Invoke(() => BackupProgress = 100);
                    }

                    // Flush any final logs remaining in buffer before completing
                    LogAndStatusUpdate(null, IsBackupCancelled ? "Backup aborted." : "Backup completed successfully!", true);

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        IsBackingUp = false;
                        if (IsBackupCancelled)
                        {
                            BackupStatus = "Backup aborted by user.";
                            BackupLogs.Add("[SYSTEM] Backup process successfully aborted.");
                            MessageBox.Show("Backup process has been aborted. Target folder contains incomplete data.", "Backup Aborted", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            BackupStatus = "Backup completed successfully!";
                            IsBackupWizardVisible = false;
                            MessageBox.Show($"Selected user backup completed successfully!\nSaved to: {targetDir}", "Rescue Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Flush logs in case of error
                    LogAndStatusUpdate(null, $"Error: {ex.Message}", true);
                    
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        BackupStatus = $"Error: {ex.Message}";
                        IsBackingUp = false;
                        MessageBox.Show($"Recovery encountered errors:\n{ex.Message}", "Recovery Incomplete", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private void ExecutePauseBackup()
        {
            IsBackupPaused = !IsBackupPaused;
            App.Current.Dispatcher.Invoke(() =>
            {
                if (BackupLogs.Count > 150) BackupLogs.RemoveAt(0);
                if (IsBackupPaused)
                {
                    BackupLogs.Add("[SYSTEM] Backup PAUSED by user.");
                    BackupStatus = "Backup session paused...";
                }
                else
                {
                    BackupLogs.Add("[SYSTEM] Backup RESUMED by user.");
                    BackupStatus = "Resuming backup session...";
                }
            });
        }

        private void ExecuteStopBackup()
        {
            var res = MessageBox.Show("Are you sure you want to stop and abort the active backup process?", "Abort Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                IsBackupCancelled = true;
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (BackupLogs.Count > 150) BackupLogs.RemoveAt(0);
                    BackupLogs.Add("[SYSTEM] Aborting backup process...");
                });
            }
        }
    }
}
