using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

namespace CustomerMail;

/// <summary>
/// Приём писем только от закреплённого адреса заказчика. Письма других отправителей
/// не сохраняются и не помечаются прочитанными. Почта не удаляется и не пересылается.
/// </summary>
internal static class ImapReceiver
{
    public static async Task<IReadOnlyList<string>> FetchAsync(string root, MailPolicy policy, int limit, CancellationToken cancellationToken)
    {
        var user = MailCredentials.RequireUser();
        var password = MailCredentials.RequirePassword();
        var alreadyStored = AuditLog.ReceivedMessageIds(root);
        var saved = new List<string>();

        using var client = new ImapClient();
        await client.ConnectAsync(policy.Imap.Host, policy.Imap.Port, policy.Imap.ToSecureSocketOptions(), cancellationToken).ConfigureAwait(false);
        await client.AuthenticateAsync(user, password, cancellationToken).ConfigureAwait(false);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken).ConfigureAwait(false);

        var found = await inbox.SearchAsync(SearchQuery.FromContains(policy.CustomerAddress), cancellationToken).ConfigureAwait(false);
        var toProcess = found.Skip(Math.Max(0, found.Count - limit)).ToList();

        foreach (var uid in toProcess)
        {
            var message = await inbox.GetMessageAsync(uid, cancellationToken).ConfigureAwait(false);

            var fromCustomer = message.From.Mailboxes.Any(m =>
                string.Equals(m.Address, policy.CustomerAddress, StringComparison.OrdinalIgnoreCase));
            if (!fromCustomer)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(message.MessageId) && alreadyStored.Contains(message.MessageId))
            {
                continue;
            }

            var receivedAtUtc = message.Date.ToUniversalTime();
            var path = AnswerWriter.Save(root, message, receivedAtUtc);
            await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken).ConfigureAwait(false);

            AuditLog.Append(root, new AuditEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Action = "receive",
                Subject = message.Subject ?? string.Empty,
                Result = "saved",
                MessageId = message.MessageId,
                Recipient = policy.CustomerAddress,
            });

            saved.Add(path);
        }

        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
        return saved;
    }
}
