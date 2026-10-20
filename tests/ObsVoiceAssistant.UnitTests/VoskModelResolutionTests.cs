using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class VoskModelResolutionTests
{
    [Fact]
    public void FindModelDirectory_PrefersLargerModelEvenWhenSmallerModelIsCloser()
    {
        // Модель в bin не должна перекрывать более точную модель в корне репозитория.
        var root = CreateTempDirectory();
        try
        {
            var binRoot = Path.Combine(root, "bin");
            Directory.CreateDirectory(Path.Combine(binRoot, "models", "vosk-model-small-ru-0.22"));
            var expected = Path.Combine(root, "models", "vosk-model-ru-0.42");
            Directory.CreateDirectory(expected);

            var found = VoskSpeechRecognizer.FindModelDirectory(
                new[] { binRoot, root },
                VoskSpeechRecognizer.ModelDirectoryNames);

            Assert.Equal(expected, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindModelDirectory_WithoutAnyModel_ReturnsNull()
    {
        var root = CreateTempDirectory();
        try
        {
            var found = VoskSpeechRecognizer.FindModelDirectory(
                new[] { root },
                VoskSpeechRecognizer.ModelDirectoryNames);

            Assert.Null(found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindModelDirectory_FallsBackToSmallModel()
    {
        var root = CreateTempDirectory();
        try
        {
            var expected = Path.Combine(root, "models", "vosk-model-small-ru-0.22");
            Directory.CreateDirectory(expected);

            var found = VoskSpeechRecognizer.FindModelDirectory(
                new[] { root },
                VoskSpeechRecognizer.ModelDirectoryNames);

            Assert.Equal(expected, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ObsVoiceAssistantTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
