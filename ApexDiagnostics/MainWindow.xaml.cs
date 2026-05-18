using System.Windows;
using System.Windows.Controls;

namespace ApexDiagnostics
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            SetWindowIcon();
        }

        private void SetWindowIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/ApexDiagnostics;component/Assets/apex_icon.png", UriKind.Absolute);
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch
            {
                // Fallback gracefully without throwing startup exceptions
            }
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string langCode)
            {
                Helpers.LanguageManager.SetLanguage(langCode);
            }
        }
    }
}
