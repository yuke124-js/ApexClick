using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Net.Http.Headers;
using ApexClick.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<CommunityStore>();
builder.Services.AddHttpClient<CommunityAiScreeningService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});

var app = builder.Build();
var adminToken = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_ADMIN_TOKEN");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/search", async (string? category, string? q, CommunityStore store, CancellationToken ct) =>
    Results.Ok(await store.SearchAsync(category, q, ct)));

app.MapPost("/publish", async (
    HttpRequest request,
    CommunityStore store,
    CommunityAiScreeningService ai,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("multipart/form-data required");

    var form = await request.ReadFormAsync(ct);
    var category = form["category"].ToString();
    var author = form["author"].ToString();
    if (!CommunityRules.Allowed.Contains(category))
        return Results.BadRequest("invalid category");

    var file = form.Files.GetFile("file");
    if (file is null)
        return Results.BadRequest("file missing");
    if (file.Length > 50 * 1024 * 1024)
        return Results.BadRequest("file too large");

    var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mscr");
    try
    {
        await using (var input = file.OpenReadStream())
        await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            await input.CopyToAsync(output, ct);

        var script = await MscrScriptSerializer.LoadAsync(temp, ct);
        if (!CommunityRules.Screen(script, out var reason))
            return Results.BadRequest(reason);

        var screening = await ai.ClassifyAsync(script, ct);
        if (screening.Decision == CommunityAiDecision.Reject)
            return Results.BadRequest(screening.Reason ?? "AI screening rejected the scenario.");

        var status = screening.Decision == CommunityAiDecision.Allow
            ? "auto-approved"
            : "pending-moderation";

        return Results.Ok(await store.PublishAsync(
            script,
            category,
            string.IsNullOrWhiteSpace(author) ? "anonymous" : author,
            status,
            screening.Reason,
            ct));
    }
    finally
    {
        try { File.Delete(temp); } catch { }
    }
});

app.MapGet("/download/{id:guid}", async (Guid id, CommunityStore store, CancellationToken ct) =>
{
    var file = await store.GetFileAsync(id, ct);
    return file is null
        ? Results.NotFound()
        : Results.File(file, "application/octet-stream", "scenario.mscr");
});

app.MapPost("/moderation/{id:guid}/approve", async (
    Guid id, HttpRequest request, CommunityStore store, CancellationToken ct) =>
{
    if (!AuthorizeAdmin(request, adminToken)) return Results.Unauthorized();
    return Results.Ok(await store.SetModerationAsync(id, true, ct));
});

app.MapPost("/moderation/{id:guid}/reject", async (
    Guid id, string? reason, HttpRequest request, CommunityStore store, CancellationToken ct) =>
{
    if (!AuthorizeAdmin(request, adminToken)) return Results.Unauthorized();
    return Results.Ok(await store.SetModerationAsync(id, false, ct, reason));
});

app.Run();

static bool AuthorizeAdmin(HttpRequest request, string? configuredToken)
{
    if (string.IsNullOrWhiteSpace(configuredToken)) return false;
    return request.Headers.TryGetValue("X-ApexClick-Admin-Token", out var supplied) &&
           CryptographicOperations.FixedTimeEquals(
               System.Text.Encoding.UTF8.GetBytes(supplied.ToString()),
               System.Text.Encoding.UTF8.GetBytes(configuredToken));
}

static class CommunityRules
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(new[]
    {
        "productivity", "software-testing", "accessibility", "education-demo"
    }, StringComparer.OrdinalIgnoreCase);

    public static bool Screen(MacroScript script, out string? reason)
    {
        reason = null;
        if (script.Events.Count > 1_000_000)
        {
            reason = "too many events";
            return false;
        }

        foreach (var e in script.Events)
        {
            if (e.Payload is not string s) continue;
            if (s.Contains("powershell", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
                s.Contains("Process.Start", StringComparison.OrdinalIgnoreCase))
            {
                reason = "forbidden execution-like payload";
                return false;
            }
        }

        return true;
    }
}

public enum CommunityAiDecision { Allow, Reject, Manual }
public sealed record CommunityAiScreeningResult(CommunityAiDecision Decision, string? Reason);

