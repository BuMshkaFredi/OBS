namespace ObsVoiceAssistant.Audio;

public interface IAudioRecorder
{
    void Start();

    AudioRecording Stop();

    /// <summary>Текущее содержимое записи без остановки. Нужно для определения конца речи.</summary>
    AudioRecording Snapshot();
}

public sealed record AudioRecording(ReadOnlyMemory<byte> Data, int SampleRate, int Channels);

public sealed class PushToTalkController
{
    private readonly IAudioRecorder recorder;
    private bool isPressed;

    public PushToTalkController(IAudioRecorder recorder)
    {
        this.recorder = recorder;
    }

    public bool IsRecording => isPressed;

    public event Action? RecordingStarted;
    public event Action<AudioRecording>? RecordingStopped;

    public void KeyDown()
    {
        if (isPressed)
        {
            return;
        }

        isPressed = true;
        recorder.Start();
        RecordingStarted?.Invoke();
    }

    public void KeyUp()
    {
        if (!isPressed)
        {
            return;
        }

        isPressed = false;
        var recording = recorder.Stop();
        RecordingStopped?.Invoke(recording);
    }
}
