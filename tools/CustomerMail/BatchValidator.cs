using System.Text.RegularExpressions;

namespace CustomerMail;

internal sealed class BatchValidationResult
{
    public List<string> Errors { get; } = [];

    public string? TopicLabel { get; set; }

    public bool IsValid => Errors.Count == 0;
}

internal static class BatchValidator
{
    private const int MinQuestionCharacters = 10;

    private static readonly Regex BatchIdPattern = new("^CQ-\\d{3}$", RegexOptions.CultureInvariant);

    private static readonly Regex QuestionIdPattern = new("^CQ-\\d{3}-\\d{1,2}$", RegexOptions.CultureInvariant);

    private const string AllowedPunctuation = ".,;:?!()«»\"'“”„-–—/№%";

    public static BatchValidationResult Validate(QuestionBatch? batch, string batchId, MailPolicy policy, bool requireApproved)
    {
        var errors = new List<string>();
        string? topicLabel = null;

        if (batch is null)
        {
            errors.Add($"Файл вопросов для {batchId} не найден или не читается.");
            return Build(errors, topicLabel);
        }

        if (!BatchIdPattern.IsMatch(batch.Id))
        {
            errors.Add($"id партии должен иметь вид CQ-001, получено: '{batch.Id}'.");
        }

        if (!string.Equals(batch.Id, batchId, StringComparison.Ordinal))
        {
            errors.Add($"id партии '{batch.Id}' не совпадает с именем файла '{batchId}'.");
        }

        if (!BatchTopics.TryGetLabel(batch.Topic, out var label))
        {
            errors.Add($"Неизвестная тема '{batch.Topic}'. Разрешены только темы из BatchTopics.");
        }
        else
        {
            topicLabel = label;
        }

        if (!string.Equals(batch.Status, "draft", StringComparison.Ordinal)
            && !string.Equals(batch.Status, "approved", StringComparison.Ordinal))
        {
            errors.Add($"status должен быть draft или approved, получено: '{batch.Status}'.");
        }

        if (requireApproved && !string.Equals(batch.Status, "approved", StringComparison.Ordinal))
        {
            errors.Add("Отправка запрещена: status должен быть approved. Статус ставится только после явного разрешения пользователя.");
        }

        if (string.Equals(batch.Status, "approved", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(batch.ApprovedAt))
        {
            errors.Add("Для status=approved требуется заполненный approvedAt.");
        }

        if (batch.Questions is null || batch.Questions.Count == 0)
        {
            errors.Add("В партии нет вопросов.");
            return Build(errors, topicLabel);
        }

        if (batch.Questions.Count > policy.Limits.MaxQuestionsPerBatch)
        {
            errors.Add($"Вопросов больше лимита: {batch.Questions.Count} из {policy.Limits.MaxQuestionsPerBatch}.");
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var question in batch.Questions)
        {
            if (!QuestionIdPattern.IsMatch(question.Id))
            {
                errors.Add($"id вопроса '{question.Id}' не соответствует шаблону CQ-001-1.");
                continue;
            }

            if (!question.Id.StartsWith(batch.Id + "-", StringComparison.Ordinal))
            {
                errors.Add($"id вопроса '{question.Id}' не начинается с '{batch.Id}-'.");
            }

            if (!seenIds.Add(question.Id))
            {
                errors.Add($"Повторяющийся id вопроса: {question.Id}.");
            }

            ValidateQuestionText(question, policy, errors);
        }

        return Build(errors, topicLabel);
    }

    private static void ValidateQuestionText(Question question, MailPolicy policy, List<string> errors)
    {
        var text = question.Text?.Trim() ?? string.Empty;
        if (text.Length < MinQuestionCharacters)
        {
            errors.Add($"{question.Id}: текст вопроса короче {MinQuestionCharacters} символов.");
            return;
        }

        if (text.Length > policy.Limits.MaxQuestionCharacters)
        {
            errors.Add($"{question.Id}: текст вопроса длиннее {policy.Limits.MaxQuestionCharacters} символов.");
        }

        if (!text.EndsWith('?'))
        {
            errors.Add($"{question.Id}: вопрос должен заканчиваться знаком '?'.");
        }

        if (text.Contains('\n') || text.Contains('\r'))
        {
            errors.Add($"{question.Id}: вопрос должен быть одной строкой.");
        }

        if (!ContainsCyrillic(text))
        {
            errors.Add($"{question.Id}: вопрос должен быть на русском языке.");
        }

        var forbidden = text.Where(c => !IsAllowedCharacter(c)).Distinct().ToArray();
        if (forbidden.Length > 0)
        {
            errors.Add($"{question.Id}: недопустимые символы: {string.Join(" ", forbidden.Select(c => $"'{c}'"))}.");
        }

        errors.AddRange(ContentGuard.FindViolations(text).Select(v => $"{question.Id}: {v}"));
    }

    private static bool IsAllowedCharacter(char c) =>
        char.IsLetterOrDigit(c) || c == ' ' || AllowedPunctuation.Contains(c);

    private static bool ContainsCyrillic(string text) =>
        text.Any(c => c is (>= 'А' and <= 'я') or 'Ё' or 'ё');

    private static BatchValidationResult Build(List<string> errors, string? topicLabel)
    {
        var result = new BatchValidationResult { TopicLabel = topicLabel };
        result.Errors.AddRange(errors);
        return result;
    }
}