sealed class CommunityAiScreeningService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _model;

    public CommunityAiScreeningService(HttpClient http)
    {
        _http = http;
        _endpoint = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_AI_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("APEXCLICK_AI_ENDPOINT")
            ?? string.Empty;
        _apiKey = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_AI_API_KEY")
            ?? Environment.GetEnvironmentVariable("APEXCLICK_AI_API_KEY")
            ?? string.Empty;
        _model = Environment.GetEnvironmentVariable("APEXCLICK_COMMUNITY_AI_MODEL")
            ?? Environment.GetEnvironmentVariable("APEXCLICK_AI_MODEL")
            ?? "gpt-4.1-mini";
    }

    public async Task<CommunityAiScreeningResult> ClassifyAsync(MacroScript script, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_endpoint) || string.IsNullOrWhiteSpace(_apiKey))
            return new(CommunityAiDecision.Manual, "AI screening not configured.");

        var summary = string.Join("\n", script.Events.Take(200).Select(e =>
            $"{e.Type} t={e.TimestampMs} payload={e.Payload}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = JsonContent.Create(new
        {
            model = _model,
            temperature = 0,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Classify an ApexClick community macro as Allow, Reject, or Manual. Reject spam, harvesting, credential theft, destructive behavior, or attempts to execute arbitrary OS commands. Return only JSON: {\"decision\":\"Allow|Reject|Manual\",\"reason\":\"string\"}."
                },
                new { role = "user", content = summary }
            }
        });

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return new(CommunityAiDecision.Manual, $"AI screening HTTP {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                return new(CommunityAiDecision.Manual, "AI returned empty screening result.");

            using var result = JsonDocument.Parse(content);
            var decision = result.RootElement.GetProperty("decision").GetString();
            var reason = result.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return decision?.ToLowerInvariant() switch
            {
                "allow" => new(CommunityAiDecision.Allow, reason),
                "reject" => new(CommunityAiDecision.Reject, reason),
                _ => new(CommunityAiDecision.Manual, reason ?? "AI requested manual review.")
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(CommunityAiDecision.Manual, "AI screening timed out.");
        }
        catch (Exception ex)
        {
            return new(CommunityAiDecision.Manual, $"AI screening unavailable: {ex.Message}");
        }
    }
}

sealed class CommunityStore
{
    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ApexClick", "CommunityServer");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CommunityStore() => Directory.CreateDirectory(_root);

    public async Task<List<object>> SearchAsync(string? category, string? q, CancellationToken ct)
    {
        var result = new List<object>();
        foreach (var f in Directory.EnumerateFiles(_root, "meta.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var m = JsonSerializer.Deserialize<Meta>(await File.ReadAllTextAsync(f, ct));
                if (m is null || !m.Approved) continue;
                if ((string.IsNullOrWhiteSpace(category) || string.Equals(category, m.Category, StringComparison.OrdinalIgnoreCase)) &&
                    (string.IsNullOrWhiteSpace(q) || m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    result.Add(new { m.Id, m.Name, m.Category, m.Downloads, m.Author });
            }
            catch { }
        }
        return result;
    }

    public async Task<object> PublishAsync(MacroScript script, string category, string author, string status, string? reason, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var id = script.Id;
            var dir = Path.Combine(_root, id.ToString("N"));
            Directory.CreateDirectory(dir);
            var mscr = Path.Combine(dir, "scenario.mscr");
            await MscrScriptSerializer.SaveAsync(script, mscr, ct);
            var approved = status == "auto-approved";
            var meta = new Meta
            {
                Id = id,
                Name = script.Name,
                Category = category,
                Author = author,
                Downloads = 0,
                Approved = approved,
                ScreeningStatus = approved ? "passed-deterministic-and-ai" : "pending-manual-review",
                Reason = reason
            };
            await File.WriteAllTextAsync(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta), ct);
            return new { Id = id, script.Name, Category = category, Status = status };
        }
        finally { _gate.Release(); }
    }

    public async Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(_root, id.ToString("N"));
            var metaPath = Path.Combine(dir, "meta.json");
            var file = Path.Combine(dir, "scenario.mscr");
            if (!File.Exists(metaPath) || !File.Exists(file)) return null;
            var meta = JsonSerializer.Deserialize<Meta>(await File.ReadAllTextAsync(metaPath, ct));
            if (meta is null || !meta.Approved) return null;
            meta.Downloads++;
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta), ct);
            return await File.ReadAllBytesAsync(file, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task<object> SetModerationAsync(Guid id, bool approved, CancellationToken ct, string? reason = null)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var dir = Path.Combine(_root, id.ToString("N"));
            var metaPath = Path.Combine(dir, "meta.json");
            if (!File.Exists(metaPath)) throw new FileNotFoundException();
            var meta = JsonSerializer.Deserialize<Meta>(await File.ReadAllTextAsync(metaPath, ct)) ?? throw new InvalidDataException();
            meta.Approved = approved;
            meta.Reason = reason;
            meta.ScreeningStatus = approved ? "approved-manual" : "rejected-manual";
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta), ct);
            return new { meta.Id, meta.Approved, meta.Reason };
        }
        finally { _gate.Release(); }
    }

    private sealed class Meta
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string Author { get; set; } = "";
        public int Downloads { get; set; }
        public bool Approved { get; set; }
        public string? Reason { get; set; }
        public string ScreeningStatus { get; set; } = "";
    }
}
