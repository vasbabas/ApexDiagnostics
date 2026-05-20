using System;
using System.Windows;
using System.Windows.Threading;
using ApexDiagnostics.Core;

namespace ApexDiagnostics
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Force WPF to use software rendering mode. WinPE lacks Direct3D graphics drivers,
            // which causes hardware-accelerated WPF windows to render completely blank/empty.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            base.OnStartup(e);

            DispatcherUnhandledException += (s, ex) =>
            {
                Logger.Log($"Unhandled UI exception: {ex.Exception}", "FATAL");
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.Exception.Message}",
                    "Apex Diagnostics — Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                Logger.Log($"Fatal domain exception: {ex.ExceptionObject}", "FATAL");
            };
        }
    }
}
