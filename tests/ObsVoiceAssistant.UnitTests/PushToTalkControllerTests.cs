using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.UnitTests;

public sealed class PushToTalkControllerTests
{
    [Fact]
    public void KeyDownAndKeyUp_StartAndStopExactlyOnce()
    {
        var recorder = new FakeRecorder();
        var controller = new PushToTalkController(recorder);
        var stopped = 0;
        controller.RecordingStopped += _ => stopped++;

        controller.KeyDown();
        controller.KeyDown();
        controller.KeyUp();
        controller.KeyUp();

        Assert.Equal(1, recorder.StartCalls);
        Assert.Equal(1, recorder.StopCalls);
        Assert.Equal(1, stopped);
        Assert.False(controller.IsRecording);
    }

    [Fact]
    public void KeyDown_DoesNotToggleCapsLockState()
    {
        var recorder = new FakeRecorder();
        var controller = new PushToTalkController(recorder);

        controller.KeyDown();

        Assert.True(controller.IsRecording);
        Assert.Equal(1, recorder.StartCalls);
    }

    private sealed class FakeRecorder : IAudioRecorder
    {
        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public void Start() => StartCalls++;

        public AudioRecording Stop()
        {
            StopCalls++;
            return new AudioRecording(ReadOnlyMemory<byte>.Empty, 16_000, 1);
        }

        public AudioRecording Snapshot() => new(ReadOnlyMemory<byte>.Empty, 16_000, 1);
    }
}
