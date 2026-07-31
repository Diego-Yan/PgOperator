using System.Windows.Controls;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class DeployView : UserControl
{
    private readonly DeployViewModel _vm;

    public DeployView(DeployViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private async void CheckEnv_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.CheckEnvCommand.ExecuteAsync(null);

    private async void InstallPg_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.InstallPgCommand.ExecuteAsync(null);

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        mainVm?.NavigateTo(App.Services.GetService(typeof(DashboardView))!);
    }
}
