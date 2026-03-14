using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace AgenticMarker.LLM;

public record Message(
    string Role,
    string? Content = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    public static Message System(string content) => new("system", Content: content);
    public static Message User(string content) => new("user", Content: content);
    public static Message Assistant(string content) => new("assistant", Content: content);
    public static Message Tool(string toolCallId, string content) => new("tool", Content: content, ToolCallId: toolCallId);
    public static Message AssistantWithToolCalls(string? content, IReadOnlyList<ToolCall> toolCalls) =>
        new("assistant", Content: content, ToolCalls: toolCalls);

    public object ToApiFormat()
    {
        if (Role == "tool")
        {
            return new { role = Role, tool_call_id = ToolCallId, content = Content };
        }

        if (ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = Role,
                content = Content,
                tool_calls = ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new
                    {
                        name = tc.FunctionName,
                        arguments = tc.ArgumentsJson
                    }
                }).ToArray()
            };
        }

        return new { role = Role, content = Content };
    }
}

public record ToolCall(string Id, string FunctionName, JsonElement Arguments, string ArgumentsJson);

public record ToolDefinition(string Name, string Description, JsonElement Parameters)
{
    public object ToApiFormat() => new
    {
        type = "function",
        function = new
        {
            name = Name,
            description = Description,
            parameters = Parameters
        }
    };
}

public record LlmResponse(string? Content, IReadOnlyList<ToolCall> ToolCalls, string? FinishReason);

public class OpenRouterClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxTokens;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenRouterClient(IConfiguration config)
    {
        var section = config.GetSection("OpenRouter");
        _model = section["Model"] ?? "anthropic/claude-sonnet-4.6";
        _maxTokens = int.Parse(section["MaxTokens"] ?? "4096", System.Globalization.CultureInfo.InvariantCulture);
        var baseUrl = section["BaseUrl"] ?? "https://openrouter.ai/api/v1";
        var apiKey = section["ApiKey"] ?? throw new InvalidOperationException("OpenRouter API key not configured. Set it in appsettings.json.");

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OpenRouter API key is empty. Set it in appsettings.json.");

        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<LlmResponse> ChatAsync(List<Message> messages, List<ToolDefinition> tools)
    {
        var request = new
        {
            model = _model,
            max_tokens = _maxTokens,
            messages = messages.Select(m => m.ToApiFormat()).ToArray(),
            tools = tools.Count > 0 ? tools.Select(t => t.ToApiFormat()).ToArray() : null as object[]
        };

        var json = JsonSerializer.Serialize(request, SerializerOptions);

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("chat/completions", content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter API error ({response.StatusCode}): {responseJson}");

        if (!responseJson.TrimStart().StartsWith('{'))
            throw new InvalidOperationException($"OpenRouter returned non-JSON response: {responseJson[..Math.Min(200, responseJson.Length)]}");

        return ParseResponse(responseJson);
    }

    private static LlmResponse ParseResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            throw new InvalidOperationException("No choices in API response");

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null;

        string? content = null;
        if (message.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
        {
            content = contentProp.GetString();
        }

        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCallsProp.EnumerateArray())
            {
                var id = tc.GetProperty("id").GetString() ?? "";
                var function = tc.GetProperty("function");
                var name = function.GetProperty("name").GetString() ?? "";
                var argumentsStr = function.GetProperty("arguments").GetString() ?? "{}";

                using var argsDoc = JsonDocument.Parse(argumentsStr);
                toolCalls.Add(new ToolCall(id, name, argsDoc.RootElement.Clone(), argumentsStr));
            }
        }

        return new LlmResponse(content, toolCalls, finishReason);
    }
}
