using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace ApexDiagnostics.Core
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "ApexDiagnostics.log");
        private static readonly object _lock = new();

        public static void Log(string message, string level = "INFO",
            [CallerMemberName] string caller = "")
        {
            try
            {
                string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level,-5}] [{caller}] {message}";
                lock (_lock)
                {
                    File.AppendAllText(LogPath, entry + Environment.NewLine);
                }
            }
            catch { /* logging must never throw */ }
        }

        public static string GetLogPath() => LogPath;
    }
}
