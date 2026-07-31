using Avalonia.Controls;
using Avalonia.Interactivity;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class AddServerDialog : Window
{
    private readonly AddServerViewModel _viewModel;
    public bool Result { get; private set; }

    public AddServerDialog(AddServerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.IsEditMode ? "编辑服务器" : "添加服务器";

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AddServerViewModel.AuthMethodIndex))
                UpdateAuthFieldVisibility();
        };

        UpdateAuthFieldVisibility();

        if (viewModel.IsEditMode)
            PasswordInput.PlaceholderText = "留空则保持原密码";
    }

    private void UpdateAuthFieldVisibility()
    {
        PasswordInput.IsVisible = _viewModel.AuthMethodIndex == 0;
        KeyPathInput.IsVisible = _viewModel.AuthMethodIndex == 1;
        KeyContentInput.IsVisible = _viewModel.AuthMethodIndex == 2;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCommand.ExecuteAsync(null);
        if (_viewModel.SavedSuccessfully)
        {
            Result = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = false;
        Close();
    }
}
