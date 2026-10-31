using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Integrations.Twitch;

/// <summary>
/// Преобразует команду смены категории в вызовы Helix. Категория сначала
/// разрешается в точное название и id, чтобы подтверждение показывало реальный
/// результат поиска Twitch, а не исходную фразу распознавания. После изменения
/// категория перечитывается: молчаливое «ничего не произошло» недопустимо.
/// </summary>
public sealed class TwitchCommandAdapter : ITwitchCommandPort
{
    private readonly ITwitchApiClient api;

    public TwitchCommandAdapter(ITwitchApiClient api)
    {
        this.api = api;
    }

    public async Task<ParsedCommand> PrepareAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Intent != CommandIntent.SetStreamCategory)
        {
            return command;
        }

        var query = command.Argument?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new TwitchApiException("Не указана категория Twitch.");
        }

        // Распознавание русское, названия категорий — латиницей. Ищем по
        // вариантам запроса: как сказали, транслитерация, цифры вместо слов.
        TwitchCategory? category = null;
        foreach (var variant in TwitchCategoryQuery.BuildVariants(query))
        {
            category = await api.FindFirstCategoryAsync(variant, cancellationToken)
                .ConfigureAwait(false);
            if (category is not null && !string.IsNullOrWhiteSpace(category.Id))
            {
                break;
            }
        }

        if (category is null || string.IsNullOrWhiteSpace(category.Id))
        {
            throw new TwitchApiException($"Категория «{query}» не найдена на Twitch.");
        }

        // По согласованному решению берём первый результат поиска. Пользователь
        // увидит его точное название в подтверждении и сможет отменить команду.
        return command with
        {
            Argument = category.Name,
            ArgumentId = category.Id
        };
    }

    public async Task<string?> ExecuteAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Intent != CommandIntent.SetStreamCategory)
        {
            throw new InvalidOperationException(
                $"Twitch не поддерживает команду {command.Intent}.");
        }

        var gameId = command.ArgumentId;
        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw new TwitchApiException(
                "Категория не проверена на Twitch. Повторите команду.");
        }

        await api.UpdateCategoryAsync(gameId, cancellationToken).ConfigureAwait(false);

        // Helix может ответить успехом и не применить изменение. Перечитываем
        // канал: если присланный game_id остался, пользователь узнает об этом,
        // а не будет искать причину в OBS.
        var channel = await api.GetChannelAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(channel.GameId, gameId, StringComparison.Ordinal))
        {
            var actual = string.IsNullOrWhiteSpace(channel.GameName)
                ? "другая категория"
                : $"«{channel.GameName}»";
            throw new TwitchApiException(
                $"Twitch не применил смену категории: сейчас стоит {actual}.");
        }

        return $"Категория трансляции изменена: «{command.Argument}»";
    }
}
