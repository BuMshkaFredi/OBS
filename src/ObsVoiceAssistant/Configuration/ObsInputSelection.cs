namespace ObsVoiceAssistant.Configuration;

/// <summary>
/// Значения полей «источник OBS» в окне настроек. Пункт <see cref="NoneLabel"/>
/// и пустая строка означают одно и то же: роль не привязана к источнику.
/// </summary>
public static class ObsInputSelection
{
    /// <summary>Пункт списка, который отвязывает роль от источника.</summary>
    public const string NoneLabel = "— не использовать —";

    /// <summary>
    /// Превращает текст поля в имя источника. «Не использовать» и пустая строка
    /// дают <c>null</c>: команда для такой роли не выполняется.
    /// </summary>
    public static string? ToInputName(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed == NoneLabel ? null : trimmed;
    }

    /// <summary>
    /// Варианты списка для одного поля. Каждое поле получает собственный список,
    /// поэтому один и тот же источник можно выбрать в нескольких ролях.
    /// </summary>
    public static IReadOnlyList<string> BuildChoices(IEnumerable<string>? names)
    {
        var choices = new List<string> { NoneLabel };
        if (names is null)
        {
            return choices;
        }

        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name) && name != NoneLabel)
            {
                choices.Add(name);
            }
        }

        return choices;
    }
}
