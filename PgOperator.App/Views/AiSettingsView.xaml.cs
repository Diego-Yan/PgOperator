using System.Windows.Controls; using PgOperator.App.ViewModels;
namespace PgOperator.App.Views;
public partial class AiSettingsView : UserControl
{ public AiSettingsView(AiSettingsViewModel vm) { InitializeComponent(); DataContext = vm; }
  private void BackButton_Click(object sender, System.Windows.RoutedEventArgs e)
  { (App.Services.GetService(typeof(MainViewModel)) as MainViewModel)?.NavigateTo(App.Services.GetService(typeof(DashboardView))!); } }
