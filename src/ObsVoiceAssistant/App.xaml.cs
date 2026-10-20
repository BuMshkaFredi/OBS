using System.IO;
using System.Windows;
using NAudio.Wave;
using ObsVoiceAssistant.Application;
using ObsVoiceAssistant.Audio;
using ObsVoiceAssistant.Commands;
using ObsVoiceAssistant.Configuration;
using ObsVoiceAssistant.Diagnostics;
using ObsVoiceAssistant.Integrations.Obs;
using ObsVoiceAssistant.Notifications;
using ObsVoiceAssistant.Recordings;
using ObsVoiceAssistant.Speech;
using ObsVoiceAssistant.Tray;

namespace ObsVoiceAssistant;

public partial class App : System.Windows.Application
{
    private SettingsService? settingsService;
    private TrayIconService? trayIcon;
    private NotificationPanelService? notifications;
    private ObsWebSocketClient? obsClient;
    private ApplicationOrchestrator? orchestrator;
    private NAudioMicrophoneRecorder? microphoneRecorder;
    private ContinuousListeningService? continuousListening;
    private CommandDispatcher? commandDispatcher;
    private VoiceCommandPipeline? voiceCommandPipeline;
    private ISpeechRecognizer? speechRecognizer;
    private RecordingNamingService? recordingNaming;
    private bool? lastObsConnectionState;
    private readonly AssistantLogger logger = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        settingsService = new SettingsService();
        trayIcon = new TrayIconService(ShowObsSettings, Shutdown);
        notifications = new NotificationPanelService();
        speechRecognizer = CreateSpeechRecognizer();
        StartObsConnection();
        logger.Info($"Доступно устройств записи: {WaveInEvent.DeviceCount}");
        for (var deviceIndex = 0; deviceIndex < WaveInEvent.DeviceCount; deviceIndex++)
        {
            logger.Info($"Устройство записи {deviceIndex}: {WaveInEvent.GetCapabilities(deviceIndex).ProductName}");
        }

        if (speechRecognizer is null)
        {
            // Без модели распознавания слушать микрофон бессмысленно, но приложение
            // остаётся в трее, чтобы модель можно было установить и запустить заново.
            trayIcon.SetStatus("нет модели распознавания");
            return;
        }

        microphoneRecorder = new NAudioMicrophoneRecorder(
            recordingError: exception => logger.Error("Ошибка записи микрофона", exception));
        continuousListening = new ContinuousListeningService(
            microphoneRecorder,
            ProcessRecordingAsync,
            ContinuousListeningOptions.Default,
            errorHandler: exception =>
            {
                logger.Error("Ошибка непрерывного прослушивания", exception);
                trayIcon.SetStatus("ошибка микрофона/распознавания");
            });
        continuousListening.Start();
        logger.Info(
            "Непрерывное прослушивание запущено: запись по речи, " +
            $"пауза {ContinuousListeningOptions.Default.SilenceToEndUtterance.TotalMilliseconds:F0} мс, " +
            $"максимум {ContinuousListeningOptions.Default.MaxUtterance.TotalSeconds:F0} с, " +
            $"порог RMS {ContinuousListeningOptions.Default.RmsThreshold:F0}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        orchestrator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        continuousListening?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        microphoneRecorder?.Dispose();
        (speechRecognizer as IDisposable)?.Dispose();
        recordingNaming?.Dispose();
        notifications?.Dispose();
        obsClient?.Dispose();
        trayIcon?.Dispose();
        base.OnExit(e);
    }

    // Vosk — единственный движок распознавания: он рассчитан на CPU и укладывается в целевое время.
    // Другие движки не подключены, чтобы не тащить вторую модель и второй native runtime.
    private ISpeechRecognizer? CreateSpeechRecognizer()
    {
        var voskModelDirectory = VoskSpeechRecognizer.ResolveDefaultModelDirectory();
        if (!Directory.Exists(voskModelDirectory))
        {
            logger.Error(
                "Каталог модели Vosk не найден. Ожидается один из: " +
                $"{string.Join(", ", VoskSpeechRecognizer.ModelDirectoryNames)} " +
                "в каталоге models рядом с приложением. Распознавание речи отключено.");
            return null;
        }

        logger.Info($"Движок распознавания: Vosk ({voskModelDirectory})");
        return new VoskSpeechRecognizer(
            voskModelDirectory,
            recognizedText: text => logger.Info($"Vosk распознал: {text}"),
            diagnostic: message => logger.Info(message));
    }

