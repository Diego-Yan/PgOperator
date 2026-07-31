using PgOperator.Core.Models;

namespace PgOperator.Tests;

/// <summary>
/// Minimal mock check for testing the diagnostic engine's filter/discovery logic.
/// </summary>
internal class MockCheck : Diagnostics.DiagnosticCheckBase
{
    private readonly string _id; private readonly string _name; private readonly string _title;
    private readonly int _layer; private readonly string _category; private readonly int _priority;

    public MockCheck(string id, string name = "", string title = "", int layer = 1,
        string category = "test", int priority = 10)
    {
        _id = id; _name = name; _title = title; _layer = layer; _category = category; _priority = priority;
    }

    public override string CheckId => _id;
    public override string CheckName => _name;
    public override string Title => _title;
    public override int Layer => _layer;
    public override string Category => _category;
    public override int Priority => _priority;

    public override Task<DiagnosticFinding> ExecuteAsync(Diagnostics.DiagnosticContext context)
        => Task.FromResult(Ok("mock ok"));
}

[TestClass]
public class DiagnosticCheckBaseTests
{
    [TestMethod]
    public void Ok_SetsPassSeverity()
    {
        var check = new MockCheck("T-001");
        var finding = check.Ok_Test("一切正常");
        Assert.AreEqual("pass", finding.Severity);
        Assert.AreEqual("T-001-OK", finding.Id);
    }

    [TestMethod]
    public void Warning_SetsWarningSeverity()
    {
        var check = new MockCheck("T-002");
        var finding = check.Warning_Test("有问题", "会影响性能",
            new DiagnosticMetric { CurrentValue = 85, Unit = "percent", Threshold = 80 });
        Assert.AreEqual("warning", finding.Severity);
        Assert.AreEqual("T-002-WARN", finding.Id);
        Assert.AreEqual("会影响性能", finding.Impact);
        Assert.IsNotNull(finding.Metric);
        Assert.AreEqual(85, finding.Metric!.CurrentValue);
    }

    [TestMethod]
    public void Critical_SetsCriticalSeverity()
    {
        var check = new MockCheck("T-003");
        var finding = check.Critical_Test("磁盘满了", "数据库不可用");
        Assert.AreEqual("critical", finding.Severity);
        Assert.AreEqual("T-003-CRIT", finding.Id);
        Assert.AreEqual("数据库不可用", finding.Impact);
    }

    [TestMethod]
    public void Info_SetsInfoSeverity()
    {
        var check = new MockCheck("T-004");
        var finding = check.Info_Test("建议优化", new DiagnosticSuggestion { Action = "tune" });
        Assert.AreEqual("info", finding.Severity);
        Assert.AreEqual("T-004-INFO", finding.Id);
        Assert.IsNotNull(finding.Suggestion);
        Assert.AreEqual("tune", finding.Suggestion!.Action);
    }

    [TestMethod]
    public void Ok_EmptyDetail_UsesTitle()
    {
        var check = new MockCheck("T-005", title: "某项检查");
        var finding = check.Ok_Test("");
        StringAssert.Contains(finding.Detail, "某项检查");
    }
}

// Extension to expose protected methods for testing
internal static class MockCheckExtensions
{
    public static DiagnosticFinding Ok_Test(this MockCheck c, string detail)
        => CallProtected(c, "Ok", detail);

    public static DiagnosticFinding Warning_Test(this MockCheck c, string detail, string impact = "",
        DiagnosticMetric? metric = null, DiagnosticSuggestion? suggestion = null)
        => CallProtected(c, "Warning", detail, impact, metric, suggestion);

    public static DiagnosticFinding Critical_Test(this MockCheck c, string detail, string impact = "",
        DiagnosticMetric? metric = null, DiagnosticSuggestion? suggestion = null)
        => CallProtected(c, "Critical", detail, impact, metric, suggestion);

    public static DiagnosticFinding Info_Test(this MockCheck c, string detail,
        DiagnosticSuggestion? suggestion = null)
        => CallProtected(c, "Info", detail, suggestion);

    private static DiagnosticFinding CallProtected(MockCheck c, string method, params object?[] args)
    {
        // Find the method by name and parameter count (try non-public instance methods)
        var methods = typeof(Diagnostics.DiagnosticCheckBase).GetMethods(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(m => m.Name == method && m.GetParameters().Length == args.Length)
            .ToList();

        if (methods.Count == 0)
            throw new InvalidOperationException($"Method '{method}' with {args.Length} params not found");

        return (DiagnosticFinding)methods[0].Invoke(c, args)!;
    }
}

[TestClass]
public class DiagnosticReportTests
{
    [TestMethod]
    public void DiagnosticReport_Defaults()
    {
        var report = new DiagnosticReport();
        Assert.IsNotNull(report.ReportMeta);
        Assert.IsNotNull(report.Findings);
        Assert.IsNotNull(report.MetricsSnapshot);
        Assert.AreEqual(0, report.Findings.Count);
    }

    [TestMethod]
    public void DiagnosticReportMeta_Defaults()
    {
        var meta = new DiagnosticReportMeta();
        Assert.AreEqual("", meta.ReportId);
        Assert.AreEqual(0, meta.Critical);
        Assert.AreEqual(0, meta.Warning);
        Assert.AreEqual(0, meta.Info);
        Assert.AreEqual(0, meta.Pass);
    }

    [TestMethod]
    public void MetricsSnapshot_HoldsAllMetricTypes()
    {
        var snap = new MetricsSnapshot
        {
            Connections = new ConnectionMetrics { Total = 10, Active = 2, Max = 100 },
            BufferCache = new BufferCacheMetrics { HitRatio = 97.5, Recommended = 95 },
            Replication = new ReplicationMetrics { LagBytes = 0, State = "streaming" },
            Locks = new LockMetrics { Waiting = 1 }
        };

        Assert.AreEqual(10, snap.Connections.Total);
        Assert.AreEqual(100, snap.Connections.Max);
        Assert.AreEqual(97.5, snap.BufferCache.HitRatio);
        Assert.AreEqual("streaming", snap.Replication.State);
        Assert.AreEqual(1, snap.Locks.Waiting);
    }

    [TestMethod]
    public void DiagnosticFinding_Defaults()
    {
        var finding = new DiagnosticFinding();
        Assert.AreEqual("info", finding.Severity);
        Assert.IsNull(finding.Metric);
        Assert.IsNull(finding.Suggestion);
    }

    [TestMethod]
    public void DiagnosticMetric_HoldsThresholdDirection()
    {
        var metric = new DiagnosticMetric
        {
            CurrentValue = 92,
            Unit = "percent",
            Threshold = 85,
            Direction = "above"
        };

        Assert.AreEqual(92, metric.CurrentValue);
        Assert.AreEqual("percent", metric.Unit);
        Assert.AreEqual(85, metric.Threshold);
        Assert.AreEqual("above", metric.Direction);
    }

    [TestMethod]
    public void DiagnosticSuggestion_WithCommands()
    {
        var suggestion = new DiagnosticSuggestion
        {
            Action = "increase_shared_buffers",
            Commands = new List<string> { "ALTER SYSTEM SET shared_buffers = '2GB';", "需重启PG" },
            Risk = "中(需重启)"
        };

        Assert.AreEqual(2, suggestion.Commands.Count);
        Assert.AreEqual("中(需重启)", suggestion.Risk);
    }
}
