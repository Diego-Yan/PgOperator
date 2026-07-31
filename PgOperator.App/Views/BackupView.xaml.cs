using System.Windows.Controls;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class BackupView : UserControl
{
    private readonly BackupViewModel _vm;
    public BackupView(BackupViewModel viewModel) { InitializeComponent(); _vm = viewModel; DataContext = viewModel; }

    private async void CheckDiskSpace_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.CheckDiskSpaceCommand.ExecuteAsync(null);
    private async void RunBackup_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.RunBackupCommand.ExecuteAsync(null);
    private async void CheckPitr_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.CheckPitrCommand.ExecuteAsync(null);
    private async void FixReplication_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.FixReplicationCommand.ExecuteAsync(null);

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        mainVm?.NavigateTo(App.Services.GetService(typeof(DashboardView))!);
    }
}