    private void ShowObsSettings()
    {
        if (settingsService is null)
        {
            return;
        }

        var window = new ObsSettingsWindow(settingsService, LoadObsInputNamesAsync);
        if (window.ShowDialog() == true)
        {
            RestartObsConnection();
        }
    }

    /// <summary>
    /// Список источников берётся у OBS отдельным коротким подключением, чтобы источники
    /// можно было настроить независимо от основного соединения приложения.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadObsInputNamesAsync(CancellationToken cancellationToken)
    {
        if (settingsService is null)
        {
            return Array.Empty<string>();
        }

        var settings = settingsService.Load();
        var options = new ObsConnectionOptions(settings.ObsUrl, settingsService.LoadObsPassword());
        var client = new ObsWebSocketClient(options);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(true);
            var names = await client.GetInputNamesAsync(cancellationToken).ConfigureAwait(true);
            logger.Info($"Получено источников OBS: {names.Count}");
            return names;
        }
        finally
        {
            try
            {
                await client.DisconnectAsync().ConfigureAwait(true);
            }
            catch
            {
                // Соединение могло не установиться; в этом случае отключать нечего.
            }

            client.Dispose();
        }
    }

    private void RestartObsConnection()
    {
        orchestrator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        recordingNaming?.Dispose();
        obsClient?.Dispose();
        StartObsConnection();
    }

    private async Task ProcessRecordingAsync(AudioRecording recording)
    {
        logger.Info($"Обработка аудиосегмента: {recording.Data.Length} байт");
        try
        {
            if (voiceCommandPipeline is not null)
            {
                await voiceCommandPipeline.ProcessAsync(recording).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            logger.Error("Ошибка обработки аудиосегмента", exception);
            trayIcon?.SetStatus("ошибка распознавания");
        }
    }

    private void StartObsConnection()
    {
        if (settingsService is null || trayIcon is null)
        {
            return;
        }

        var settings = settingsService.Load();
        var password = settingsService.LoadObsPassword();
        var obsOptions = new ObsConnectionOptions(settings.ObsUrl, password);
        obsClient = new ObsWebSocketClient(obsOptions);
        recordingNaming = new RecordingNamingService(
            new WpfRecordingNamePrompt(),
            message => notifications?.Show(message),
            message => logger.Info(message));
        recordingNaming.Attach(obsClient);
        var obsConnection = new ObsConnectionService(obsClient);
        obsConnection.ConnectionStateChanged += connected =>
        {
            // Событие приходит на каждой попытке, поэтому пишем только смену состояния.
            if (lastObsConnectionState == connected)
            {
                return;
            }

            lastObsConnectionState = connected;
            logger.Info(connected ? "OBS подключена" : "OBS недоступна, идёт переподключение");
        };
        var inputResolver = new ConfiguredObsInputResolver(settings.AudioInputs);
        commandDispatcher = new CommandDispatcher(
            new ObsCommandPortAdapter(obsClient, inputResolver),
            new NullTwitchCommandPort(),
            new ConfirmationPolicy());

        // Без распознавания соединение с OBS всё равно нужно: Replay Buffer и статус в трее.
        if (speechRecognizer is not null)
        {
            voiceCommandPipeline = new VoiceCommandPipeline(
                speechRecognizer,
                new CommandParser(),
                commandDispatcher,
                new WindowsToastConfirmationService(),
                diagnostic: message => logger.Info(message),
                notify: message => notifications?.Show(message),
                compactNotify: message => notifications?.ShowCompact(message));
        }

        orchestrator = new ApplicationOrchestrator(obsConnection, trayIcon);
        orchestrator.Start();
    }
}
