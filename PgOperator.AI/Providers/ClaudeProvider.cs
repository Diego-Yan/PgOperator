using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PgOperator.AI.Providers;

/// <summary>
/// Anthropic Claude API provider using the Messages API.
/// </summary>
public class ClaudeProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    public void Dispose() => _httpClient.Dispose();

    public string Name => "Claude";

    public ClaudeProvider(string apiKey, string model = "claude-sonnet-4-6")
    {
        _model = model;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.anthropic.com/v1/"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            max_tokens = 4096,
            temperature = 0.3,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("messages", content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ClaudeResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        return result?.Content?.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Empty response from Claude");
    }

    private class ClaudeResponse
    {
        public List<ClaudeContentBlock>? Content { get; set; }
    }

    private class ClaudeContentBlock
    {
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
