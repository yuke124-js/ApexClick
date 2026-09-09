using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApexClick.Models;

namespace ApexClick.Services.AI;

public sealed record PromptInterpreterOptions
{
    public string Endpoint { get; init; } = Environment.GetEnvironmentVariable("APEXCLICK_AI_ENDPOINT")
        ?? "https://api.openai.com/v1/chat/completions";
    public string ApiKey { get; init; } = Environment.GetEnvironmentVariable("APEXCLICK_AI_API_KEY") ?? string.Empty;
    public string Model { get; init; } = Environment.GetEnvironmentVariable("APEXCLICK_AI_MODEL") ?? "gpt-4.1-mini";
    public double Temperature { get; init; } = 0.1;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(45);
}

public sealed class PromptInterpreterService : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private PromptInterpreterOptions _options;

    public PromptInterpreterService(HttpClient? http = null, PromptInterpreterOptions? options = null)
    {
        _options = options ?? new PromptInterpreterOptions();
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        _http.Timeout = _options.Timeout;
    }

    public void Configure(string endpoint, string apiKey, string model, double temperature = 0.1)
    {
        _options = new PromptInterpreterOptions
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? _options.Endpoint : endpoint,
            ApiKey = apiKey ?? string.Empty,
            Model = string.IsNullOrWhiteSpace(model) ? _options.Model : model,
            Temperature = Math.Clamp(temperature, 0, 2),
            Timeout = _options.Timeout
        };
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    public async Task<MacroScript> GenerateFromPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("APEXCLICK_AI_API_KEY не задан.");

        const string systemPrompt = """
You generate ApexClick automation plans. Return ONLY one JSON object:
{"name":"string","description":"string","events":[{"type":"MouseMove|MouseButtonDown|MouseButtonUp|MouseWheel|KeyDown|KeyUp|TextInput|Wait","timestampMs":0,"normalizedX":null,"normalizedY":null,"virtualKeyCode":null,"scanCode":null,"keyboardLayoutId":null,"payload":null,"requiresClarification":false,"clarification":""}]}
Never emit shell commands, PowerShell, arbitrary OS execution, process launch, or executable code.
The result is a plan only and will be reviewed by the user; it must never be executed automatically.
If exact coordinates or an action cannot be inferred safely from the prompt, set requiresClarification=true and explain it in clarification instead of inventing a precise action.
""";

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("LLM не вернул JSON.");

        var generated = JsonSerializer.Deserialize<GeneratedScenario>(content, SerializerOptions)
            ?? throw new InvalidDataException("Ответ LLM не соответствует схеме.");

        var script = new MacroScript { Name = string.IsNullOrWhiteSpace(generated.Name) ? "AI Scenario" : generated.Name };
        if (!string.IsNullOrWhiteSpace(generated.Description)) script.Metadata["ai.description"] = generated.Description;
        script.Metadata["ai.generated"] = "true";
        script.Metadata["ai.requiresUserReview"] = "true";
        script.Metadata["ai.sourcePromptHash"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userPrompt)));

        long previous = -1;
        foreach (var item in generated.Events)
        {
            if (!Enum.TryParse<MacroEventType>(item.Type, ignoreCase: true, out var type) ||
                type is MacroEventType.ConditionScreenTemplate or MacroEventType.ConditionScreenPixel or MacroEventType.ConditionOcrText or MacroEventType.Loop or MacroEventType.Variable or MacroEventType.SubScenarioCall)
                throw new InvalidDataException($"AI сгенерировал тип, недопустимый в базовой action-схеме: {item.Type}");

            var ts = Math.Max(previous + 1, item.TimestampMs);
            var evt = new MacroEvent
            {
                TimestampMs = ts,
                Type = type,
                NormalizedX = item.NormalizedX.HasValue ? Math.Clamp(item.NormalizedX.Value, 0, 1) : null,
                NormalizedY = item.NormalizedY.HasValue ? Math.Clamp(item.NormalizedY.Value, 0, 1) : null,
                VirtualKeyCode = item.VirtualKeyCode,
                ScanCode = item.ScanCode,
                KeyboardLayoutId = item.KeyboardLayoutId,
                Payload = item.Payload
            };
            script.Events.Add(evt);
            if (item.RequiresClarification)
                script.Metadata[$"ai.clarification.{evt.Id:N}"] = item.Clarification ?? "Требует уточнения.";
            previous = ts;
        }
        return script;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private sealed class GeneratedScenario
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public List<GeneratedEvent> Events { get; set; } = new();
    }

    private sealed class GeneratedEvent
    {
        public string Type { get; set; } = string.Empty;
        public long TimestampMs { get; set; }
        public double? NormalizedX { get; set; }
        public double? NormalizedY { get; set; }
        public int? VirtualKeyCode { get; set; }
        public int? ScanCode { get; set; }
        public string? KeyboardLayoutId { get; set; }
        public object? Payload { get; set; }
        public bool RequiresClarification { get; set; }
        public string? Clarification { get; set; }
    }
}
