using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class ObjectBrowserViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedTab;

    private ServerConnection? _server; private PgInstance? _pg;
    public ObjectBrowserViewModel(ISshService ssh) { _ssh = ssh; }
    public void SetContext(ServerConnection s, PgInstance p) { _server = s; _pg = p; }

    [RelayCommand] private async Task ListDatabasesAsync()
    {
        if (_server == null || _pg == null) return; IsLoading = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT datname,pg_size_pretty(pg_database_size(datname))," +
                "datconnlimit,pg_encoding_to_char(encoding) FROM pg_database ORDER BY datname;");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "数据库列表" : "加载失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand] private async Task ListTablesAsync()
    {
        if (_server == null || _pg == null) return; IsLoading = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT schemaname,relname,pg_size_pretty(pg_total_relation_size(schemaname||'.'||relname))," +
                "n_live_tup,n_dead_tup,last_vacuum,last_analyze " +
                "FROM pg_stat_user_tables ORDER BY pg_total_relation_size(schemaname||'.'||relname) DESC LIMIT 100;");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? $"表列表 ({Output.Split('\n').Length}行)" : "加载失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand] private async Task ListIndexesAsync()
    {
        if (_server == null || _pg == null) return; IsLoading = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT schemaname,tablename,indexname,pg_size_pretty(pg_relation_size(indexrelid))," +
                "idx_scan,idx_tup_read,idx_tup_fetch FROM pg_stat_user_indexes ORDER BY pg_relation_size(indexrelid) DESC LIMIT 100;");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "索引列表" : "加载失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand] private async Task ListFunctionsAsync()
    {
        if (_server == null || _pg == null) return; IsLoading = true;
        try
        {
            var r = await _ssh.ExecutePsqlAsync(_server, _pg,
                "SELECT n.nspname,p.proname,pg_get_function_arguments(p.oid)," +
                "pg_get_function_result(p.oid),l.lanname FROM pg_proc p " +
                "JOIN pg_namespace n ON p.pronamespace=n.oid JOIN pg_language l ON p.prolang=l.oid " +
                "WHERE n.nspname NOT IN ('pg_catalog','information_schema') ORDER BY n.nspname,p.proname LIMIT 100;");
            Output = r.Success ? r.Output : $"错误: {r.Error}";
            Status = r.Success ? "函数列表" : "加载失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsLoading = false; }
    }
}
