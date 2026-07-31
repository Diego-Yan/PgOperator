using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;
using PgOperator.Core.Services;

namespace PgOperator.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly BackupService _backupService;
    private ServerConnection? _server;
    private PgInstance? _pgInstance;

    // Backup execution
    [ObservableProperty] private string _sudoPassword = string.Empty;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _remotePath = "$HOME/pg_backups";
    [ObservableProperty] private string? _selectedDatabase;
    [ObservableProperty] private int _backupTypeIndex;
    [ObservableProperty] private int _formatIndex;
    [ObservableProperty] private int _retentionDays = 7;
    [ObservableProperty] private string _lastBackupInfo = "尚未备份";

    // Disk space
    [ObservableProperty] private string _diskSpaceInfo = "点击检查";
    [ObservableProperty] private bool _canBackup;

    // Backup file list
    [ObservableProperty] private ObservableCollection<BackupFileInfo> _backupFiles = new();
    [ObservableProperty] private BackupFileInfo? _selectedFile;
    [ObservableProperty] private string _totalBackupSize = string.Empty;
    [ObservableProperty] private int _selectedTabIndex; // 0=执行, 1=管理

    // Cleanup
    [ObservableProperty] private int _cleanupDays = 7;

    public List<string> BackupTypes { get; } = new() { "逻辑备份 (pg_dump)", "物理备份 (pg_basebackup)" };
    public List<string> Formats { get; } = new() { "Custom (.dump)", "Plain (.sql)", "Directory", "Tar" };

    public BackupViewModel(BackupService backupService)
    {
        _backupService = backupService;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 1) _ = RefreshFileListCommand.ExecuteAsync(null);
    }

    public void SetContext(ServerConnection server, PgInstance? pgInstance)
    {
        _server = server;
        _pgInstance = pgInstance;
    }

    // ─── Disk Space Check ───────────────────────────────────

    [RelayCommand]
    private async Task CheckDiskSpaceAsync()
    {
        if (_server == null || _pgInstance == null) return;
        IsRunning = true;
        try
        {
            var check = await _backupService.CheckDiskSpaceAsync(_server, _pgInstance,
                RemotePath, SelectedDatabase ?? _pgInstance.Database);
            DiskSpaceInfo = check.Reason;
            CanBackup = check.CanProceed;

            var usageIcon = check.UsagePercent switch { > 90 => "🔴", > 75 => "⚠️", _ => "✅" };
            DiskSpaceInfo = $"{usageIcon} {check.Reason} (分区使用{check.UsagePercent}%)";
        }
        catch (Exception ex) { DiskSpaceInfo = $"检查失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    // ─── Backup Execution ────────────────────────────────────

    [RelayCommand]
    private async Task RunBackupAsync()
    {
        if (_server == null || _pgInstance == null) return;
        IsRunning = true;
        StatusMessage = "正在检查磁盘空间...";

        try
        {
            // Pre-check
            var spaceCheck = await _backupService.CheckDiskSpaceAsync(_server, _pgInstance, RemotePath,
                SelectedDatabase ?? _pgInstance.Database);
            DiskSpaceInfo = spaceCheck.Reason;
            CanBackup = spaceCheck.CanProceed;

            if (!spaceCheck.CanProceed)
            {
                StatusMessage = $"❌ 备份取消: {spaceCheck.Reason}";
                IsRunning = false;
                return;
            }

            StatusMessage = "正在执行备份...";
            var job = new BackupJob
            {
                ServerId = _server.Id, PgInstanceId = _pgInstance.Id,
                Name = $"手动备份-{DateTime.Now:yyyyMMdd-HHmmss}",
                Type = BackupTypeIndex == 0 ? BackupType.Logical : BackupType.Physical,
                Format = (BackupFormat)FormatIndex,
                Database = SelectedDatabase ?? _pgInstance.Database,
                RemotePath = RemotePath, RetentionDays = RetentionDays
            };

            BackupHistory history = job.Type == BackupType.Physical
                ? await _backupService.ExecutePhysicalBackupAsync(_server, _pgInstance, job)
                : await _backupService.ExecuteLogicalBackupAsync(_server, _pgInstance, job);

            LastBackupInfo = history.Status == BackupJobStatus.Success
                ? $"✅ 备份成功 ({history.DurationSeconds:F1}s{(history.FileSizeBytes.HasValue ? $", {history.FileSizeBytes.Value / 1024.0 / 1024.0:F1}MB" : "")})"
                : $"❌ 备份失败: {history.ErrorMessage}";
            StatusMessage = LastBackupInfo;

            if (history.Status == BackupJobStatus.Success)
                await RefreshFileListCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { StatusMessage = $"备份异常: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    // ─── Backup File Management ──────────────────────────────

    [RelayCommand]
    private async Task RefreshFileListAsync()
    {
        if (_server == null) return;
        try
        {
            var files = await _backupService.ListBackupsAsync(_server, RemotePath);
            BackupFiles = new ObservableCollection<BackupFileInfo>(files);
            var totalSize = await _backupService.GetTotalBackupSizeAsync(_server, RemotePath);
            TotalBackupSize = totalSize switch
            {
                >= 1_073_741_824 => $"{totalSize / 1_073_741_824.0:F1} GB",
                >= 1_048_576 => $"{totalSize / 1_048_576.0:F1} MB",
                _ => $"{totalSize / 1024.0:F1} KB"
            };
            StatusMessage = $"共 {BackupFiles.Count} 个备份文件, 总计 {TotalBackupSize}";
        }
        catch (Exception ex) { StatusMessage = $"列表加载失败: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (_server == null || SelectedFile == null) return;
        var result = await _backupService.DeleteBackupAsync(_server, SelectedFile.FilePath);
        StatusMessage = result.Success ? $"已删除: {SelectedFile.FileName}" : $"删除失败: {result.Error}";
        if (result.Success) await RefreshFileListCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task CleanupOldBackupsAsync()
    {
        if (_server == null) return;
        IsRunning = true;
        StatusMessage = $"正在清理 {CleanupDays} 天前的备份...";
        try
        {
            var result = await _backupService.DeleteOldBackupsAsync(_server, RemotePath, CleanupDays);
            StatusMessage = result.Message;
            await RefreshFileListCommand.ExecuteAsync(null);
        }
        catch (Exception ex) { StatusMessage = $"清理失败: {ex.Message}"; }
        finally { IsRunning = false; }
    }

    [RelayCommand]
    private async Task ValidateSelectedAsync()
    {
        if (_server == null || _pgInstance == null || SelectedFile == null) return;
        if (!SelectedFile.IsLogical) { StatusMessage = "仅逻辑备份(.dump)支持校验"; return; }
        IsRunning = true;
        var valid = await _backupService.ValidateBackupAsync(_server, _pgInstance, SelectedFile.FilePath);
        StatusMessage = valid ? $"✅ {SelectedFile.FileName} 校验通过" : $"❌ {SelectedFile.FileName} 校验失败，备份可能损坏！";
        IsRunning = false;
    }

    // ─── PITR ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task FixReplicationAsync()
    {
        if (_server == null) { StatusMessage = "错误: 未连接到服务器"; return; }
        StatusMessage = "正在检查并修复复制连接配置...";
        StatusMessage = await _backupService.FixReplicationHostAsync(_server, SudoPassword);
    }

    [RelayCommand]
    private async Task CheckPitrAsync()
    {
        if (_server == null) { StatusMessage = "错误: 未连接到服务器"; return; }
        if (_pgInstance == null) { StatusMessage = "错误: 未配置PG数据库密码"; return; }
        StatusMessage = "正在检查PITR配置...";
        var config = await _backupService.CheckPitrConfigAsync(_server, _pgInstance);
        StatusMessage = config.IsValid
            ? "✅ PITR配置正常 (archive_mode=on, archive_command已配置)"
            : $"❌ PITR未就绪: {config.ValidationError}";
    }
}
