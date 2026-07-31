namespace PgOperator.AI.Providers;

/// <summary>
/// Abstraction for different LLM backends.
/// </summary>
public interface IAiProvider : IDisposable
{
    string Name { get; }
    Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
}
