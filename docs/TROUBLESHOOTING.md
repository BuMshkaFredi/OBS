# Troubleshooting: the command is not executed

Application log: `%LOCALAPPDATA%\ObsVoiceAssistant\assistant.log` (read as UTF-8).
A convenient filter:

```powershell
Get-Content "$env:LOCALAPPDATA\ObsVoiceAssistant\assistant.log" -Encoding UTF8 |
  Select-String 'Движок распознавания|Модель распознавания|Непрерывное прослушивание запущено|OBS подключена|OBS недоступна|Запись без речи|Vosk распознал|Vosk не распознал сегмент|Исправлено распознавание|Распознавание заняло|Распознана команда|Команда не распознана|Речь была, но текст не распознан|Требуется подтверждение|Команда отклонена|Команда отправлена|Ошибка' |
  Select-Object -Last 60
```

## How to read the log

| What you see in the log | What it means |
| --- | --- |
| No `OBS подключена` line | The application did not connect to OBS. Check that OBS is running, OBS WebSocket is enabled, and the address/password in the “OBS settings” window match the OBS settings. |
| `Команда не распознана: «...»` | The text matched no dictionary phrase even after word correction and whole-phrase comparison within the limited threshold. The quotes contain what the engine actually heard. The nearest dictionary phrase may follow; the log does not print a ready-made `new(...)` line, and a human decides whether to add it. The hint in this line is never executed by itself. |
| `Исправлено распознавание: «остонови»→«останови»` | The word differed by 1–2 letters, and all allowed variants produced the same command, so it was executed. For a whole-phrase correction, the log shows the full pair, for example `«нашли запись»→«начни запись»`. If the correction looks wrong, that is a reason to fix the dictionary or the threshold. |
| `Речь была, но текст не распознан` | The gate detected speech, but the engine returned empty text. This is usually noise or a phrase that is too quiet/short. |
| `Ошибка выполнения ...` and a notification panel without an icon | The command was recognized, but OBS/Twitch did not accept it. The reason is in the notification and the log. |
| `Требуется подтверждение`, but nothing happens | A `Подтвердить` (Confirm) press in the Windows notification is expected. `Отмена` (Cancel) and closing the notification perform no action. |
| `Команда отправлена: ...`, but OBS is silent | The command reached OBS; the problem is on the OBS side (for example, the action is impossible in the current state: a recording is already running or there is no Replay Buffer). |
| `Ошибка обработки аудиосегмента` with text about an unconfigured source | No OBS source name is set for toggling. See the section below. |
| `Запись без речи: распознавание пропущено` | The energy gate triggered: no speech was found in the recording. Possible causes are a microphone that is too quiet or a wrongly selected recording device. |
| `Vosk не распознал сегмент «Бочи»: ...` | Continuous listening found speech by energy, but Vosk returned empty text. The line shows the duration, peak RMS, and segment size. If the RMS is noticeably above the threshold of 200 and the duration is close to a normal phrase, missed speech is likely rather than noise. |
| `ERROR ... Каталог модели Vosk не найден`, tray status `нет модели распознавания` | The recognition model is not installed. Download and unpack a model from the table in `docs/SPEECH_AND_NOTIFICATIONS.md` into the `models` directory and start the application again. |

## MP4 copy of a recording and a replay

After saving a file, the application renames it and makes an MP4 copy next to the original. The copy
is created by ffmpeg without re-encoding (`-c copy`), so quality and sound do not change.

- `MP4-копия не создана: Не найден ffmpeg...` — there is no `ffmpeg.exe` on the computer. Install
  ffmpeg (for example, `winget install Gyan.FFmpeg`), then set the path to `ffmpeg.exe` in the
  “Recording and replay” section of the “Settings” window, or put `ffmpeg.exe` into the `ffmpeg`
  folder next to the application. While ffmpeg is missing, recording and renaming work as before.
- `MP4-копия не создана: <строка ошибки ffmpeg>` — ffmpeg started but could not process the file.
  This is usually caused by incompatible streams in the recording: try recording in H.264 + AAC.
- `Файл уже в MP4, копия не нужна` — this is expected: OBS saves the replay directly to MP4, and no
  copy is created for it.
- `MP4-копия создана: <имя>.mp4` — the copy is ready and lies in the OBS folder together with the
  original.

## Twitch

- `Twitch не настроен` — set the Client ID in the Twitch section of the “Settings” window. A Client
  secret is not used.
- `Twitch не подключён` or `Подключите Twitch заново` — start the connection through Device Code in
  the settings and confirm the `channel:manage:broadcast` scope.
- A category search error appears before the confirmation notification: either Twitch returned no
  category or the connection is temporarily unavailable. Repeat the phrase with the category name.
- The category notification shows the Twitch search result, not the original recognized name.
  Check it before pressing `Подтвердить` (Confirm).
- After a successful update, the notification shows the new title or category. Helix errors are
  shown as a safe message; tokens are never included in diagnostics.

Connection and session recovery details: [`integrations/twitch.md`](integrations/twitch.md).

