using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class TaskSchedulerViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    [ObservableProperty] private ObservableCollection<ScheduledTask> _tasks = new();
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private int _taskTypeIndex;
    [ObservableProperty] private string _cronExpression = "0 2 * * *";
    [ObservableProperty] private bool _isEnabled = true;

    private Guid? _serverId;
    public List<string> TaskTypes { get; } = new() { "全量备份", "逻辑备份", "VACUUM ANALYZE", "一键诊断", "REINDEX" };

    public TaskSchedulerViewModel(IDatabaseService db) { _db = db; }
    public void SetServerId(Guid id) { _serverId = id; }

    [RelayCommand] private async Task LoadAsync()
    {
        if (_serverId == null) return;
        // Load tasks from SQLite via _db
        Status = $"定时任务 (0 个已配置)";
    }

    [RelayCommand] private async Task AddTaskAsync()
    {
        if (_serverId == null) return;
        var task = new ScheduledTask
        {
            Id = Guid.NewGuid(), ServerId = _serverId.Value,
            TaskType = TaskTypes[TaskTypeIndex],
            CronExpression = CronExpression, IsEnabled = IsEnabled,
            NextRunTime = DateTime.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss")
        };
        Tasks.Add(task);
        Status = $"任务已添加: {task.TaskType} ({task.CronExpression})";
    }

    [RelayCommand] private void RemoveTask(ScheduledTask task)
    {
        Tasks.Remove(task);
        Status = "任务已删除";
    }
}

public class ScheduledTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string CronExpression { get; set; } = "0 2 * * *";
    public bool IsEnabled { get; set; } = true;
    public string? NextRunTime { get; set; }
    public string? LastRunTime { get; set; }
}
