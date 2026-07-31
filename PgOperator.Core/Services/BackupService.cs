using System.Diagnostics;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.Core.Services;

public class BackupService
{
    private readonly ISshService _sshService;

    // [REVIEW-FIX] 移除未使用的 _dbService 字段（注入后从未引用）
    public BackupService(ISshService sshService)
    {
        _sshService = sshService;
    }

    // [REVIEW-FIX] 修复 Shell 注入：密码中的单引号需要转义，避免命令拼接被破坏
    private static string Pwd(PgInstance instance) => (instance.Password ?? "").Replace("'", "'\\''");
    // [REVIEW-FIX] 修复 SQL 注入：数据库名中的单引号需要双写转义
    private static string EscDbName(string db) => db.Replace("'", "''");

    // ─── Disk Space Pre-check ─────────────────────────────────

    /// <summary>
    /// Check available disk space and estimated backup size before backup.
    /// Returns a result indicating whether backup can proceed.
    /// </summary>
    public async Task<DiskSpaceCheckResult> CheckDiskSpaceAsync(
        ServerConnection server, PgInstance instance, string remotePath, string? database = null, CancellationToken ct = default)
    {
        var result = new DiskSpaceCheckResult();

        // 1. Ensure the backup directory exists, then check disk space on /
        await _sshService.ExecuteCommandAsync(server, $"mkdir -p {remotePath}", ct);

        var dfResult = await _sshService.ExecuteCommandAsync(server,
            "df -BM / | tail -1", ct);
        if (dfResult.Success && !string.IsNullOrEmpty(dfResult.Output))
        {
            var parts = dfResult.Output.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                // df output: Filesystem 1M-blocks Used Available Use% Mounted
                result.AvailableMb = ParseMb(parts[3]);
                result.PartitionSizeMb = ParseMb(parts[1]);
                result.UsagePercent = int.TryParse(parts[4].TrimEnd('%'), out var pct) ? pct : 0;
            }
            else
            {
                result.AvailableMb = -1; // signal parse failure
            }
        }

        // 2. Estimate backup size from pg_database_size
        if (instance != null && !string.IsNullOrEmpty(database))
        {
            var sizeResult = await _sshService.ExecuteCommandAsync(server,
                $"PGPASSWORD='{Pwd(instance)}' psql -h {instance.Host} -p {instance.Port} " +
                $"-U {instance.Username} -d postgres -t -A " +
                $"-c \"SELECT pg_size_pretty(pg_database_size('{EscDbName(database)}')), pg_database_size('{EscDbName(database)}');\"", ct);
            if (sizeResult.Success)
            {
                var sizeParts = sizeResult.Output.Trim().Split('|');
                if (sizeParts.Length >= 2 && long.TryParse(sizeParts[1].Trim(), out var dbSizeBytes))
                {
                    result.EstimatedBackupSizeMb = dbSizeBytes / (1024.0 * 1024.0);
                }
            }
        }

        // 3. Safety margin
        if (result.AvailableMb < 0)
        {
            result.CanProceed = false;
            result.Reason = $"df命令输出解析失败，原始输出: {dfResult?.Output}";
        }
        else if (result.AvailableMb <= 0)
        {
            result.CanProceed = false;
            result.Reason = $"磁盘可用空间为0，df输出: {dfResult?.Output}";
        }
        else if (result.EstimatedBackupSizeMb <= 0)
        {
            // Database size unknown — be lenient, just check we have at least 1GB
            result.CanProceed = result.AvailableMb > 1024;
            result.RequiredMb = 1024;
            result.Reason = result.CanProceed
                ? $"磁盘可用{result.AvailableMb:F0}MB (数据库大小未知，保守要求≥1GB即可)"
                : $"磁盘可用{result.AvailableMb:F0}MB，不足1GB，请清理空间";
        }
        else
        {
            var requiredMb = result.EstimatedBackupSizeMb * 1.5;
            result.RequiredMb = requiredMb;
            result.CanProceed = result.AvailableMb > requiredMb + 500;
            result.Reason = result.CanProceed
                ? $"磁盘空间充足 (可用{result.AvailableMb:F0}MB, 预计需要{requiredMb:F0}MB)"
                : $"磁盘空间不足！可用{result.AvailableMb:F0}MB, 预计需要{requiredMb:F0}MB, 差{requiredMb - result.AvailableMb:F0}MB";
        }

