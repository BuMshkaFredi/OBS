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
    SetStreamCategory
}

public sealed record ParsedCommand(
    CommandIntent Intent,
    string? Argument = null,
    string? Target = null);
