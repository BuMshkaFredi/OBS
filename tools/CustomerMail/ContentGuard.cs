using System.Text.RegularExpressions;

namespace CustomerMail;

internal sealed class ContentRule
{
    public ContentRule(string pattern, string reason)
    {
        Pattern = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        Reason = reason;
    }

    public Regex Pattern { get; }

    public string Reason { get; }
}

/// <summary>
/// Защита от утечки: письмо не должно содержать ссылки, адреса, пути, IP, код, токены,
/// упоминания секретов и внутренних данных проекта. Правила намеренно грубые —
/// лучше отклонить безобидную фразу, чем отправить лишнее.
/// </summary>
internal static class ContentGuard
{
    private static readonly ContentRule[] Rules =
    [
        new("https?://|ftp://|www\\.", "ссылка"),
        new("\\b[A-Za-z0-9._%+\\-]+@[A-Za-z0-9.\\-]+\\.[A-Za-z]{2,}\\b", "адрес электронной почты"),
        new("[A-Za-z]:\\\\|\\\\\\\\", "путь к файлу"),
        new("\\b\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\b", "IP-адрес"),
        new("```|`", "фрагмент кода"),
        new("\\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\b", "GUID"),
        new("[A-Za-z0-9+/=]{32,}", "токен или хеш"),
        new("\\$\\{|\\{\\{|<%", "подстановка шаблона"),
        new("(парол|passwd|password|токен|\\btoken\\b|секрет|\\bsecret\\b|логин|\\blogin\\b|cookie|bearer|apikey|api[- ]ключ|private key|приватн)", "секрет или учётные данные"),
        new("(репозитор|\\bgit\\b|коммит|\\.sln\\b|\\.csproj\\b|\\.cs\\b)", "внутренние данные проекта"),
    ];

    public static IReadOnlyList<string> FindViolations(string text)
    {
        var violations = new List<string>();
        foreach (var rule in Rules)
        {
            if (rule.Pattern.IsMatch(text))
            {
                violations.Add($"запрещённый фрагмент: {rule.Reason}");
            }
        }

        return violations;
    }
}
