using System.Security.Cryptography;
using System.Text;
using ApexClick.LicenseServer;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<LicenseRevocationStore>();
builder.Services.AddSingleton<LicenseMachineBindingStore>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddHttpClient<YooKassaClient>();
var app=builder.Build();
var secret=Environment.GetEnvironmentVariable("APEXCLICK_LICENSE_SECRET") ?? throw new InvalidOperationException("APEXCLICK_LICENSE_SECRET is required.");
var adminToken=Environment.GetEnvironmentVariable("APEXCLICK_LICENSE_ADMIN_TOKEN");
var publicBaseUrl=Environment.GetEnvironmentVariable("APEXCLICK_PUBLIC_BASE_URL");

app.MapGet("/health",()=>Results.Ok(new{status="ok"}));

app.MapPost("/validate",(LicenseRequest request,LicenseRevocationStore store,LicenseMachineBindingStore bindings)=>{
    if(string.IsNullOrWhiteSpace(request.Key)) return Results.Ok(new LicenseResponse(false,"Free",null,"key missing"));
    if(!TryVerify(request.Key,secret,out var tier,out var expiry,out var id)) return Results.Ok(new LicenseResponse(false,"Free",null,"invalid signature"));
    if(store.IsRevoked(id)) return Results.Ok(new LicenseResponse(false,"Free",expiry,"revoked"));
    if(!bindings.ValidateOrBind(id, request.MachineFingerprint)) return Results.Ok(new LicenseResponse(false,"Free",expiry,"license is bound to another machine"));
    if(expiry<=DateTime.UtcNow) return Results.Ok(new LicenseResponse(false,"Free",expiry,"expired"));
    return Results.Ok(new LicenseResponse(true,tier,expiry,null));
});
app.MapPost("/revoke",(RevokeRequest request,HttpRequest http,LicenseRevocationStore store)=>{if(!AuthorizeAdmin(http,adminToken)) return Results.Unauthorized(); store.Revoke(request.KeyId);return Results.Ok(new{revoked=true});});

app.MapPost("/checkout/create", async (CheckoutRequest request, YooKassaClient yooKassa, OrderStore orders) =>
{
    if (!yooKassa.IsConfigured) return Results.Problem("YooKassa ещё не настроена (нет shopId/secretKey).", statusCode: 503);
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        return Results.BadRequest(new { error = "invalid email" });
    if (!string.Equals(request.Tier, "Pro", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "unknown tier" });

    var priceEnv = Environment.GetEnvironmentVariable("APEXCLICK_PRICE_PRO_RUB");
    if (!decimal.TryParse(priceEnv, System.Globalization.CultureInfo.InvariantCulture, out var price))
        return Results.Problem("Цена не сконфигурирована (APEXCLICK_PRICE_PRO_RUB).", statusCode: 503);
    if (string.IsNullOrWhiteSpace(publicBaseUrl))
        return Results.Problem("APEXCLICK_PUBLIC_BASE_URL не сконфигурирован.", statusCode: 503);

    var orderId = Guid.NewGuid().ToString("N");
    orders.CreatePending(orderId, request.Email, "Pro");
    var (paymentId, confirmationUrl) = await yooKassa.CreatePaymentAsync(
        price, "ApexClick Pro — пожизненная лицензия", orderId,
        $"{publicBaseUrl.TrimEnd('/')}/checkout/return?order={orderId}", default);
    orders.AttachPayment(orderId, paymentId);
    return Results.Ok(new { orderId, confirmationUrl });
});

app.MapPost("/webhooks/yookassa", async (HttpRequest http, YooKassaClient yooKassa, OrderStore orders, EmailSender email) =>
{
    using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body);
    if (!doc.RootElement.TryGetProperty("object", out var obj) || !obj.TryGetProperty("id", out var idProp))
        return Results.BadRequest();
    var paymentId = idProp.GetString();
    if (string.IsNullOrWhiteSpace(paymentId)) return Results.BadRequest();

    var payment = await yooKassa.GetPaymentAsync(paymentId, default);
    if (payment.Status != "succeeded") return Results.Ok();

    var order = payment.OrderId is { } oid ? orders.Find(oid) : orders.FindByPaymentId(paymentId);
    if (order is null) return Results.Ok();

    var expiry = DateTime.UtcNow.AddYears(100);
    var key = LicenseKeyIssuer.Issue(order.Tier, expiry, LicenseKeyIssuer.NewId12Hex(), secret);

    if (orders.TryFulfill(order.Id, key))
        await email.SendLicenseAsync(order.Email, order.Tier, key, default);

    return Results.Ok();
});

