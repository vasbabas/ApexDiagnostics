using System;
using System.Threading;
using System.Threading.Tasks;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Engines
{
    public class FullSystemEngine : EngineBase
    {
        private readonly CpuEngine _cpu;
        private readonly RamDiagEngine _ram;

        public FullSystemEngine(double maxRamGb) : base("Full System Stress")
        {
            _cpu = new CpuEngine();
            // Use Quick Scan for Full System by default, or Deep if preferred
            _ram = new RamDiagEngine(maxRamGb, isDeepMode: true);
        }

        protected override void OnStart(CancellationToken token)
        {
            Task.Run(() => _cpu.Start(), CancellationToken.None);
            Task.Run(() => _ram.Start(), CancellationToken.None);
            
            Task.Run(async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (!token.IsCancellationRequested && _isRunning)
                {
                    ElapsedTime = sw.Elapsed;
                    await Task.Delay(500, token);
                }
            }, token);
        }

        protected override void OnStop()
        {
            _cpu.Stop();
            _ram.Stop();
        }

        public void StopCpu() => _cpu.Stop();
        
        public bool IsCpuRunning => _cpu.IsRunning;
        public bool IsRamRunning => _ram.IsRunning;
        public CpuEngine Cpu => _cpu;
        public RamDiagEngine Ram => _ram;
    }
}
