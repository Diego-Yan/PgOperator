using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PgOperator.App.ViewModels;
using PgOperator.App.Views;

namespace PgOperator.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _mainVm;

    public MainWindow()
    {
        InitializeComponent();
        _mainVm = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _mainVm;
        _mainVm.NavigateTo(App.Services.GetRequiredService<ServerListView>());
    }
}
