using System.Runtime.InteropServices;

namespace ObsVoiceAssistant.Audio;

/// <summary>
/// Разбор аудио, которое отдаёт запись микрофона. Запись приходит как WAV
/// (RIFF/WAVE, 16-битный моно PCM), а распознавателям нужны только отсчёты,
/// поэтому заголовок и служебные чанки нужно пропустить.
/// </summary>
public static class PcmAudio
{
    /// <summary>Возвращает 16-битные PCM-данные без RIFF/WAVE-заголовка и служебных чанков.</summary>
    public static ReadOnlySpan<byte> SlicePcm16(ReadOnlySpan<byte> waveData)
    {
        var offset = FindPcmOffset(waveData);
        var pcm = waveData[offset..];
        return pcm[..(pcm.Length / 2 * 2)];
    }

    /// <summary>Представляет 16-битные PCM-отсчёты как знаковые сэмплы.</summary>
    public static ReadOnlySpan<short> AsInt16(ReadOnlySpan<byte> waveData) =>
        MemoryMarshal.Cast<byte, short>(SlicePcm16(waveData));

    /// <summary>Определяет смещение PCM-данных внутри WAV. Для сырого PCM возвращает 0.</summary>
    public static int FindPcmOffset(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || !IsRiffWave(data))
        {
            return 0;
        }

        var position = 12;
        while (position + 8 <= data.Length)
        {
            var chunkId = data.Slice(position, 4);
            var chunkSize = BitConverter.ToInt32(data.Slice(position + 4, 4));
            var chunkDataStart = position + 8;

            if (chunkId[0] == (byte)'d' && chunkId[1] == (byte)'a' && chunkId[2] == (byte)'t' && chunkId[3] == (byte)'a')
            {
                return chunkDataStart <= data.Length ? chunkDataStart : 44;
            }

            if (chunkSize < 0)
            {
                break;
            }

            position = chunkDataStart + chunkSize + (chunkSize % 2);
        }

        return 44;
    }

    private static bool IsRiffWave(ReadOnlySpan<byte> data) =>
        data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
        data[8] == (byte)'W' && data[9] == (byte)'A' && data[10] == (byte)'V' && data[11] == (byte)'E';
}
