using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

    public class BackupSelectionItem : ViewModelBase
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public bool IsDirectory { get; set; }
        public string Icon => IsDirectory ? "📁" : "📄";
    }

    public class BackupPreviewItem : ViewModelBase
    {
        public string Name { get; set; } = "";
        public string SourcePath { get; set; } = "";
        private string _sizeInfo = "Hesaplanıyor...";
        public string SizeInfo
        {
            get => _sizeInfo;
            set => SetProperty(ref _sizeInfo, value);
        }
        public string Icon => Name.Contains("Kimlik") || Name.Contains("Cred") ? "🔑" : "📁";
    }

    public class ExplorerViewModel : ViewModelBase
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int CredBackupCredentials(
            IntPtr token,
            string targetFilePath,
            string password,
            int flags
        );

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

        private bool _isPreviewVisible;
        public bool IsPreviewVisible
        {
            get => _isPreviewVisible;
            set
            {
                if (SetProperty(ref _isPreviewVisible, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public ObservableCollection<BackupPreviewItem> PreviewItems { get; } = new();

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
        public ObservableCollection<BackupSelectionItem> CustomItemsToBackup { get; } = new();

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
        public ICommand ShowPreviewCommand { get; }
        public ICommand CancelPreviewCommand { get; }

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
            ShowPreviewCommand = new RelayCommand(ExecuteShowPreview, () => !IsBackingUp);
            CancelPreviewCommand = new RelayCommand(() => IsPreviewVisible = false, () => !IsBackingUp);

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
                MessageBox.Show($"Erişim Reddedildi / Okuma Hatası:\n{ex.Message}", "Dosya Yöneticisi Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{(bytes / 1073741824.0):F1} GB";
            if (bytes >= 1048576) return $"{(bytes / 1048576.0):F1} MB";
            if (bytes >= 1024) return $"{(bytes / 1024.0):F1} KB";
            return $"{bytes} B";
        }

        private long GetDirectorySize(string path)
        {
            long size = 0;
            try
            {
                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        size += fi.Length;
                    }
                    catch { }
                }
            }
            catch
            {
                try
                {
                    foreach (var file in Directory.GetFiles(path))
                    {
                        try
                        {
                            size += new FileInfo(file).Length;
                        }
                        catch { }
                    }
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        size += GetDirectorySize(dir);
                    }
                }
                catch { }
            }
            return size;
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
                MessageBox.Show("Aktarım başarıyla tamamlandı!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yapıştırma başarısız:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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
                        LogAndStatusUpdate($"[+] Kurtarıldı: {name}", $"[{_copiedFilesCount} dosya kurtarıldı] Kopyalanıyor: {name}");
                    }
                    catch (Exception ex)
                    {
                        LogAndStatusUpdate($"[!] Dosya Hatası ({name}): {ex.Message}", $"[{_copiedFilesCount} dosya kurtarıldı] Kopyalanıyor: {name}");
                    }
                }
                foreach (string folder in Directory.GetDirectories(source))
                {
                    if (IsBackupCancelled) return;
                    string name = Path.GetFileName(folder);
                    string destFolder = Path.Combine(dest, name);
                    LogAndStatusUpdate($"[📁] Klasör: {name}", $"[{_copiedFilesCount} dosya kurtarıldı] Klasöre giriliyor: {name}");
                    CopyDirectory(folder, destFolder);
                }
            }
            catch (Exception ex)
            {
                LogAndStatusUpdate($"[!] Klasör Hatası: {ex.Message}", $"[{_copiedFilesCount} dosya kurtarıldı] Klasörde hata: {Path.GetFileName(source)}");
            }
        }

        private void ExecuteDelete()
        {
            if (SelectedItem == null) return;

            var result = MessageBox.Show($"Aşağıdaki öğeyi kalıcı olarak silmek istediğinizden emin misiniz:\n{SelectedItem.Name}?", "Silmeyi Onayla", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
                    MessageBox.Show($"Silme başarısız:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Klasör oluşturulamadı:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show("C:\\Users klasöründe kullanıcı profili bulunamadı! Hedef sistemin kullanıcı profillerine sahip olduğundan emin olun.", "Profil Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            // Populate custom folders checklist
            CustomItemsToBackup.Clear();
            string[] standardSystemDirs = {
                "windows", "program files", "program files (x86)", "programdata", "users",
                "system volume information", "recovery", "$recycle.bin", "$winreagent",
                "documents and settings", "perflogs", "esd", "$windows.~bt", "$windows.~ws",
                "boot", "efi"
            };
            string[] standardSystemFiles = {
                "pagefile.sys", "hiberfil.sys", "swapfile.sys", "dumpstack.log.tmp",
                "bootmgr", "bootsect.bak", "ntldr"
            };

            try
            {
                if (Directory.Exists(@"C:\"))
                {
                    foreach (var dir in Directory.GetDirectories(@"C:\"))
                    {
                        string name = Path.GetFileName(dir);
                        string nameLower = name.ToLowerInvariant();
                        if (!standardSystemDirs.Contains(nameLower))
                        {
                            CustomItemsToBackup.Add(new BackupSelectionItem
                            {
                                Name = name,
                                FullPath = dir,
                                IsDirectory = true,
                                IsSelected = true
                            });
                        }
                    }

                    foreach (var file in Directory.GetFiles(@"C:\"))
                    {
                        string name = Path.GetFileName(file);
                        string nameLower = name.ToLowerInvariant();
                        if (!standardSystemFiles.Contains(nameLower))
                        {
                            CustomItemsToBackup.Add(new BackupSelectionItem
                            {
                                Name = name,
                                FullPath = file,
                                IsDirectory = false,
                                IsSelected = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error listing C:\\: {ex.Message}", "WARN");
            }

            BackupStatus = "Ready to start...";
            BackupProgress = 0;
            IsPreviewVisible = false;
            IsBackupWizardVisible = true;
        }

        private void ExecuteShowPreview()
        {
            if (string.IsNullOrEmpty(SelectedUserProfile))
            {
                MessageBox.Show("Lütfen yedeklenecek bir kullanıcı profili seçin!", "Doğrulama Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedBackupDest) || !Directory.Exists(SelectedBackupDest))
            {
                MessageBox.Show("Lütfen geçerli bir yedekleme hedef klasörü seçin!", "Doğrulama Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PreviewItems.Clear();

            string sourceUserDir = Path.Combine(@"C:\Users", SelectedUserProfile);

            var folderMappings = new Dictionary<string, string[]>
            {
                { "Masaüstü",   new[] { "Desktop",   "Masaüstü"   } },
                { "Belgeler",   new[] { "Documents", "Belgeler",  "My Documents" } },
                { "Resimler",   new[] { "Pictures",  "Resimler",  "My Pictures"  } },
                { "İndirilenler", new[] { "Downloads", "İndirilenler" } },
                { "Müzik",      new[] { "Music",     "Müzik",     "My Music"     } },
                { "Videolar",   new[] { "Videos",    "Videolar",  "My Videos"    } }
            };

            foreach (var kvp in folderMappings)
            {
                string folderLabel = kvp.Key;
                foreach (var variant in kvp.Value)
                {
                    string path = Path.Combine(sourceUserDir, variant);
                    if (Directory.Exists(path))
                    {
                        PreviewItems.Add(new BackupPreviewItem
                        {
                            Name = folderLabel,
                            SourcePath = path,
                            SizeInfo = "Hesaplanıyor..."
                        });
                        break;
                    }
                }
            }

            if (BackupRootCustomFolders)
            {
                var selectedItems = CustomItemsToBackup.Where(item => item.IsSelected).ToList();
                foreach (var item in selectedItems)
                {
                    PreviewItems.Add(new BackupPreviewItem
                    {
                        Name = item.Name,
                        SourcePath = item.FullPath,
                        SizeInfo = "Hesaplanıyor..."
                    });
                }
            }

            if (BackupCredentials)
            {
                PreviewItems.Add(new BackupPreviewItem
                {
                    Name = "Windows Kimlik Bilgileri (.crd)",
                    SourcePath = "Win32 API (Live Credentials)",
                    SizeInfo = "API Live"
                });
            }

            IsPreviewVisible = true;

            System.Threading.Tasks.Task.Run(CalculatePreviewSizesAsync);
        }

        private async System.Threading.Tasks.Task CalculatePreviewSizesAsync()
        {
            foreach (var item in PreviewItems.ToList())
            {
                if (item.SourcePath == "Win32 API (Live Credentials)")
                {
                    continue;
                }

                if (Directory.Exists(item.SourcePath))
                {
                    string path = item.SourcePath;
                    await System.Threading.Tasks.Task.Run(() =>
                    {
                        try
                        {
                            long size = GetDirectorySize(path);
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                item.SizeInfo = FormatFileSize(size);
                            });
                        }
                        catch
                        {
                            App.Current.Dispatcher.Invoke(() =>
                            {
                                item.SizeInfo = "Erişilemedi";
                            });
                        }
                    });
                }
                else if (File.Exists(item.SourcePath))
                {
                    try
                    {
                        var fi = new FileInfo(item.SourcePath);
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            item.SizeInfo = FormatFileSize(fi.Length);
                        });
                    }
                    catch
                    {
                        App.Current.Dispatcher.Invoke(() =>
                        {
                            item.SizeInfo = "Erişilemedi";
                        });
                    }
                }
                else
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        item.SizeInfo = "Mevcut Değil";
                    });
                }
            }
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
                MessageBox.Show("Lütfen yedeklenecek bir kullanıcı profili seçin!", "Doğrulama Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(SelectedBackupDest) || !Directory.Exists(SelectedBackupDest))
            {
                MessageBox.Show("Lütfen geçerli bir yedekleme hedef klasörü seçin!", "Doğrulama Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsPreviewVisible = false;
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
                    string targetDir = Path.Combine(SelectedBackupDest, $"Yedek_{SelectedUserProfile}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");
                    Directory.CreateDirectory(targetDir);
                    
                    LogAndStatusUpdate($"[SİSTEM] Yedekleme klasörü oluşturuldu: {Path.GetFileName(targetDir)}", "Veri kurtarma oturumu hazırlanıyor...", true);

                    // Her klasör için bilinen tüm yerel isimler: İngilizce, Türkçe
                    var folderMappings = new Dictionary<string, string[]>
                    {
                        { "Masaüstü",   new[] { "Desktop",   "Masaüstü"   } },
                        { "Belgeler",   new[] { "Documents", "Belgeler",  "My Documents" } },
                        { "Resimler",   new[] { "Pictures",  "Resimler",  "My Pictures"  } },
                        { "İndirilenler", new[] { "Downloads", "İndirilenler" } },
                        { "Müzik",      new[] { "Music",     "Müzik",     "My Music"     } },
                        { "Videolar",   new[] { "Videos",    "Videolar",  "My Videos"    } }
                    };
                    double stepWeight = 100.0 / (folderMappings.Count + (BackupCredentials ? 1 : 0) + (BackupRootCustomFolders ? 1 : 0));

                    foreach (var kvp in folderMappings)
                    {
                        string folderLabel = kvp.Key;       // Türkçe çıktı etiketi
                        string[] folderVariants = kvp.Value;
                        
                        LogAndStatusUpdate(null, $"Kullanıcı '{folderLabel}' klasörü kurtarılıyor...", true);
                        
                        string dst = Path.Combine(targetDir, folderLabel);
                        bool copiedAny = false;

                        foreach (var variant in folderVariants)
                        {
                            string path = Path.Combine(sourceUserDir, variant);
                            if (Directory.Exists(path))
                            {
                                LogAndStatusUpdate($"[+] '{folderLabel}' için kaynak bulundu: {variant}", $"'{folderLabel}' kopyalanıyor...", true);
                                CopyDirectory(path, dst);
                                copiedAny = true;
                            }
                        }

                        if (copiedAny)
                        {
                            LogAndStatusUpdate($"[+] '{folderLabel}' klasörü başarıyla kurtarıldı → {dst}", $"'{folderLabel}' tamamlandı.", true);
                        }
                        else
                        {
                            LogAndStatusUpdate($"[-] '{folderLabel}' klasörü bulunamadı veya boş, atlanıyor.", $"'{folderLabel}' bulunamadı — atlandı.", true);
                        }

                        App.Current.Dispatcher.Invoke(() => BackupProgress += stepWeight);
                    }

                    if (BackupRootCustomFolders)
                    {
                        LogAndStatusUpdate(null, "Seçili C:\\ klasörleri kurtarılıyor...", true);
                        
                        try
                        {
                            string targetRootRescueDir = Path.Combine(targetDir, "C_Kök_Klasörler");
                            bool foundCustom = false;

                            var selectedItems = CustomItemsToBackup.Where(item => item.IsSelected).ToList();

                            foreach (var item in selectedItems)
                            {
                                if (!foundCustom)
                                {
                                    Directory.CreateDirectory(targetRootRescueDir);
                                    foundCustom = true;
                                }

                                if (item.IsDirectory)
                                {
                                    if (Directory.Exists(item.FullPath))
                                    {
                                        LogAndStatusUpdate(null, $"C:\\{item.Name} klasörü kurtarılıyor...", true);
                                        CopyDirectory(item.FullPath, Path.Combine(targetRootRescueDir, item.Name));
                                    }
                                }
                                else
                                {
                                    if (File.Exists(item.FullPath))
                                    {
                                        LogAndStatusUpdate($"[+] Dosya kurtarıldı: {item.Name}", $"C:\\{item.Name} dosyası kurtarılıyor...");
                                        try
                                        {
                                            File.Copy(item.FullPath, Path.Combine(targetRootRescueDir, item.Name), true);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"C:\\ kök klasörleri kopyalanırken hata: {ex.Message}", "WARN");
                        }

                        LogAndStatusUpdate(null, "C:\\ kök klasörleri başarıyla kurtarıldı.", true);
                        App.Current.Dispatcher.Invoke(() => BackupProgress += stepWeight);
                    }

                    if (BackupCredentials)
                    {
                        LogAndStatusUpdate(null, "Windows Kimlik Bilgileri yedekleniyor...", true);
                        try
                        {
                            string backupFile = Path.Combine(targetDir, "credentials.crd");
                            LogAndStatusUpdate(null, "Canlı kimlik bilgileri Win32 API ile dışa aktarılıyor...", true);
                            int res = CredBackupCredentials(IntPtr.Zero, backupFile, "123", 0);
                            if (res != 0)
                            {
                                LogAndStatusUpdate($"[+] credentials.crd başarıyla oluşturuldu (şifre: '123')", "Kimlik bilgileri başarıyla aktarıldı.", true);
                            }
                            else
                            {
                                int err = Marshal.GetLastWin32Error();
                                LogAndStatusUpdate($"[-] CredBackupCredentials API çağrısı hata verdi (Hata kodu: {res}, Win32 Hata Kodu: {err}).", "Kimlik bilgileri aktarılamadı.", true);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogAndStatusUpdate($"[!] API Hatası: {ex.Message}.", "API hatası oluştu.", true);
                        }

                        LogAndStatusUpdate(null, "Windows Kimlik Bilgileri yedekleme tamamlandı.", true);
                        App.Current.Dispatcher.Invoke(() => BackupProgress = 100);
                    }

                    // Kalan tüm logları temizle
                    LogAndStatusUpdate(null, IsBackupCancelled ? "Yedekleme iptal edildi." : "Yedekleme başarıyla tamamlandı!", true);

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        IsBackingUp = false;
                        if (IsBackupCancelled)
                        {
                            BackupStatus = "Yedekleme kullanıcı tarafından iptal edildi.";
                            BackupLogs.Add("[SİSTEM] Yedekleme işlemi başarıyla iptal edildi.");
                            MessageBox.Show("Yedekleme işlemi iptal edildi. Hedef klasörde eksik veriler bulunabilir.", "Yedekleme İptal Edildi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else
                        {
                            BackupStatus = "Yedekleme başarıyla tamamlandı!";
                            IsBackupWizardVisible = false;
                            MessageBox.Show($"Seçili kullanıcı yedeği başarıyla tamamlandı!\nKayıt yeri: {targetDir}", "Kurtarma Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                catch (Exception ex)
                {
                    // Hata durumunda logları temizle
                    LogAndStatusUpdate(null, $"Hata: {ex.Message}", true);
                    
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        BackupStatus = $"Hata: {ex.Message}";
                        IsBackingUp = false;
                        MessageBox.Show($"Kurtarma işlemi sırasında hata oluştu:\n{ex.Message}", "Kurtarma Tamamlanamadı", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    BackupLogs.Add("[SİSTEM] Yedekleme DURAKLATıLDI.");
                    BackupStatus = "Yedekleme oturumu duraklatıldı...";
                }
                else
                {
                    BackupLogs.Add("[SİSTEM] Yedekleme DEVAM ETTİRİLDİ.");
                    BackupStatus = "Yedekleme oturumu devam ediyor...";
                }
            });
        }

        private void ExecuteStopBackup()
        {
            var res = MessageBox.Show("Aktif yedekleme işlemini durdurmak ve iptal etmek istediğinizden emin misiniz?", "Yedeklemeyi İptal Et", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                IsBackupCancelled = true;
                App.Current.Dispatcher.Invoke(() =>
                {
                    if (BackupLogs.Count > 150) BackupLogs.RemoveAt(0);
                    BackupLogs.Add("[SİSTEM] Yedekleme işlemi iptal ediliyor...");
                });
            }
        }
    }
}
