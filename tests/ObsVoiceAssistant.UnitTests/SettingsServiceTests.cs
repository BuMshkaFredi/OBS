using System.Text.Json;
using ObsVoiceAssistant.Configuration;
using ObsVoiceAssistant.Integrations.Obs;

namespace ObsVoiceAssistant.UnitTests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_PersistsObsInputNames()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.Save(new AppSettings
            {
                AudioInputs = new ObsAudioInputs(
                    Microphone: "Mic/Aux",
                    Browser: "Браузер",
                    StreamMusic: "Музыка стрима",
                    PersonalMusic: "Личная музыка")
            });

            var loaded = service.Load();

            Assert.Equal("Mic/Aux", loaded.AudioInputs.Microphone);
            Assert.Equal("Браузер", loaded.AudioInputs.Browser);
            Assert.Equal("Музыка стрима", loaded.AudioInputs.StreamMusic);
            Assert.Equal("Личная музыка", loaded.AudioInputs.PersonalMusic);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_AllowsSameObsInputForSeveralRoles()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.Save(new AppSettings
            {
                AudioInputs = new ObsAudioInputs(
                    StreamMusic: "Звук рабочего стола",
                    PersonalMusic: "Звук рабочего стола")
            });

            var loaded = service.Load();

            Assert.Equal("Звук рабочего стола", loaded.AudioInputs.StreamMusic);
            Assert.Equal("Звук рабочего стола", loaded.AudioInputs.PersonalMusic);
            Assert.Null(loaded.AudioInputs.Microphone);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WithSettingsFileFromPreviousVersion_StillReadsSettings()
    {
        var directory = CreateTempDirectory();
        try
        {
            // Раньше в файле было лишнее свойство ObsAudioInputs; он должен читаться без ошибок.
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """
                {
                  "ObsUrl": "ws://127.0.0.1:4455",
                  "ObsAudioInputs": null,
                  "AudioInputs": {
                    "Microphone": "Mic/Aux",
                    "Browser": null,
                    "StreamMusic": null,
                    "PersonalMusic": null
                  }
                }
                """);

            var loaded = new SettingsService(directory).Load();

            Assert.Equal("ws://127.0.0.1:4455", loaded.ObsUrl);
            Assert.Equal("Mic/Aux", loaded.AudioInputs.Microphone);
            Assert.Null(loaded.AudioInputs.Browser);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoadObsPassword_UsesProtectedStorageNotSettingsJson()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.SaveObsPassword("test-password");

            Assert.Equal("test-password", service.LoadObsPassword());
            var settingsPath = Path.Combine(directory, "settings.json");
            Assert.False(File.Exists(settingsPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ObsVoiceAssistantTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
