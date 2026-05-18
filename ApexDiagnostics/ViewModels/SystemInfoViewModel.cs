using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ApexDiagnostics.Core;
using ApexDiagnostics.Helpers;

namespace ApexDiagnostics.ViewModels
{
    public class InfoItem : ViewModelBase
    {
        public string Category { get; set; } = "";
        public string Property { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class InfoCategory : ViewModelBase
    {
        public string Name { get; set; } = "";
        public ObservableCollection<InfoItem> Items { get; } = new();
    }

    public class SystemInfoViewModel : ViewModelBase
    {
        private readonly TelemetryManager _telemetry;
        public ObservableCollection<InfoCategory> Categories { get; } = new();

        public ICommand RefreshCommand { get; }
        public ICommand ExportCommand { get; }

        public SystemInfoViewModel(TelemetryManager telemetry)
        {
            _telemetry = telemetry;
            RefreshCommand = new RelayCommand(() => Task.Run(LoadSystemInfo));
            ExportCommand = new RelayCommand(ExportToFile);
            
            if (_telemetry.IsInitialized)
            {
                Task.Run(LoadSystemInfo);
            }
            else
            {
                _telemetry.OnInitialized += () => Task.Run(LoadSystemInfo);
            }

            LanguageManager.LanguageChanged += () => Task.Run(LoadSystemInfo);
        }

        private void LoadSystemInfo()
        {
            try
            {
                var newCategories = new List<InfoCategory>();
                
                // 1. CPU
                var cpuCat = CreateCategory("PROCESSOR (CPU)");
                newCategories.Add(cpuCat);
                AddInfo(cpuCat, "Model", _telemetry.CpuName);
                AddInfo(cpuCat, "Cores / Threads", $"{_telemetry.PhysicalCores} Cores / {_telemetry.LogicalCores} Threads");
                AddInfo(cpuCat, "Architecture", _telemetry.CpuArch);
                AddInfo(cpuCat, "Socket", _telemetry.CpuSocket);
                AddInfo(cpuCat, "L2 Cache", _telemetry.L2CacheKB);
                AddInfo(cpuCat, "L3 Cache", _telemetry.L3CacheMB);
                QueryWmi(cpuCat, "Win32_Processor", new[] { "Manufacturer", "MaxClockSpeed", "Version" });

                // 2. RAM (Per stick)
                var ramCat = CreateCategory("MEMORY (RAM)");
                newCategories.Add(ramCat);
                AddInfo(ramCat, "Total Physical", $"{_telemetry.TotalRamGB:F2} GB");
                
                try {
                    using var searcher = new ManagementObjectSearcher("SELECT BankLabel, DeviceLocator, Capacity, Manufacturer, PartNumber, Speed, ConfiguredClockSpeed, SerialNumber, FormFactor FROM Win32_PhysicalMemory");
                    int stickIndex = 1;
                    foreach (var item in searcher.Get()) {
                        AddInfo(ramCat, $"--- STICK #{stickIndex} ---", item["BankLabel"]?.ToString() ?? "N/A");
                        AddInfo(ramCat, "Slot", item["DeviceLocator"]?.ToString() ?? "N/A");
                        AddInfo(ramCat, "Manufacturer", item["Manufacturer"]?.ToString() ?? "N/A");
                        long cap = Convert.ToInt64(item["Capacity"] ?? 0);
                        AddInfo(ramCat, "Capacity", $"{cap / (1024 * 1024 * 1024)} GB");
                        AddInfo(ramCat, "Speed", $"{item["Speed"]} MHz (Configured: {item["ConfiguredClockSpeed"]} MHz)");
                        AddInfo(ramCat, "Part Number", item["PartNumber"]?.ToString()?.Trim() ?? "N/A");
                        AddInfo(ramCat, "Form Factor", GetRamFormFactor(item["FormFactor"]?.ToString() ?? ""));
                        stickIndex++;
                    }
                } catch { }

                // 3. STORAGE
                var storageCat = CreateCategory("STORAGE (DISKS)");
                newCategories.Add(storageCat);
                try {
                    using var searcher = new ManagementObjectSearcher("SELECT Index, Model, InterfaceType, Size, Status, SerialNumber FROM Win32_DiskDrive");
                    foreach (var item in searcher.Get()) {
                        string model = item["Model"]?.ToString() ?? "N/A";
                        string interfaceType = item["InterfaceType"]?.ToString() ?? "N/A";
                        int diskIndex = Convert.ToInt32(item["Index"] ?? 0);

                        AddInfo(storageCat, "--- DISK ---", model);
                        AddInfo(storageCat, "Disk Index", $"Drive #{diskIndex}");
                        AddInfo(storageCat, "Detailed Type", ResolveDetailedDiskType(model, interfaceType, diskIndex));
                        long size = Convert.ToInt64(item["Size"] ?? 0);
                        AddInfo(storageCat, "Total Size", $"{size / (1024 * 1024 * 1024)} GB");
                        AddInfo(storageCat, "Health Status", item["Status"]?.ToString() ?? "N/A");
                        AddInfo(storageCat, "Serial", item["SerialNumber"]?.ToString()?.Trim() ?? "N/A");
                    }
                } catch { }

                // 4. MOTHERBOARD
                var mbCat = CreateCategory("MOTHERBOARD & BIOS");
                newCategories.Add(mbCat);
                QueryWmi(mbCat, "Win32_BaseBoard", new[] { "Manufacturer", "Product", "Version", "SerialNumber" });
                QueryWmi(mbCat, "Win32_BIOS", new[] { "Manufacturer", "Name", "Version", "ReleaseDate" });

                // 5. GPU
                var gpuCat = CreateCategory("GRAPHICS (GPU)");
                newCategories.Add(gpuCat);
                QueryWmi(gpuCat, "Win32_VideoController", new[] { "Name", "DriverVersion", "AdapterRAM" });

                // Update UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Categories.Clear();
                    foreach (var cat in newCategories) Categories.Add(cat);
                });
            }
            catch (Exception ex) { Logger.Log($"LoadSystemInfo background error: {ex.Message}", "ERROR"); }
        }

