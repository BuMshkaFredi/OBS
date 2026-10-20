namespace ObsVoiceAssistant.Audio;

/// <summary>
/// Простой детектор речи по энергии сигнала. По нему приложение решает,
/// есть ли в сегменте речь, и где заканчивается произнесённая фраза.
/// </summary>
public static class SpeechActivityDetector
{
    /// <summary>Порог RMS для 16-битного PCM. Ниже порога считается, что речи нет.</summary>
    /// <remarks>
    /// Порог намеренно низкий: он должен отсекать только тишину и шум, но не тихую речь.
    /// Ложное срабатывание безопасно — точный парсер не выполнит команду по случайным словам.
    /// </remarks>
    public const double DefaultRmsThreshold = 200.0;

    /// <summary>
    /// Есть ли в сегменте речь. Сравнивается самый громкий участок, а не средний уровень:
    /// длинная тишина до начала фразы размывала среднее, и тихая команда терялась.
    /// </summary>
    public static bool ContainsSpeech(AudioRecording recording, double rmsThreshold = DefaultRmsThreshold)
    {
        var windowSamples = Math.Max(1, recording.SampleRate / 20);
        return ComputePeakRms(recording.Data.Span, windowSamples) >= rmsThreshold;
    }

    /// <summary>Максимальный RMS по окнам: показывает самый громкий участок записи.</summary>
    public static double ComputePeakRms(ReadOnlySpan<byte> data, int windowSampleCount)
    {
        var samples = PcmAudio.AsInt16(data);
        if (samples.IsEmpty || windowSampleCount <= 0)
        {
            return 0.0;
        }

        var peak = 0.0;
        for (var offset = 0; offset < samples.Length; offset += windowSampleCount)
        {
            var length = Math.Min(windowSampleCount, samples.Length - offset);
            double sumOfSquares = 0.0;
            for (var index = 0; index < length; index++)
            {
                var sample = (double)samples[offset + index];
                sumOfSquares += sample * sample;
            }

            var rms = Math.Sqrt(sumOfSquares / length);
            if (rms > peak)
            {
                peak = rms;
            }
        }

        return peak;
    }

    /// <summary>RMS всего сегмента.</summary>
    public static double ComputeRms(ReadOnlySpan<byte> data) =>
        ComputeRmsOfTail(data, int.MaxValue);

    /// <summary>RMS последних <paramref name="tailSampleCount"/> отсчётов. Нужен для поиска конца фразы.</summary>
    public static double ComputeRmsOfTail(ReadOnlySpan<byte> data, int tailSampleCount)
    {
        var samples = PcmAudio.AsInt16(data);
        if (samples.IsEmpty || tailSampleCount <= 0)
        {
            return 0.0;
        }

        var count = Math.Min(tailSampleCount, samples.Length);
        var tail = samples[^count..];
        double sumOfSquares = 0.0;
        foreach (var sample in tail)
        {
            sumOfSquares += (double)sample * sample;
        }

        return Math.Sqrt(sumOfSquares / tail.Length);
    }
}
