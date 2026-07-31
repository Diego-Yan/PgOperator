using System.Text.Json;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.Diagnostics;

/// <summary>
/// Orchestrates diagnostic checks across all layers and generates reports.
/// </summary>
public class DiagnosticEngine
{
    private readonly ISshService _sshService;
    private readonly List<IDiagnosticCheck> _checks;

    public DiagnosticEngine(ISshService sshService)
    {
        _sshService = sshService;
        _checks = DiscoverChecks();
    }

    public async Task<DiagnosticReport> RunAsync(ServerConnection server, PgInstance? pgInstance = null,
        string depth = "standard", CancellationToken ct = default)
    {
        var ctx = new DiagnosticContext(_sshService, server, pgInstance, ct);
        var report = new DiagnosticReport
        {
            ReportMeta = new DiagnosticReportMeta
            {
                ReportId = $"diag-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{server.Name}",
                Timestamp = DateTime.UtcNow,
                Server = server.Id.ToString(),
                Host = server.Host,
                Os = server.OsInfo ?? "Unknown"
            }
        };

        // Get PG version
        if (pgInstance != null)
        {
            var verResult = await ctx.QueryAsync("SELECT version();");
            if (verResult.Success)
            {
                report.ReportMeta.PgVersion = verResult.Output.Trim();
            }
        }

        // Determine which checks to run based on depth
        var checksToRun = FilterChecksByDepth(depth);

        foreach (var check in checksToRun)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var finding = await check.ExecuteAsync(ctx);
                report.Findings.Add(finding);

                // Count severity
                switch (finding.Severity)
                {
                    case "critical": report.ReportMeta.Critical++; break;
                    case "warning": report.ReportMeta.Warning++; break;
                    case "info": report.ReportMeta.Info++; break;
                    default: report.ReportMeta.Pass++; break;
                }
            }
            catch (Exception ex)
            {
                report.Findings.Add(new DiagnosticFinding
                {
                    Id = $"{check.CheckId}-ERR",
                    Layer = check.Layer,
                    Category = "error",
                    Severity = "warning",
                    Title = $"{check.Title} 执行失败: {ex.Message}",
                    Detail = ex.Message
                });
                report.ReportMeta.Warning++;
            }
        }

        report.ReportMeta.TotalChecks = report.Findings.Count;

        // Collect metrics snapshot
        try
        {
            report.MetricsSnapshot = await CollectMetricsAsync(ctx);
        }
        catch { /* metrics are optional */ }

        return report;
    }

    private List<IDiagnosticCheck> FilterChecksByDepth(string depth)
    {
        // Quick: only priority 1-10 checks (critical stuff)
        // Standard: priority 1-20
        // Deep: all checks
        return depth switch
        {
            "quick" => _checks.Where(c => c.Priority <= 10).ToList(),
            "standard" => _checks.Where(c => c.Priority <= 20).ToList(),
            "deep" => _checks.ToList(),
            _ => _checks.Where(c => c.Priority <= 20).ToList()
        };
    }

    private async Task<MetricsSnapshot> CollectMetricsAsync(DiagnosticContext ctx)
    {
        var snapshot = new MetricsSnapshot();

        try
        {
            // Connections
            var r = await ctx.QueryAsync(
                "SELECT count(*) AS total, count(*) FILTER (WHERE state='active') AS active, " +
                "count(*) FILTER (WHERE state='idle') AS idle, " +
                "count(*) FILTER (WHERE state='idle in transaction') AS iit " +
                "FROM pg_stat_activity;");
            if (r.Success)
            {
                var parts = r.Output.Trim().Split('|');
                snapshot.Connections = new ConnectionMetrics
                {
                    Total = int.Parse(parts[0].Trim()),
                    Active = int.Parse(parts[1].Trim()),
                    Idle = int.Parse(parts[2].Trim()),
                    IdleInTransaction = parts.Length > 3 ? int.Parse(parts[3].Trim()) : 0
                };
            }

            // Buffer cache hit ratio
            r = await ctx.QueryAsync(
                "SELECT round(100.0 * sum(blks_hit) / NULLIF(sum(blks_read)+sum(blks_hit),0), 1) " +
                "FROM pg_stat_database;");
            if (r.Success && double.TryParse(r.Output.Trim(), out var hitRatio))
            {
                snapshot.BufferCache = new BufferCacheMetrics { HitRatio = hitRatio, Recommended = 95 };
            }

            // Replication lag
            r = await ctx.QueryAsync(
                "SELECT COALESCE(pg_wal_lsn_diff(pg_current_wal_lsn(), flush_lsn), 0) " +
                "FROM pg_stat_replication LIMIT 1;");
            if (r.Success && long.TryParse(r.Output.Trim(), out var lagBytes))
            {
                snapshot.Replication = new ReplicationMetrics { LagBytes = lagBytes, State = "streaming" };
            }

            // Locks
            r = await ctx.QueryAsync("SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock';");
            if (r.Success && int.TryParse(r.Output.Trim(), out var lockWait))
            {
                snapshot.Locks = new LockMetrics { Waiting = lockWait };
            }
        }
        catch { /* ignore metric collection errors */ }

        return snapshot;
    }

    /// <summary>
    /// Serialize report to JSON for AI analysis.
    /// </summary>
    public string SerializeReport(DiagnosticReport report)
    {
        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Discover all diagnostic checks via reflection.
    /// </summary>
    private static List<IDiagnosticCheck> DiscoverChecks()
    {
        var checkType = typeof(IDiagnosticCheck);
        var checks = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => checkType.IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .Select(t =>
            {
                try { return (IDiagnosticCheck)Activator.CreateInstance(t)!; }
                catch { return null!; }
            })
            .Where(c => c != null)
            .OrderBy(c => c.Layer)
            .ThenBy(c => c.Priority)
            .ToList();

        return checks;
    }
}
