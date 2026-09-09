using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using ApexClick.Services.Licensing;

namespace ApexClick.FeaturePack.Licensing;

public sealed class OnlineLicenseClient
{
    private readonly HttpClient _http;

    public OnlineLicenseClient(HttpClient http) => _http = http;

    public async Task<OnlineLicenseValidationResult> ValidateOnlineAsync(
        LicenseState current,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(current.KeyId))
            throw new InvalidOperationException("Нет KeyId активной лицензии.");
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint обязателен.", nameof(endpoint));

        var request = new OnlineLicenseValidationRequest(
            current.SignedKey ?? throw new InvalidOperationException("В локальном состоянии нет подписанного ключа. Активируйте ключ заново."),
            CreateMachineFingerprint());

        using var response = await _http.PostAsJsonAsync(
            endpoint.TrimEnd('/') + "/validate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OnlineLicenseValidationResponse>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("Backend вернул пустой ответ.");

        if (!result.Valid)
            return new OnlineLicenseValidationResult(
                false, result.Tier, result.ExpiresUtc, result.Error, DateTime.UtcNow);

        return new OnlineLicenseValidationResult(
            true, result.Tier, result.ExpiresUtc, null, DateTime.UtcNow);
    }

    private static string CreateMachineFingerprint()
    {
        var raw = $"{Environment.MachineName}|{Environment.OSVersion}|{Environment.UserName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}

public sealed record OnlineLicenseValidationRequest(
    string Key,
    string MachineFingerprint);

public sealed record OnlineLicenseValidationResponse(
    bool Valid,
    LicenseTier Tier,
    DateTime? ExpiresUtc,
    string? Error);

public sealed record OnlineLicenseValidationResult(
    bool Valid,
    LicenseTier Tier,
    DateTime? ExpiresUtc,
    string? Error,
    DateTime ValidatedUtc);
