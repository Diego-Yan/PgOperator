namespace PgOperator.Core.Models;

/// <summary>
/// Represents a diagnostic finding from the diagnostic engine.
/// </summary>
public class DiagnosticFinding
{
    public string Id { get; set; } = string.Empty;
    public int Layer { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = "info"; // critical, warning, info, pass
    public string CheckName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public DiagnosticMetric? Metric { get; set; }
    public DiagnosticSuggestion? Suggestion { get; set; }
    public Dictionary<string, string>? RawQuery { get; set; }
}

public class DiagnosticMetric
{
    public double? CurrentValue { get; set; }
    public string? Unit { get; set; }
    public double? Threshold { get; set; }
    public string? Direction { get; set; } // "above" = above threshold is bad, "below" = below threshold is bad
}

public class DiagnosticSuggestion
{
    public string Action { get; set; } = string.Empty;
    public List<string>? Commands { get; set; }
    public string? Risk { get; set; }
    public string? Prevention { get; set; }
}

/// <summary>
/// Complete diagnostic report aggregating all findings.
/// </summary>
public class DiagnosticReport
{
    public DiagnosticReportMeta ReportMeta { get; set; } = new();
    public List<DiagnosticFinding> Findings { get; set; } = new();
    public MetricsSnapshot MetricsSnapshot { get; set; } = new();
    public TrendData? Trends24H { get; set; }
}

public class DiagnosticReportMeta
{
    public string ReportId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Server { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string PgVersion { get; set; } = string.Empty;
    public int PgUptimeDays { get; set; }
    public int TotalChecks { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Info { get; set; }
    public int Pass { get; set; }
}

public class MetricsSnapshot
{
    public ConnectionMetrics? Connections { get; set; }
    public BufferCacheMetrics? BufferCache { get; set; }
    public ReplicationMetrics? Replication { get; set; }
    public LockMetrics? Locks { get; set; }
    public DiskMetrics? DiskUsage { get; set; }
    public BackupMetrics? Backup { get; set; }
    public int? SlowQueries24H { get; set; }
}

public class ConnectionMetrics
{
    public int Total { get; set; }
    public int Active { get; set; }
    public int Idle { get; set; }
    public int IdleInTransaction { get; set; }
    public int Max { get; set; }
}

public class BufferCacheMetrics
{
    public double HitRatio { get; set; }
    public double Recommended { get; set; }
}

public class ReplicationMetrics
{
    public long LagBytes { get; set; }
    public double LagSeconds { get; set; }
    public string State { get; set; } = string.Empty;
}

public class LockMetrics
{
    public int Waiting { get; set; }
    public int BlockedQueries { get; set; }
}

public class DiskMetrics
{
    public double Data { get; set; }
    public double Wal { get; set; }
    public double Logs { get; set; }
    public double Backup { get; set; }
}

public class BackupMetrics
{
    public DateTime? LastFull { get; set; }
    public DateTime? LastWalArchive { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class TrendData
{
    public double ConnectionsAvg { get; set; }
    public double ConnectionsMax { get; set; }
    public double DiskGrowthGb { get; set; }
    public int SlowQueryCount { get; set; }
    public long XidConsumption { get; set; }
}
