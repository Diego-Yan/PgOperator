using PgOperator.Core.Models;

namespace PgOperator.Diagnostics;

/// <summary>
/// Interface for a single diagnostic check.
/// </summary>
public interface IDiagnosticCheck
{
    string CheckId { get; }
    string CheckName { get; }
    string Title { get; }
    int Layer { get; }
    string Category { get; }
    int Priority { get; }
    Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext context);
}

/// <summary>
/// Base class for diagnostic checks with helper methods.
/// </summary>
public abstract class DiagnosticCheckBase : IDiagnosticCheck
{
    public abstract string CheckId { get; }
    public abstract string CheckName { get; }
    public abstract string Title { get; }
    public abstract int Layer { get; }
    public abstract string Category { get; }
    public abstract int Priority { get; }

    public abstract Task<DiagnosticFinding> ExecuteAsync(DiagnosticContext context);

    protected DiagnosticFinding Ok(string detail = "") => new()
    {
        Id = $"{CheckId}-OK",
        Layer = Layer, Category = Category, Severity = "pass",
        CheckName = CheckName, Title = Title, Detail = string.IsNullOrEmpty(detail) ? $"{Title}: 正常" : detail,
        Impact = ""
    };

    protected DiagnosticFinding Warning(string detail, string impact = "", DiagnosticMetric? metric = null,
        DiagnosticSuggestion? suggestion = null) => new()
    {
        Id = $"{CheckId}-WARN",
        Layer = Layer, Category = Category, Severity = "warning",
        CheckName = CheckName, Title = Title, Detail = detail, Impact = impact,
        Metric = metric, Suggestion = suggestion
    };

    protected DiagnosticFinding Critical(string detail, string impact = "", DiagnosticMetric? metric = null,
        DiagnosticSuggestion? suggestion = null) => new()
    {
        Id = $"{CheckId}-CRIT",
        Layer = Layer, Category = Category, Severity = "critical",
        CheckName = CheckName, Title = Title, Detail = detail, Impact = impact,
        Metric = metric, Suggestion = suggestion
    };

    protected DiagnosticFinding Info(string detail, DiagnosticSuggestion? suggestion = null) => new()
    {
        Id = $"{CheckId}-INFO",
        Layer = Layer, Category = Category, Severity = "info",
        CheckName = CheckName, Title = Title, Detail = detail, Impact = "",
        Suggestion = suggestion
    };
}
