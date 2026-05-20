using System.Windows;

namespace ApexShell
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Force WPF to use software rendering mode. WinPE lacks Direct3D graphics drivers,
            // which causes hardware-accelerated WPF windows to render completely blank/empty.
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            base.OnStartup(e);
        }
    }
}
