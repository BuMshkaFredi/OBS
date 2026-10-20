using System.Diagnostics;
using System.Threading.Channels;

namespace ObsVoiceAssistant.Audio;

/// <summary>
/// Параметры записи по голосовой активности. Короткие значения используются в тестах.
/// </summary>
public sealed record ContinuousListeningOptions
{
    /// <summary>Как часто проверяется энергия сигнала.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(60);

    /// <summary>Длина окна в конце записи, по которому считается энергия.</summary>
    public TimeSpan SpeechTailDuration { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Пауза, после которой фраза считается законченной. Значение намеренно большое:
    /// разрыв фразы на естественной паузе теряет команду целиком, а это хуже,
    /// чем лишние доли секунды задержки.
    /// </summary>
    public TimeSpan SilenceToEndUtterance { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>Слишком короткие звуки (щелчки) игнорируются.</summary>
    public TimeSpan MinUtterance { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Верхняя граница длительности самой речи, чтобы запись не шла бесконечно.</summary>
    public TimeSpan MaxUtterance { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Сколько ждать начала речи, прежде чем начать сегмент заново.</summary>
    public TimeSpan InitialSilenceTimeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Порог RMS, выше которого сигнал считается речью.</summary>
    public double RmsThreshold { get; init; } = SpeechActivityDetector.DefaultRmsThreshold;

    public static ContinuousListeningOptions Default { get; } = new();
}

/// <summary>
/// Непрерывное прослушивание с разбиением по речи. Приложение ждёт начало фразы,
/// затем завершает запись по паузе, поэтому команда не разрезается границей
/// фиксированного сегмента и обрабатывается сразу после окончания речи.
/// </summary>
public sealed class ContinuousListeningService : IAsyncDisposable
{
    private readonly IAudioRecorder recorder;
    private readonly Func<AudioRecording, Task> recordingHandler;
    private readonly ContinuousListeningOptions options;
    private readonly Action<Exception>? errorHandler;
    private readonly CancellationTokenSource cancellation = new();

    // Обработка сегментов идёт отдельно от записи: пока распознаётся предыдущая
    // команда, микрофон продолжает слушать, и следующая фраза не теряется.
    private readonly Channel<AudioRecording> pending = Channel.CreateUnbounded<AudioRecording>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private Task? listeningTask;
    private Task? processingTask;

    public ContinuousListeningService(
        IAudioRecorder recorder,
        Func<AudioRecording, Task> recordingHandler,
        ContinuousListeningOptions? options = null,
        Action<Exception>? errorHandler = null)
    {
        this.recorder = recorder;
        this.recordingHandler = recordingHandler;
        this.options = options ?? ContinuousListeningOptions.Default;
        this.errorHandler = errorHandler;
    }

    public void Start()
    {
        listeningTask ??= ListenAsync();
        processingTask ??= ProcessPendingAsync();
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        TryStopRecorder();
        pending.Writer.TryComplete();

        foreach (var task in new[] { listeningTask, processingTask })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        cancellation.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var recording = await CaptureUtteranceAsync().ConfigureAwait(false);
                if (recording is not null && !recording.Data.IsEmpty)
                {
                    pending.Writer.TryWrite(recording);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                errorHandler?.Invoke(exception);
                TryStopRecorder();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        TryStopRecorder();
        pending.Writer.TryComplete();
    }

    /// <summary>Разбирает сегменты строго по очереди, чтобы команды выполнялись в порядке речи.</summary>
    private async Task ProcessPendingAsync()
    {
        try
        {
            await foreach (var recording in pending.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await recordingHandler(recording).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Ошибка одной команды не должна останавливать прослушивание.
                    errorHandler?.Invoke(exception);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Записывает одну фразу. Возвращает <c>null</c>, если речи не было или она слишком короткая.
    /// </summary>
    private async Task<AudioRecording?> CaptureUtteranceAsync()
    {
        recorder.Start();
        var stopwatch = Stopwatch.StartNew();
        var speechDetected = false;
        var speechDuration = TimeSpan.Zero;
        var silenceDuration = TimeSpan.Zero;
        var utteranceDuration = TimeSpan.Zero;

        while (!cancellation.IsCancellationRequested)
        {
            await Task.Delay(options.PollInterval, cancellation.Token).ConfigureAwait(false);

            var snapshot = recorder.Snapshot();
            var tailSamples = (int)(snapshot.SampleRate * options.SpeechTailDuration.TotalSeconds);
            var isSpeech = SpeechActivityDetector.ComputeRmsOfTail(snapshot.Data.Span, tailSamples) >= options.RmsThreshold;

            if (isSpeech)
            {
                speechDetected = true;
                speechDuration += options.PollInterval;
                silenceDuration = TimeSpan.Zero;
            }
            else if (speechDetected)
            {
                silenceDuration += options.PollInterval;
            }

            if (!speechDetected)
            {
                if (stopwatch.Elapsed >= options.InitialSilenceTimeout)
                {
                    break;
                }

                continue;
            }

            // Верхняя граница считается от начала речи, а не от начала сегмента:
            // иначе ожидание речи съедало бы допустимую длину фразы.
            utteranceDuration += options.PollInterval;
            if (silenceDuration >= options.SilenceToEndUtterance || utteranceDuration >= options.MaxUtterance)
            {
                break;
            }
        }

        var recording = recorder.Stop();
        if (!speechDetected || speechDuration < options.MinUtterance)
        {
            return null;
        }

        return recording;
    }

    private void TryStopRecorder()
    {
        try
        {
            recorder.Stop();
        }
        catch (Exception exception)
        {
            errorHandler?.Invoke(exception);
        }
    }
}
