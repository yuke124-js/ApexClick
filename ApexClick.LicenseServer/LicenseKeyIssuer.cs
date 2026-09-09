using System.Security.Cryptography;
using System.Text;

namespace ApexClick.LicenseServer;

internal static class LicenseKeyIssuer
{
    public static string Issue(string tier, DateTime expiryUtc, string id12Hex, string secret)
    {
        var payload = $"APX-{tier.ToUpperInvariant()}-{expiryUtc:yyyyMMdd}-{id12Hex}";
        var sig = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)))[..16];
        return $"{payload}-{sig}";
    }

    public static string NewId12Hex()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
    }
}
