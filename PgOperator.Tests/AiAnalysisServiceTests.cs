using PgOperator.AI;
using PgOperator.AI.Models;
using PgOperator.AI.Providers;
using PgOperator.Core.Models;

namespace PgOperator.Tests;

/// <summary>
/// Mock AI provider that returns a predefined response for testing.
/// </summary>
internal class MockAiProvider : IAiProvider
{
    private readonly string _response;
    public MockAiProvider(string response) { _response = response; }
    public string Name => "mock";
    public Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
        => Task.FromResult(_response);
    public void Dispose() { }
}

[TestClass]
public class AiAnalysisServiceTests
{
    private DiagnosticReport CreateSampleReport()
    {
        return new DiagnosticReport
        {
            ReportMeta = new DiagnosticReportMeta
            {
                ReportId = "test-001",
                Host = "test-server",
                PgVersion = "PostgreSQL 16.3",
                TotalChecks = 10,
                Critical = 1,
                Warning = 3,
                Info = 2,
                Pass = 4
            },
            Findings = new List<DiagnosticFinding>
            {
                new() { Severity = "critical", Title = "磁盘空间不足", Detail = "可用空间5%" },
                new() { Severity = "warning", Title = "shared_buffers偏小", Detail = "仅128MB" },
                new() { Severity = "warning", Title = "无慢查询日志", Detail = "log_min_duration=-1" },
                new() { Severity = "info", Title = "建议开启HugePages", Detail = "未配置" }
            },
            MetricsSnapshot = new MetricsSnapshot
            {
                Connections = new ConnectionMetrics { Total = 12, Active = 3, Idle = 7, IdleInTransaction = 2, Max = 100 },
                BufferCache = new BufferCacheMetrics { HitRatio = 98.5, Recommended = 95 }
            }
        };
    }

    [TestMethod]
    public async Task AnalyzeAsync_NotConfigured_ReturnsError()
    {
        var service = new AiAnalysisService();
        var report = new DiagnosticReport();
        var result = await service.AnalyzeAsync(report);
        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "未配置");
    }

    [TestMethod]
    public async Task AnalyzeAsync_ConfiguredProvider_CalledWithPrompt()
    {
        var validJson = """
        {
          "summary": "数据库运行良好，但存在磁盘空间和配置问题",
          "recommendations": [
            {
              "priority": "P0",
              "title": "清理磁盘空间",
              "rootCause": "磁盘使用率过高",
              "impact": "可能导致数据库停止写入",
              "actionSteps": ["清理WAL日志", "扩展磁盘"],
              "difficulty": "低",
              "risk": "低",
              "estimatedTime": "30min"
            },
            {
              "priority": "P1",
              "title": "调整shared_buffers",
              "rootCause": "shared_buffers配置过小",
              "impact": "缓存命中率低",
              "actionSteps": ["ALTER SYSTEM SET shared_buffers = '2GB';", "重启PG"],
              "difficulty": "中",
              "risk": "中",
              "estimatedTime": "10min"
            }
          ]
        }
        """;

        var service = new AiAnalysisService();

        // Configure with mock provider
        typeof(AiAnalysisService).GetMethod("Configure")!
            .Invoke(service, new object[] { new AiConfig { Provider = "mock", ApiKey = "test" } });

        // Replace provider with mock
        var providerField = typeof(AiAnalysisService).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        providerField!.SetValue(service, new MockAiProvider(validJson));

        var report = CreateSampleReport();
        var result = await service.AnalyzeAsync(report);

        Assert.IsTrue(result.Success, $"Expected success but got error: {result.Error}");
        StringAssert.Contains(result.Summary, "数据库运行良好");
        Assert.AreEqual(2, result.Recommendations.Count);

        var first = result.Recommendations[0];
        Assert.AreEqual("P0", first.Priority);
        Assert.AreEqual("清理磁盘空间", first.Title);
        Assert.AreEqual(2, first.ActionSteps.Count);
        Assert.AreEqual("低", first.Difficulty);

        var second = result.Recommendations[1];
        Assert.AreEqual("P1", second.Priority);
        Assert.AreEqual("调整shared_buffers", second.Title);
    }

    [TestMethod]
    public async Task AnalyzeAsync_Cancelled_PropagatesException()
    {
        var service = new AiAnalysisService();
        typeof(AiAnalysisService).GetMethod("Configure")!
            .Invoke(service, new object[] { new AiConfig { Provider = "mock", ApiKey = "test" } });

        var providerField = typeof(AiAnalysisService).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var mockProvider = new CancellingMockProvider();
        providerField!.SetValue(service, mockProvider);

        // The mock throws TaskCanceledException synchronously from ChatAsync,
        // which gets caught by the OperationCanceledException handler and rethrown.
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => service.AnalyzeAsync(new DiagnosticReport(), CancellationToken.None));
    }

    [TestMethod]
    public async Task AnalyzeAsync_InvalidJson_ReturnsErrorWithRawResponse()
    {
        var garbageResponse = "This is not JSON at all, just some text from the model.";

        var service = new AiAnalysisService();
        typeof(AiAnalysisService).GetMethod("Configure")!
            .Invoke(service, new object[] { new AiConfig { Provider = "mock", ApiKey = "test" } });

        var providerField = typeof(AiAnalysisService).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        providerField!.SetValue(service, new MockAiProvider(garbageResponse));

        var result = await service.AnalyzeAsync(new DiagnosticReport());

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error!, "JSON");
        StringAssert.Contains(result.Summary, "This is not JSON");
    }

    [TestMethod]
    public async Task AnalyzeAsync_EmptyResponse_HandlesGracefully()
    {
        var service = new AiAnalysisService();
        typeof(AiAnalysisService).GetMethod("Configure")!
            .Invoke(service, new object[] { new AiConfig { Provider = "mock", ApiKey = "test" } });

        var providerField = typeof(AiAnalysisService).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        providerField!.SetValue(service, new MockAiProvider(""));

        var result = await service.AnalyzeAsync(new DiagnosticReport());

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public async Task AnalyzeAsync_ProviderThrows_ReturnsError()
    {
        var service = new AiAnalysisService();
        typeof(AiAnalysisService).GetMethod("Configure")!
            .Invoke(service, new object[] { new AiConfig { Provider = "mock", ApiKey = "test" } });

        var providerField = typeof(AiAnalysisService).GetField("_provider",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        providerField!.SetValue(service, new ThrowingMockProvider());

        var result = await service.AnalyzeAsync(new DiagnosticReport());

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error!, "模拟错误");
    }

    /// <summary>
    /// Mock that throws to simulate network failure.
    /// </summary>
    private class ThrowingMockProvider : IAiProvider
    {
        public string Name => "thrower";
        public Task<string> ChatAsync(string sp, string um, CancellationToken ct)
            => throw new InvalidOperationException("模拟错误");
        public void Dispose() { }
    }

    /// <summary>
    /// Mock that cancels to test OperationCanceledException propagation.
    /// Throws TaskCanceledException, which derives from OperationCanceledException.
    /// </summary>
    private class CancellingMockProvider : IAiProvider
    {
        public string Name => "canceller";
        public Task<string> ChatAsync(string sp, string um, CancellationToken ct)
            => throw new TaskCanceledException();
        public void Dispose() { }
    }
}
