using ObsVoiceAssistant.Commands;

namespace ObsVoiceAssistant.Notifications;

public interface IConfirmationService
{
    Task<bool> ConfirmAsync(ParsedCommand command, CancellationToken cancellationToken = default);
}
