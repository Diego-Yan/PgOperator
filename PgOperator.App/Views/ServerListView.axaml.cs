using Avalonia.Controls;
using PgOperator.App.ViewModels;
using PgOperator.Core.Models;

namespace PgOperator.App.Views;

public partial class ServerListView : UserControl
{
    private readonly ServerListViewModel _viewModel;

    public ServerListView(ServerListViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (s, e) => await _viewModel.LoadServersCommand.ExecuteAsync(null);
    }

    private async void EditButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServerConnection server)
        {
            var editVm = App.Services.GetService(typeof(AddServerViewModel)) as AddServerViewModel;
            editVm!.LoadForEdit(server);
            var dialog = new AddServerDialog(editVm);
            await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window);
            if (dialog.Result)
                _ = _viewModel.LoadServersCommand.ExecuteAsync(null);
        }
    }

    private async void AddButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var addServerVm = App.Services.GetService(typeof(AddServerViewModel)) as AddServerViewModel;
        var dialog = new AddServerDialog(addServerVm!);
        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window);
        if (dialog.Result)
            _ = _viewModel.LoadServersCommand.ExecuteAsync(null);
    }

    private void EnterButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ServerConnection server)
            NavigateToDashboard(server);
    }

    private void NavigateToDashboard(ServerConnection server)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        var dashboardVm = App.Services.GetService(typeof(DashboardViewModel)) as DashboardViewModel;
        var dashboardView = App.Services.GetService(typeof(DashboardView)) as DashboardView;

        if (dashboardVm != null && dashboardView != null)
        {
            dashboardVm.SelectedServer = server;
            dashboardVm.SetContext(server, server.PgInstances.FirstOrDefault());
            dashboardVm.UpdatePgConfigStatus();
            _ = dashboardVm.RefreshCommand.ExecuteAsync(null);
            mainVm?.NavigateTo(dashboardView);
        }
    }
}
