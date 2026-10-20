using System.Text;
using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.UnitTests;

public sealed class SpeechActivityDetectorTests
{
    [Fact]
    public void ContainsSpeech_WithLoudPcm_ReturnsTrue()
    {
        var recording = new AudioRecording(CreatePcm(amplitude: 4000, sampleCount: 800), 16_000, 1);

        Assert.True(SpeechActivityDetector.ContainsSpeech(recording));
    }

    [Fact]
    public void ContainsSpeech_WithSilence_ReturnsFalse()
    {
        var recording = new AudioRecording(CreatePcm(amplitude: 0, sampleCount: 800), 16_000, 1);

        Assert.False(SpeechActivityDetector.ContainsSpeech(recording));
    }

    [Fact]
    public void ContainsSpeech_WithQuietNoiseBelowThreshold_ReturnsFalse()
    {
        var recording = new AudioRecording(CreatePcm(amplitude: 50, sampleCount: 800), 16_000, 1);

        Assert.False(SpeechActivityDetector.ContainsSpeech(recording));
    }

    [Fact]
    public void ComputeRms_WithWavHeader_SkipsHeaderAndServiceChunks()
    {
        var wav = CreateWav(amplitude: 4000, sampleCount: 400);

        var recording = new AudioRecording(wav, 16_000, 1);

        Assert.True(SpeechActivityDetector.ContainsSpeech(recording));
    }

    [Fact]
    public void ComputeRmsOfTail_MeasuresOnlyTheEndOfTheRecording()
    {
        var loud = CreatePcm(amplitude: 4000, sampleCount: 400);
        var silence = CreatePcm(amplitude: 0, sampleCount: 400);
        var combined = loud.Concat(silence).ToArray();

        var tailRms = SpeechActivityDetector.ComputeRmsOfTail(combined, tailSampleCount: 400);
        var wholeRms = SpeechActivityDetector.ComputeRms(combined);

        Assert.Equal(0.0, tailRms);
        Assert.True(wholeRms > 0);
    }

    [Fact]
    public void ComputeRmsOfTail_WithEmptyData_ReturnsZero()
    {
        Assert.Equal(0.0, SpeechActivityDetector.ComputeRmsOfTail(ReadOnlySpan<byte>.Empty, 400));
    }

    [Fact]
    public void ContainsSpeech_WithLongSilenceBeforeQuietSpeech_ReturnsTrue()
    {
        // Регрессия: средний RMS по всему сегменту размывался паузой до речи,
        // и тихая команда отбрасывалась как «запись без речи».
        var silence = CreatePcm(amplitude: 0, sampleCount: 16_000 * 4);
        var quietSpeech = CreatePcm(amplitude: 500, sampleCount: 16_000 / 2);
        var combined = silence.Concat(quietSpeech).ToArray();

        var recording = new AudioRecording(combined, 16_000, 1);

        Assert.True(SpeechActivityDetector.ComputeRms(combined) < SpeechActivityDetector.DefaultRmsThreshold);
        Assert.True(SpeechActivityDetector.ContainsSpeech(recording));
    }

    [Fact]
    public void ComputePeakRms_ReturnsLoudestWindow()
    {
        var quiet = CreatePcm(amplitude: 100, sampleCount: 800);
        var loud = CreatePcm(amplitude: 4000, sampleCount: 800);
        var combined = quiet.Concat(loud).ToArray();

        var peak = SpeechActivityDetector.ComputePeakRms(combined, windowSampleCount: 800);

        Assert.Equal(4000.0, peak, precision: 3);
    }

    [Fact]
    public void ComputePeakRms_WithEmptyData_ReturnsZero()
    {
        Assert.Equal(0.0, SpeechActivityDetector.ComputePeakRms(ReadOnlySpan<byte>.Empty, 800));
    }

    private static byte[] CreatePcm(short amplitude, int sampleCount)
    {
        var bytes = new byte[sampleCount * 2];
        for (var index = 0; index < sampleCount; index++)
        {
            bytes[index * 2] = (byte)(amplitude & 0xFF);
            bytes[index * 2 + 1] = (byte)((amplitude >> 8) & 0xFF);
        }

        return bytes;
    }

    private static byte[] CreateWav(short amplitude, int sampleCount)
    {
        var pcm = CreatePcm(amplitude, sampleCount);
        var header = new List<byte>();
        header.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        header.AddRange(BitConverter.GetBytes(36 + pcm.Length));
        header.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        header.AddRange(Encoding.ASCII.GetBytes("fmt "));
        header.AddRange(BitConverter.GetBytes(16));
        header.AddRange(BitConverter.GetBytes((short)1));
        header.AddRange(BitConverter.GetBytes((short)1));
        header.AddRange(BitConverter.GetBytes(16_000));
        header.AddRange(BitConverter.GetBytes(16_000 * 2));
        header.AddRange(BitConverter.GetBytes((short)2));
        header.AddRange(BitConverter.GetBytes((short)16));
        header.AddRange(Encoding.ASCII.GetBytes("data"));
        header.AddRange(BitConverter.GetBytes(pcm.Length));
        header.AddRange(pcm);
        return header.ToArray();
    }
}
