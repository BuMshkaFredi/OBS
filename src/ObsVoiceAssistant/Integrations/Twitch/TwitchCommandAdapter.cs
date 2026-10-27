using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Преобразует команды названия и категории в вызовы Helix. Категория сначала
/// разрешается в точное название и id, чтобы подтверждение показывало реальный
/// результат поиска Twitch, а не исходную фразу распознавания.
/// </summary>
public sealed class TwitchCommandAdapter : ITwitchCommandPort
{
    private const int MaxTitleLength = TwitchTitleComposer.MaxTitleLength;

    private readonly ITwitchApiClient api;

    public TwitchCommandAdapter(ITwitchApiClient api)
    {
        this.api = api;
    }

    public async Task<ParsedCommand> PrepareAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Intent == CommandIntent.SetStreamTitle)
        {
            return await PrepareTitleAsync(command, cancellationToken).ConfigureAwait(false);
        }

        if (command.Intent != CommandIntent.SetStreamCategory)
        {
            return command;
        }

        var query = command.Argument?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new TwitchApiException("Не указана категория Twitch.");
        }

        // По согласованному решению берём первый результат поиска. Пользователь
        // увидит его точное название в подтверждении и сможет отменить команду.
        var category = await api.FindFirstCategoryAsync(query, cancellationToken)
            .ConfigureAwait(false);
        if (category is null || string.IsNullOrWhiteSpace(category.Id))
        {
            throw new TwitchApiException($"Категория «{query}» не найдена на Twitch.");
        }

        return command with
        {
            Argument = category.Name,
            ArgumentId = category.Id
        };
    }

    /// <summary>
    /// Добавляет к новому названию теги текущего. Пользователь увидит итог
    /// в подтверждении; слишком длинный результат отклоняется без вызова PATCH.
    /// </summary>
    private async Task<ParsedCommand> PrepareTitleAsync(
        ParsedCommand command,
        CancellationToken cancellationToken)
    {
        var title = command.Argument?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new TwitchApiException("Не указано новое название трансляции.");
        }

        var channel = await api.GetChannelAsync(cancellationToken).ConfigureAwait(false);
        var composition = TwitchTitleComposer.Compose(title, channel.Title);
        if (!composition.IsSuccess)
        {
            throw new TwitchApiException(
                composition.Error ?? "Не удалось собрать название трансляции.");
        }

        return command with { Argument = composition.Title };
    }

    public async Task<string?> ExecuteAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        switch (command.Intent)
        {
            case CommandIntent.SetStreamTitle:
            {
                var title = command.Argument?.Trim() ?? string.Empty;
                if (title.Length == 0)
                {
                    throw new TwitchApiException("Не указано новое название трансляции.");
                }

                if (title.Length > MaxTitleLength)
                {
                    throw new TwitchApiException(
                        $"Название трансляции длиннее {MaxTitleLength} символов.");
                }

                await api.UpdateTitleAsync(title, cancellationToken).ConfigureAwait(false);
                return $"Название трансляции изменено: «{title}»";
            }

            case CommandIntent.SetStreamCategory:
            {
                var gameId = command.ArgumentId;
                if (string.IsNullOrWhiteSpace(gameId))
                {
                    throw new TwitchApiException(
                        "Категория не проверена на Twitch. Повторите команду.");
                }

                await api.UpdateCategoryAsync(gameId, cancellationToken).ConfigureAwait(false);
                return $"Категория трансляции изменена: «{command.Argument}»";
            }

            default:
                throw new InvalidOperationException(
                    $"Twitch не поддерживает команду {command.Intent}.");
        }
    }
}
