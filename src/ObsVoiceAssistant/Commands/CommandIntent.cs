namespace ObsVoiceAssistant.Commands;

public enum CommandIntent
{
    StartRecording,
    StopRecording,
    StartStream,
    StopStream,
    StartReplayBuffer,
    SaveReplay,
    MuteInput,
    UnmuteInput,
    ToggleInput,
    MuteAllAudio,
    SetStreamTitle,
    SetStreamCategory,
    AdjustInputVolume
}

public sealed record ParsedCommand(
    CommandIntent Intent,
    string? Argument = null,
    string? Target = null,
    int? Delta = null);
