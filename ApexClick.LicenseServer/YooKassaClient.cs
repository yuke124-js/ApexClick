using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ApexClick.LicenseServer;

public sealed record YooKassaPayment(string Id, string Status, string? OrderId);

public sealed class YooKassaClient
{
    private readonly HttpClient _http;
    private readonly string? _shopId;
    private readonly string? _secretKey;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_shopId) && !string.IsNullOrWhiteSpace(_secretKey);

    public YooKassaClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.yookassa.ru/v3/");
        _shopId = config["YOOKASSA_SHOP_ID"] ?? Environment.GetEnvironmentVariable("YOOKASSA_SHOP_ID");
        _secretKey = config["YOOKASSA_SECRET_KEY"] ?? Environment.GetEnvironmentVariable("YOOKASSA_SECRET_KEY");
        if (IsConfigured)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_shopId}:{_secretKey}")));
    }

    public async Task<(string PaymentId, string ConfirmationUrl)> CreatePaymentAsync(
        decimal amountRub, string description, string orderId, string returnUrl, CancellationToken ct)
    {
        var body = new
        {
            amount = new { value = amountRub.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), currency = "RUB" },
            confirmation = new { type = "redirect", return_url = returnUrl },
            capture = true,
            description,
            metadata = new { orderId }
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "payments")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        
        req.Headers.Add("Idempotence-Key", orderId);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var paymentId = doc.RootElement.GetProperty("id").GetString()!;
        var confirmationUrl = doc.RootElement.GetProperty("confirmation").GetProperty("confirmation_url").GetString()!;
        return (paymentId, confirmationUrl);
    }

    public async Task<YooKassaPayment> GetPaymentAsync(string paymentId, CancellationToken ct)
    {
        using var resp = await _http.GetAsync($"payments/{paymentId}", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string? orderId = null;
        if (root.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("orderId", out var oid))
            orderId = oid.GetString();
        return new YooKassaPayment(root.GetProperty("id").GetString()!, root.GetProperty("status").GetString()!, orderId);
    }
}