        private InfoCategory CreateCategory(string name) => new InfoCategory { Name = LocalizeCategory(name) };

        private void AddInfo(InfoCategory cat, string prop, string val)
        {
            string localizedProp = prop;
            if (prop.StartsWith("---"))
            {
                if (prop.Contains("STICK"))
                {
                    string stickText = GetTranslation("SysStickLabel", "STICK");
                    localizedProp = prop.Replace("STICK", stickText);
                }
                else if (prop.Contains("DISK"))
                {
                    string diskText = GetTranslation("SysDiskLabel", "DISK");
                    localizedProp = prop.Replace("DISK", diskText);
                }
            }
            else
            {
                localizedProp = LocalizeProperty(prop);
            }
            cat.Items.Add(new InfoItem { Category = cat.Name, Property = localizedProp, Value = val });
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

        private string LocalizeCategory(string name)
        {
            string key = name switch
            {
                "PROCESSOR (CPU)" => "SysCatCpu",
                "MEMORY (RAM)" => "SysCatRam",
                "STORAGE (DISKS)" => "SysCatStorage",
                "MOTHERBOARD & BIOS" => "SysCatMotherboard",
                "GRAPHICS (GPU)" => "SysCatGpu",
                _ => name
            };
            return GetTranslation(key, name);
        }

        private string LocalizeProperty(string prop)
        {
            string key = prop switch
            {
                "Model" => "SysPropModel",
                "Cores / Threads" => "SysPropCoresThreads",
                "Architecture" => "SysPropArchitecture",
                "Socket" => "SysPropSocket",
                "L2 Cache" => "SysPropL2Cache",
                "L3 Cache" => "SysPropL3Cache",
                "Manufacturer" => "SysPropManufacturer",
                "MaxClockSpeed" => "SysPropMaxClockSpeed",
                "Version" => "SysPropVersion",
                "Total Physical" => "SysPropTotalPhysical",
                "Slot" => "SysPropSlot",
                "Capacity" => "SysPropCapacity",
                "Speed" => "SysPropSpeed",
                "Part Number" => "SysPropPartNumber",
                "Form Factor" => "SysPropFormFactor",
                "Total Size" => "SysPropTotalSize",
                "Health Status" => "SysPropHealthStatus",
                "Serial" => "SysPropSerial",
                "Disk Index" => "SysPropDiskIndex",
                "Detailed Type" => "SysPropDetailedType",
                "Product" => "SysPropProduct",
                "ReleaseDate" => "SysPropReleaseDate",
                "DriverVersion" => "SysPropDriverVersion",
                "AdapterRAM" => "SysPropAdapterRAM",
                _ => prop
            };
            return GetTranslation(key, prop);
        }

        private void QueryWmi(InfoCategory cat, string @class, string[] props, string filter = "")
        {
            try
            {
                string query = $"SELECT {string.Join(",", props)} FROM {@class}";
                if (!string.IsNullOrEmpty(filter)) query += $" WHERE {filter}";
                using var s = new ManagementObjectSearcher(query);
                foreach (var item in s.Get())
                {
                    foreach (var p in props)
                    {
                        try
                        {
                            string val = item[p]?.ToString() ?? "N/A";
                            if (p == "Capacity" || p == "Size") val = $"{Convert.ToInt64(val) / (1024 * 1024 * 1024)} GB";
                            if (p == "AdapterRAM") val = $"{Convert.ToInt64(val) / (1024 * 1024)} MB";
                            if (p == "FormFactor") val = GetRamFormFactor(val);
                            AddInfo(cat, p, val);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private string GetRamFormFactor(string val) => val switch { "8"=>"DIMM", "12"=>"SODIMM", "13"=>"SRWM", "14"=>"STWM", _=>val };

        private void ExportToFile()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = "ApexSystemReport",
                    DefaultExt = ".txt",
                    Filter = "Text documents (.txt)|*.txt|All files (*.*)|*.*",
                    Title = "Export System Hardware Report"
                };

                if (dialog.ShowDialog() == true)
                {
                    var lines = new List<string> 
                    { 
                        "============================================================",
                        "                APEX HARDWARE DIAGNOSTIC SUITE               ",
                        "                    SYSTEM INVENTORY REPORT                  ",
                        "============================================================",
                        $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        $"CPU: {_telemetry.CpuName}",
                        $"RAM: {_telemetry.TotalRamGB:F2} GB",
                        "============================================================",
                        "" 
                    };

                    foreach (var cat in Categories)
                    {
                        lines.Add($"[ {cat.Name.ToUpper()} ]");
                        lines.Add(new string('-', cat.Name.Length + 4));
                        foreach (var item in cat.Items)
                        {
                            lines.Add($"{item.Property,-25}: {item.Value}");
                        }
                        lines.Add("");
                    }

                    System.IO.File.WriteAllLines(dialog.FileName, lines);
                    Logger.Log($"System report exported to {dialog.FileName}");
                    
                    MessageBox.Show($"Report successfully exported to:\n{dialog.FileName}", 
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex) 
            { 
                Logger.Log($"Export failed: {ex.Message}", "ERROR");
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ResolveDetailedDiskType(string model, string interfaceType, int diskIndex)
        {
            string modelUpper = model.ToUpperInvariant();
            
            // 1. Check MSFT_PhysicalDisk in root\Microsoft\Windows\Storage for modern Windows 10/11/WinPE bus and media types
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", 
                    $"SELECT MediaType, BusType FROM MSFT_PhysicalDisk WHERE DeviceId = '{diskIndex}'");
                foreach (var obj in searcher.Get())
                {
                    ushort mediaType = Convert.ToUInt16(obj["MediaType"] ?? 0); // 3=HDD, 4=SSD, 0=Unspecified
                    ushort busType = Convert.ToUInt16(obj["BusType"] ?? 0);     // 11=SATA, 17=NVMe, 7=USB, etc.
                    
                    string media = mediaType switch
                    {
                        3 => "HDD (Mechanical)",
                        4 => "SSD (Solid State)",
                        _ => modelUpper.Contains("SSD") || modelUpper.Contains("NVME") || modelUpper.Contains("M.2") ? "SSD (Solid State)" : "HDD"
                    };

                    string bus = busType switch
                    {
                        17 => "NVMe (PCIe)",
                        11 => "SATA",
                        7 => "USB External",
                        8 => "SCSI",
                        3 => "IDE",
                        _ => interfaceType
                    };

                    // Intelligent Gen4 / Gen5 Speed Class Detection from popular high-end NVMe model names!
                    if (bus.Contains("NVMe"))
                    {
                        if (modelUpper.Contains("990 PRO") || modelUpper.Contains("980 PRO") || modelUpper.Contains("SN850") || 
                            modelUpper.Contains("KC3000") || modelUpper.Contains("T500") || modelUpper.Contains("GEN4"))
                        {
                            return GetTranslation("DiskTypeNvmeGen4", "NVMe SSD (Ultra-Speed PCIe Gen 4.0)");
                        }
                        if (modelUpper.Contains("T700") || modelUpper.Contains("T705") || modelUpper.Contains("GEN5"))
                        {
                            return GetTranslation("DiskTypeNvmeGen5", "NVMe SSD (Extreme PCIe Gen 5.0)");
                        }
                        if (modelUpper.Contains("970 EVO") || modelUpper.Contains("970 PRO") || modelUpper.Contains("SN750") || modelUpper.Contains("GEN3"))
                        {
                            return GetTranslation("DiskTypeNvmeGen3", "NVMe SSD (High-Speed PCIe Gen 3.0)");
                        }
                        return GetTranslation("DiskTypeNvmeGeneric", "NVMe SSD (PCIe Solid State)");
                    }

                    if (media.Contains("SSD"))
                    {
                        return $"{bus} SSD (" + GetTranslation("LabelSolidState", "Solid State Drive") + ")";
                    }
                    
                    return $"{bus} {media}";
                }
            }
            catch { /* Fallback to keyword-based analysis if MSFT namespace is not in this WinPE build */ }

            // 2. Intelligent Keyword-based fallback parsing
            if (modelUpper.Contains("NVME") || modelUpper.Contains("PCIe") || modelUpper.Contains("M2") || modelUpper.Contains("M.2"))
            {
                if (modelUpper.Contains("990 PRO") || modelUpper.Contains("980 PRO") || modelUpper.Contains("SN850") || modelUpper.Contains("KC3000") || modelUpper.Contains("T500"))
                    return GetTranslation("DiskTypeNvmeGen4", "NVMe SSD (Ultra-Speed PCIe Gen 4.0)");
                return GetTranslation("DiskTypeNvmeGeneric", "NVMe SSD (PCIe Solid State)");
            }
            
            if (modelUpper.Contains("SSD") || modelUpper.Contains("SATA") || modelUpper.Contains("EVO") || modelUpper.Contains("PRO") || modelUpper.Contains("KINGSTON") || modelUpper.Contains("CRUCIAL"))
            {
                if (interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase))
                    return GetTranslation("DiskTypePortableSsd", "Portable SSD (USB External)");
                return GetTranslation("DiskTypeSataSsd", "SATA SSD (High-Speed Solid State)");
            }

            if (interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase))
            {
                return GetTranslation("DiskTypeUsbDisk", "USB External Disk Drive");
            }

            if (interfaceType.Equals("IDE", StringComparison.OrdinalIgnoreCase) || interfaceType.Equals("SCSI", StringComparison.OrdinalIgnoreCase))
            {
                // Most modern SATA drives are reported as IDE/SCSI by legacy Win32_DiskDrive
                return GetTranslation("DiskTypeSataHdd", "SATA Hard Drive (Mechanical HDD)");
            }

            return $"{interfaceType} HDD";
        }
    }
}
