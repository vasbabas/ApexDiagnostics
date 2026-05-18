using System.Windows.Controls;
using System.Windows.Input;
using ApexDiagnostics.ViewModels;

namespace ApexDiagnostics.Views
{
    public partial class ExplorerView : UserControl
    {
        public ExplorerView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ExplorerViewModel vm)
                {
                    vm.BackupLogs.CollectionChanged += (sender, args) =>
                    {
                        try
                        {
                            if (LogConsole.Items.Count > 0)
                            {
                                LogConsole.ScrollIntoView(LogConsole.Items[LogConsole.Items.Count - 1]);
                            }
                        }
                        catch { }
                    };
                }
            };
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ExplorerViewModel vm && vm.SelectedItem != null)
            {
                if (vm.SelectedItem.IsDirectory)
                {
                    vm.NavigateToFolderCommand.Execute(vm.SelectedItem);
                }
            }
        }
    }
}
