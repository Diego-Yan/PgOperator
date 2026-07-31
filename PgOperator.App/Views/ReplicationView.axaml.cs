using Avalonia.Controls; using PgOperator.App.ViewModels;
namespace PgOperator.App.Views;
public partial class ReplicationView : UserControl
{
    public ReplicationView(ReplicationViewModel vm) { InitializeComponent(); DataContext = vm;
        Loaded += async (s, e) => await vm.RefreshCommand.ExecuteAsync(null); }
    private void BackButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    { var m = App.Services.GetService(typeof(MainViewModel)) as MainViewModel; m?.NavigateTo(App.Services.GetService(typeof(DashboardView))!); }
}
