using System.Text;

namespace CustomerMail;

internal sealed record ComposedEmail(string Subject, string Body);

/// <summary>
/// Текст письма собирается только из шаблона и проверенных вопросов. Произвольный текст
/// от агента сюда попасть не может.
/// </summary>
internal static class MailComposer
{
    public static ComposedEmail Compose(QuestionBatch batch, string topicLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Здравствуйте!");
        builder.AppendLine();
        builder.AppendLine("Я разрабатываю OBS Voice Assistant и хочу уточнить несколько деталей,");
        builder.AppendLine("которые зависят от ваших предпочтений. Вопросы ниже — только о том,");
        builder.AppendLine("как должно вести себя приложение, а не просьбы что-то сделать.");
        builder.AppendLine("Отвечать можно прямо под каждым вопросом.");
        builder.AppendLine();

        for (var i = 0; i < batch.Questions.Count; i++)
        {
            builder
                .Append(i + 1)
                .Append(") [")
                .Append(batch.Questions[i].Id)
                .Append("] ")
                .AppendLine(batch.Questions[i].Text.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("Если есть пожелания, которых нет в списке, — допишите их в конце письма.");
        builder.AppendLine();
        builder.AppendLine("Спасибо!");
        builder.AppendLine();
        builder.AppendLine("--");
        builder.AppendLine("OBS Voice Assistant, автоматическое письмо с вопросами по реализации.");

        var subject = $"OBS Voice Assistant: вопросы по реализации ({batch.Id}, {topicLabel})";
        return new ComposedEmail(subject, builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
