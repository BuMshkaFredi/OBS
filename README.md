# OBS Voice Assistant

An offline Windows voice assistant for OBS Studio and Twitch. Hold `Caps Lock`, say a short Russian
command, and release: the application recognizes speech locally, controls OBS Studio, and shows the
result in a notification. The wake word «Бочи» (Bochi) works alongside push-to-talk.

The application runs in the system tray and never sends audio to the cloud. Documentation for
development and architecture is in [`AGENTS.md`](AGENTS.md).

## Requirements

- Windows 10 or Windows 11, x64.
- [OBS Studio 32.2.2](https://obsproject.com/) with the built-in OBS WebSocket server.
- .NET SDK 10.0.401 to build from source.
- A working microphone.
- The Russian Vosk model (installed separately, see below).
- Optional: `ffmpeg` for an MP4 copy of recordings and replays.
- Optional: Twitch application (Client ID) for title, category, and viewer count.
- Optional: the Yandex Music desktop app for pausing and switching tracks by voice.

The application targets `net10.0-windows10.0.22621.0` and uses WPF, NAudio 3.2.0,
obs-websocket-dotnet 5.7.0, Vosk 0.3.38, and CommunityToolkit.WinUI.Notifications 7.1.2.

## Build and run

From the repository root:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet run --project src/ObsVoiceAssistant/ObsVoiceAssistant.csproj
```

The application starts in the tray. A second launch shows a message and exits: only one instance is
allowed, otherwise the global Caps Lock handler and microphone capture would run twice.

Self-contained publish:

```powershell
dotnet publish src/ObsVoiceAssistant/ObsVoiceAssistant.csproj `
  --configuration Release --runtime win-x64 --self-contained true
```

The publish output includes the .NET runtime and the native Vosk libraries. The speech model is a
separate artifact: place the selected model into a `models` directory next to the published
executable or in any parent directory.

The application does not register itself in Windows startup yet. To start it automatically, create a
shortcut to the executable in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`.

## Speech model

Commands are recognized by Vosk locally, on the CPU. Model directories are not committed to the
repository. The application searches for models in this priority order:

1. `vosk-model-ru-0.42` (1.8 GB, best accuracy)
2. `vosk-model-ru-0.22` (1.5 GB)
3. `vosk-model-small-ru-0.22` (45 MB, fastest)

Download and unpack at least one model into `models` in the repository root:

```powershell
$url = 'https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip'
$out = '.\models\vosk-model-small-ru-0.22.zip'
Invoke-WebRequest -Uri $url -OutFile $out
Expand-Archive -Path $out -DestinationPath '.\models' -Force
```

For the larger model, replace the file name with `vosk-model-ru-0.42.zip` and allow several
gigabytes of disk space. If no model is found, recognition is disabled and the tray status shows
`нет модели распознавания`.

## OBS setup

1. Open OBS Studio → **Tools** → **WebSocket Server Settings**.
2. Enable the WebSocket server. The default address is `ws://127.0.0.1:4455`.
3. Set a password if needed and enable authentication.
4. In the application, open the tray menu → **Настройки** and enter the same address and password.
5. In the same window, select the real OBS source names for the microphone, browser, personal music,
   and stream music. The **Загрузить из OBS** button pulls the list from a running OBS instance.
6. Save the settings.

The application enables Replay Buffer after connecting to OBS. If you disable it manually in OBS,
the application does not enable it again until the next restart.

Source names are matched exactly: the application never guesses or fuzzy-matches OBS source names.
A role without a selected source is unavailable, and the command reports that the source is not
configured. The same OBS source may be assigned to several roles deliberately.

## Twitch setup

1. Create an application at [dev.twitch.tv/console/apps](https://dev.twitch.tv/console/apps).
2. Copy its **Client ID** into the Twitch section of the application settings. A Client Secret is not
   used.
3. Click **Подключить Twitch**, open the activation page, and enter the displayed code.
4. Confirm the only requested permission, `channel:manage:broadcast`.

Access and refresh tokens are stored through Windows DPAPI for the current user and are never
written to the log. The Client ID is a public identifier and is stored in `settings.json`.

## Voice control

Commands are Russian-only. There are two activation modes:

- **Push-to-talk:** hold `Caps Lock`, speak, release. The normal Caps Lock toggle is suppressed.
- **Wake word:** say «Бочи» alone and then a command within 6 seconds, or say the wake word right
  before the command, for example «Бочи, включи микрофон». Background speech without the wake word
  is ignored. While Caps Lock is held, wake-word listening is suspended so the same command is not
  executed twice.

Typical commands:

| Action | Example phrase | Confirmation |
| --- | --- | --- |
| Start recording | `начни запись` | No |
| Stop recording | `останови запись` | Yes |
| Start stream | `начни стрим` | Yes |
| Stop stream | `останови стрим` | Yes |
| Save replay | `сохрани повтор` | No |
| Enable Replay Buffer | `включи повтор` | No |
| Toggle microphone | `включи микрофон`, `выключи микрофон`, `мут` | No |
| Toggle personal music | `выключи музыку`, `включи музыку` | No |
| Next music track | `следующий трек`, `следующая песня`, `переключи трек` | No |
| Mute/unmute browser | `выключи браузер`, `включи браузер` | No |
| Mute/unmute stream music | `выключи музыку стрима`, `включи звук стрима` | No |
| Mute all sound | `ол мут`, `выключи весь звук` | No |
| Change volume | `громкость микрофона плюс 10`, `громкость музыки минус 5` | No |
| Change stream title | `измени название стрима на ...` | Yes |
| Change stream category | `измени категорию стрима на ...` | Yes |

Dangerous actions show a Windows notification with **Подтвердить** and **Отмена** buttons. The
action is performed only after **Подтвердить**; **Отмена**, closing the notification, or no response
performs nothing.

Microphone and personal music are toggles: the word «включи» or «выключи» does not choose the final
state. The notification always reports the actual state. Stream music and the browser use explicit
mute/unmute. «Ол мут» mutes every OBS audio source and reports how many sources were muted.

Volume is changed in percentage points from 0 to 100; without a number the step is 10, and
«громче»/«тише» mean plus/minus 10. The notification shows the actual value after clamping.

When personal music is muted, the application pauses the Yandex Music desktop app through Windows
media sessions. Unmuting the OBS source does not resume playback: the player is never started
automatically. If the player has no media session, the OBS action still succeeds and the notification
reports that the pause or track switch was not performed.

## Twitch title tags and viewer count

When the stream title changes, the current title is read from Twitch before confirmation. Words
starting with `!`, for example `!tg` and `!exitlag`, are appended to the new title in the order they
appear in the old title, without duplicates. The confirmation shows the final title in full. A title
longer than 140 characters, together with the tags, is rejected without calling Twitch. Only trailing
punctuation is removed from the spoken title.

The viewer count overlay is disabled by default. Enable it with the **Счётчик зрителей** item in the
tray menu or with the checkbox in settings. The always-on-top window does not take focus, does not
appear in the taskbar or Alt+Tab, and hides while the stream is offline. The default polling interval
is 30 seconds (15–3600 in settings). On a network error the window keeps the last value dimmed; no
toast is shown. Twitch caches the viewer count, so the number may lag behind the real audience by
tens of seconds.

To show the overlay in the stream, capture this window or the whole screen in OBS. The application
does not create a separate OBS source. The default position is the top-right corner with a 24 px
margin; drag the window to move it, and the position is saved.

## Recordings and replays

After a recording stops or a replay is saved, a small window asks for a file name. The entered name
replaces the automatic OBS name; an empty field, `Esc`, or closing the window keeps the OBS name.
Invalid file-name characters are replaced with `_`, and an existing file is never overwritten.

If MP4 copy is enabled, the application creates an MP4 copy next to the original OBS file using
`ffmpeg -c copy` (no re-encoding). The original file is neither deleted nor renamed. Install ffmpeg
and set its path in the settings, or place `ffmpeg.exe` in an `ffmpeg` folder next to the application.
If ffmpeg is missing, recording and renaming continue to work and a notification explains that the
copy was not created. OBS files that are already MP4 do not get a copy.

## Settings, secrets, and logs

All local data is stored in `%LOCALAPPDATA%\ObsVoiceAssistant`:

- `settings.json` — OBS address, Twitch Client ID, source names, overlay settings, ffmpeg path;
- `secrets` — OBS password and Twitch tokens, protected with Windows DPAPI;
- `assistant.log` — the current run log.

The application keeps no persistent command history. Voice audio is processed locally and is not
saved or sent anywhere. The log does not contain tokens, passwords, or audio.

## Tests

```powershell
dotnet test
dotnet test tests/ObsVoiceAssistant.UnitTests
dotnet test tests/CustomerMail.UnitTests
```

Unit tests do not need OBS, Twitch credentials, or a speech model. Integration tests for OBS/Twitch
boundaries are in `tests/ObsVoiceAssistant.IntegrationTests`. Manual scenarios for Caps Lock,
notifications, Twitch, Yandex Music, and the overlay are listed in
[`docs/MVP_PLAN.md`](docs/MVP_PLAN.md#6-mandatory-manual-test-cases).

## Troubleshooting

- Command not recognized: check the tray status, the log lines `Vosk распознал` and
  `Команда не распознана`, and the selected microphone. See
  [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md).
- No OBS connection: verify that OBS is running, OBS WebSocket is enabled, and the address and
  password match.
- Yandex Music is not paused: start playback in the desktop app and make sure it appears in the
  Windows media popup. The browser version is not controlled.
- The overlay is hidden: Twitch must be connected and the stream must be live. A network error keeps
  the last value dimmed instead of showing a toast.

## Documentation

- [`docs/MVP_PLAN.md`](docs/MVP_PLAN.md) — scope, slices, and manual test cases.
- [`docs/SPEECH_AND_NOTIFICATIONS.md`](docs/SPEECH_AND_NOTIFICATIONS.md) — recognition, parser,
  activation modes, and notifications.
- [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md) — diagnostics for OBS, Twitch, media, and the
  overlay.
- [`docs/integrations/twitch.md`](docs/integrations/twitch.md) — OAuth, Helix, title tags, and viewer
  count.
- [`AGENTS.md`](AGENTS.md) — architecture, rules, and development workflow.

Russian originals of the documentation are stored next to the English files with the `.ru.md` suffix.
The README is intentionally English-only.
