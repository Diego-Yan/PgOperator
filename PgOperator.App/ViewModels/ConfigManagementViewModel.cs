using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class ConfigManagementViewModel : ObservableObject
{
    private readonly ISshService _sshService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;
    [ObservableProperty] private string _configContent = string.Empty;
    [ObservableProperty] private string _originalContent = string.Empty;
    [ObservableProperty] private string _configFilePath2 = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _activeTab = "config"; // config or hba
    [ObservableProperty] private string _hbaContent = string.Empty;
    [ObservableProperty] private string _originalHbaContent = string.Empty;
    [ObservableProperty] private bool _hbaDirty;

    // Quick config shortcuts
    [ObservableProperty] private ObservableCollection<ConfigParameter> _keyParameters = new();

    public ConfigManagementViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    public void SetContext(ServerConnection server, PgInstance pgInstance)
    {
        _server = server;
        _pgInstance = pgInstance;
    }

    [RelayCommand]
    private async Task LoadConfigAsync()
    {
        if (_server == null || _pgInstance == null) return;
        IsLoading = true;

        try
        {
            // Get config file path
            var pathResult = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                "SHOW config_file;");
            if (!pathResult.Success) { StatusMessage = "无法获取配置文件路径"; return; }

            ConfigFilePath2 = pathResult.Output.Trim();

            // Read the config file
            var result = await _sshService.ExecuteCommandAsync(_server, $"cat {ConfigFilePath2}");
            if (result.Success)
            {
                ConfigContent = result.Output;
                OriginalContent = result.Output;
                IsDirty = false;
                StatusMessage = $"已加载: {ConfigFilePath2}";
            }

            // Load key parameters for quick view
            await LoadKeyParametersAsync();
        }
        catch (Exception ex) { StatusMessage = $"加载失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadHbaAsync()
    {
        if (_server == null) return;
        ActiveTab = "hba";
        try
        {
            var pathResult = await _sshService.ExecuteCommandAsync(_server,
                "psql -t -A -c 'SHOW hba_file;' 2>/dev/null");
            if (!pathResult.Success) return;

            var hbaPath = pathResult.Output.Trim();
            var result = await _sshService.ExecuteCommandAsync(_server, $"cat {hbaPath}");
            if (result.Success)
            {
                HbaContent = result.Output;
                OriginalHbaContent = result.Output;
                HbaDirty = false;
            }
        }
        catch { }
    }

    private async Task LoadKeyParametersAsync()
    {
        if (_server == null || _pgInstance == null) return;

        var keyParams = new[] {
            "shared_buffers", "effective_cache_size", "work_mem", "maintenance_work_mem",
            "wal_level", "max_wal_size", "checkpoint_timeout", "max_connections",
            "autovacuum", "log_min_duration_statement", "random_page_cost", "effective_io_concurrency"
        };

        var list = new ObservableCollection<ConfigParameter>();
        foreach (var param in keyParams)
        {
            var r = await _sshService.ExecutePsqlAsync(_server, _pgInstance, $"SHOW {param};");
            list.Add(new ConfigParameter { Name = param, Value = r.Success ? r.Output.Trim() : "?" });
        }
        KeyParameters = list;
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        if (_server == null || string.IsNullOrEmpty(ConfigFilePath2)) return;
        IsSaving = true;

        try
        {
            // Write via SSH using base64 (safe — no shell escaping needed)
            var tempPath = $"/tmp/pg_config_{Guid.NewGuid():N}.conf";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ConfigContent));
            var result = await _sshService.ExecuteCommandAsync(_server,
                $"echo {b64} | base64 -d > {tempPath} && cp {tempPath} {ConfigFilePath2} && rm {tempPath}");

            if (result.Success)
            {
                OriginalContent = ConfigContent;
                IsDirty = false;
                StatusMessage = "配置已保存。需要 RELOAD 或 RESTART 来生效。";
            }
            else
                StatusMessage = $"保存失败: {result.Error}";
        }
        catch (Exception ex) { StatusMessage = $"保存异常: {ex.Message}"; }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private async Task ReloadConfigAsync()
    {
        if (_server == null || _pgInstance == null) return;
        var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, "SELECT pg_reload_conf();");
        StatusMessage = result.Success ? "配置已重新加载(SIGHUP)" : $"失败: {result.Error}";
    }

    [RelayCommand]
    private void MarkDirty() { IsDirty = ConfigContent != OriginalContent; }

    [RelayCommand]
    private void RevertConfig()
    {
        ConfigContent = OriginalContent;
        IsDirty = false;
    }
}

public class ConfigParameter
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
