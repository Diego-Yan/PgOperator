using System.Windows.Controls;
using PgOperator.App.ViewModels;
using PgOperator.Core.Models;

namespace PgOperator.App.Views;

public partial class DashboardView : UserControl
{
    private readonly DashboardViewModel _vm;
    private ServerConnection? S => _vm.SelectedServer;
    private PgInstance? P => _vm.GetPgInstance();

    public DashboardView(DashboardViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
    }

    private void Nav(object view)
    {
        (App.Services.GetService(typeof(MainViewModel)) as MainViewModel)?.NavigateTo(view);
    }

    private Vm GetVm<Vm>() where Vm : class
    {
        var vm = App.Services.GetService(typeof(Vm)) as Vm;
        vm?.GetType().GetMethod("SetContext")?.Invoke(vm, new object?[] { S, P });
        return vm!;
    }

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Nav(App.Services.GetService(typeof(ServerListView))!);
    }

    private T NavTo<T>() where T : class
    {
        var view = App.Services.GetService(typeof(T)) as T;
        var dc = view?.GetType().GetProperty("DataContext")?.GetValue(view);
        dc?.GetType().GetMethod("SetContext")?.Invoke(dc, new object?[] { S, P });
        Nav(view!);
        return view!;
    }

    private void DiagnoseButton_Click(object s, System.Windows.RoutedEventArgs e)  => NavTo<DiagnoseView>();
    private void BackupButton_Click(object s, System.Windows.RoutedEventArgs e)    => NavTo<BackupView>();
    private void SqlQueryButton_Click(object s, System.Windows.RoutedEventArgs e)  => NavTo<SqlQueryView>();
    private void ConfigButton_Click(object s, System.Windows.RoutedEventArgs e)    => NavTo<ConfigManagementView>();
    private void UserMgmtButton_Click(object s, System.Windows.RoutedEventArgs e)  => NavTo<UserManagementView>();
    private void ReplicationButton_Click(object s, System.Windows.RoutedEventArgs e)=> NavTo<ReplicationView>();
    private void MaintenanceButton_Click(object s, System.Windows.RoutedEventArgs e)=> NavTo<MaintenanceView>();
    private void ObjectBrowserButton_Click(object s, System.Windows.RoutedEventArgs e)=> NavTo<ObjectBrowserView>();
    private void ImportExportButton_Click(object s, System.Windows.RoutedEventArgs e)=> NavTo<ImportExportView>();
    private void AiSettingsButton_Click(object s, System.Windows.RoutedEventArgs e) => NavTo<AiSettingsView>();
    private void DeployButton_Click(object s, System.Windows.RoutedEventArgs e)    => NavTo<DeployView>();

    private void ConfigPgButton_Click(object s, System.Windows.RoutedEventArgs e)
    {
        var dlg = new PgConfigDialog(_vm) { Owner = System.Windows.Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
            _ = _vm.RefreshCommand.ExecuteAsync(null);
    }
}