## Microphone toggle

The microphone works as a toggle: `включи микрофон` (turn on the microphone) and `выключи микрофон`
(turn off the microphone) are the same action, flipping the current state to the opposite one. The
word in the command does not choose the result, so after each command the application shows a
notification with the actual state: `Микрофон выключен` or `Микрофон включён`.

If you say the command twice in a row, the microphone returns to its original state — that is
expected, not an error.

The short command `мут` (Vosk may hear it as `муть` or `мьют`) does the same as `выключи микрофон`:
it toggles the state and shows the actual result. The command `мут все` / `ол мут` is no longer the
microphone but muting all sound, see below.

## All sound

`ол мут` (variants: `all mute`, `выключи весь звук`, `заглуши все`) mutes every OBS audio source,
including the microphone, desktop audio, browser, and music. This action needs no confirmation: it is
reversible, and the sources can be enabled with regular mute/unmute commands or manually in OBS.

Video-only sources (for example, `Захват экрана`) are skipped: OBS answers an attempt to mute them
with error 604, and the application does not treat this as a failure. The notification shows the
number of muted sources. If some sources could not be muted, the notification lists their names; in
that case, check the OBS connection and repeat the command.

## Personal music and stream music

These roles use the `PersonalMusic` and `StreamMusic` fields from “OBS settings”.

**Personal music** works like the microphone: `выключи музыку`, `выключи мою музыку`,
`включи личную музыку` toggle the source state to the opposite one. The notification shows the
actual result: `Личная музыка выключена` or `Личная музыка включена`. A repeated command returns the
original state.

**Stream music** works like the browser: `выключи музыку стрима` and `включи звук стрима` are
explicit mute/unmute, without a notification. The word “стрим” is mandatory: `выключи стрим` stops
the broadcast, while `выключи музыку` toggles personal music. Vosk often drops the ending and says
`стрим` instead of `стрима`; both variants are supported.

Phrasings are assembled from verbs and sources, so other variants work as well: `заглуши музыку`,
`верни мою музыку`, `выруби звук стрима`, `музыку стрима выключи`, `выключи музыку на стриме`,
`включи звук на стриме`. Vosk often hears the preposition “на” as “на стремя”, “на стриму”, or
“на стримов” — these forms are recognized too.

The application does not guess source names: the name must match the source name in OBS. While the
name is not set, the command is not executed and a clear error is written to the log.

Setup:

1. Open the tray icon menu and select “OBS settings”.
2. Click “Load from OBS” — the source list is pulled from the running OBS.
3. Select the microphone source (in OBS, the default is `Mic/Aux`).
4. Save the settings.

The “do not use” item disables the role: the command for it is not executed and reports that the
source is not configured. This item is available even without an OBS connection.

The fields are independent: the same source can be set in several roles. For example, “Desktop
audio” can be both stream music and personal music. This is a deliberate setting, not an error: the
application does not require unique names.

Source names are stored in `%LOCALAPPDATA%\ObsVoiceAssistant\settings.json` in the `AudioInputs`
property: `Microphone`, `Browser`, `StreamMusic`, `PersonalMusic`. An empty value means the
corresponding voice commands are unavailable.

Implicit matching of similar source names is deliberately not applied: a wrong name must not mute
the wrong source.

## Recording and replay name

After stopping a recording or saving a replay, the application opens a small window with a name
field. The name **replaces** the automatic OBS name entirely: instead of `2026-10-20 18-16-32.mkv`
you get `Мой клип.mkv`. The extension and the OBS folder are preserved, and the file stays where OBS
wrote it.

- An empty field, `Esc`, or closing the window leaves the OBS name unchanged.
- Characters invalid in a file name (`/`, `:`, `*`, `?`, etc.) are replaced with `_`.
- An extension entered together with the name (`клип.mp4`) is dropped: OBS defines the format.
- If a file with that name already exists, ` (2)`, ` (3)` is appended; the existing file is not
  overwritten.
- The window also appears on a manual recording stop or replay save inside OBS itself.
- If OBS did not report the file path, the window does not open: there is nothing to rename.
- The result is written to the log with the lines `Запись переименована` / `Повтор переименован` or
  `Запись не переименована` / `Повтор не переименован`.

## Second launch

The application runs as a single instance. If you start it a second time, the message
“OBS Voice Assistant уже запущен” appears and the new process exits immediately: a second instance
would create a second global Caps Lock handler and a second microphone listener, and one command
would be executed twice. Control stays with the first instance, whose icon is in the system tray.

## Volume does not change

- The command must contain a direction: `плюс`, `минус`, `громче`, or `тише`. The phrase
  `громкость микрофона 50` is not executed: absolute values are not supported.
- The source must be configured in the “OBS settings” window. For a role without a name, an error is
  written to the log, and the notification says that the source is not configured.
- The value is clamped to 0–100%, so `плюс 10` at 100% and `минус 10` at 0% do not change the
  volume. The notification still shows the actual value.
- If OBS is unavailable, the command fails with a connection error, like the other commands.
