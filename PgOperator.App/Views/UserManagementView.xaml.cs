using System.Windows.Controls;
using PgOperator.App.ViewModels;
using PgOperator.Core.Models;

namespace PgOperator.App.Views;

public partial class UserManagementView : UserControl
{
    private readonly UserManagementViewModel _vm;
    public UserManagementView(UserManagementViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += async (s, e) => await viewModel.LoadRolesCommand.ExecuteAsync(null);
    }

    private void RoleGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleGrid.SelectedItem is PgRole role)
            _vm.SelectRole(role);
    }

    private async void ChangePwd_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.ChangePasswordCommand.ExecuteAsync(null);

    private async void UpdatePrivs_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.UpdatePrivilegesCommand.ExecuteAsync(null);

    private async void CreateRole_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.CreateRoleCommand.ExecuteAsync(null);

    private async void RevokePublic_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.RevokePublicSchemaCommand.ExecuteAsync(null);

    private async void Refresh_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.LoadRolesCommand.ExecuteAsync(null);

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        mainVm?.NavigateTo(App.Services.GetService(typeof(DashboardView))!);
    }
}
