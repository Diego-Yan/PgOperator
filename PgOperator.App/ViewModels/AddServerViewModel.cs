using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class AddServerViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private ServerConnection? _originalServer;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = "root";
    [ObservableProperty] private string? _group;
    [ObservableProperty] private int _authMethodIndex;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _privateKeyPath = string.Empty;
    [ObservableProperty] private string _privateKeyContent = string.Empty;
    [ObservableProperty] private string _passphrase = string.Empty;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private Guid? _editServerId;
    [ObservableProperty] private bool _savedSuccessfully;

    // PG Instance fields
    [ObservableProperty] private string _pgHost = "localhost";
    [ObservableProperty] private int _pgPort = 5432;
    [ObservableProperty] private string _pgDatabase = "postgres";
    [ObservableProperty] private string _pgUser = "postgres";
    [ObservableProperty] private string _pgPassword = "";

    public List<string> Groups { get; } = new() { "生产环境", "测试环境", "开发环境" };
    public List<string> AuthMethods { get; } = new() { "密码", "私钥文件", "私钥内容粘贴" };

    public AddServerViewModel(IDatabaseService databaseService) => _databaseService = databaseService;

    public void LoadForEdit(ServerConnection server)
    {
        _originalServer = server;
        IsEditMode = true; EditServerId = server.Id;
        Name = server.Name; Host = server.Host; Port = server.Port;
        Username = server.Username; Group = server.Group;
        AuthMethodIndex = (int)server.AuthMethod;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Host))
        { StatusMessage = "名称和主机地址不能为空"; return; }

        IsSaving = true;
        try
        {
            var server = new ServerConnection
            {
                Id = EditServerId ?? Guid.NewGuid(),
                Name = Name.Trim(), Host = Host.Trim(), Port = Port,
                Username = Username.Trim(), Group = Group,
                AuthMethod = (SshAuthMethod)AuthMethodIndex,
            };

            switch (server.AuthMethod)
            {
                case SshAuthMethod.Password:
                    if (!string.IsNullOrEmpty(Password))
                        server.Password = Password;
                    else if (IsEditMode && _originalServer != null)
                        server.Password = _originalServer.Password;
                    else { StatusMessage = "密码不能为空"; return; }
                    break;
                case SshAuthMethod.PrivateKeyFile:
                    server.PrivateKeyPath = !string.IsNullOrEmpty(PrivateKeyPath) ? PrivateKeyPath : (IsEditMode ? _originalServer?.PrivateKeyPath : null);
                    server.Passphrase = !string.IsNullOrEmpty(Passphrase) ? Passphrase : (IsEditMode ? _originalServer?.Passphrase : null);
                    break;
                case SshAuthMethod.PrivateKeyContent:
                    server.PrivateKeyContent = !string.IsNullOrEmpty(PrivateKeyContent) ? PrivateKeyContent : (IsEditMode ? _originalServer?.PrivateKeyContent : null);
                    server.Passphrase = !string.IsNullOrEmpty(Passphrase) ? Passphrase : (IsEditMode ? _originalServer?.Passphrase : null);
                    break;
            }

            await _databaseService.SaveServerAsync(server);

            // Also save PG instance if configured
            if (!string.IsNullOrEmpty(PgPassword))
            {
                var pg = new PgInstance
                {
                    ServerConnectionId = server.Id,
                    Name = $"{server.Name}-PG",
                    Host = PgHost, Port = PgPort, Database = PgDatabase,
                    Username = PgUser, Password = PgPassword
                };
                await _databaseService.SavePgInstanceAsync(pg);
            }

            SavedSuccessfully = true;
            StatusMessage = IsEditMode ? "服务器更新成功" : "服务器添加成功";
        }
        catch (Exception ex) { StatusMessage = $"保存失败: {ex.Message}"; }
        finally { IsSaving = false; }
    }
}
