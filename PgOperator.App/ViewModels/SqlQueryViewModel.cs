using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class SqlQueryViewModel : ObservableObject
{
    private readonly ISshService _sshService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;

    [ObservableProperty] private string _sqlText = "SELECT version();";
    [ObservableProperty] private string _queryResult = string.Empty;
    [ObservableProperty] private string _explainPlan = string.Empty;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _executionTime = string.Empty;
    [ObservableProperty] private int _rowCount;
    [ObservableProperty] private string _statusMessage = "就绪";

    // Tab management
    [ObservableProperty] private ObservableCollection<QueryTab> _tabs = new();
    [ObservableProperty] private QueryTab? _selectedTab;

    public SqlQueryViewModel(ISshService sshService)
    {
        _sshService = sshService;
    }

    public void SetContext(ServerConnection server, PgInstance pgInstance)
    {
        _server = server;
        _pgInstance = pgInstance;
    }

    [RelayCommand]
    private async Task ExecuteQueryAsync()
    {
        if (_server == null || _pgInstance == null || string.IsNullOrWhiteSpace(SqlText)) return;

        IsExecuting = true;
        StatusMessage = "执行中...";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Detect query type
            var trimmed = SqlText.Trim().ToUpper();

            if (trimmed.StartsWith("EXPLAIN"))
            {
                var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, SqlText);
                ExplainPlan = result.Success ? result.Output : $"错误: {result.Error}";
                QueryResult = string.Empty;
            }
            else
            {
                var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance, SqlText);
                QueryResult = result.Success ? result.Output : $"错误: {result.Error}";
                RowCount = result.Success ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : 0;

                // Auto-EXPLAIN for SELECT queries
                if (trimmed.StartsWith("SELECT") && result.Success && !trimmed.Contains("LIMIT"))
                {
                    var explainResult = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                        $"EXPLAIN {SqlText}");
                    ExplainPlan = explainResult.Success ? explainResult.Output : "";
                }
                else
                {
                    ExplainPlan = "";
                }
            }

            sw.Stop();
            ExecutionTime = $"{sw.Elapsed.TotalSeconds:F2}s";
            StatusMessage = $"完成 ({ExecutionTime}, {RowCount} 行)";
        }
        catch (Exception ex)
        {
            QueryResult = $"执行异常: {ex.Message}";
            StatusMessage = "执行失败";
        }
        finally
        {
            IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task ExplainCurrentAsync()
    {
        if (_server == null || _pgInstance == null || string.IsNullOrWhiteSpace(SqlText)) return;
        if (!SqlText.Trim().ToUpper().StartsWith("SELECT") && !SqlText.Trim().ToUpper().StartsWith("UPDATE")
            && !SqlText.Trim().ToUpper().StartsWith("DELETE") && !SqlText.Trim().ToUpper().StartsWith("INSERT"))
        {
            StatusMessage = "EXPLAIN仅适用于SELECT/INSERT/UPDATE/DELETE";
            return;
        }

        IsExecuting = true;
        try
        {
            var result = await _sshService.ExecutePsqlAsync(_server, _pgInstance,
                $"EXPLAIN (ANALYZE, BUFFERS, FORMAT TEXT) {SqlText}");
            ExplainPlan = result.Success ? result.Output : $"错误: {result.Error}";
            StatusMessage = "EXPLAIN完成";
        }
        catch (Exception ex)
        {
            ExplainPlan = $"异常: {ex.Message}";
        }
        finally
        {
            IsExecuting = false;
        }
    }
}

public partial class QueryTab : ObservableObject
{
    [ObservableProperty] private string _title = "新查询";
    [ObservableProperty] private string _sqlText = string.Empty;
    [ObservableProperty] private string? _result;
}
