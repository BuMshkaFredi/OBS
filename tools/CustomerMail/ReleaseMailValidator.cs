using System.Text.RegularExpressions;

namespace CustomerMail;

internal sealed class ReleaseMailValidationResult
{
    public List<string> Errors { get; } = [];

    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Проверяет письмо о сборке: схему, статус, объём, отсутствие запрещённых
/// фрагментов в тексте и точное совпадение ссылок с allowlist из policy.json.
/// </summary>
internal static class ReleaseMailValidator
{
    private const int MinSubjectCharacters = 10;
    private const int MaxSubjectCharacters = 140;
    private const int MaxIntroParagraphs = 6;
    private const int MaxSections = 12;
    private const int MaxParagraphsPerSection = 12;
    private const int MaxBulletsPerSection = 25;
    private const int MaxStepsPerSection = 20;
    private const int MaxClosingParagraphs = 6;
    private const int MaxTextCharacters = 800;
    private const int MaxSectionTitleCharacters = 100;

    private static readonly Regex MailIdPattern = new("^REL-\\d{3}$", RegexOptions.CultureInvariant);

    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

    public static ReleaseMailValidationResult Validate(
        ReleaseMail? mail,
        string mailId,
        MailPolicy policy,
        bool requireApproved)
    {
        var result = new ReleaseMailValidationResult();
        if (mail is null)
        {
            result.Errors.Add($"Файл письма для {mailId} не найден или не читается.");
            return result;
        }

        if (!MailIdPattern.IsMatch(mail.Id))
        {
            result.Errors.Add($"id письма должен иметь вид REL-001, получено: '{mail.Id}'.");
        }

        if (!string.Equals(mail.Id, mailId, StringComparison.Ordinal))
        {
            result.Errors.Add($"id письма '{mail.Id}' не совпадает с именем файла '{mailId}'.");
        }

        if (!string.Equals(mail.Status, "draft", StringComparison.Ordinal)
            && !string.Equals(mail.Status, "approved", StringComparison.Ordinal))
        {
            result.Errors.Add($"status должен быть draft или approved, получено: '{mail.Status}'.");
        }

        if (requireApproved && !string.Equals(mail.Status, "approved", StringComparison.Ordinal))
        {
            result.Errors.Add("Отправка запрещена: status должен быть approved. Статус ставится только после явного разрешения пользователя.");
        }

        if (string.Equals(mail.Status, "approved", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(mail.ApprovedAt))
        {
            result.Errors.Add("Для status=approved требуется заполненный approvedAt.");
        }

        ValidateSubject(mail.Subject, result.Errors);

        var intro = mail.Intro ?? [];
        ValidateParagraphs("Вступление", intro, MaxIntroParagraphs, result.Errors);

        var sections = mail.Sections ?? [];
        ValidateSections(sections, result.Errors);

        var closing = mail.Closing ?? [];
        ValidateParagraphs("Заключение", closing, MaxClosingParagraphs, result.Errors);

        if (intro.Count == 0 && sections.Count == 0 && closing.Count == 0)
        {
            result.Errors.Add("Письмо о сборке пустое.");
        }

        ValidateUrl("releaseUrl", mail.ReleaseUrl, policy, result.Errors);
        ValidateUrl("downloadUrl", mail.DownloadUrl, policy, result.Errors);

        if (!Sha256Pattern.IsMatch(mail.ArchiveSha256?.Trim() ?? string.Empty))
        {
            result.Errors.Add("archiveSha256 должен содержать ровно 64 hex-символа контрольной суммы SHA-256.");
        }

        if (string.IsNullOrWhiteSpace(mail.ReleaseUrl) || string.IsNullOrWhiteSpace(mail.DownloadUrl))
        {
            return result;
        }

        var body = ReleaseMailComposer.Compose(mail).Body;
        if (!ContainsCyrillic(body))
        {
            result.Errors.Add("Письмо о сборке должно быть на русском языке.");
        }

        if (body.Length > policy.Limits.MaxBodyCharacters)
        {
            result.Errors.Add($"Письмо длиннее {policy.Limits.MaxBodyCharacters} символов.");
        }

        return result;
    }

    private static void ValidateSubject(string subject, List<string> errors)
    {
        var text = subject?.Trim() ?? string.Empty;
        if (text.Length < MinSubjectCharacters)
        {
            errors.Add($"Тема короче {MinSubjectCharacters} символов.");
            return;
        }

        if (text.Length > MaxSubjectCharacters)
        {
            errors.Add($"Тема длиннее {MaxSubjectCharacters} символов.");
        }

        if (ContainsLineBreak(text) || text.Any(char.IsControl))
        {
            errors.Add("Тема должна быть одной строкой без управляющих символов.");
        }

        errors.AddRange(ContentGuard.FindViolations(text).Select(v => $"Тема: {v}"));
    }

    private static void ValidateParagraphs(
        string label,
        IReadOnlyList<string> paragraphs,
        int maxCount,
        List<string> errors)
    {
        if (paragraphs.Count > maxCount)
        {
            errors.Add($"{label}: абзацев больше {maxCount}.");
        }

        for (var i = 0; i < paragraphs.Count; i++)
        {
            ValidateText($"{label}, абзац {i + 1}", paragraphs[i], errors);
        }
    }

    private static void ValidateSections(IReadOnlyList<ReleaseSection> sections, List<string> errors)
    {
        if (sections.Count == 0)
        {
            errors.Add("В письме нет разделов.");
            return;
        }

        if (sections.Count > MaxSections)
        {
            errors.Add($"Разделов больше {MaxSections}.");
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var number = i + 1;

            if (section is null)
            {
                errors.Add($"Раздел {number} пустой.");
                continue;
            }

            var title = section.Title?.Trim() ?? string.Empty;
            if (title.Length == 0)
            {
                errors.Add($"Раздел {number}: не задан заголовок.");
            }
            else
            {
                if (title.Length > MaxSectionTitleCharacters)
                {
                    errors.Add($"Раздел {number}: заголовок длиннее {MaxSectionTitleCharacters} символов.");
                }

                if (ContainsLineBreak(title) || title.Any(char.IsControl))
                {
                    errors.Add($"Раздел {number}: заголовок должен быть одной строкой.");
                }

                errors.AddRange(ContentGuard.FindViolations(title).Select(v => $"Раздел {number}: {v}"));
            }

            var paragraphs = section.Paragraphs ?? [];
            var bullets = section.Bullets ?? [];
            var steps = section.Steps ?? [];
            if (paragraphs.Count == 0 && bullets.Count == 0 && steps.Count == 0)
            {
                errors.Add($"Раздел {number}: нет ни абзацев, ни пунктов, ни шагов.");
            }

            ValidateParagraphs($"Раздел {number}", paragraphs, MaxParagraphsPerSection, errors);

            if (bullets.Count > MaxBulletsPerSection)
            {
                errors.Add($"Раздел {number}: пунктов больше {MaxBulletsPerSection}.");
            }

            for (var j = 0; j < bullets.Count; j++)
            {
                ValidateText($"Раздел {number}, пункт {j + 1}", bullets[j], errors);
            }

            if (steps.Count > MaxStepsPerSection)
            {
                errors.Add($"Раздел {number}: шагов больше {MaxStepsPerSection}.");
            }

            for (var j = 0; j < steps.Count; j++)
            {
                ValidateText($"Раздел {number}, шаг {j + 1}", steps[j], errors);
            }
        }
    }

    private static void ValidateText(string label, string text, List<string> errors)
    {
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            errors.Add($"{label}: пустой текст.");
            return;
        }

        if (value.Length > MaxTextCharacters)
        {
            errors.Add($"{label}: текст длиннее {MaxTextCharacters} символов.");
        }

        if (ContainsLineBreak(value) || value.Any(char.IsControl))
        {
            errors.Add($"{label}: текст должен быть одной строкой без управляющих символов.");
        }

        errors.AddRange(ContentGuard.FindViolations(value).Select(v => $"{label}: {v}"));
    }

    private static void ValidateUrl(string label, string value, MailPolicy policy, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label}: ссылка не задана.");
            return;
        }

        if (!IsAllowedUrl(value.Trim(), policy.Release.AllowedUrlPrefixes))
        {
            errors.Add($"{label}: ссылка не входит в release.allowedUrlPrefixes из policy.json.");
        }
    }

    private static bool IsAllowedUrl(string value, IReadOnlyList<string> prefixes)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            return false;
        }

        foreach (var prefix in prefixes)
        {
            if (!Uri.TryCreate(prefix, UriKind.Absolute, out var allowed) ||
                allowed.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(uri.Host, allowed.Host, StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith(allowed.AbsolutePath, StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool ContainsLineBreak(string value) =>
        value.Contains('\n') || value.Contains('\r');

    private static bool ContainsCyrillic(string value) =>
        value.Any(c => c is (>= 'А' and <= 'я') or 'Ё' or 'ё');
}
