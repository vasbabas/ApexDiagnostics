using System;
using System.Threading;
using ApexDiagnostics.Core;

namespace ApexDiagnostics.Engines
{
    public abstract class EngineBase : IDisposable
    {
        protected CancellationTokenSource? Cts;
        protected volatile bool _isRunning;
        
        public bool IsRunning => _isRunning;
        public string EngineName { get; }
        public TimeSpan ElapsedTime { get; protected set; }

        protected EngineBase(string name)
        {
            EngineName = name;
        }

        public virtual void Start()
        {
            if (_isRunning) return;
            Cts = new CancellationTokenSource();
            _isRunning = true;
            ElapsedTime = TimeSpan.Zero;
            Logger.Log($"Starting {EngineName}...");
            OnStart(Cts.Token);
        }

        public virtual void Stop()
        {
            if (!_isRunning) return;
            Logger.Log($"Stopping {EngineName}...");
            _isRunning = false;
            Cts?.Cancel();
            OnStop();
            Logger.Log($"{EngineName} stopped.");
        }

        protected abstract void OnStart(CancellationToken token);
        protected abstract void OnStop();

        public void Dispose()
        {
            if (_isRunning) Stop();
            Cts?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
