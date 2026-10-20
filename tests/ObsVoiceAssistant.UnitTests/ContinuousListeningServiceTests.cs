using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ContinuousListeningServiceTests
{
    private const int SampleRate = 16_000;

    [Fact]
    public async Task SilenceAfterSpeech_EndsUtteranceAndProcessesIt()
    {
        // Речь, затем пауза: сервис должен завершить фразу сам, без ожидания фиксированного окна.
        var recorder = new ScriptedRecorder(loud: 2, silent: 3);
        var processed = new List<AudioRecording>();
        var firstProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var service = new ContinuousListeningService(
            recorder,
            recording =>
            {
                processed.Add(recording);
                firstProcessed.TrySetResult();
                return Task.CompletedTask;
            },
            CreateOptions());

        service.Start();
        await firstProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();

        Assert.Single(processed);
        Assert.False(processed[0].Data.IsEmpty);
        Assert.True(recorder.StartCalls >= 1);
    }

    [Fact]
    public async Task SilenceOnly_IsNeverProcessed()
    {
        var recorder = new ScriptedRecorder(loud: 0, silent: 40);
        var processed = new List<AudioRecording>();

        var service = new ContinuousListeningService(
            recorder,
            recording =>
            {
                processed.Add(recording);
                return Task.CompletedTask;
            },
            CreateOptions());

        service.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await service.DisposeAsync();

        Assert.Empty(processed);
    }

    [Fact]
    public async Task TooShortSpeech_IsNotProcessed()
    {
        // Один короткий звук не должен превращаться в команду.
        var recorder = new ScriptedRecorder(loud: 1, silent: 10);
        var processed = new List<AudioRecording>();

        var service = new ContinuousListeningService(
            recorder,
            recording =>
            {
                processed.Add(recording);
                return Task.CompletedTask;
            },
            CreateOptions());

        service.Start();
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        await service.DisposeAsync();

        Assert.Empty(processed);
    }

    [Fact]
    public async Task ContinuousSpeech_IsCutAtMaxUtterance()
    {
        // Без паузы запись всё равно должна завершиться по верхней границе.
        var recorder = new ScriptedRecorder(loud: 30, silent: 0);
        var processed = new List<AudioRecording>();
        var firstProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var service = new ContinuousListeningService(
            recorder,
            recording =>
            {
                processed.Add(recording);
                firstProcessed.TrySetResult();
                return Task.CompletedTask;
            },
            CreateOptions(maxUtterance: TimeSpan.FromMilliseconds(100)));

        service.Start();
        await firstProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();

        Assert.True(processed.Count >= 1);
        Assert.False(processed[0].Data.IsEmpty);
    }

    [Fact]
    public async Task RecordingContinuesWhilePreviousCommandIsStillProcessing()
    {
        // Регрессия: раньше запись останавливалась на время распознавания,
        // и команда, сказанная сразу после предыдущей, терялась.
        var recorder = new RepeatingRecorder();
        var gate = new object();
        var order = new List<int>();
        var maxConcurrent = 0;
        var concurrent = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var service = new ContinuousListeningService(
            recorder,
            async _ =>
            {
                var current = Interlocked.Increment(ref concurrent);
                try
                {
                    int position;
                    lock (gate)
                    {
                        maxConcurrent = Math.Max(maxConcurrent, current);
                        position = order.Count;
                        order.Add(position);
                    }

                    if (position == 0)
                    {
                        firstStarted.TrySetResult();
                        await releaseFirst.Task;
                    }
                    else
                    {
                        secondProcessed.TrySetResult();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref concurrent);
                }
            },
            CreateOptions());

        service.Start();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var snapshotsBefore = recorder.SnapshotCalls;
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        var snapshotsDuringHandling = recorder.SnapshotCalls - snapshotsBefore;

        releaseFirst.TrySetResult();
        await secondProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();

        Assert.True(
            snapshotsDuringHandling > 0,
            "Запись должна продолжаться, пока обрабатывается предыдущая команда.");
        Assert.Equal(1, maxConcurrent);
        Assert.True(order.Count >= 2);
    }

    private static ContinuousListeningOptions CreateOptions(TimeSpan? maxUtterance = null) => new()
    {
        PollInterval = TimeSpan.FromMilliseconds(10),
        SpeechTailDuration = TimeSpan.FromMilliseconds(20),
        SilenceToEndUtterance = TimeSpan.FromMilliseconds(30),
        MinUtterance = TimeSpan.FromMilliseconds(20),
        MaxUtterance = maxUtterance ?? TimeSpan.FromSeconds(2),
        InitialSilenceTimeout = TimeSpan.FromMilliseconds(150),
    };

    /// <summary>
    /// Бесконечно повторяет «речь, пауза», чтобы проверить несколько команд подряд.
    /// В отличие от <see cref="ScriptedRecorder"/>, последовательность не заканчивается.
    /// </summary>
    private sealed class RepeatingRecorder : IAudioRecorder
    {
        private static readonly bool[] Pattern = { true, true, false, false, false };

        private readonly List<short> samples = new();
        private int position;

        public int SnapshotCalls { get; private set; }

        public void Start()
        {
        }

        public AudioRecording Snapshot()
        {
            SnapshotCalls++;
            AppendFrame(Pattern[position % Pattern.Length]);
            position++;
            return Current();
        }

        public AudioRecording Stop() => Current();

        private void AppendFrame(bool loud)
        {
            var count = SampleRate / 50; // 20 мс при 16 кГц.
            for (var index = 0; index < count; index++)
            {
                samples.Add(loud ? (short)8_000 : (short)0);
            }
        }

        private AudioRecording Current()
        {
            var bytes = new byte[samples.Count * 2];
            for (var index = 0; index < samples.Count; index++)
            {
                var value = samples[index];
                bytes[index * 2] = (byte)(value & 0xFF);
                bytes[index * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new AudioRecording(bytes, SampleRate, 1);
        }
    }

    /// <summary>
    /// Отдаёт заранее заданную последовательность кадров по 20 мс: сначала громкие, затем тихие.
    /// Скрипт расходуется один раз, поэтому каждый тест видит ровно одну фразу.
    /// </summary>
    private sealed class ScriptedRecorder : IAudioRecorder
    {
        private readonly bool[] script;
        private readonly List<short> samples = new();
        private int framesReturned;

        public ScriptedRecorder(int loud, int silent)
        {
            script = new bool[loud + silent];
            for (var index = 0; index < loud; index++)
            {
                script[index] = true;
            }
        }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public void Start() => StartCalls++;

        public AudioRecording Snapshot()
        {
            if (framesReturned < script.Length)
            {
                AppendFrame(script[framesReturned]);
                framesReturned++;
            }

            return Current();
        }

        public AudioRecording Stop()
        {
            StopCalls++;
            return Current();
        }

        private void AppendFrame(bool loud)
        {
            var count = SampleRate / 50; // 20 мс при 16 кГц.
            for (var index = 0; index < count; index++)
            {
                samples.Add(loud ? (short)8_000 : (short)0);
            }
        }

        private AudioRecording Current()
        {
            var bytes = new byte[samples.Count * 2];
            for (var index = 0; index < samples.Count; index++)
            {
                var value = samples[index];
                bytes[index * 2] = (byte)(value & 0xFF);
                bytes[index * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return new AudioRecording(bytes, SampleRate, 1);
        }
    }
}
