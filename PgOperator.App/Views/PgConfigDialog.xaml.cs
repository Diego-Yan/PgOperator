using System.Windows;
using PgOperator.App.ViewModels;

namespace PgOperator.App.Views;

public partial class PgConfigDialog : Window
{
    public bool Saved { get; private set; }
    private readonly DashboardViewModel _vm;

    public PgConfigDialog(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        // Default PG host to the server's SSH host (PG runs on the same machine)
        var serverHost = vm.SelectedServer?.Host ?? "localhost";
        var pg = vm.GetPgInstance();
        if (pg != null)
        {
            PgHostBox.Text = pg.Host;
            PgPortBox.Text = pg.Port.ToString();
            DatabaseBox.Text = pg.Database;
            UsernameBox.Text = pg.Username;
            PasswordBox.Text = pg.Password ?? "";
        }
        else
        {
            PgHostBox.Text = serverHost;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PasswordBox.Text))
        { StatusText.Text = "PG密码不能为空"; return; }
        if (!int.TryParse(PgPortBox.Text, out var port))
        { StatusText.Text = "端口号格式错误"; return; }

        try
        {
            await _vm.SavePgPasswordAsync(
                PgHostBox.Text, port,
                DatabaseBox.Text, UsernameBox.Text, PasswordBox.Text);
            Saved = true; DialogResult = true; Close();
        }
        catch (Exception ex) { StatusText.Text = $"保存失败: {ex.Message}"; }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    { DialogResult = false; Close(); }
}
