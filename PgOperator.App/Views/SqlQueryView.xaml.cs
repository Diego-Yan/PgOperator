using System.Windows.Controls;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class SqlQueryView : UserControl
{
    public SqlQueryView(SqlQueryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var mainVm = App.Services.GetService(typeof(MainViewModel)) as MainViewModel;
        var dashboardView = App.Services.GetService(typeof(DashboardView));
        mainVm?.NavigateTo(dashboardView!);
    }
}
