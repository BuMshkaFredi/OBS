using System.IO;
using NAudio.Wave;

namespace ObsVoiceAssistant.Audio;

public sealed class NAudioMicrophoneRecorder : IAudioRecorder, IDisposable
{
    private readonly object sync = new();
    private readonly WaveFormat format;
    private readonly Action<int>? dataReceived;
    private readonly Action<Exception>? recordingError;
    private WaveInEvent? waveIn;
    private MemoryStream? buffer;
    private WaveFileWriter? writer;
    private bool isRecording;

    public NAudioMicrophoneRecorder(
        int sampleRate = 16_000,
        int channels = 1,
        Action<int>? dataReceived = null,
        Action<Exception>? recordingError = null)
    {
        format = new WaveFormat(sampleRate, 16, channels);
        this.dataReceived = dataReceived;
        this.recordingError = recordingError;
    }

    public void Start()
    {
        lock (sync)
        {
            if (isRecording)
            {
                return;
            }

            buffer = new MemoryStream();
            writer = new WaveFileWriter(buffer, format);
            waveIn = new WaveInEvent
            {
                WaveFormat = format,
                BufferMilliseconds = 50
            };
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.RecordingStopped += OnRecordingStopped;
            waveIn.StartRecording();
            isRecording = true;
        }
    }

    public AudioRecording Stop()
    {
        lock (sync)
        {
            if (!isRecording || waveIn is null || buffer is null || writer is null)
            {
                return new AudioRecording(ReadOnlyMemory<byte>.Empty, format.SampleRate, format.Channels);
            }

            waveIn.StopRecording();
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.RecordingStopped -= OnRecordingStopped;
            waveIn.Dispose();
            waveIn = null;

            writer.Flush();
            writer.Dispose();
            writer = null;
            var data = buffer.ToArray();
            buffer.Dispose();
            buffer = null;
            isRecording = false;

            return new AudioRecording(data, format.SampleRate, format.Channels);
        }
    }

    public AudioRecording Snapshot()
    {
        lock (sync)
        {
            if (!isRecording || buffer is null || writer is null)
            {
                return new AudioRecording(ReadOnlyMemory<byte>.Empty, format.SampleRate, format.Channels);
            }

            // Обновляем размер данных в RIFF-заголовке, чтобы снимок был корректным WAV.
            writer.Flush();
            return new AudioRecording(buffer.ToArray(), format.SampleRate, format.Channels);
        }
    }

    public void Dispose()
    {
        if (isRecording)
        {
            Stop();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (sync)
        {
            writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        dataReceived?.Invoke(e.BytesRecorded);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            recordingError?.Invoke(e.Exception);
        }
    }
}
