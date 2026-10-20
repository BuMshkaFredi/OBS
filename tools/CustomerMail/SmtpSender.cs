using MailKit.Net.Smtp;
using MimeKit;

namespace CustomerMail;

/// <summary>
/// Отправка возможна ровно по одному адресу из policy.json. Ни CC, ни BCC, ни вложений,
/// ни произвольного получателя здесь нет: добавить их можно только правкой кода.
/// </summary>
internal static class SmtpSender
{
    public static async Task<string> SendAsync(MailPolicy policy, ComposedEmail email, CancellationToken cancellationToken)
    {
        var user = MailCredentials.RequireUser();
        var password = MailCredentials.RequirePassword();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("OBS Voice Assistant", policy.FromAddress));
        message.To.Add(new MailboxAddress(string.Empty, policy.CustomerAddress));
        message.Subject = email.Subject;
        message.Body = new TextPart("plain") { Text = email.Body };
        message.Headers.Add("Auto-Submitted", "auto-generated");

        using var client = new SmtpClient();
        await client.ConnectAsync(policy.Smtp.Host, policy.Smtp.Port, policy.Smtp.ToSecureSocketOptions(), cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(user, password, cancellationToken).ConfigureAwait(false);
        var messageId = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        return messageId;
    }
}
