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
    private NAudioMicrophoneRecorder? wakeWordRecorder;
    private ContinuousListeningService? wakeWordListening;
    private WakeWordSession? wakeWordSession;
    private CapsLockHook? capsLockHook;
    private PushToTalkController? pushToTalk;
    private readonly SemaphoreSlim recognitionGate = new(1, 1);
    private CommandDispatcher? commandDispatcher;
    private VoiceCommandPipeline? voiceCommandPipeline;
    private ISpeechRecognizer? speechRecognizer;
    private RecordingNamingService? recordingNaming;
    private SingleInstanceGuard? singleInstance;
    private bool? lastObsConnectionState;
    private readonly AssistantLogger logger = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Второй экземпляр создал бы второй глобальный обработчик Caps Lock и второе
        // непрерывное прослушивание микрофона. Поэтому он только сообщает о себе
        // и завершается, оставляя работу первому экземпляру.
        singleInstance = new SingleInstanceGuard();
        if (!singleInstance.IsPrimary)
        {
            System.Windows.MessageBox.Show(
                "OBS Voice Assistant уже запущен. Управление доступно из значка в системном трее.",
                "OBS Voice Assistant",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

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
            recordingError: exception => logger.Error("Ошибка записи микрофона (Caps Lock)", exception));
        wakeWordRecorder = new NAudioMicrophoneRecorder(
            recordingError: exception => logger.Error("Ошибка записи микрофона (ключевое слово)", exception));

        // Второй режим работает постоянно и слушает микрофон отдельным потоком:
        // Push-to-talk не должен терять запись, пока идёт ожидание ключевого слова.
        // Общий IAudioRecorder здесь не подходит: Stop() одного режима очищает буфер
        // и обрывает фразу другого.
        wakeWordSession = new WakeWordSession();
        wakeWordListening = new ContinuousListeningService(
            wakeWordRecorder,
            ProcessWakeWordRecordingAsync,
            ContinuousListeningOptions.Default,
            errorHandler: exception =>
            {
                logger.Error("Ошибка прослушивания ключевого слова", exception);
                trayIcon.SetStatus("ошибка микрофона/распознавания");
            });

        pushToTalk = new PushToTalkController(microphoneRecorder);
        pushToTalk.RecordingStarted += () =>
        {
            // Пока нажат Caps Lock, фраза принадлежит Push-to-talk. Без паузы
            // одна и та же команда выполнилась бы дважды.
            wakeWordListening.Suspend();

            // Команда по кнопке отменяет ожидание команды после «Бочи»:
            // иначе следующая фоновая фраза попала бы в открытое окно.
            wakeWordSession.Reset();
        };
        pushToTalk.RecordingStopped += recording =>
        {
            wakeWordListening.Resume();
            _ = ProcessPushToTalkRecordingAsync(recording);
        };
        capsLockHook = new CapsLockHook(
            pushToTalk,
            exception =>
            {
                logger.Error("Ошибка Caps Lock push-to-talk", exception);
                // Если запись не остановилась штатно, непрерывное прослушивание
                // не должно остаться выключенным навсегда.
                wakeWordListening.Resume();
                notifications.Show("Ошибка микрофона. Проверьте микрофон и перезапустите приложение.");
            });
        wakeWordListening.Start();
        logger.Info(
            $"Прослушивание ключевого слова «{WakeWordSession.WakeWord}» включено: " +
            "скажите слово отдельно или сразу вместе с командой.");

        try
        {
            capsLockHook.Start();
            logger.Info(
                "Push-to-talk включён: удерживайте Caps Lock во время речи, " +
                "отпустите для распознавания. Обычное переключение Caps Lock отключено.");
        }
        catch (Exception exception)
        {
            logger.Error("Не удалось включить Caps Lock push-to-talk", exception);
            capsLockHook.Dispose();
            capsLockHook = null;
            notifications.Show("Не удалось включить Caps Lock. Перезапустите приложение.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        orchestrator?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        capsLockHook?.Dispose();
        wakeWordListening?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        microphoneRecorder?.Dispose();
        wakeWordRecorder?.Dispose();
        (speechRecognizer as IDisposable)?.Dispose();
        recordingNaming?.Dispose();
        notifications?.Dispose();
        obsClient?.Dispose();
        trayIcon?.Dispose();
        singleInstance?.Dispose();
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

    private async Task ProcessPushToTalkRecordingAsync(AudioRecording recording)
    {
        if (recording.Data.IsEmpty)
        {
            return;
        }

        // Vosk не рассчитан на параллельные вызовы: если пользователь быстро нажмёт
        // Caps Lock несколько раз, распознаём записи строго по одной.
        await recognitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ProcessRecordingAsync(recording).ConfigureAwait(false);
        }
        finally
        {
            recognitionGate.Release();
        }
    }

    /// <summary>
    /// Обрабатывает фразу, записанную непрерывным прослушиванием. Команда выполняется,
    /// только если прозвучало ключевое слово «Бочи» — отдельно или перед командой.
    /// Фоновая речь без ключевого слова не выполняется и не показывает уведомлений.
    /// </summary>
    private async Task ProcessWakeWordRecordingAsync(AudioRecording recording)
    {
        if (recording.Data.IsEmpty ||
            pushToTalk?.IsRecording == true ||
            wakeWordSession is null ||
            speechRecognizer is null)
        {
            return;
        }

        // Vosk не рассчитан на параллельные вызовы: Caps Lock и ключевое слово
        // распознаются строго по одной записи.
        await recognitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var text = await speechRecognizer.RecognizeAsync(recording).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                // На фоновом шуме и музыке Vosk часто не даёт текста — это не ошибка.
                return;
            }

            var decision = wakeWordSession.Handle(text);
            switch (decision.Kind)
            {
                case WakeWordDecisionKind.Armed:
                    logger.Info($"Услышано ключевое слово: «{text}». Жду команду.");
                    notifications?.ShowCompact("Слушаю");
                    break;

                case WakeWordDecisionKind.Command:
                    logger.Info($"Команда по ключевому слову: «{decision.CommandText}»");
                    var pipeline = voiceCommandPipeline;
                    if (pipeline is not null)
                    {
                        await pipeline.ProcessTextAsync(decision.CommandText).ConfigureAwait(false);
                    }

                    break;

                default:
                    logger.Info($"Без ключевого слова, пропущено: «{text}»");
                    break;
            }
        }
        catch (Exception exception)
        {
            logger.Error("Ошибка обработки команды по ключевому слову", exception);
            trayIcon?.SetStatus("ошибка распознавания");
        }
        finally
        {
            recognitionGate.Release();
        }
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
