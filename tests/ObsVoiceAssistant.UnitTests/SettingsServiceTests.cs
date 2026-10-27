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
    public void SaveAndLoad_PersistsTwitchClientId()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.Save(new AppSettings { TwitchClientId = "twitch-client-id" });

            var loaded = service.Load();

            Assert.Equal("twitch-client-id", loaded.TwitchClientId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoad_PersistsViewerOverlaySettings()
    {
        var directory = CreateTempDirectory();
        try
        {
            var service = new SettingsService(directory);
            service.Save(new AppSettings
            {
                ViewerOverlayEnabled = true,
                ViewerOverlayIntervalSeconds = 45,
                ViewerOverlayLeft = 100.5,
                ViewerOverlayTop = 30.25
            });

            var loaded = service.Load();

            Assert.True(loaded.ViewerOverlayEnabled);
            Assert.Equal(45, loaded.ViewerOverlayIntervalSeconds);
            Assert.Equal(100.5, loaded.ViewerOverlayLeft);
            Assert.Equal(30.25, loaded.ViewerOverlayTop);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_WithoutOverlaySettings_UsesDisabledOverlayAndThirtySeconds()
    {
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "settings.json"),
                """{ "ObsUrl": "ws://127.0.0.1:4455" }""");

            var loaded = new SettingsService(directory).Load();

            Assert.False(loaded.ViewerOverlayEnabled);
            Assert.Equal(30, loaded.ViewerOverlayIntervalSeconds);
            Assert.Null(loaded.ViewerOverlayLeft);
            Assert.Null(loaded.ViewerOverlayTop);
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
