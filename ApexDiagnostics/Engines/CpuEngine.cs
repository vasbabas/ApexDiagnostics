using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using System.Threading.Tasks;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Engines
{
    public class CpuEngine : EngineBase
    {
        private List<Task> _workers = new();
        private Stopwatch _timer = new();

        public int ActiveThreads { get; private set; }
        private long _totalOperationsCalculated;
        public long TotalOperationsCalculated => Interlocked.Read(ref _totalOperationsCalculated);

        public CpuEngine() : base("CPU Stress & Validation") { }

        protected override void OnStart(CancellationToken token)
        {
            _workers.Clear();
            _timer.Restart();
            Interlocked.Exchange(ref _totalOperationsCalculated, 0);

            int coreCount = Environment.ProcessorCount;
            ActiveThreads = coreCount;

            for (int i = 0; i < coreCount; i++)
            {
                _workers.Add(Task.Factory.StartNew(() => CpuWorker(token), 
                    token, 
                    TaskCreationOptions.LongRunning, 
                    TaskScheduler.Default));
            }

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _isRunning)
                {
                    ElapsedTime = _timer.Elapsed;
                    await Task.Delay(500, token);
                }
            }, token);
        }

        protected override void OnStop()
        {
            _timer.Stop();
            var workersToWait = _workers.ToArray();
            Task.Run(() => {
                try { Task.WaitAll(workersToWait, 2000); } catch { }
            });
            _workers.Clear();
            ActiveThreads = 0;
        }

        private unsafe void CpuWorker(CancellationToken token)
        {
            // Set priority lower than UI to keep system usable during 100% load
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            
            // Allocate cache thrashing arrays
            const int arraySize = 1024 * 1024 * 2; // 8MB float array
            float[] dataA = new float[arraySize];
            float[] dataB = new float[arraySize];
            float[] dataC = new float[arraySize];
            
            Random rnd = new();
            for(int i = 0; i < arraySize; i++) 
            {
                dataA[i] = (float)rnd.NextDouble();
                dataB[i] = (float)rnd.NextDouble();
            }

            int index = 0;
            double dummy = 0;
            long ops = 0;

            bool useAvx = Avx.IsSupported;
            bool useAvx2 = Avx2.IsSupported;

            while (!token.IsCancellationRequested)
            {
                // Increase the batch size per iteration to saturate the pipeline
                for (int batch = 0; batch < 50; batch++)
                {
                    if (useAvx)
                    {
                        // AVX Float Torture - Heavy FMA simulation
                        fixed (float* pA = dataA, pB = dataB, pC = dataC)
                        {
                            for (int i = 0; i < 4000; i += 8)
                            {
                                int idx1 = (index + i) % (arraySize - 8);
                                Vector256<float> vA = Avx.LoadVector256(pA + idx1);
                                Vector256<float> vB = Avx.LoadVector256(pB + idx1);
                                Vector256<float> vC = Avx.Add(Avx.Multiply(vA, vB), vA); // FMA-ish
                                Avx.Store(pC + idx1, vC);
                                ops += 16;
                            }
                        }
                    }
                    
                    if (useAvx2)
                    {
                        // AVX2 Integer Torture
                        for (int i = 0; i < 2000; i++)
                        {
                            Vector256<int> v1 = Vector256.Create(i, i+1, i+2, i+3, i+4, i+5, i+6, i+7);
                            Vector256<int> v2 = Vector256.Create(7, 6, 5, 4, 3, 2, 1, 0);
                            Vector256<int> v3 = Avx2.Add(v1, v2);
                            Vector256<int> v4 = Avx2.Xor(v3, v1);
                            ops += 32;
                        }
                    }

                    // Standard FP / Branching Torture
                    for (int i = 0; i < 10000; i++)
                    {
                        int idx1 = (index + i) % arraySize;
                        int idx2 = (arraySize - 1) - idx1;
                        
                        dataC[idx1] = dataA[idx1] * dataB[idx2] + (float)Math.Sqrt(Math.Abs(dataA[idx1]));
                        
                        if ((idx1 & 1) == 0)
                            dummy += Math.Sin(dataC[idx1]) * Math.Exp(dataA[idx2] % 1.0);
                        else
                            dummy += Math.Cos(dataC[idx1]) * Math.Log10(Math.Abs(dataB[idx2]) + 1.1);

                        ops += 10;
                    }
                }

                index = (index + 73) % arraySize;
                Interlocked.Add(ref _totalOperationsCalculated, ops);
                ops = 0;

                // DO NOT SLEEP - it kills the stress load. 
                // The CancellationToken check in the loop and BelowNormal priority handle responsiveness.
            }
        }
    }
}
