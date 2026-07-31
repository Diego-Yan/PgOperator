using System.Windows;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class AddServerDialog : Window
{
    private readonly AddServerViewModel _viewModel;

    public AddServerDialog(AddServerViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AddServerViewModel.AuthMethodIndex))
                UpdateAuthFieldVisibility();
        };

        UpdateAuthFieldVisibility();
    }

    private void UpdateAuthFieldVisibility()
    {
        PasswordInput.Visibility = _viewModel.AuthMethodIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyPathInput.Visibility = _viewModel.AuthMethodIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        KeyContentInput.Visibility = _viewModel.AuthMethodIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = PasswordInput.Password;
    }

    private void PassphraseInput_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Passphrase = PassphraseInput.Password;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveCommand.ExecuteAsync(null);
        if (_viewModel.SavedSuccessfully)
        {
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
