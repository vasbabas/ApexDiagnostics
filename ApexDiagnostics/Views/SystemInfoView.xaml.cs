using System.Windows.Controls;

namespace ApexDiagnostics.Views
{
    public partial class SystemInfoView : UserControl
    {
        public SystemInfoView() => InitializeComponent();

        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.ScrollToEnd();
            }
        }
    }
}
