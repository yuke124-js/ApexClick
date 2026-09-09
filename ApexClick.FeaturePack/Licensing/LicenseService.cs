using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApexClick.Services.Licensing;

namespace ApexClick.FeaturePack.Licensing;

public sealed record LicenseState(LicenseTier Tier, DateTime? ActivatedUtc, DateTime? LastValidatedUtc, DateTime? ExpiresUtc, string? KeyId, string? SignedKey = null);

public sealed class LicenseService
{
    private const int GraceDays = 7;
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ApexClick","license.json");

    public LicenseState Current => Load() ?? new(LicenseTier.Free,null,null,null,null,null);

    public Task<LicenseState> ActivateOfflineAsync(string key, CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        if (!TryParse(key,out var tier,out var expiry,out var id)) throw new InvalidOperationException("Неверный ключ лицензии.");
        var state = new LicenseState(tier,DateTime.UtcNow,DateTime.UtcNow,expiry,id,key);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
        return Task.FromResult(state);
    }

    public IFeatureFlags ResolveFlags()
    {
        var s = Current;
        var now = DateTime.UtcNow;
        if (s.Tier == LicenseTier.Free) return FeatureFlagsResolver.Resolve(LicenseTier.Free);
        if (string.IsNullOrWhiteSpace(s.SignedKey)) return FeatureFlagsResolver.Resolve(LicenseTier.Free);
        try
        {
            if (!TryParse(s.SignedKey, out var signedTier, out var signedExpiry, out var signedId) ||
                signedTier != s.Tier || !string.Equals(signedId, s.KeyId, StringComparison.OrdinalIgnoreCase) ||
                signedExpiry != s.ExpiresUtc)
                return FeatureFlagsResolver.Resolve(LicenseTier.Free);
        }
        catch (InvalidOperationException)
        {
            return FeatureFlagsResolver.Resolve(LicenseTier.Free);
        }
        if (s.ExpiresUtc is { } exp && exp <= now) return FeatureFlagsResolver.Resolve(LicenseTier.Free);
        if (s.LastValidatedUtc is { } last && now - last <= TimeSpan.FromDays(GraceDays))
            return FeatureFlagsResolver.Resolve(s.Tier);
        return FeatureFlagsResolver.Resolve(LicenseTier.Free);
    }

    public async Task<LicenseState> ValidateOnlineResultAsync(OnlineLicenseClient client, string endpoint, CancellationToken cancellationToken = default)
    {
        var current=Current;
        var result=await client.ValidateOnlineAsync(current,endpoint,cancellationToken);
        if(!result.Valid) throw new InvalidOperationException(result.Error ?? "Онлайн-валидация отклонена.");
        var updated=current with { Tier=result.Tier, LastValidatedUtc=result.ValidatedUtc, ExpiresUtc=result.ExpiresUtc };
        Save(updated);
        return updated;
    }

    private void Save(LicenseState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
    }

    private LicenseState? Load(){ try { return File.Exists(_path) ? JsonSerializer.Deserialize<LicenseState>(File.ReadAllText(_path)) : null; } catch { return null; } }

    private static bool TryParse(string key,out LicenseTier tier,out DateTime expiry,out string id)
    {
        tier=LicenseTier.Free; expiry=default; id="";
        
        var p=key.Split('-',StringSplitOptions.RemoveEmptyEntries);
        if(p.Length!=5 || !p[0].Equals("APX",StringComparison.OrdinalIgnoreCase)) return false;
        tier=p[1].ToUpperInvariant() switch { "FREE"=>LicenseTier.Free, "PRO"=>LicenseTier.Pro, _ => (LicenseTier)(-1) };
        if((int)tier<0 || !DateTime.TryParseExact(p[2],"yyyyMMdd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal,out expiry)) return false;
        if(p[3].Length!=12 || !p[3].All(Uri.IsHexDigit)) return false;
        id=p[3];

        var payload=$"{p[0]}-{p[1]}-{p[2]}-{p[3]}";
        var expectedSig=Convert.ToHexString(HMACSHA256.HashData(HmacKey,Encoding.UTF8.GetBytes(payload)))[..16];
        return p.Length==5 && string.Equals(p[4],expectedSig,StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] HmacKey => Encoding.UTF8.GetBytes(
        Environment.GetEnvironmentVariable("APEXCLICK_LICENSE_SECRET")
        ?? throw new InvalidOperationException("APEXCLICK_LICENSE_SECRET is not configured."));
}
