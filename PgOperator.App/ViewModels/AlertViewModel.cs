using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.Core.Interfaces;
using PgOperator.Core.Models;

namespace PgOperator.App.ViewModels;

public partial class AlertViewModel : ObservableObject
{
    private readonly IDatabaseService _db;
    [ObservableProperty] private ObservableCollection<AlertRuleItem> _rules = new();
    [ObservableProperty] private string _status = "就绪";
    [ObservableProperty] private string _metricKey = "connections";
    [ObservableProperty] private int _thresholdOpIndex;
    [ObservableProperty] private double _thresholdValue = 80;
    [ObservableProperty] private int _severityIndex;
    [ObservableProperty] private int _cooldownMin = 60;
    [ObservableProperty] private bool _isEnabled = true;

    private Guid? _serverId;
    public List<string> Metrics { get; } = new() { "connections", "disk_usage_pct", "replication_lag", "slow_query_count", "xid_usage_pct", "buffer_hit_ratio", "lock_wait_count", "backup_age_hours" };
    public List<string> Operators { get; } = new() { "> (大于)", "< (小于)", ">= (大于等于)", "<= (小于等于)" };
    public List<string> Severities { get; } = new() { "warning", "critical", "info" };

    public AlertViewModel(IDatabaseService db) { _db = db; }

    public void SetServerId(Guid id) { _serverId = id; }

    [RelayCommand] private async Task LoadAsync()
    {
        if (_serverId == null) return;
        var json = await _db.GetSettingAsync($"alert_rules_{_serverId}");
        if (!string.IsNullOrEmpty(json))
        {
            var rules = System.Text.Json.JsonSerializer.Deserialize<List<AlertRuleItem>>(json);
            if (rules != null) Rules = new ObservableCollection<AlertRuleItem>(rules);
        }
        Status = $"已加载 {Rules.Count} 条告警规则";
    }

    [RelayCommand] private async Task AddRuleAsync()
    {
        if (_serverId == null) return;
        var rule = new AlertRuleItem
        {
            Id = Guid.NewGuid(), ServerId = _serverId.Value, MetricKey = MetricKey,
            Operator = ThresholdOpIndex switch { 0 => ">", 1 => "<", 2 => ">=", _ => "<=" },
            ThresholdValue = ThresholdValue, Severity = Severities[SeverityIndex],
            CooldownMin = CooldownMin, IsEnabled = IsEnabled
        };
        Rules.Add(rule);
        await PersistRules();
        Status = $"告警规则已添加: {rule.MetricKey} {rule.Operator} {rule.ThresholdValue}";
    }

    private async Task PersistRules()
    {
        if (_serverId == null) return;
        var json = System.Text.Json.JsonSerializer.Serialize(Rules.ToList());
        await _db.SaveSettingAsync($"alert_rules_{_serverId}", json);
    }

    [RelayCommand] private async Task RemoveRule(AlertRuleItem rule)
    {
        Rules.Remove(rule);
        await PersistRules();
        Status = "告警规则已删除";
    }
}

public class AlertRuleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServerId { get; set; }
    public string MetricKey { get; set; } = string.Empty;
    public string Operator { get; set; } = ">";
    public double ThresholdValue { get; set; }
    public string Severity { get; set; } = "warning";
    public int CooldownMin { get; set; } = 60;
    public bool IsEnabled { get; set; } = true;
}
