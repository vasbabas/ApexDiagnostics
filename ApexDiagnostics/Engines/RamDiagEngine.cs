using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Engines
{
    public class MemoryFault
    {
        public long Offset { get; set; }
        public byte Expected { get; set; }
        public byte Actual { get; set; }
        public string TestName { get; set; } = "";
    }

    public class RamDiagEngine : EngineBase
    {
        private Task? _worker;
        private readonly double _totalRamGb;
        private readonly bool _isDeepMode;

        public event Action<int, byte>? OnBlockResult;

        public long AllocatedMB { get; private set; }
        public long TargetMB { get; private set; }
        public long TotalErrorsFound { get; private set; }
        public string CurrentTestName { get; private set; } = "Initializing...";
        public double CurrentTestProgress { get; private set; }
        public int CurrentPass { get; private set; }
        public List<MemoryFault> DetectedFaults { get; } = new();

        public RamDiagEngine(double totalRamGb, bool isDeepMode) : base(isDeepMode ? "RAM Deep Validation" : "RAM Quick Scan")
        {
            _totalRamGb = totalRamGb;
            _isDeepMode = isDeepMode;
        }

        protected override void OnStart(CancellationToken token)
        {
            TotalErrorsFound = 0;
            CurrentPass = 0;
            CurrentTestProgress = 0;
            DetectedFaults.Clear();

            // Quick mode: 40% of RAM. Deep mode: 85% of RAM (leaving OS headroom)
            double targetRatio = _isDeepMode ? 0.85 : 0.40;
            TargetMB = (long)(_totalRamGb * 1024 * targetRatio);

            _worker = Task.Factory.StartNew(() => RunDiagnostics(token),
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
            var workerToWait = _worker;
            Task.Run(() => {
                try { workerToWait?.Wait(10000); } catch { }
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            });
        }

        private void RunDiagnostics(CancellationToken token)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Normal;
            
            int blockSize = 64 * 1024 * 1024; // 64MB blocks
            List<byte[]> memoryBlocks = new();
            List<GCHandle> handles = new();

            try
            {
                CurrentTestName = "Allocating Memory...";
                long currentAllocated = 0;
                long targetBytes = TargetMB * 1024 * 1024;

                while (!token.IsCancellationRequested && currentAllocated < targetBytes)
                {
                    try
                    {
                        byte[] block = new byte[blockSize];
                        handles.Add(GCHandle.Alloc(block, GCHandleType.Pinned));
                        memoryBlocks.Add(block);
                        currentAllocated += blockSize;
                        AllocatedMB = currentAllocated / (1024 * 1024);
                        CurrentTestProgress = (double)currentAllocated / targetBytes;
                        Thread.Sleep(10);
                    }
                    catch (OutOfMemoryException)
                    {
                        Logger.Log("RAM Engine hit hard limit during allocation.", "WARN");
                        TargetMB = AllocatedMB;
                        break;
                    }
                }

                if (token.IsCancellationRequested) return;

                CurrentPass = 1;
                while (!token.IsCancellationRequested)
                {
                    if (!RunTest("0x00 / 0xFF Pattern", memoryBlocks, token, RunPatternFillTest)) break;
                    if (!RunTest("Moving Inversions", memoryBlocks, token, RunMovingInversionsTest)) break;
                    if (!RunTest("Random Data Verification", memoryBlocks, token, RunRandomPatternTest)) break;
                    
                    if (_isDeepMode)
                    {
                        if (!RunTest("Address Line Verification", memoryBlocks, token, RunAddressLineTest)) break;
                        if (!RunTest("Hammer Stress Test", memoryBlocks, token, RunHammerTest)) break;
                    }

                    CurrentPass++;
                    if (!_isDeepMode && CurrentPass > 1) break; // Quick mode runs only 1 pass
                }
                
                CurrentTestName = "Finished.";
                CurrentTestProgress = 1.0;
            }
            finally
            {
                CurrentTestName = "Releasing Memory...";
                foreach (var h in handles)
                {
                    if (h.IsAllocated) h.Free();
                }
                handles.Clear();
                memoryBlocks.Clear();
                AllocatedMB = 0;
            }
        }

        private bool RunTest(string name, List<byte[]> blocks, CancellationToken token, Action<byte[], long, CancellationToken> testAction)
        {
            CurrentTestName = name;
            CurrentTestProgress = 0;
            long totalBlocks = blocks.Count;

            for (int i = 0; i < totalBlocks; i++)
            {
                if (token.IsCancellationRequested) return false;
                long errorsBefore = TotalErrorsFound;
                testAction(blocks[i], (long)i * blocks[i].Length, token);
                CurrentTestProgress = (double)(i + 1) / totalBlocks;
                
                byte status = (TotalErrorsFound > errorsBefore) ? (byte)6 : (byte)1;
                OnBlockResult?.Invoke(i, status);
            }
            return true;
        }

        private unsafe void RunPatternFillTest(byte[] block, long baseOffset, CancellationToken token)
        {
            fixed (byte* p = block)
            {
                int len = block.Length;
                // Write 0xAA
                for (int i = 0; i < len; i += 8) *(ulong*)(p + i) = 0xAAAAAAAAAAAAAAAA;
                // Verify 0xAA and Write 0x55
                for (int i = 0; i < len; i++)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i & 0x3FF) == 0) Thread.Yield();
                    if (p[i] != 0xAA) ReportFault(baseOffset + i, 0xAA, p[i], "Pattern 0xAA");
                    p[i] = 0x55;
                }
                // Verify 0x55
                for (int i = 0; i < len; i++)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i & 0x3FF) == 0) Thread.Yield();
                    if (p[i] != 0x55) ReportFault(baseOffset + i, 0x55, p[i], "Pattern 0x55");
                }
            }
        }

        private unsafe void RunMovingInversionsTest(byte[] block, long baseOffset, CancellationToken token)
        {
            fixed (byte* p = block)
            {
                int len = block.Length;
                // Write 0s
                for (int i = 0; i < len; i += 8) *(ulong*)(p + i) = 0;
                
                // Forward pass: verify 0s, write 1s
                for (int i = 0; i < len; i++)
                {
                    if (p[i] != 0) ReportFault(baseOffset + i, 0, p[i], "Moving Inversions (Fwd)");
                    p[i] = 0xFF;
                }
                
                // Backward pass: verify 1s, write 0s
                for (int i = len - 1; i >= 0; i--)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i & 0x3FF) == 0) Thread.Yield();
                    if (p[i] != 0xFF) ReportFault(baseOffset + i, 0xFF, p[i], "Moving Inversions (Bwd)");
                    p[i] = 0;
                }
            }
        }

        private unsafe void RunRandomPatternTest(byte[] block, long baseOffset, CancellationToken token)
        {
            Random rnd = new(CurrentPass ^ (int)baseOffset);
            byte[] pattern = new byte[4096];
            rnd.NextBytes(pattern);

            fixed (byte* p = block)
            fixed (byte* ptrn = pattern)
            {
                int len = block.Length;
                // Write
                for (int i = 0; i < len; i += 4096)
                {
                    Buffer.MemoryCopy(ptrn, p + i, len - i, 4096);
                }
                // Verify
                for (int i = 0; i < len; i++)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i & 0x3FF) == 0) Thread.Yield();
                    byte expected = pattern[i % 4096];
                    if (p[i] != expected) ReportFault(baseOffset + i, expected, p[i], "Random Pattern");
                }
            }
        }

        private unsafe void RunAddressLineTest(byte[] block, long baseOffset, CancellationToken token)
        {
            fixed (byte* p = block)
            {
                int len = block.Length;
                // Write address XOR signature
                for (int i = 0; i < len; i += 4)
                {
                    uint addr = (uint)(baseOffset + i);
                    *(uint*)(p + i) = addr ^ 0x55AA55AA;
                }
                // Verify
                for (int i = 0; i < len; i += 4)
                {
                    if (token.IsCancellationRequested) return;
                    if ((i & 0x3FF) == 0) Thread.Yield();
                    uint addr = (uint)(baseOffset + i);
                    uint expected = addr ^ 0x55AA55AA;
                    uint actual = *(uint*)(p + i);
                    if (actual != expected)
                    {
                        ReportFault(baseOffset + i, (byte)(expected & 0xFF), (byte)(actual & 0xFF), "Address Line");
                    }
                }
            }
        }

        private unsafe void RunHammerTest(byte[] block, long baseOffset, CancellationToken token)
        {
            fixed (byte* p = block)
            {
                int len = block.Length;
                // Basic rowhammer simulation: Rapidly toggle between distant memory locations
                // Write background
                for (int i = 0; i < len; i += 8) *(ulong*)(p + i) = 0x5555555555555555;
                
                int aggressor1 = len / 4;
                int aggressor2 = (len / 4) * 3;
                
                for (int i = 0; i < 100000; i++)
                {
                    p[aggressor1] = (byte)~p[aggressor1];
                    p[aggressor2] = (byte)~p[aggressor2];
                }

                // Verify background hasn't flipped
                for (int i = 0; i < len; i++)
                {
                    if (i == aggressor1 || i == aggressor2) continue;
                    if (p[i] != 0x55) ReportFault(baseOffset + i, 0x55, p[i], "Hammer Stress");
                }
            }
        }

        private void ReportFault(long offset, byte expected, byte actual, string testName)
        {
            TotalErrorsFound++;
            var fault = new MemoryFault
            {
                Offset = offset,
                Expected = expected,
                Actual = actual,
                TestName = testName
            };
            lock (DetectedFaults)
            {
                if (DetectedFaults.Count < 1000) // limit list size
                    DetectedFaults.Add(fault);
            }
            Logger.Log($"RAM Fault: {testName} at 0x{offset:X16}. Expected {expected:X2}, got {actual:X2}", "ERROR");
        }
    }
}
