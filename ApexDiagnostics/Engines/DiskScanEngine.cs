using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Engines
{
    public class DiskScanEngine : EngineBase
    {
        public event Action<long, byte>? OnSectorScanned;

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        private const uint FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(
            IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        private static extern bool SetFilePointerEx(
            IntPtr hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize,
            byte[] lpOutBuffer, uint nOutBufferSize, out uint lpBytesReturned, IntPtr lpOverlapped);

        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private Task? _worker;

        public int SelectedDriveNumber { get; set; } = 0;
        public int BlockSizeKB { get; set; } = 64;
        public int RetryCount { get; set; } = 3;
        public int TotalPasses { get; set; } = 1;

        public string CurrentDisk { get; private set; } = "";
        public long TotalBytes { get; private set; }
        public long ScannedBytes { get; private set; }
        public long GoodSectors { get; private set; }
        public long SlowSectors { get; private set; }
        public long DelayedSectors { get; private set; }
        public long WeakSectors { get; private set; }
        public long BadSectors { get; private set; }
        public long TimeoutSectors { get; private set; }
        public long ReadErrors { get; private set; }
        public long RetrySuccesses { get; private set; }
        public string CurrentSectorRange { get; private set; } = "0 - 0";
        public string EstimatedTimeRemaining { get; private set; } = "--:--:--";
        public int CurrentPass { get; private set; } = 1;
        public double ProgressPercent => TotalBytes > 0 ? (double)ScannedBytes / TotalBytes * 100.0 : 0;
        public bool ScanComplete { get; private set; }
        public double SpeedMBps { get; private set; }
        public double LastReadLatencyMs { get; private set; }
        public double MaxReadLatencyMs { get; private set; }
        public double AvgReadLatencyMs { get; private set; }
        public bool UsingDirectIO { get; private set; }
        public string ScanErrorMessage { get; private set; } = "";

        private long _latencySum;
        private long _latencyCount;

        public DiskScanEngine() : base("Disk Surface Scan") { }

        protected override void OnStart(CancellationToken token)
        {
            ScanErrorMessage = "";
            ScanComplete = false;
            BadSectors = 0; SlowSectors = 0; DelayedSectors = 0;
            WeakSectors = 0; TimeoutSectors = 0; ReadErrors = 0;
            RetrySuccesses = 0; GoodSectors = 0;
            ScannedBytes = 0; _latencySum = 0; _latencyCount = 0;
            MaxReadLatencyMs = 0; AvgReadLatencyMs = 0;
            CurrentPass = 1;

            _worker = Task.Factory.StartNew(() => ScanDrive(token),
                token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                while (!token.IsCancellationRequested && _isRunning)
                {
                    ElapsedTime = sw.Elapsed;
                    await Task.Delay(500, token);
                }
            }, token);
        }

        protected override void OnStop()
        {
            try { _worker?.Wait(10000); } catch { }
        }

        private void ScanDrive(CancellationToken token)
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            string path = $@"\\.\PhysicalDrive{SelectedDriveNumber}";
            CurrentDisk = $"PhysicalDrive{SelectedDriveNumber}";

            try
            {
                for (CurrentPass = 1; CurrentPass <= TotalPasses; CurrentPass++)
                {
                    if (token.IsCancellationRequested) break;
                    Logger.Log($"Starting Pass {CurrentPass}/{TotalPasses} on {CurrentDisk}");

                    ScannedBytes = 0;
                    _latencySum = 0; _latencyCount = 0;

                    if (!TryScanWithDirectIO(path, token))
                        TryScanWithFileStream(path, token);

                    Logger.Log($"Pass {CurrentPass} complete: Bad={BadSectors} Slow={SlowSectors} Delayed={DelayedSectors} Weak={WeakSectors} Timeout={TimeoutSectors}");
                }

                if (!token.IsCancellationRequested)
                {
                    ScanComplete = true;
                    CurrentSectorRange = "DONE";
                    EstimatedTimeRemaining = "00:00:00";
                    Logger.Log($"Scan complete: {CurrentDisk} — Total bad sectors: {BadSectors}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Scan failed: {ex.Message}", "ERROR");
                ScanErrorMessage = ex.Message;
                _isRunning = false;
            }
        }

        private bool TryScanWithDirectIO(string path, CancellationToken token)
        {
            IntPtr handle = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_NO_BUFFERING | FILE_FLAG_SEQUENTIAL_SCAN, IntPtr.Zero);

            if (handle == INVALID_HANDLE_VALUE)
            {
                Logger.Log($"Direct I/O open failed. Falling back to FileStream.", "WARN");
                return false;
            }

            UsingDirectIO = true;
            try
            {
                long diskSize = GetDiskSizeViaHandle(handle);
                if (diskSize <= 0) diskSize = GetDiskSizeViaWMI(SelectedDriveNumber);
                if (diskSize <= 0) return false;

                TotalBytes = diskSize;

                int blockSize = BlockSizeKB * 1024;
                if (blockSize % 512 != 0) blockSize = (blockSize / 512 + 1) * 512;
                byte[] buffer = new byte[blockSize];

                long position = 0;
                var speedTimer = Stopwatch.StartNew();
                var ioTimer = new Stopwatch();
                int consecutiveErrors = 0;

                while (position < diskSize && !token.IsCancellationRequested)
                {
                    int toRead = (int)Math.Min(blockSize, diskSize - position);
                    if (toRead % 512 != 0) toRead = (toRead / 512) * 512;
                    if (toRead <= 0) break;

                    long startSector = position / 512;
                    long endSector = (position + toRead) / 512;
                    CurrentSectorRange = $"{startSector:N0} — {endSector:N0}";

                    SetFilePointerEx(handle, position, out _, 0);

                    ioTimer.Restart();
                    bool readOk = ReadFile(handle, buffer, (uint)toRead, out uint bytesRead, IntPtr.Zero);
                    ioTimer.Stop();
                    double latencyMs = ioTimer.Elapsed.TotalMilliseconds;
                    LastReadLatencyMs = latencyMs;

                    if (readOk && bytesRead > 0)
                    {
                        ClassifySector(latencyMs, startSector, false);
                        position += bytesRead;
                        consecutiveErrors = 0;
                    }
                    else
                    {
                        bool recovered = false;
                        for (int r = 1; r <= RetryCount && !token.IsCancellationRequested; r++)
                        {
                            Thread.Sleep(100 * r);
                            SetFilePointerEx(handle, position, out _, 0);
                            ioTimer.Restart();
                            readOk = ReadFile(handle, buffer, (uint)toRead, out bytesRead, IntPtr.Zero);
                            ioTimer.Stop();
                            latencyMs = ioTimer.Elapsed.TotalMilliseconds;

                            if (readOk && bytesRead > 0)
                            {
                                recovered = true;
                                RetrySuccesses++;
                                ClassifySector(latencyMs, startSector, true);
                                break;
                            }
                        }

                        if (!recovered)
                        {
                            ReadErrors++;
                            BadSectors += (toRead / 512);
                            OnSectorScanned?.Invoke(startSector * 512, 5); // 5 = Dark Red (Bad)
                            consecutiveErrors++;
                            if (consecutiveErrors >= 50)
                            {
                                throw new IOException("Too many consecutive read errors. Drive may have disconnected.");
                            }
                        }
                        else
                        {
                            consecutiveErrors = 0;
                        }
                        position += toRead;
                    }

                    ScannedBytes = position;
                    UpdateSpeedAndEta(position, speedTimer);
                }

                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private void TryScanWithFileStream(string path, CancellationToken token)
        {
            UsingDirectIO = false;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.SequentialScan);
                long diskSize = fs.Length;
                TotalBytes = diskSize;

                int blockSize = BlockSizeKB * 1024;
                byte[] buffer = new byte[blockSize];
                long position = 0;
                var speedTimer = Stopwatch.StartNew();
                var ioTimer = new Stopwatch();

                while (position < diskSize && !token.IsCancellationRequested)
                {
                    int toRead = (int)Math.Min(blockSize, diskSize - position);
                    long startSector = position / 512;
                    long endSector = (position + toRead) / 512;
                    CurrentSectorRange = $"{startSector:N0} — {endSector:N0}";

                    fs.Position = position;
                    ioTimer.Restart();

                    try
                    {
                        int bytesRead = fs.Read(buffer, 0, toRead);
                        ioTimer.Stop();
                        double latencyMs = ioTimer.Elapsed.TotalMilliseconds;
                        LastReadLatencyMs = latencyMs;

                        if (bytesRead <= 0) break;
                        ClassifySector(latencyMs, startSector, false);
                        position += bytesRead;
                    }
                    catch (IOException)
                    {
                        ioTimer.Stop();
                        ReadErrors++;
                        BadSectors += (toRead / 512);
                        OnSectorScanned?.Invoke(startSector * 512, 5); // 5 = Dark Red (Bad)
                        position += toRead;
                    }

                    ScannedBytes = position;
                    UpdateSpeedAndEta(position, speedTimer);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Cannot open {path}: {ex.Message}", "ERROR");
            }
        }

        private void ClassifySector(double latencyMs, long sector, bool wasRetry)
        {
            _latencySum += (long)latencyMs;
            _latencyCount++;
            AvgReadLatencyMs = (double)_latencySum / _latencyCount;
            if (latencyMs > MaxReadLatencyMs) MaxReadLatencyMs = latencyMs;

            byte status = 1; // Good
            if (wasRetry) { WeakSectors++; status = 4; }
            else if (latencyMs > 5000) 
            { 
                TimeoutSectors++; 
                BadSectors++; // Count timeouts as bad
                status = 6; 
            }
            else if (latencyMs > 1000) { WeakSectors++; status = 4; }
            else if (latencyMs > 200) { DelayedSectors++; status = 3; }
            else if (latencyMs > 50) { SlowSectors++; status = 2; }
            else GoodSectors++;

            OnSectorScanned?.Invoke(sector * 512, status);
        }

        private void UpdateSpeedAndEta(long position, Stopwatch speedTimer)
        {
            double elapsed = speedTimer.Elapsed.TotalSeconds;
            if (elapsed > 0)
            {
                SpeedMBps = Math.Round((position / (1024.0 * 1024)) / elapsed, 1);
                if (SpeedMBps > 0)
                {
                    double remainingSeconds = ((TotalBytes - position) / (1024.0 * 1024)) / SpeedMBps;
                    EstimatedTimeRemaining = TimeSpan.FromSeconds(remainingSeconds).ToString(@"hh\:mm\:ss");
                }
            }
        }

        private long GetDiskSizeViaHandle(IntPtr handle)
        {
            try
            {
                byte[] outBuffer = new byte[256];
                if (DeviceIoControl(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, IntPtr.Zero, 0, outBuffer, (uint)outBuffer.Length, out _, IntPtr.Zero))
                {
                    return BitConverter.ToInt64(outBuffer, 24);
                }
            }
            catch { }
            return 0;
        }

        private long GetDiskSizeViaWMI(int driveNumber)
        {
            try
            {
                using var s = new ManagementObjectSearcher($"SELECT Size FROM Win32_DiskDrive WHERE Index={driveNumber}");
                foreach (var i in s.Get()) return Convert.ToInt64(i["Size"] ?? 0);
            }
            catch { }
            return 0;
        }
    }
}
