using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.Integrations.Obs;

public sealed class ObsCommandPortAdapter : IObsCommandPort
{
    private readonly ObsCommandAdapter adapter;

    public ObsCommandPortAdapter(IObsClient client, IObsInputResolver inputResolver)
    {
        adapter = new ObsCommandAdapter(client, inputResolver);
    }

    public Task<string?> ExecuteAsync(ParsedCommand command, CancellationToken cancellationToken = default) =>
        adapter.ExecuteAsync(command, cancellationToken);

    public Task<InputToggleResult> ToggleInputAsync(
        ParsedCommand command,
        CancellationToken cancellationToken = default) =>
        adapter.ToggleInputAsync(command, cancellationToken);
}
