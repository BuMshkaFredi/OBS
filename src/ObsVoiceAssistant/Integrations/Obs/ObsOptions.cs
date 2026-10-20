namespace ObsVoiceAssistant.Integrations.Obs;

public sealed record ObsConnectionOptions(
    string Url = "ws://127.0.0.1:4455",
    string? Password = null);

public sealed record ObsAudioInputs(
    string? Microphone = null,
    string? Browser = null,
    string? StreamMusic = null,
    string? PersonalMusic = null);
