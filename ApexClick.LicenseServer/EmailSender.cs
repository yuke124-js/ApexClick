using System.Net;
using System.Net.Mail;

namespace ApexClick.LicenseServer;

public sealed class EmailSender
{
    private readonly IConfiguration _config;
    public EmailSender(IConfiguration config) => _config = config;

    private string? Get(string key) => _config[key] ?? Environment.GetEnvironmentVariable(key);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Get("APEXCLICK_SMTP_HOST"));

    public async Task SendLicenseAsync(string toEmail, string tier, string licenseKey, CancellationToken ct)
    {
        if (!IsConfigured) return;
        var host = Get("APEXCLICK_SMTP_HOST")!;
        var port = int.TryParse(Get("APEXCLICK_SMTP_PORT"), out var p) ? p : 587;
        var user = Get("APEXCLICK_SMTP_USER");
        var password = Get("APEXCLICK_SMTP_PASSWORD");
        var from = Get("APEXCLICK_SMTP_FROM") ?? user ?? "noreply@apexclick.local";

        using var client = new SmtpClient(host, port) { EnableSsl = true };
        if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, password);

        using var message = new MailMessage(from, toEmail)
        {
            Subject = "Ваш ключ ApexClick",
            Body = $"""
                Спасибо за покупку ApexClick ({tier}).

                Ваш лицензионный ключ:
                {licenseKey}

                Активируйте его в приложении: Настройки → Лицензия → Активировать.
                Ключ также был показан на странице после оплаты.
                """
        };
        await client.SendMailAsync(message, ct);
    }
}
