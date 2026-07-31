using System.Text.Json;
using PgOperator.AI.Models;
using PgOperator.AI.Providers;
using PgOperator.Core.Models;

namespace PgOperator.AI;

/// <summary>
/// Takes a diagnostic report, builds a prompt, calls an AI provider,
/// and returns structured recommendations.
/// </summary>
public class AiAnalysisService
{
    private IAiProvider? _provider;
    private AiConfig _config = new();

    private const string SystemPrompt = """
        你是一位资深PostgreSQL数据库管理员(DBA)专家。你的任务是分析诊断报告中的问题，并按以下格式给出优化建议。

        规则：
        1. 按严重程度排序：critical(P0) > warning(P1) > info(P2)
        2. 每个建议都要分析根因(RootCause)
        3. 给出具体可执行的步骤(ActionSteps)
        4. 评估难度和风险
        5. 评估预估耗时(EstimatedTime)
        6. 用中文回复

        回复格式 (严格JSON):
        {
          "summary": "一句话总结当前数据库状况",
          "recommendations": [
            {
              "priority": "P0|P1|P2",
              "title": "问题标题",
              "rootCause": "根因分析",
              "impact": "不处理的影响",
              "actionSteps": ["步骤1", "步骤2"],
              "difficulty": "低|中|高",
              "risk": "低|中|高",
              "estimatedTime": "预估耗时(如30min/2h/1d)"
            }
          ]
        }
        """;

    public void Configure(AiConfig config)
    {
        _config = config;
        _provider?.Dispose();
        _provider = AiProviderFactory.Create(config);
    }

    public bool IsConfigured => _provider != null;

    public async Task<AiAnalysisResult> AnalyzeAsync(DiagnosticReport report, CancellationToken ct = default)
    {
        if (_provider == null)
            return new AiAnalysisResult { Error = "AI提供商未配置" };

        try
        {
            // Build user prompt from report
            var userPrompt = BuildPrompt(report);

            // Call AI
            var rawResponse = await _provider.ChatAsync(SystemPrompt, userPrompt, ct);

            // Parse the response
            var result = ParseResponse(rawResponse);
            result.RawResponse = rawResponse;
            return result;
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation so caller can distinguish timeout from failure
        }
        catch (Exception ex)
        {
            return new AiAnalysisResult { Error = $"AI分析失败: {ex.Message}" };
        }
    }

    private string BuildPrompt(DiagnosticReport report)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"## 诊断报告概览");
        sb.AppendLine($"- 服务器: {report.ReportMeta.Host}");
        sb.AppendLine($"- PG版本: {report.ReportMeta.PgVersion}");
        sb.AppendLine($"- 检查项总数: {report.ReportMeta.TotalChecks}");
        sb.AppendLine($"- 🔴 严重: {report.ReportMeta.Critical} | ⚠️ 警告: {report.ReportMeta.Warning} | 🔵 信息: {report.ReportMeta.Info} | ✅ 正常: {report.ReportMeta.Pass}");
        sb.AppendLine();

        // Critical findings
        var critical = report.Findings.Where(f => f.Severity == "critical").ToList();
        if (critical.Count > 0)
        {
            sb.AppendLine("## 🔴 严重问题");
            foreach (var f in critical)
                sb.AppendLine($"- [{f.Title}] {f.Detail}");
            sb.AppendLine();
        }

        // Warning findings
        var warnings = report.Findings.Where(f => f.Severity == "warning").ToList();
        if (warnings.Count > 0)
        {
            sb.AppendLine("## ⚠️ 警告");
            foreach (var f in warnings)
                sb.AppendLine($"- [{f.Title}] {f.Detail}");
            sb.AppendLine();
        }

        // Info findings
        var infos = report.Findings.Where(f => f.Severity == "info").ToList();
        if (infos.Count > 0)
        {
            sb.AppendLine("## 🔵 建议优化");
            foreach (var f in infos.Take(10)) // Limit info items
                sb.AppendLine($"- [{f.Title}] {f.Detail}");
            sb.AppendLine();
        }

        // Metrics
        if (report.MetricsSnapshot.Connections != null)
        {
            var c = report.MetricsSnapshot.Connections;
            sb.AppendLine("## 当前指标快照");
            sb.AppendLine($"- 连接数: {c.Total}/{c.Max} (活跃{c.Active}, 空闲{c.Idle}, IIT{c.IdleInTransaction})");
        }
        if (report.MetricsSnapshot.BufferCache != null)
        {
            sb.AppendLine($"- 缓冲区命中率: {report.MetricsSnapshot.BufferCache.HitRatio}%");
        }
        if (report.MetricsSnapshot.Replication != null)
        {
            sb.AppendLine($"- 复制状态: {report.MetricsSnapshot.Replication.State}, 延迟: {report.MetricsSnapshot.Replication.LagBytes} bytes");
        }

        // Add preference context
        sb.AppendLine();
        sb.AppendLine($"分析偏好: {_config.Preference switch
        {
            "aggressive" => "激进 — 积极提出优化建议，即使改动较大",
            "conservative" => "保守 — 优先推荐低风险改动，避免重大变更",
            _ => "平衡 — 权衡效果与风险"
        }}");
        sb.AppendLine($"关注重点: {_config.Focus switch
        {
            "security" => "安全性 — 优先关注认证、权限、加密等安全问题",
            "cost" => "成本 — 优先关注资源利用率和降本增效",
            _ => "性能 — 优先关注查询性能、缓存优化、索引等"
        }}");

        return sb.ToString();
    }

    private static AiAnalysisResult ParseResponse(string rawResponse)
    {
        try
        {
            // Try to find JSON in the response
            var jsonStart = rawResponse.IndexOf('{');
            var jsonEnd = rawResponse.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0)
                return new AiAnalysisResult { Summary = rawResponse[..Math.Min(200, rawResponse.Length)], Error = "无法从AI响应中解析JSON" };

            var json = rawResponse[jsonStart..(jsonEnd + 1)];
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new AiAnalysisResult
            {
                Summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : ""
            };

            if (root.TryGetProperty("recommendations", out var recs))
            {
                foreach (var rec in recs.EnumerateArray())
                {
                    var recommendation = new AiRecommendation
                    {
                        Priority = rec.TryGetProperty("priority", out var p) ? p.GetString() ?? "P2" : "P2",
                        Title = rec.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                        RootCause = rec.TryGetProperty("rootCause", out var rc) ? rc.GetString() ?? "" : "",
                        Impact = rec.TryGetProperty("impact", out var imp) ? imp.GetString() ?? "" : "",
                        Difficulty = rec.TryGetProperty("difficulty", out var diff) ? diff.GetString() ?? "中" : "中",
                        Risk = rec.TryGetProperty("risk", out var risk) ? risk.GetString() ?? "低" : "低",
                        EstimatedTime = rec.TryGetProperty("estimatedTime", out var et) ? et.GetString() : null
                    };

                    if (rec.TryGetProperty("actionSteps", out var steps))
                    {
                        recommendation.ActionSteps = steps.EnumerateArray()
                            .Select(step => step.GetString() ?? "").ToList();
                    }

                    result.Recommendations.Add(recommendation);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            return new AiAnalysisResult { Summary = rawResponse[..Math.Min(500, rawResponse.Length)], Error = $"JSON解析失败: {ex.Message}" };
        }
    }
}
