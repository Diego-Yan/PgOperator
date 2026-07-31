using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PgOperator.AI;
using PgOperator.AI.Models;

namespace PgOperator.App.ViewModels;

public partial class AiSettingsViewModel : ObservableObject
{
    private readonly AiAnalysisService _aiService;

    [ObservableProperty] private int _providerIndex;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _apiEndpoint = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private int _preferenceIndex = 1; // balanced
    [ObservableProperty] private int _focusIndex; // performance
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isConfigured;

    public List<string> Providers { get; } = new() { "DeepSeek", "OpenAI", "Claude", "Ollama", "自定义" };
    public List<string> Preferences { get; } = new() { "激进 (积极优化)", "平衡 (效果与风险并重)", "保守 (优先低风险)" };
    public List<string> Focuses { get; } = new() { "性能优先", "安全优先", "成本优先" };

    public AiSettingsViewModel(AiAnalysisService aiService)
    {
        _aiService = aiService;
        IsConfigured = aiService.IsConfigured;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) && ProviderIndex != 3) // Ollama不需要key
        {
            StatusMessage = "API Key不能为空";
            return;
        }

        var config = new AiConfig
        {
            Provider = Providers[ProviderIndex].ToLower(),
            ApiKey = ProviderIndex == 3 ? "ollama" : ApiKey,
            ApiEndpoint = string.IsNullOrWhiteSpace(ApiEndpoint) ? null : ApiEndpoint,
            Model = string.IsNullOrWhiteSpace(Model) ? null : Model,
            Preference = PreferenceIndex switch { 0 => "aggressive", 2 => "conservative", _ => "balanced" },
            Focus = FocusIndex switch { 1 => "security", 2 => "cost", _ => "performance" }
        };

        try
        {
            _aiService.Configure(config);
            IsConfigured = true;
            StatusMessage = "AI配置成功！";
        }
        catch (Exception ex)
        {
            StatusMessage = $"配置失败: {ex.Message}";
        }
    }
}
