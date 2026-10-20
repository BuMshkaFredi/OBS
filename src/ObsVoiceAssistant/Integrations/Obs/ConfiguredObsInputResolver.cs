namespace ObsVoiceAssistant.Integrations.Obs;

public sealed class ConfiguredObsInputResolver : IObsInputResolver
{
    private readonly IReadOnlyDictionary<string, string> inputs;

    public ConfiguredObsInputResolver(ObsAudioInputs options)
    {
        inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["microphone"] = options.Microphone ?? string.Empty,
            ["browser"] = options.Browser ?? string.Empty,
            ["stream_music"] = options.StreamMusic ?? string.Empty,
            ["personal_music"] = options.PersonalMusic ?? string.Empty
        };
    }

    public string? Resolve(string target) =>
        inputs.TryGetValue(target, out var inputName) && !string.IsNullOrWhiteSpace(inputName)
            ? inputName
            : null;
}
