using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class ImportExportViewModel : ObservableObject
{
    private readonly ISshService _ssh;
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _tableName = string.Empty;
    [ObservableProperty] private string _csvPath = "/tmp/export.csv";
    [ObservableProperty] private string _database = "postgres";
    [ObservableProperty] private bool _withHeader = true;
    [ObservableProperty] private string _delimiter = ",";

    private ServerConnection? _server; private PgInstance? _pg;
    public ImportExportViewModel(ISshService ssh) { _ssh = ssh; }
    public void SetContext(ServerConnection s, PgInstance p) { _server = s; _pg = p; }

    [RelayCommand] private async Task ExportCsvAsync()
    {
        if (_server == null || _pg == null || string.IsNullOrWhiteSpace(TableName)) return;
        IsRunning = true; Status = "导出中...";
        try
        {
            var header = WithHeader ? "HEADER" : "";
            var sql = $"COPY (SELECT * FROM \"{TableName.Trim()}\") TO '{CsvPath}' WITH (FORMAT CSV, {header} DELIMITER '{Delimiter}');";
            var r = await _ssh.ExecutePsqlAsync(_server, _pg, sql);
            Output = r.Success ? $"导出到 {CsvPath}" : $"错误: {r.Error}";
            Status = r.Success ? "导出完成" : "导出失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand] private async Task ImportCsvAsync()
    {
        if (_server == null || _pg == null || string.IsNullOrWhiteSpace(TableName)) return;
        IsRunning = true; Status = "导入中...";
        try
        {
            var header = WithHeader ? "HEADER" : "";
            var sql = $"COPY \"{TableName.Trim()}\" FROM '{CsvPath}' WITH (FORMAT CSV, {header} DELIMITER '{Delimiter}');";
            var r = await _ssh.ExecutePsqlAsync(_server, _pg, sql);
            Output = r.Success ? $"从 {CsvPath} 导入到 {TableName}" : $"错误: {r.Error}";
            Status = r.Success ? "导入完成" : "导入失败";
        }
        catch (Exception ex) { Status = $"失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }
}
