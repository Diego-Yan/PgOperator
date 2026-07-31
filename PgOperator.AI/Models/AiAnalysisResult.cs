using System.Linq;

namespace PgOperator.AI.Models;

public class AiAnalysisResult
{
    public string Summary { get; set; } = string.Empty;
    public List<AiRecommendation> Recommendations { get; set; } = new();
    public string RawResponse { get; set; } = string.Empty;
    public string? Error { get; set; }
    public bool Success => string.IsNullOrEmpty(Error);
}

public class AiRecommendation
{
    public string Priority { get; set; } = "P2";
    public string Title { get; set; } = string.Empty;
    public string RootCause { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public List<string> ActionSteps { get; set; } = new();
    public string ActionStepsStr => string.Join("\n", ActionSteps.Select(s => $"$ {s}"));
    public string Difficulty { get; set; } = "中";
    public string Risk { get; set; } = "低";
    public string? EstimatedTime { get; set; }
}

public class AiConfig
{
    public string Provider { get; set; } = "deepseek";
    public string? ApiKey { get; set; }
    public string? ApiEndpoint { get; set; }
    public string? Model { get; set; }
    public string Preference { get; set; } = "balanced"; // aggressive, balanced, conservative
    public string Focus { get; set; } = "performance"; // performance, security, cost
}
