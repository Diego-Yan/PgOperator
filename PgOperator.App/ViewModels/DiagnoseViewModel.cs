using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;
using PgOperator.Diagnostics;
using PgOperator.AI;
using PgOperator.AI.Models;

namespace PgOperator.App.ViewModels;

public partial class DiagnoseViewModel : ObservableObject
{
    private readonly DiagnosticEngine _engine;
    private readonly AiAnalysisService _aiService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;

    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private DiagnosticReport? _report;
    [ObservableProperty] private AiAnalysisResult _aiResult = new();
    [ObservableProperty] private string _findingSummary = string.Empty;
    [ObservableProperty] private string _aiConfigStatus = "未配置AI";

    public DiagnoseViewModel(DiagnosticEngine engine, AiAnalysisService aiService)
    {
        _engine = engine;
        _aiService = aiService;
        AiConfigStatus = aiService.IsConfigured ? "AI已配置" : "AI未配置 (在设置中配置AI Key)";
    }

    public void SetContext(ServerConnection server, PgInstance? pgInstance)
    {
        _server = server;
        _pgInstance = pgInstance;
    }

    [RelayCommand]
    private async Task RunDiagnosisAsync()
    {
        if (_server == null) return;
        IsRunning = true;
        StatusMessage = "正在执行诊断检查...";

        try
        {
            Report = await _engine.RunAsync(_server, _pgInstance, "standard");

            var critical = Report.Findings.Count(f => f.Severity == "critical");
            var warning = Report.Findings.Count(f => f.Severity == "warning");
            FindingSummary = $"诊断完成: 🔴{critical} ⚠️{warning} ✅{Report.ReportMeta.Pass}";

            StatusMessage = FindingSummary;

            // Run AI analysis if configured
            if (_aiService.IsConfigured)
            {
                StatusMessage = "正在AI分析诊断报告...";
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    AiResult = await _aiService.AnalyzeAsync(Report, cts.Token);
                    StatusMessage = AiResult.Success
                        ? $"AI分析完成: {AiResult.Recommendations.Count} 条建议"
                        : $"AI分析失败: {AiResult.Error}";
                }
                catch (OperationCanceledException) { StatusMessage = "AI分析超时(60s)，请检查API Key和网络"; }
                catch (Exception ex) { StatusMessage = $"AI分析异常: {ex.Message}"; }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"诊断失败: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
