using ObsVoiceAssistant.Audio;

namespace ObsVoiceAssistant.Speech;

public interface ISpeechRecognizer
{
    Task<string?> RecognizeAsync(AudioRecording recording, CancellationToken cancellationToken = default);
}
