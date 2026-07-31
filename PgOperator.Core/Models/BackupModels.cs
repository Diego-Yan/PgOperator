namespace PgOperator.Core.Models;

public class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public Guid? PgInstanceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public BackupType Type { get; set; } = BackupType.Logical;
    public BackupFormat Format { get; set; } = BackupFormat.Custom;
    public string? Database { get; set; }
    public string RemotePath { get; set; } = "/var/backups/postgresql";
    public string? LocalPath { get; set; }
    public string CronExpression { get; set; } = "0 2 * * *"; // Daily 2am
    public int RetentionDays { get; set; } = 7;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public BackupJobStatus? LastStatus { get; set; }
}

public enum BackupType { Logical, Physical }
public enum BackupFormat { Custom, Plain, Directory, Tar }
public enum BackupJobStatus { Success, Failed, Running, Unknown }

public class BackupHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BackupJobId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public BackupJobStatus Status { get; set; }
    public string? FilePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public double? DurationSeconds { get; set; }
}

public class PitrConfig
{
    public Guid ServerId { get; set; }
    public bool ArchiveMode { get; set; }
    public string? ArchiveCommand { get; set; }
    public string? ArchiveDirectory { get; set; }
    public string? RestoreCommand { get; set; }
    public bool IsValid { get; set; }
    public string? ValidationError { get; set; }
}

public class RestoreRequest
{
    public Guid ServerId { get; set; }
    public Guid PgInstanceId { get; set; }
    public RestoreType Type { get; set; }
    public string? BackupFilePath { get; set; }
    public string? Database { get; set; }
    public DateTime? TargetTime { get; set; } // For PITR
    public bool CreateDatabase { get; set; } = true;
    public bool CleanBeforeRestore { get; set; }
}

public enum RestoreType { Full, PointInTime, SelectiveDatabase }

// ─── Disk Space Check ───────────────────────────────────

public class DiskSpaceCheckResult
{
    public bool CanProceed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public double AvailableMb { get; set; }
    public double PartitionSizeMb { get; set; }
    public int UsagePercent { get; set; }
    public double EstimatedBackupSizeMb { get; set; }
    public double RequiredMb { get; set; }
    public bool ShouldWarn => UsagePercent > 75 || AvailableMb < RequiredMb * 1.5;
}

// ─── Backup File Management ─────────────────────────────

public class BackupFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted => SizeBytes switch
    {
        >= 1_073_741_824 => $"{SizeBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{SizeBytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{SizeBytes / 1_024.0:F1} KB",
        _ => $"{SizeBytes} B"
    };
    public string LastModified { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public bool IsLogical => FileName.EndsWith(".dump") || FileName.EndsWith(".sql") || FileName.EndsWith(".sql.gz");
    public bool IsPhysical => FileName.Contains("basebackup");
}

public class BatchDeleteResult
{
    public List<string> DeletedFiles { get; set; } = new();
    public long FreedBytes { get; set; }
    public string FreedFormatted => FreedBytes switch
    {
        >= 1_073_741_824 => $"{FreedBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{FreedBytes / 1_048_576.0:F1} MB",
        _ => $"{FreedBytes / 1024.0:F1} KB"
    };
    public string Message { get; set; } = string.Empty;
}
