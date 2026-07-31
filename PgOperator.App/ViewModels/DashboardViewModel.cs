using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ISshService _sshService;
    private readonly IDatabaseService _dbService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;

    public DashboardViewModel(ISshService sshService, IDatabaseService dbService)
    { _sshService = sshService; _dbService = dbService; }

    [ObservableProperty] private ServerConnection? _selectedServer;
    [ObservableProperty] private string _serverStatus = "未知";
    [ObservableProperty] private string _pgVersion = string.Empty;
    [ObservableProperty] private int _activeConnections;
    [ObservableProperty] private int _totalConnections;
    [ObservableProperty] private int _maxConnections = 100;
    [ObservableProperty] private int _idleConnections;
    [ObservableProperty] private string _lastBackupTime = "未备份";
    [ObservableProperty] private string _replicationLag = "无";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPgConfigured;
    [ObservableProperty] private string _pgConfigStatus = "未配置PG数据库密码";

    public PgInstance? GetPgInstance() => _pgInstance;

    public void SetContext(ServerConnection server, PgInstance? pgInstance)
    {
        _server = server;
        _pgInstance = pgInstance;
        SelectedServer = server;
        UpdatePgConfigStatus();
    }

    public void UpdatePgConfigStatus()
    {
        IsPgConfigured = _pgInstance != null && !string.IsNullOrEmpty(_pgInstance.Password);
        PgConfigStatus = IsPgConfigured
            ? $"✅ PG已配置 | {_pgInstance!.Host}:{_pgInstance.Port}/{_pgInstance.Database} ({_pgInstance.Username})"
            : "⚠️ 未配置PG数据库密码";
    }

    public async Task SavePgPasswordAsync(string host, int port, string database, string username, string password)
    {
        if (_server == null) return;
        if (_pgInstance == null)
        {
            _pgInstance = new PgInstance
            {
                ServerConnectionId = _server.Id,
                Name = $"{_server.Name}-PG",
                Host = host, Port = port,
                Database = database, Username = username, Password = password
            };
        }
        else
        {
            _pgInstance.Host = host; _pgInstance.Port = port;
            _pgInstance.Database = database; _pgInstance.Username = username;
            _pgInstance.Password = password;
        }
        await _dbService.SavePgInstanceAsync(_pgInstance);
        UpdatePgConfigStatus();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_server == null) return;
        IsLoading = true;

        try
        {
            if (_pgInstance != null)
            {
                var r = await _sshService.ExecutePsqlAsync(_server, _pgInstance, "SELECT 1;");
                if (r.Success)
                    ServerStatus = "● 运行中";
                else
                    ServerStatus = $"● 不可达 — {r.Error}".Replace('\n', ' ').Replace('\r', ' ');

                if (r.Success)
                {
                    var verR = await _sshService.ExecutePsqlAsync(_server, _pgInstance, "SELECT version();");
                    if (verR.Success) PgVersion = string.Join(" ", verR.Output.Trim().Split(' ').Take(3));

                    var connR = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                        "SELECT count(*), count(*) FILTER(WHERE state='active'), " +
                        "count(*) FILTER(WHERE state='idle'), current_setting('max_connections')::int " +
                        "FROM pg_stat_activity;");
                    if (connR.Success)
                    {
                        var parts = connR.Output.Trim().Split('|');
                        // [REVIEW-FIX] 使用 TryParse 保护解析，避免异常 PG 输出导致 UI 崩溃
                        if (parts.Length >= 4
                            && int.TryParse(parts[0].Trim(), out var total)
                            && int.TryParse(parts[1].Trim(), out var active)
                            && int.TryParse(parts[2].Trim(), out var idle)
                            && int.TryParse(parts[3].Trim(), out var max))
                        {
                            TotalConnections = total;
                            ActiveConnections = active;
                            IdleConnections = idle;
                            MaxConnections = max;
                        }
                    }

                    var repR = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                        "SELECT COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), flush_lsn), 0) " +
                        "FROM pg_stat_replication LIMIT 1;");
                    ReplicationLag = repR.Success && long.TryParse(repR.Output.Trim(), out var lag) && lag > 0
                        ? $"{lag / 1024 / 1024} MB" : "无延迟";
                }
            }
            else { ServerStatus = "未配置PG实例"; }
        }
        catch (Exception ex) { ServerStatus = $"错误: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
