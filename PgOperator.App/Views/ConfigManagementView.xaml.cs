using System.Windows.Controls;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class ConfigManagementView : UserControl
{
    private readonly ConfigManagementViewModel _vm;
    public ConfigManagementView(ConfigManagementViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
        Loaded += async (s, e) => await viewModel.LoadConfigCommand.ExecuteAsync(null);
    }
    private void ConfigText_Changed(object sender, TextChangedEventArgs e)
        => _vm.MarkDirtyCommand.Execute(null);
    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        mainVm?.NavigateTo(App.Services.GetService(typeof(DashboardView))!);
    }
}
