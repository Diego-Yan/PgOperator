using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class ReplicationViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _replicationInfo = string.Empty;
    [ObservableProperty] private string _slotInfo = string.Empty;
    [ObservableProperty] private string _logicalReplicationInfo = string.Empty;
    [ObservableProperty] private string _createSlotName = string.Empty;
    [ObservableProperty] private bool _isLoading;

    private ServerConnection? _server; private PgInstance? _pg;
    public ReplicationViewModel(ISshService ssh) { _ssh = ssh; }
    public void SetContext(ServerConnection s, PgInstance p) { _server = s; _pg = p; }

    [RelayCommand] private async Task RefreshAsync()
    {
        if (_server == null || _pg == null) return; IsLoading = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT application_name,client_addr,state,sync_state," +
                "pg_wal_lsn_diff(pg_current_wal_lsn(),flush_lsn) AS lag_bytes," +
                "extract(epoch from flush_lag)::int AS lag_sec FROM pg_stat_replication;");
            ReplicationInfo = r.Success ? r.Output : "无流复制连接";

            r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT slot_name,active,database,pg_size_pretty(pg_wal_lsn_diff(pg_current_wal_lsn(),restart_lsn)) " +
                "FROM pg_replication_slots;");
            SlotInfo = r.Success ? r.Output : "无复制槽";

            try
            {
                r = await _ssh.ExecutePsqlAsync(_server, _pg,
                    "SELECT subname,subenabled,slot_name,publication_names FROM pg_stat_subscription;");
                LogicalReplicationInfo = r.Success ? r.Output : "";
            }
            catch (Exception ex) { LogicalReplicationInfo = $"逻辑复制查询失败: {ex.Message}"; }

            Status = "刷新完成";
        }
        catch (Exception ex) { Status = $"错误: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand] private async Task CreateSlotAsync()
    {
        if (_server == null || _pg == null || string.IsNullOrWhiteSpace(CreateSlotName)) return;
        var r = await _ssh.ExecutePsqlAsync(_server, _pg,
            $"SELECT pg_create_physical_replication_slot('{CreateSlotName}');");
        Status = r.Success ? $"复制槽 {CreateSlotName} 创建成功" : $"失败: {r.Error}";
        if (r.Success) { CreateSlotName = ""; await RefreshCommand.ExecuteAsync(null); }
    }

    [RelayCommand] private async Task DropSlotAsync(string name)
    {
        if (_server == null || _pg == null) return;
        var r = await _ssh.ExecutePsqlAsync(_server, _pg,
            $"SELECT pg_drop_replication_slot('{name}');");
        Status = r.Success ? $"复制槽 {name} 已删除" : $"失败: {r.Error}";
        if (r.Success) await RefreshCommand.ExecuteAsync(null);
    }
}
