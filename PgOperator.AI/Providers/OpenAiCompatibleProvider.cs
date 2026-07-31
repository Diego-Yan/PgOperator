using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PgOperator.AI.Models;

namespace PgOperator.AI.Providers;

/// <summary>
/// Generic OpenAI-compatible API provider. Works with OpenAI, DeepSeek, and any
/// API that implements the /v1/chat/completions endpoint.
/// </summary>
public class OpenAiCompatibleProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    public void Dispose() => _httpClient.Dispose();

    public string Name { get; }

    public OpenAiCompatibleProvider(string name, string apiKey, string? apiEndpoint = null, string model = "gpt-4o")
    {
        Name = name;
        _model = model;

        var baseUrl = apiEndpoint ?? "https://api.openai.com/v1";
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
    }

    public async Task<string> ChatAsync(string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            },
            temperature = 0.3,
            max_tokens = 4096
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("chat/completions", content, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

        return result?.Choices?.FirstOrDefault()?.Message?.Content
            ?? throw new InvalidOperationException("Empty response from AI provider");
    }

    private class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public MessageContent? Message { get; set; }
    }

    private class MessageContent
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}

/// <summary>
/// Factory for creating AI providers based on config.
/// </summary>
public static class AiProviderFactory
{
    public static IAiProvider Create(AiConfig config)
    {
        return config.Provider.ToLowerInvariant() switch
        {
            "deepseek" => new OpenAiCompatibleProvider(
                "DeepSeek", config.ApiKey!,
                config.ApiEndpoint ?? "https://api.deepseek.com/v1",
                config.Model ?? "deepseek-chat"),

            "openai" => new OpenAiCompatibleProvider(
                "OpenAI", config.ApiKey!,
                config.ApiEndpoint ?? "https://api.openai.com/v1",
                config.Model ?? "gpt-4o"),

            "ollama" => new OpenAiCompatibleProvider(
                "Ollama", "ollama",
                config.ApiEndpoint ?? "http://localhost:11434/v1",
                config.Model ?? "llama3"),

            "claude" => new ClaudeProvider(config.ApiKey!, config.Model ?? "claude-sonnet-4-6"),

            _ => new OpenAiCompatibleProvider(
                "Custom", config.ApiKey!,
                config.ApiEndpoint ?? "https://api.openai.com/v1",
                config.Model ?? "gpt-4o")
        };
    }
}