app.MapGet("/checkout/status", (string order, OrderStore orders) =>
{
    var record = orders.Find(order);
    if (record is null) return Results.NotFound();
    return Results.Ok(new { status = record.Status, key = record.Status == "Paid" ? record.LicenseKey : null });
});

app.Run();

static bool AuthorizeAdmin(HttpRequest request,string? configuredToken)
{
    if(string.IsNullOrWhiteSpace(configuredToken)) return false;
    return request.Headers.TryGetValue("X-ApexClick-Admin-Token", out var supplied) &&
           CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied.ToString()),Encoding.UTF8.GetBytes(configuredToken));
}

static bool TryVerify(string key,string secret,out string tier,out DateTime expiry,out string id)
{
    tier="Free"; expiry=default; id="";
    var parts=key.Split('-',StringSplitOptions.RemoveEmptyEntries);
    if(parts.Length!=5 || !parts[0].Equals("APX",StringComparison.OrdinalIgnoreCase)) return false;
    tier=parts[1].ToUpperInvariant() switch { "PRO"=>"Pro", "FREE"=>"Free", _=>string.Empty };
    if(string.IsNullOrEmpty(tier)) return false;
    if(!DateTime.TryParseExact(parts[2],"yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out expiry)) return false;
    id=parts[3];
    if(id.Length!=12 || !id.All(Uri.IsHexDigit) || parts[4].Length!=16 || !parts[4].All(Uri.IsHexDigit)) return false;
    var payload=$"{parts[0]}-{parts[1]}-{parts[2]}-{parts[3]}";
    var sig=Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),Encoding.UTF8.GetBytes(payload)))[..16];
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sig.ToUpperInvariant()),Encoding.UTF8.GetBytes(parts[4].ToUpperInvariant()));
}
record LicenseRequest(string Key,string MachineFingerprint); record LicenseResponse(bool Valid,string Tier,DateTime? ExpiresUtc,string? Error); record RevokeRequest(string KeyId);
record CheckoutRequest(string Email, string Tier);

sealed class LicenseRevocationStore
{
    private readonly string _path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ApexClick","LicenseServer","revoked.json");
    private readonly object _gate=new();
    private HashSet<string> _revoked;
    public LicenseRevocationStore(){Directory.CreateDirectory(Path.GetDirectoryName(_path)!);try{_revoked=File.Exists(_path)?System.Text.Json.JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_path)) ?? new(StringComparer.OrdinalIgnoreCase):new(StringComparer.OrdinalIgnoreCase);}catch{_revoked=new(StringComparer.OrdinalIgnoreCase);}}
    public void Revoke(string id){lock(_gate){_revoked.Add(id);Persist();}}
    public bool IsRevoked(string id){lock(_gate)return _revoked.Contains(id);}
    private void Persist()=>File.WriteAllText(_path,System.Text.Json.JsonSerializer.Serialize(_revoked));
}

sealed class LicenseMachineBindingStore
{
    private readonly string _path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ApexClick","LicenseServer","machines.json");
    private readonly object _gate=new();
    private Dictionary<string,string> _bindings;
    public LicenseMachineBindingStore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        try
        {
            var json = File.Exists(_path) ? File.ReadAllText(_path) : "{}";
            _bindings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(json)
                ?? new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { _bindings = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); }
    }
    public bool ValidateOrBind(string keyId,string fingerprint){if(string.IsNullOrWhiteSpace(fingerprint)||fingerprint.Length>512)return false;lock(_gate){if(_bindings.TryGetValue(keyId,out var existing))return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(existing),Encoding.UTF8.GetBytes(fingerprint));_bindings[keyId]=fingerprint;File.WriteAllText(_path,System.Text.Json.JsonSerializer.Serialize(_bindings));return true;}}
}