        return result;
    }

    private static double ParseMb(string value)
    {
        value = value.Trim().TrimEnd('M', 'm', 'B', 'b');
        return double.TryParse(value, out var n) ? n : 0;
    }

    // ─── Backup Listing & Management ──────────────────────────

    /// <summary>
    /// List all backup files in the backup directory.
    /// </summary>
    public async Task<List<BackupFileInfo>> ListBackupsAsync(
        ServerConnection server, string remotePath, CancellationToken ct = default)
    {
        var files = new List<BackupFileInfo>();

        // Logical backups: find files
        var result = await _sshService.ExecuteCommandAsync(server,
            $"find {remotePath} -type f \\( -name '*.dump' -o -name '*.sql' -o -name '*.tar' -o -name '*.sql.gz' \\) " +
            "-printf '%p\t%s\t%TY-%Tm-%Td %TH:%TM\t%u\\n' 2>/dev/null; echo '---DONE---'", ct);

        if (result.Success)
        {
            foreach (var line in result.Output.Replace("---DONE---", "").Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length < 4) continue;
                files.Add(new BackupFileInfo
                {
                    FilePath = parts[0].Trim(),
                    SizeBytes = long.TryParse(parts[1].Trim(), out var sz) ? sz : 0,
                    LastModified = parts[2].Trim(),
                    Owner = parts[3].Trim(),
                    FileName = System.IO.Path.GetFileName(parts[0].Trim())
                });
            }
        }

        // Physical backups: find basebackup directories with size
        var bbCmd = "for d in " + remotePath + "/basebackup_*; do [ -d \"$d\" ] && printf '%s|%s|%s\\n' \"$d\" \"$(du -sb \"$d\" 2>/dev/null | awk '{print $1}')\" \"$(stat -c '%Y' \"$d\" 2>/dev/null)\"; done 2>/dev/null; echo '---DONE---'";
        var bbResult = await _sshService.ExecuteCommandAsync(server, bbCmd, ct);

        if (bbResult.Success)
        {
            foreach (var line in bbResult.Output.Replace("---DONE---", "").Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                long sizeBytes = 0;
                long.TryParse(parts.Length >= 2 ? parts[1].Trim() : "0", out sizeBytes);
                var ts = parts.Length >= 3 && long.TryParse(parts[2].Trim(), out var epoch) ? epoch : 0;
                var modTime = ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).ToString("yyyy-MM-dd HH:mm") : "";
                files.Add(new BackupFileInfo
                {
                    FilePath = parts[0].Trim(),
                    SizeBytes = sizeBytes,
                    LastModified = modTime,
                    Owner = "",
                    FileName = System.IO.Path.GetFileName(parts[0].Trim())
                });
            }
        }

        if (files.Count == 0) return files;

        return files.OrderByDescending(f => f.LastModified).ToList();
    }

    /// <summary>
    /// Delete a specific backup file.
    /// </summary>
    public async Task<SshResult> DeleteBackupAsync(
        ServerConnection server, string filePath, CancellationToken ct = default)
    {
        // Safety check
        if (!filePath.Contains("backup") && !filePath.Contains("dump"))
            return new SshResult { Success = false, Error = "安全校验: 路径不包含 backup/dump，拒绝删除" };

        // Use rm -rf to handle both files and directories (physical backups are dirs)
        return await _sshService.ExecuteCommandAsync(server, $"rm -rf '{filePath}' && echo 'DELETED'", ct);
    }

    /// <summary>
    /// Delete all backups older than specified days.
    /// </summary>
    public async Task<BatchDeleteResult> DeleteOldBackupsAsync(
        ServerConnection server, string remotePath, int olderThanDays, CancellationToken ct = default)
    {
        var result = new BatchDeleteResult();

        // First list what will be deleted
        var listResult = await _sshService.ExecuteCommandAsync(server,
            $"find {remotePath} -type f \\( -name '*.dump' -o -name '*.sql' -o -name '*.tar' \\) " +
            $"-mtime +{olderThanDays} -printf '%p\t%s\\n' 2>/dev/null || echo 'NONE'", ct);

        if (!listResult.Success || listResult.Output.Contains("NONE"))
        {
            result.Message = "没有需要清理的旧备份";
            return result;
        }

        var lines = listResult.Output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                result.DeletedFiles.Add(parts[0].Trim());
                if (long.TryParse(parts[1].Trim(), out var fb)) result.FreedBytes += fb;
            }
        }

        // Execute deletion
        if (result.DeletedFiles.Count > 0)
        {
            var delResult = await _sshService.ExecuteCommandAsync(server,
                $"find {remotePath} \\( -name '*.dump' -o -name '*.sql' -o -name '*.tar' -o -name 'basebackup_*' \\) -mtime +{olderThanDays} " +
                $"-exec rm -rf {{}} \\; 2>/dev/null && echo 'OK'", ct);
            result.Message = delResult.Success && delResult.Output.Contains("OK")
                ? $"已删除 {result.DeletedFiles.Count} 个备份, 释放 {result.FreedBytes / 1024.0 / 1024.0:F1}MB"
                : $"删除命令执行失败: {delResult.Error}";
        }

        return result;
    }

    /// <summary>
    /// Quick integrity check — try listing the contents of a .dump file.
    /// </summary>
    public async Task<bool> ValidateBackupAsync(
        ServerConnection server, PgInstance instance, string backupFilePath, CancellationToken ct = default)
    {
        var result = await _sshService.ExecuteCommandAsync(server,
            $"PGPASSWORD='{Pwd(instance)}' pg_restore -l '{backupFilePath}' > /dev/null 2>&1 && echo 'VALID' || echo 'INVALID'",
            ct);
        return result.Success && result.Output.Contains("VALID");
    }

    /// <summary>
    /// Get total backup disk usage.
    /// </summary>
    public async Task<long> GetTotalBackupSizeAsync(
        ServerConnection server, string remotePath, CancellationToken ct = default)
    {
        var result = await _sshService.ExecuteCommandAsync(server,
            $"du -sb {remotePath} 2>/dev/null | awk '{{print $1}}' || echo '0'", ct);
        return result.Success && long.TryParse(result.Output.Trim(), out var size) ? size : 0;
    }

    // ─── Logical Backup (pg_dump) ────────────────────────────

    public async Task<BackupHistory> ExecuteLogicalBackupAsync(
        ServerConnection server, PgInstance instance, BackupJob job, CancellationToken ct = default)
    {
        var history = new BackupHistory { BackupJobId = job.Id, StartedAt = DateTime.UtcNow, Status = BackupJobStatus.Running };

        try
        {
            // Pre-check: disk space
            var spaceCheck = await CheckDiskSpaceAsync(server, instance, job.RemotePath, job.Database ?? instance.Database, ct);
            if (!spaceCheck.CanProceed)
            {
                history.Status = BackupJobStatus.Failed;
                history.ErrorMessage = spaceCheck.Reason;
                history.CompletedAt = DateTime.UtcNow;
                return history;
            }

            // Create backup in SSH user's home directory (no sudo needed)
            var backupDir = "$HOME/pg_backups";
            await _sshService.ExecuteCommandAsync(server, $"mkdir -p {backupDir}", ct);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var db = job.Database ?? instance.Database;
            var fileName = $"{db}_{timestamp}.dump";
            var filePath = $"{backupDir}/{fileName}";

            // Find pg_dump path (Ubuntu: /usr/bin/pg_dump or versioned path)
            var pgDumpPath = "/usr/bin/pg_dump";
            var pathCheck = await _sshService.ExecuteCommandAsync(server, $"test -f {pgDumpPath} && echo OK || echo 'NOTFOUND'", ct);
            if (!pathCheck.Success || pathCheck.Output.Contains("NOTFOUND"))
                pgDumpPath = "pg_dump"; // fallback to PATH

            // Build pg_dump command
            var format = job.Format switch
            {
                BackupFormat.Custom => "c", BackupFormat.Plain => "p",
                BackupFormat.Directory => "d", BackupFormat.Tar => "t", _ => "c"
            };

            // Run pg_dump with password auth (works for both local and remote)
            var pgDumpCmd = $"PGPASSWORD='{Pwd(instance)}' {pgDumpPath} -h {instance.Host} -p {instance.Port} -U {instance.Username} " +
                            $"-d {db} -F{format} -f {filePath} --no-owner --no-acl -v 2>&1";

            var result = await _sshService.ExecuteCommandWithProgressAsync(
                server, pgDumpCmd,
                onOutput: msg => Debug.WriteLine($"pg_dump [{job.Name}]: {msg}"),
                onError: err => Debug.WriteLine($"pg_dump [{job.Name}] error: {err}"),
                ct: ct);

            await _sshService.ExecuteCommandAsync(server, $"chmod 600 {filePath}", ct);

            if (result.Success)
            {
                var sizeResult = await _sshService.ExecuteCommandAsync(server, $"stat -c%s {filePath}", ct);
                history.Status = BackupJobStatus.Success;
                history.FilePath = filePath;
                history.FileSizeBytes = sizeResult.Success && long.TryParse(sizeResult.Output.Trim(), out var sz) ? sz : null;
                history.CompletedAt = DateTime.UtcNow;
                history.DurationSeconds = result.Duration.TotalSeconds;

                // Download to local if configured
                if (!string.IsNullOrEmpty(job.LocalPath))
                {
                    await DownloadBackupAsync(server, filePath, job.LocalPath, ct);
                }

                // Cleanup old backups
                await CleanupOldBackupsAsync(server, job, ct);
            }
            else
            {
                history.Status = BackupJobStatus.Failed;
                history.ErrorMessage = !string.IsNullOrEmpty(result.Error) ? result.Error : $"命令执行失败(退出码:{result.ExitCode})";
                if (!string.IsNullOrEmpty(result.Output))
                    history.ErrorMessage += " | " + result.Output[..Math.Min(200, result.Output.Length)];
                history.CompletedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            history.Status = BackupJobStatus.Failed;
            history.ErrorMessage = ex.Message;
            history.CompletedAt = DateTime.UtcNow;
            Debug.WriteLine($"Backup job {job.Name} failed: {ex.Message}");
        }

        return history;
    }

    // ─── Physical Backup (pg_basebackup) ──────────────────────

    public async Task<BackupHistory> ExecutePhysicalBackupAsync(
        ServerConnection server, PgInstance instance, BackupJob job, CancellationToken ct = default)
    {
        var history = new BackupHistory { BackupJobId = job.Id, StartedAt = DateTime.UtcNow, Status = BackupJobStatus.Running };

        try
        {
            // Pre-check: disk space (physical backup needs even more space)
            var spaceCheck = await CheckDiskSpaceAsync(server, instance, job.RemotePath, job.Database ?? instance.Database, ct);
            spaceCheck.RequiredMb *= 2; // Physical backup is larger (~2x logical)
            // Recompute CanProceed after doubling RequiredMb
            spaceCheck.CanProceed = spaceCheck.AvailableMb > spaceCheck.RequiredMb + 500;
            if (!spaceCheck.CanProceed)
            {
                history.Status = BackupJobStatus.Failed;
                history.ErrorMessage = $"物理备份磁盘空间不足！可用{spaceCheck.AvailableMb:F0}MB, 预计需要{spaceCheck.RequiredMb:F0}MB (数据库{spaceCheck.EstimatedBackupSizeMb:F0}MB × 3)";
                history.CompletedAt = DateTime.UtcNow;
                return history;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var dirName = $"basebackup_{timestamp}";
            var remotePath = job.RemotePath.StartsWith("~") ? "$HOME" + job.RemotePath[1..] : job.RemotePath;
            var dirPath = $"{remotePath.TrimEnd('/')}/{dirName}";

            // pg_basebackup creates the directory itself (must NOT pre-exist)
            await _sshService.ExecuteCommandAsync(server, $"rm -rf {dirPath}", ct);

            // Find pg_basebackup path
            var bbPath = "/usr/bin/pg_basebackup";
            var bbCheck = await _sshService.ExecuteCommandAsync(server, $"test -f {bbPath} && echo OK || echo 'NOTFOUND'", ct);
            if (!bbCheck.Success || bbCheck.Output.Contains("NOTFOUND"))
                bbPath = "pg_basebackup"; // fallback to PATH

            // pg_basebackup requires REPLICATION privilege (superuser has it)
            var baseBackupCmd = $"PGSSLMODE=disable PGPASSWORD='{Pwd(instance)}' {bbPath} " +
                                $"-h {instance.Host} -p {instance.Port} -U {instance.Username} " +
                                $"-D {dirPath} -Fp -Xs -P -R -v 2>&1";

            var result = await _sshService.ExecuteCommandWithProgressAsync(
                server, baseBackupCmd,
                onOutput: msg => Debug.WriteLine($"pg_basebackup [{job.Name}]: {msg}"),
                onError: err => Debug.WriteLine($"pg_basebackup [{job.Name}] error: {err}"),
                ct: ct);

            if (result.Success)
            {
                history.Status = BackupJobStatus.Success;
                history.FilePath = dirPath;
                history.CompletedAt = DateTime.UtcNow;
                history.DurationSeconds = result.Duration.TotalSeconds;
            }
            else
            {
                history.Status = BackupJobStatus.Failed;
                history.ErrorMessage = !string.IsNullOrEmpty(result.Error) ? result.Error : $"命令执行失败(退出码:{result.ExitCode})";
                if (!string.IsNullOrEmpty(result.Output))
                    history.ErrorMessage += " | " + result.Output[..Math.Min(200, result.Output.Length)];
                history.CompletedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            history.Status = BackupJobStatus.Failed;
            history.ErrorMessage = ex.Message;
            history.CompletedAt = DateTime.UtcNow;
            Debug.WriteLine($"Physical backup {job.Name} failed: {ex.Message}");
        }

        return history;
    }

    // ─── Restore ──────────────────────────────────────────────

    public async Task<SshResult> ExecuteRestoreAsync(
        ServerConnection server, PgInstance instance, RestoreRequest request, CancellationToken ct = default)
    {
        return request.Type switch
        {
            RestoreType.Full => await ExecuteFullRestoreAsync(server, instance, request, ct),
            RestoreType.PointInTime => await ExecutePitrRestoreAsync(server, instance, request, ct),
            RestoreType.SelectiveDatabase => await ExecuteDbRestoreAsync(server, instance, request, ct),
            _ => new SshResult { Success = false, Error = "未知恢复类型" }
        };
    }

    private async Task<SshResult> ExecuteDbRestoreAsync(
        ServerConnection server, PgInstance instance, RestoreRequest request, CancellationToken ct)
    {
        var db = request.Database ?? instance.Database;
        var flags = request.CleanBeforeRestore ? "-c" : "";
        if (request.CreateDatabase) flags += " -C";

        var restoreCmd = $"PGPASSWORD='{Pwd(instance)}' " +
                         $"pg_restore -h {instance.Host} -p {instance.Port} -U {instance.Username} " +
                         $"-d {db} {flags} -v '{request.BackupFilePath}'";

        return await _sshService.ExecuteCommandWithProgressAsync(
            server, restoreCmd,
            onOutput: msg => Debug.WriteLine($"pg_restore: {msg}"),
            onError: err => Debug.WriteLine($"pg_restore error: {err}"),
            ct: ct);
    }

    private async Task<SshResult> ExecuteFullRestoreAsync(
        ServerConnection server, PgInstance instance, RestoreRequest request, CancellationToken ct)
    {
        // Stop PG, clear data dir, restore from basebackup
        var stopResult = await _sshService.ExecuteCommandAsync(server,
            "systemctl stop postgresql", ct);
        if (!stopResult.Success)
            return new SshResult { Success = false, Error = "无法停止PG服务: " + stopResult.Error };

        // Clear data dir and restore
        var dataDir = instance.DataDirectory ?? "/var/lib/postgresql/16/main";
        var restoreCmd = $"rm -rf {dataDir}/* && cp -r {request.BackupFilePath}/* {dataDir}/ && " +
                         $"chown -R postgres:postgres {dataDir} && systemctl start postgresql";

        var result = await _sshService.ExecuteCommandAsync(server, restoreCmd, ct);
        return result;
    }

    private async Task<SshResult> ExecutePitrRestoreAsync(
        ServerConnection server, PgInstance instance, RestoreRequest request, CancellationToken ct)
    {
        var dataDir = instance.DataDirectory ?? "/var/lib/postgresql/16/main";
        var stopResult = await _sshService.ExecuteCommandAsync(server,
            "systemctl stop postgresql", ct);
        if (!stopResult.Success)
            return new SshResult { Success = false, Error = "无法停止PG服务" };

        // Create recovery.signal and configure recovery
        var targetTime = request.TargetTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "latest";
        var setupCmd = $"rm -rf {dataDir}/* && " +
                       $"cp -r {request.BackupFilePath}/* {dataDir}/ && " +
                       $"touch {dataDir}/recovery.signal && " +
                       $"echo \"restore_command = 'cp /archive/%f %p'\" >> {dataDir}/postgresql.auto.conf && " +
                       $"echo \"recovery_target_time = '{targetTime}'\" >> {dataDir}/postgresql.auto.conf && " +
                       $"chown -R postgres:postgres {dataDir} && systemctl start postgresql";

        return await _sshService.ExecuteCommandAsync(server, setupCmd, ct);
    }

    // ─── Replication Host Fix ──────────────────────────────────

    public async Task<string> FixReplicationHostAsync(ServerConnection server, string? sudoPassword = null, CancellationToken ct = default)
    {
        // Find the newest PG version's pg_hba.conf
        var verResult = await _sshService.ExecuteCommandAsync(server,
            "ls /etc/postgresql/ 2>/dev/null | sort -V | tail -1", ct);
        var pgVer = verResult.Success ? verResult.Output.Trim() : "16";
        var pgHbaPath = $"/etc/postgresql/{pgVer}/main/pg_hba.conf";

        // Check if replication entry exists
        var checkR = await _sshService.ExecuteCommandAsync(server,
            $"grep -q 'host.*replication' {pgHbaPath} 2>/dev/null && echo 'FOUND' || echo 'MISSING'", ct);

        if (checkR.Output.Contains("FOUND"))
            return "✅ 复制连接配置已存在，无需修复";

        // Try to add it (with or without sudo password)
        var sudo = string.IsNullOrEmpty(sudoPassword) ? "sudo" : $"echo '{sudoPassword.Replace("'", "'\\''")}' | sudo -S";
        var addR = await _sshService.ExecuteCommandAsync(server,
            $"{sudo} sh -c 'echo \"host replication all 0.0.0.0/0 md5\" >> {pgHbaPath}' 2>&1 && " +
            $"{sudo} systemctl reload postgresql 2>&1 && echo 'OK' || echo 'FAILED'", ct);

        if (addR.Output.Contains("OK"))
            return $"✅ 已添加 replication 条目到 {pgHbaPath} 并重载PG配置";

        return $"❌ 自动修复失败。请SSH到服务器手动执行:\n" +
               $"  sudo sh -c 'echo \"host replication all 0.0.0.0/0 md5\" >> {pgHbaPath}'\n" +
               "  sudo systemctl reload postgresql";
    }

    // ─── PITR Configuration Check ─────────────────────────────

    public async Task<PitrConfig> CheckPitrConfigAsync(ServerConnection server, PgInstance instance, CancellationToken ct = default)
    {
        var config = new PitrConfig { ServerId = server.Id };
        var psql = $"PGPASSWORD='{Pwd(instance)}' psql -h {instance.Host} -p {instance.Port} -U {instance.Username} -d {instance.Database} -t -A -c";

        // Check wal_level
        var r = await _sshService.ExecuteCommandAsync(server,
            $"{psql} \"SHOW wal_level;\" 2>&1", ct);
        config.IsValid = r.Success && r.Output.Trim() is "replica" or "logical";

        if (!config.IsValid)
        {
            config.ValidationError = $"wal_level={r.Output.Trim()}, 需要replica或logical才能PITR";
            return config;
        }

        // Check archive_mode
        r = await _sshService.ExecuteCommandAsync(server,
            $"{psql} \"SHOW archive_mode;\" 2>&1", ct);
        config.ArchiveMode = r.Output.Trim() == "on" || r.Output.Trim() == "always";

        // Check archive_command
        r = await _sshService.ExecuteCommandAsync(server,
            $"{psql} \"SHOW archive_command;\" 2>&1", ct);
        config.ArchiveCommand = r.Output.Trim();

        if (!config.ArchiveMode || string.IsNullOrEmpty(config.ArchiveCommand))
        {
            config.ValidationError = "archive_mode未启用或archive_command未配置";
            config.IsValid = false;
        }

        return config;
    }

    // ─── Helpers ──────────────────────────────────────────────

    private async Task DownloadBackupAsync(ServerConnection server, string remotePath, string localDir, CancellationToken ct)
    {
        // SCP download not yet implemented; skip silently for now
        Debug.WriteLine($"SCP download not implemented: {remotePath} -> {localDir}");
        await Task.CompletedTask;
    }

    private async Task CleanupOldBackupsAsync(ServerConnection server, BackupJob job, CancellationToken ct)
    {
        var cleanupCmd = $"find {job.RemotePath} -name '*.dump' -mtime +{job.RetentionDays} -delete 2>/dev/null; " +
                         $"find {job.RemotePath} -name '*.sql*' -mtime +{job.RetentionDays} -delete 2>/dev/null; " +
                         $"echo 'Cleanup done'";

        await _sshService.ExecuteCommandAsync(server, cleanupCmd, ct);
    }
}
