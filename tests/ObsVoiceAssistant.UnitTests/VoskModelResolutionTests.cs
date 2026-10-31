using ObsVoiceAssistant.Speech;

namespace ObsVoiceAssistant.UnitTests;

public sealed class VoskModelResolutionTests
{
    [Fact]
    public void FindModelDirectory_PrefersModelNextToApplication()
    {
        // Копия рядом с приложением (bin) важнее копии выше по дереву каталогов.
        var root = CreateTempDirectory();
        try
        {
            var binRoot = Path.Combine(root, "bin");
            var expected = Path.Combine(binRoot, "models", "vosk-model-small-ru-0.22");
            Directory.CreateDirectory(expected);
            Directory.CreateDirectory(Path.Combine(root, "models", "vosk-model-small-ru-0.22"));

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
    public void FindModelDirectory_IgnoresUnsupportedLargeModel()
    {
        // Большие модели не поддерживаются: их каталоги не должны выбираться, иначе загрузка
        // занимает минуты и съедает гигабайты ОЗУ без единой строки в логе.
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "models", "vosk-model-ru-0.42"));
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

    [Fact]
    public void FindModelDirectory_WithOnlyUnsupportedModel_ReturnsNull()
    {
        var root = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "models", "vosk-model-ru-0.22"));

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
    public void FindModelDirectory_FindsSupportedModel()
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
