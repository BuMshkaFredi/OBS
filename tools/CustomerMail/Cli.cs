using System.Security.Cryptography;
using System.Text;

namespace CustomerMail;

internal static class Cli
{
    private const int ExitOk = 0;
    private const int ExitError = 1;
    private const int ExitRejected = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return ExitOk;
        }

        var command = args[0];
        var options = CommandOptions.Parse(args.Skip(1));

        try
        {
            return command switch
            {
                "check" => Check(),
                "send" => await SendAsync(options).ConfigureAwait(false),
                "fetch" => await FetchAsync(options).ConfigureAwait(false),
                "pin-hash" => PinHash(options),
                _ => Unknown(command),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            return ExitError;
        }
    }

    private static int Check()
    {
        var root = RepoLayout.FindRoot();
        Console.WriteLine($"Корень репозитория: {root}");

        var policyPath = RepoLayout.PolicyPath(root);
        if (!File.Exists(policyPath))
        {
            Console.Error.WriteLine($"Отсутствует {policyPath}.");
            return ExitError;
        }

        var policy = MailPolicy.Load(policyPath);
        Console.WriteLine($"Адрес заказчика: {Mask(policy.CustomerAddress)}");
        Console.WriteLine($"PIN адреса: {(string.IsNullOrWhiteSpace(AddressPin.CustomerAddressSha256) ? "не настроен (отправка запрещена)" : "настроен")}");
        Console.WriteLine($"Адрес отправителя: {Mask(policy.FromAddress)}");
        Console.WriteLine($"SMTP: {policy.Smtp.Host}:{policy.Smtp.Port}");
        Console.WriteLine($"IMAP: {policy.Imap.Host}:{policy.Imap.Port}");
        Console.WriteLine($"Лимит писем в сутки: {policy.Limits.MaxEmailsPerDay}");
        Console.WriteLine($"{MailCredentials.UserVariable}: {(MailCredentials.HasUser ? "задана" : "НЕ задана")}");
        Console.WriteLine($"{MailCredentials.PasswordVariable}: {(MailCredentials.HasPassword ? "задана" : "НЕ задана")}");

        var errors = new[]
        {
            policy.ValidateEndpoints(),
            string.IsNullOrWhiteSpace(policy.FromAddress) ? "В policy.json не задан fromAddress (ящик Mail.ru, с которого идут письма)." : null,
            policy.ValidateRecipient(AddressPin.CustomerAddressSha256),
        }.Where(e => e is not null).ToArray();

        if (errors.Length > 0)
        {
            foreach (var error in errors)
            {
                Console.Error.WriteLine($"Проблема: {error}");
            }

            return ExitRejected;
        }

        if (!MailCredentials.HasUser || !MailCredentials.HasPassword)
        {
            var missing = (MailCredentials.HasUser, MailCredentials.HasPassword) switch
            {
                (false, false) => "не заданы учётная запись и пароль приложения почты",
                (false, true) => "не задана учётная запись почты",
                _ => "не задан пароль приложения почты",
            };
            Console.WriteLine($"Адрес и PIN настроены, но {missing}: отправка и приём пока невозможны.");
            return ExitOk;
        }

        Console.WriteLine("Настройки готовы к отправке.");
        return ExitOk;
    }

    private static async Task<int> SendAsync(CommandOptions options)
    {
        var root = RepoLayout.FindRoot();
        var batchId = options.Get("batch");
        if (string.IsNullOrWhiteSpace(batchId))
        {
            Console.Error.WriteLine("Не указан обязательный параметр --batch <CQ-001>.");
            return ExitRejected;
        }

        var preview = options.Has("preview");
        var policy = MailPolicy.Load(RepoLayout.PolicyPath(root));

        var endpointError = policy.ValidateEndpoints();
        if (endpointError is not null)
        {
            Console.Error.WriteLine($"Проблема: {endpointError}");
            return ExitRejected;
        }

        var recipientError = policy.ValidateRecipient(AddressPin.CustomerAddressSha256);
        if (recipientError is not null)
        {
            Console.Error.WriteLine($"Проблема: {recipientError}");
            return ExitRejected;
        }

        var batchPath = Path.Combine(RepoLayout.BatchesDirectory(root), batchId + ".json");
        var batch = QuestionBatch.TryLoad(batchPath, out var loadError);
        if (loadError is not null)
        {
            Console.Error.WriteLine(loadError);
            return ExitRejected;
        }

        var validation = BatchValidator.Validate(batch, batchId, policy, requireApproved: !preview);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                Console.Error.WriteLine($"Отклонено: {error}");
            }

            return ExitRejected;
        }

        var email = MailComposer.Compose(batch!, validation.TopicLabel!);
        var violations = ContentGuard.FindViolations(email.Subject + "\n" + email.Body);
        if (violations.Count > 0)
        {
            foreach (var violation in violations.Distinct(StringComparer.Ordinal))
            {
                Console.Error.WriteLine($"Отклонено: {violation}");
            }

            return ExitRejected;
        }

        if (email.Body.Length > policy.Limits.MaxBodyCharacters)
        {
            Console.Error.WriteLine($"Отклонено: письмо длиннее {policy.Limits.MaxBodyCharacters} символов.");
            return ExitRejected;
        }

        Console.WriteLine($"Кому: {Mask(policy.CustomerAddress)}");
        Console.WriteLine($"Тема: {email.Subject}");
        Console.WriteLine();
        Console.WriteLine(email.Body);

        if (preview)
        {
            Console.WriteLine("[preview] Письмо не отправлено.");
            return ExitOk;
        }

        if (AuditLog.WasSent(root, batchId))
        {
            Console.Error.WriteLine($"Отклонено: партия {batchId} уже была отправлена.");
            return ExitRejected;
        }

        var sentToday = AuditLog.CountSentOnUtcDate(root, DateOnly.FromDateTime(DateTime.UtcNow));
        if (sentToday >= policy.Limits.MaxEmailsPerDay)
        {
            Console.Error.WriteLine($"Отклонено: исчерпан лимит {policy.Limits.MaxEmailsPerDay} писем в сутки.");
            return ExitRejected;
        }

        var user = MailCredentials.RequireUser();
        var senderError = policy.ValidateSender(user);
        if (senderError is not null)
        {
            Console.Error.WriteLine($"Проблема: {senderError}");
            return ExitRejected;
        }

        var messageId = string.Empty;
        try
        {
            messageId = await SmtpSender.SendAsync(policy, email, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AuditLog.Append(root, new AuditEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Action = "send",
                BatchId = batchId,
                Recipient = policy.CustomerAddress,
                Subject = email.Subject,
                BodySha256 = Hash(email.Body),
                QuestionCount = batch!.Questions.Count,
                Result = "failed",
                MessageId = null,
            });
            Console.Error.WriteLine($"Ошибка отправки: {ex.Message}");
            return ExitError;
        }

        AuditLog.Append(root, new AuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Action = "send",
            BatchId = batchId,
            Recipient = policy.CustomerAddress,
            Subject = email.Subject,
            BodySha256 = Hash(email.Body),
            QuestionCount = batch!.Questions.Count,
            Result = "sent",
            MessageId = messageId,
        });

        Console.WriteLine($"Отправлено: {batchId} -> {Mask(policy.CustomerAddress)}");
        return ExitOk;
    }

    private static async Task<int> FetchAsync(CommandOptions options)
    {
        var root = RepoLayout.FindRoot();
        var policy = MailPolicy.Load(RepoLayout.PolicyPath(root));

        var recipientError = policy.ValidateRecipient(AddressPin.CustomerAddressSha256);
        if (recipientError is not null)
        {
            Console.Error.WriteLine($"Проблема: {recipientError}");
            return ExitRejected;
        }

        var limit = 20;
        if (options.Get("limit") is { } limitText && int.TryParse(limitText, out var parsed) && parsed > 0)
        {
            limit = parsed;
        }

        var saved = await ImapReceiver.FetchAsync(root, policy, limit, CancellationToken.None).ConfigureAwait(false);
        if (saved.Count == 0)
        {
            Console.WriteLine("Новых писем от заказчика нет.");
            return ExitOk;
        }

        Console.WriteLine($"Сохранено новых писем: {saved.Count}");
        foreach (var path in saved)
        {
            Console.WriteLine($"  {path}");
        }

        Console.WriteLine("Разобрать ответ и записать его в docs/customer-questions/QUESTIONS.md.");
        return ExitOk;
    }

    private static int PinHash(CommandOptions options)
    {
        var address = options.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(address))
        {
            Console.Error.WriteLine("Укажите адрес: pin-hash <email>.");
            return ExitRejected;
        }

        if (!System.Net.Mail.MailAddress.TryCreate(address.Trim(), out _))
        {
            Console.Error.WriteLine("Адрес не является корректным email.");
            return ExitRejected;
        }

        Console.WriteLine(MailPolicy.ComputeAddressHash(address));
        return ExitOk;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Неизвестная команда: {command}.");
        PrintUsage();
        return ExitRejected;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            CustomerMail — почтовый канал для вопросов заказчику.

            Команды:
              check                          проверить policy.json, PIN адреса и переменные окружения
              send --batch CQ-001            отправить партию вопросов (нужен status=approved)
              send --batch CQ-001 --preview  показать письмо, ничего не отправляя
              fetch [--limit 20]             забрать новые ответы только от адреса заказчика
              pin-hash <email>               посчитать SHA-256 адреса для AddressPin

            Коды возврата: 0 — успех, 1 — ошибка, 2 — действие отклонено проверками.
            """);
    }

    private static string Mask(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return "(не задан)";
        }

        var at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : address[..1] + "***" + address[at..];
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string?> values;
    private readonly List<string> positionals;

    private CommandOptions(Dictionary<string, string?> values, List<string> positionals)
    {
        this.values = values;
        this.positionals = positionals;
    }

    public IReadOnlyList<string> Positionals => positionals;

    public static CommandOptions Parse(IEnumerable<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        var positionals = new List<string>();
        string? pendingKey = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pendingKey = arg[2..];
                values[pendingKey] = null;
            }
            else if (pendingKey is not null)
            {
                values[pendingKey] = arg;
                pendingKey = null;
            }
            else
            {
                positionals.Add(arg);
            }
        }

        return new CommandOptions(values, positionals);
    }

    public bool Has(string name) => values.ContainsKey(name);

    public string? Get(string name) => values.TryGetValue(name, out var value) ? value : null;
}
