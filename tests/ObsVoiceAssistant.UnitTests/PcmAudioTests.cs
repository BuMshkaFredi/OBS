using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.UnitTests;

public sealed class PcmAudioTests
{
    [Fact]
    public void FindPcmOffset_WithRawPcm_ReturnsZero()
    {
        var raw = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        Assert.Equal(0, PcmAudio.FindPcmOffset(raw));
    }

    [Fact]
    public void FindPcmOffset_WithStandardWav_ReturnsFortyFour()
    {
        var wav = CreateWav(new byte[] { 0, 0, 1, 0 }, extraChunk: null);

        Assert.Equal(44, PcmAudio.FindPcmOffset(wav));
    }

    [Fact]
    public void FindPcmOffset_WithExtraChunk_SkipsItAndFindsData()
    {
        var wav = CreateWav(new byte[] { 0, 0, 1, 0 }, extraChunk: "LIST"u8.ToArray());

        var offset = PcmAudio.FindPcmOffset(wav);

        Assert.Equal(44 + 8 + 4, offset);
    }

    [Fact]
    public void SlicePcm16_WithExtraChunk_ReturnsOnlySamples()
    {
        var wav = CreateWav(new byte[] { 10, 0, 20, 0 }, extraChunk: "LIST"u8.ToArray());

        var pcm = PcmAudio.SlicePcm16(wav).ToArray();

        Assert.Equal(new byte[] { 10, 0, 20, 0 }, pcm);
    }

    [Fact]
    public void SlicePcm16_WithOddByteCount_DropsTrailingByte()
    {
        var raw = new byte[] { 1, 2, 3 };

        var pcm = PcmAudio.SlicePcm16(raw).ToArray();

        Assert.Equal(new byte[] { 1, 2 }, pcm);
    }

    [Fact]
    public void AsInt16_InterpretsSamplesAsSignedLittleEndian()
    {
        // 3000 (0x0BB8) и -3000 (0xF448)
        var raw = new byte[] { 0xB8, 0x0B, 0x48, 0xF4 };

        var samples = PcmAudio.AsInt16(raw).ToArray();

        Assert.Equal(new short[] { 3000, -3000 }, samples);
    }

    [Fact]
    public void SlicePcm16_AfterFlushingNaudioWriter_ReturnsAllWrittenSamples()
    {
        // Так работает непрерывное прослушивание: запись сбрасывается (Flush) и читается
        // как снимок, не дожидаясь остановки. Снимок должен содержать все записанные отсчёты.
        var samples = Enumerable.Range(0, 500).Select(index => (short)(index - 250)).ToArray();
        var pcm = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, pcm, 0, pcm.Length);

        using var stream = new MemoryStream();
        using var writer = new WaveFileWriter(stream, new WaveFormat(16_000, 16, 1));
        writer.Write(pcm, 0, pcm.Length);
        writer.Flush();

        var parsed = MemoryMarshal.Cast<byte, short>(PcmAudio.SlicePcm16(stream.ToArray())).ToArray();

        Assert.Equal(samples, parsed);
    }

    private static byte[] CreateWav(byte[] pcm, byte[]? extraChunk)
    {
        var result = new List<byte>();
        var extra = extraChunk is null ? Array.Empty<byte>() : BuildChunk("LIST", extraChunk);

        result.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        result.AddRange(BitConverter.GetBytes(36 + pcm.Length + extra.Length));
        result.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        result.AddRange(BuildChunk("fmt ", new byte[]
        {
            1, 0, 1, 0,
            0x80, 0x3E, 0, 0,
            0, 0x7D, 0, 0,
            2, 0, 16, 0
        }));
        result.AddRange(extra);
        result.AddRange(BuildChunk("data", pcm));
        return result.ToArray();
    }

    private static byte[] BuildChunk(string id, byte[] payload)
    {
        var chunk = new List<byte>();
        chunk.AddRange(Encoding.ASCII.GetBytes(id));
        chunk.AddRange(BitConverter.GetBytes(payload.Length));
        chunk.AddRange(payload);
        if (payload.Length % 2 != 0)
        {
            chunk.Add(0);
        }

        return chunk.ToArray();
    }
}
