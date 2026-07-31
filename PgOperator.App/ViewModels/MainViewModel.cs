using CommunityToolkit.Mvvm.ComponentModel;

namespace PgOperator.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    // [REVIEW-FIX] 移除未使用的 _sshService 和 _databaseService 字段（注入后从未引用）
    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private string _title = "PgOperator";

    public MainViewModel() { }

    public void NavigateTo(object view)
    {
        CurrentView = view;
    }
}
