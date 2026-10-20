using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CustomerMail;

internal sealed class MailPolicy
{
    public string CustomerAddress { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public MailEndpoint Smtp { get; init; } = new();

    public MailEndpoint Imap { get; init; } = new();

    public MailLimits Limits { get; init; } = new();

    public static MailPolicy Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Не найден файл настроек: {path}");
        }

        try
        {
            return JsonSerializer.Deserialize<MailPolicy>(File.ReadAllText(path), JsonFiles.Options)
                ?? throw new InvalidOperationException("Файл policy.json пуст.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"policy.json не соответствует схеме: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Единственная точка, где решается, кому разрешено писать. Адрес берётся из policy.json
    /// и обязан совпадать с PIN-хешем, зашитым в код.
    /// </summary>
    public string? ValidateRecipient(string pinnedSha256)
    {
        if (string.IsNullOrWhiteSpace(CustomerAddress))
        {
            return "В policy.json не задан customerAddress.";
        }

        if (!System.Net.Mail.MailAddress.TryCreate(CustomerAddress.Trim(), out _))
        {
            return "customerAddress не является корректным email.";
        }

        if (string.IsNullOrWhiteSpace(pinnedSha256))
        {
            return "PIN адреса не настроен: AddressPin.CustomerAddressSha256 пуст, отправка и приём запрещены.";
        }

        var actual = ComputeAddressHash(CustomerAddress);
        if (!string.Equals(actual, pinnedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Адрес в policy.json не совпадает с закреплённым PIN. Отправка и приём запрещены.";
        }

        return null;
    }

    public string? ValidateSender(string smtpUser)
    {
        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            return "В policy.json не задан fromAddress.";
        }

        if (!System.Net.Mail.MailAddress.TryCreate(FromAddress.Trim(), out _))
        {
            return "fromAddress не является корректным email.";
        }

        if (!string.Equals(FromAddress.Trim(), smtpUser.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return $"fromAddress не совпадает с учётной записью из {MailCredentials.UserVariable}.";
        }

        return null;
    }

    public string? ValidateEndpoints()
    {
        if (string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Port is < 1 or > 65535)
        {
            return "В policy.json некорректно задан smtp (host/port).";
        }

        if (string.IsNullOrWhiteSpace(Imap.Host) || Imap.Port is < 1 or > 65535)
        {
            return "В policy.json некорректно задан imap (host/port).";
        }

        if (!MailEndpoint.IsSupportedSecurity(Smtp.Security) || !MailEndpoint.IsSupportedSecurity(Imap.Security))
        {
            return "В policy.json security должен быть ssl, starttls или auto.";
        }

        if (Limits.MaxEmailsPerDay < 1 || Limits.MaxQuestionsPerBatch < 1 || Limits.MaxQuestionCharacters < 1 || Limits.MaxBodyCharacters < 1)
        {
            return "В policy.json limits должны быть положительными.";
        }

        return null;
    }

    public static string ComputeAddressHash(string address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant()))).ToLowerInvariant();
}

internal sealed class MailEndpoint
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; }

    /// <summary>ssl (implicit TLS), starttls или auto. Если не задан — ssl.</summary>
    public string? Security { get; init; }

    public static bool IsSupportedSecurity(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Equals("ssl", StringComparison.OrdinalIgnoreCase)
        || value.Equals("starttls", StringComparison.OrdinalIgnoreCase)
        || value.Equals("auto", StringComparison.OrdinalIgnoreCase);

    public MailKit.Security.SecureSocketOptions ToSecureSocketOptions()
    {
        if (string.Equals(Security, "starttls", StringComparison.OrdinalIgnoreCase))
        {
            return MailKit.Security.SecureSocketOptions.StartTls;
        }

        if (string.Equals(Security, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return MailKit.Security.SecureSocketOptions.Auto;
        }

        return MailKit.Security.SecureSocketOptions.SslOnConnect;
    }
}

internal sealed class MailLimits
{
    public int MaxEmailsPerDay { get; init; } = 3;

    public int MaxQuestionsPerBatch { get; init; } = 12;

    public int MaxQuestionCharacters { get; init; } = 350;

    public int MaxBodyCharacters { get; init; } = 7000;
}
