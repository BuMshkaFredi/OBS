using System.Text;

namespace CustomerMail;

/// <summary>
/// Собирает письмо о сборке из проверенной схемы. Ссылки берутся только из полей
/// releaseUrl и downloadUrl, а не из свободного текста.
/// </summary>
internal static class ReleaseMailComposer
{
    public static ComposedEmail Compose(ReleaseMail mail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Здравствуйте!");
        builder.AppendLine();
        AppendParagraphs(builder, mail.Intro ?? []);

        builder.AppendLine("Ссылка на релиз:");
        builder.AppendLine((mail.ReleaseUrl ?? string.Empty).Trim());
        builder.AppendLine();
        builder.AppendLine("Прямая ссылка на архив:");
        builder.AppendLine((mail.DownloadUrl ?? string.Empty).Trim());
        builder.AppendLine();
        builder.AppendLine("Контрольная сумма SHA-256 архива:");
        builder.AppendLine((mail.ArchiveSha256 ?? string.Empty).Trim().ToLowerInvariant());
        builder.AppendLine();

        foreach (var section in mail.Sections ?? [])
        {
            if (section is null)
            {
                continue;
            }

            builder.AppendLine((section.Title ?? string.Empty).Trim());
            AppendParagraphs(builder, section.Paragraphs ?? []);

            var bullets = section.Bullets ?? [];
            foreach (var bullet in bullets)
            {
                builder.Append("— ").AppendLine((bullet ?? string.Empty).Trim());
            }

            if (bullets.Count > 0)
            {
                builder.AppendLine();
            }

            var steps = section.Steps ?? [];
            for (var i = 0; i < steps.Count; i++)
            {
                builder.Append(i + 1).Append(". ").AppendLine((steps[i] ?? string.Empty).Trim());
            }

            if (steps.Count > 0)
            {
                builder.AppendLine();
            }
        }

        AppendParagraphs(builder, mail.Closing ?? []);
        builder.AppendLine("--");
        builder.AppendLine("OBS Voice Assistant, письмо о новой сборке.");

        return new ComposedEmail(
            (mail.Subject ?? string.Empty).Trim(),
            builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void AppendParagraphs(StringBuilder builder, IReadOnlyList<string> paragraphs)
    {
        foreach (var paragraph in paragraphs)
        {
            builder.AppendLine((paragraph ?? string.Empty).Trim());
            builder.AppendLine();
        }
    }
}
