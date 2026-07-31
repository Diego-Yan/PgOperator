using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class MaintenanceViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _vacuumType;
    [ObservableProperty] private string? _targetTable;
    [ObservableProperty] private string _cronExpression = "0 3 * * 0";
    [ObservableProperty] private bool _isScheduleEnabled;

    private ServerConnection? _server; private PgInstance? _pg;
    public List<string> VacuumTypes { get; } = new() { "VACUUM (普通)", "VACUUM ANALYZE", "VACUUM FULL", "VACUUM FREEZE" };
    public MaintenanceViewModel(ISshService ssh) { _ssh = ssh; }
    public void SetContext(ServerConnection s, PgInstance p) { _server = s; _pg = p; }

    [RelayCommand] private async Task ExecuteVacuumAsync()
    {
        if (_server == null || _pg == null) return; IsRunning = true;
        try
        {
            var cmd = VacuumType switch { 0 => "VACUUM VERBOSE", 1 => "VACUUM ANALYZE VERBOSE",
                2 => "VACUUM FULL VERBOSE", 3 => "VACUUM FREEZE VERBOSE", _ => "VACUUM VERBOSE" };
            if (!string.IsNullOrWhiteSpace(TargetTable)) cmd += $" {TargetTable.Trim()}";
            cmd += ";";

            var r = await _ssh.ExecutePsqlAsync(_server, _pg, cmd);
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "VACUUM完成" : "VACUUM失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand] private async Task ExecuteReindexAsync()
    {
        if (_server == null || _pg == null) return; IsRunning = true;
        try
        {
            var target = string.IsNullOrWhiteSpace(TargetTable) ? "DATABASE " + _pg.Database : $"TABLE {TargetTable.Trim()}";
            var r = await _ssh.ExecutePsqlAsync(_server, _pg, $"REINDEX (VERBOSE) {target};");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "REINDEX完成" : "REINDEX失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand] private async Task ExecuteAnalyzeAsync()
    {
        if (_server == null || _pg == null) return; IsRunning = true;
        try
        {
            var target = string.IsNullOrWhiteSpace(TargetTable) ? "" : TargetTable.Trim();
            var r = await _ssh.ExecutePsqlAsync(_server, _pg, $"ANALYZE VERBOSE {target};");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "ANALYZE完成" : "ANALYZE失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand] private async Task CheckBloatAsync()
    {
        if (_server == null || _pg == null) return; IsRunning = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT schemaname||'.'||relname, n_dead_tup, n_live_tup, " +
                "round(100.0*n_dead_tup/NULLIF(n_live_tup,0),1) AS dead_pct " +
                "FROM pg_stat_user_tables WHERE n_dead_tup>100 ORDER BY dead_pct DESC LIMIT 20;");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "膨胀检查完成" : "检查失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }
}
