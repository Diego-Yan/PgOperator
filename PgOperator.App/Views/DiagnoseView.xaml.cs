using System.Windows.Controls;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class DiagnoseView : UserControl
{
    private readonly DiagnoseViewModel _vm;
    public DiagnoseView(DiagnoseViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = viewModel;
    }
    private async void DiagnoseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => await _vm.RunDiagnosisCommand.ExecuteAsync(null);
    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        mainVm?.NavigateTo(App.Services.GetService(typeof(DashboardView))!);
    }
}
