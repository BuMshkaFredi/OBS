# MVP Plan: OBS Voice Assistant

Status: plan before implementation starts. The source code, project file, and commands below have not
been created or verified yet.

## 1. MVP Goal

Create a separate Windows application in the system tray that accepts Russian voice commands in
Push-to-talk mode via `Caps Lock`, recognizes them fully offline, and controls OBS Studio 32.2.2
through OBS WebSocket.

The MVP must run after a self-contained publish without Python installed and without a mandatory
cloud recognition service.

## 2. MVP Scope

### Included in the MVP

- Windows-only desktop application.
- Operation in the system tray.
- Autostart together with Windows.
- Push-to-talk: `Caps Lock down -> speak -> Caps Lock up`.
- Activation by the wake word «Бочи» alongside Push-to-talk: the word alone or before the command.
- Blocking the normal Caps Lock state toggle.
- Offline recognition of short Russian commands.
- OBS control:
  - start and stop recording;
  - start and stop the stream;
  - enable Replay Buffer;
  - save a replay;
  - enable and disable the configured audio sources;
  - change the volume of the configured audio sources.
- Automatic enabling of Replay Buffer after connecting to OBS, unless the user disabled it manually.
- Reconnect to OBS and Twitch after temporary unavailability.
- Windows notifications about success, errors, and confirmation requests.
- Confirmation buttons `Подтвердить` and `Отмена`.
- Confirmation for starting and stopping the stream, stopping the recording, and changing the Twitch
  title and category.
- Twitch connection through OAuth.
- Changing the Twitch title after confirmation.
- Exact Twitch category lookup, showing the found result, and changing the category after
  confirmation.
- Setting real OBS source names in the “OBS settings” window: names are entered manually or pulled
  from the running OBS as a list.
- A log of the current run in memory without persisting history between runs.
- Entering a name for the recording and replay file after a recording finishes or a replay is saved.
- Unit tests for the parser, dispatcher, confirmation policy, and configuration logic.
- Integration/contract tests for the OBS and Twitch boundaries with test doubles.
- A manual test plan for Caps Lock, notifications, autostart, and self-contained publish.

### Not included in the MVP

- A full settings window; it is planned after the MVP. For now there is a minimal “OBS settings”
  window: address, password, and source names.
- Editing user commands through the UI.
- Switching scenes.
- Support for platforms other than Twitch.
- Persistent command history.
- Voice replies.
- Cloud speech recognition.
- Automatic implicit matching of similar OBS source names.
- Mandatory GPU acceleration of recognition.

## 3. Fixed Decisions

- Language and runtime: C# 12 and .NET 10.0.12 (SDK 10.0.401), target framework `net10.0-windows`;
  versions are pinned in `.csproj`.
- UI: WPF with a system tray; do not add another UI framework.
- OBS: OBS Studio 32.2.2, built-in OBS WebSocket 5.x.
- Speech engine: Vosk 0.3.38 (CPU, offline). The decision was fixed after a benchmark: Whisper on the
  target machine took 28–48 s per segment.
- The Vosk model is selected by priority: `vosk-model-ru-0.42` → `vosk-model-ru-0.22` →
  `vosk-model-small-ru-0.22`.
- Target recognition mode: CPU is mandatory; the RTX 5060 may be used as an optimization, but not as
  a requirement.
- Platform: Twitch Helix API and OAuth with the minimum necessary scopes.
- Secrets: Windows DPAPI or Credential Manager; do not store tokens and passwords in plain JSON and
  do not log them.
- Confirmation: a Windows notification with two buttons — `Подтвердить` and `Отмена`; do not add an
  automatic timeout yet.
- Replay Buffer: enable automatically after a successful connection to OBS; if the user disabled it
  manually, do not enable it again until the application restarts; save replays with OBS into the
  standard OBS folder.

- Connection to OBS Studio 32.2.2 via `ws://127.0.0.1:4455` and automatic Replay Buffer start were
  verified manually by the user; no parallel connections were observed after the lifecycle fix.

### Slice 0. Project setup and minimal build

Create the solution, the WPF project, the test projects, and the base directory structure from the
root `AGENTS.md`.

Readiness criteria:

- `.csproj` pins the exact versions of .NET, C#, and dependencies.
- `dotnet restore` and `dotnet build` pass.
- The application starts in the tray with a minimal exit menu.
- A test framework is chosen and one passing test is added.
- Preliminary commands and versions are updated in `AGENTS.md`.

### Slice 1. OBS WebSocket and runtime state

Add the OBS port, the WebSocket adapter, and the state service with retry/reconnect.

Verify:

- connecting and disconnecting;
- reading the OBS state;
- starting and stopping recording;
- starting and stopping the stream;
- enabling Replay Buffer;
- saving Replay Buffer;
- reading the input list and mute state;
- mute/unmute of the selected input.

Readiness criteria:

- the command does not depend directly on the SDK;
- connection errors are returned explicitly;
- after connecting, Replay Buffer is enabled automatically but is not re-enabled after a manual
  shutdown;
- a wrong or missing source name does not change another source;
- there are unit tests for the dispatcher and adapter test doubles;
- there are manual/integration checks with OBS 32.2.2.

### Slice 2. Caps Lock and audio capture

Separate the global key interception and audio recording into distinct components.

Readiness criteria:

- pressing `Caps Lock` starts recording;
- releasing `Caps Lock` finishes recording;
- the normal Caps Lock state is not changed;
- a repeated or incorrect key event is handled safely;
- an empty or too short recording is not sent to recognition;
- a manual test confirms operation in OBS and in a regular text application.

### Slice 3. Offline recognition and parser

Connect the local speech engine, choose the model by benchmark, and convert text into strictly typed
intents.

Readiness criteria:

- audio never leaves the computer;
- short Russian commands are supported;
- the parser does not perform the action itself;
- an unknown or ambiguous phrase leads to a safe notification;
- unit tests cover phrase variants and negative cases;
- the benchmark records the latency after the key is released and the quality on both target
  configurations.

Target: a short command is processed in no more than 1–1.5 seconds after the key is released, if
this is achievable on the specific hardware.

### Slice 4. Command dispatcher, notifications, and confirmations

Wire intents to the OBS/Twitch ports and add Windows notifications.

Readiness criteria:

- safe commands are executed without confirmation;
- starting the stream always requires confirmation;
- stopping the recording always requires confirmation;
- stopping the stream always requires confirmation;
- changing the Twitch title always requires confirmation;
- changing the Twitch category always requires confirmation;
- confirmation is performed only after pressing `Подтвердить`;
- `Отмена` does not invoke an external operation;
- no response must not automatically confirm the action;
- the operation result is shown by a safe notification;
- credentials and sensitive payloads never reach the log.

### Slice 5. Twitch

Device Code OAuth without a client secret, the DPAPI token store, the Helix adapter, the Twitch
section in the general settings, and category preparation before confirmation are implemented.
Details: [`integrations/twitch.md`](integrations/twitch.md).

Automated unit tests cover OAuth/Helix requests and command handling. Manual verification with a real
Twitch account and Windows Toast remains an operational check.

Readiness criteria:

- OAuth scopes are agreed before implementation and are minimal;
- tokens are stored through Windows DPAPI/Credential Manager;
- the application checks the connection before changing metadata;
- the title changes only after confirmation;
- the category is matched to an exact `game_id` before confirmation;
- on temporary unavailability, a reconnect is performed instead of an application crash;
- tokens are absent from tests, documentation, and logs.

### Slice 6. Publish, autostart, and operational verification

Prepare the self-contained publish and application autostart.

Readiness criteria:

- the application runs on Windows without Python;
- native speech libraries and the selected model are shipped explicitly and verified at startup;
- autostart does not create several application instances;
- a second launch focuses or uses the existing instance;
- the manual test plan is passed on Ryzen 5 5500H / 16 GB RAM;
- if possible, verification is performed on Ryzen 5 3600 / RTX 5060 / 16 GB RAM;
- latency, accuracy, and known limitations are recorded in `docs/testing/`.

## 5. Minimum MVP Command Set

| Intent | Example phrases | Confirmation |
| --- | --- | --- |
| `StartRecording` | `начни запись`, `запусти запись` | No |
| `StopRecording` | `останови запись`, `выключи запись` | Yes |
| `StartStream` | `начни стрим`, `запусти стрим` | Yes |
| `StopStream` | `останови стрим`, `заверши стрим` | Yes |
| `StartReplayBuffer` | `включи повтор`, `включи буфер повтора` | No |
| `SaveReplay` | `сохрани повтор` | No |
| `ToggleInput` (microphone) | `включи микрофон`, `выключи микрофон`, `мут` | No |
| `AdjustInputVolume` | `громкость микрофона плюс 10`, `громкость музыки минус 5`, `громкость -5` | No |
| `MuteInput` (browser) | `выключи звук браузера` | No |
| `UnmuteInput` (browser) | `включи звук браузера` | No |
| `MuteAllAudio` | `ол мут`, `выключи весь звук`, `заглуши все` | No |
| `NextMusicTrack` | `следующий трек`, `переключи трек`, `следующая песня` | No |
| `SetStreamTitle` | `измени название стрима на ...` | Yes |
| `SetStreamCategory` | `поменяй категорию игры на ...` | Yes |

The actual phrase variants and OBS source names must not be considered final until the settings panel
appears. Phrase fuzzy matching must not choose an OBS source: the source is taken only from settings.

## 6. Mandatory Manual Test Cases

1. Press and release Caps Lock without speech: the Caps Lock state does not change.
2. Hold Caps Lock, say a short Russian command, release: the command is recognized and executed;
   `начни запись` shows the notification `Запись началась`.
3. Execute `останови запись`: without pressing `Подтвердить`, the recording does not stop.
4. Execute `останови запись`, then press `Отмена`: the recording does not stop.
5. Check confirmation for starting the stream, stopping the stream, and the Twitch title and
   category: without pressing `Подтвердить`, the action is not performed.
6. Disconnect OBS and check that there is no crash, that a notification appears, and that the
   connection is restored.
7. Connect OBS after the application starts and check that Replay Buffer is enabled automatically.
   **Verified manually by the user: the application connected to OBS Studio 32.2.2 via
   `127.0.0.1:4455` and Replay Buffer started.** Then disable Replay Buffer manually in OBS and make
   sure the application does not enable it again, including after a reconnect.
8. Set the microphone source name in the “OBS settings” window (the “Load from OBS” button pulls the
   list) and check that toggling changes only this source and that the notification shows the actual
   state (`Микрофон выключен` / `Микрофон включён`). The short command `мут` does the same.
   **Without a configured name, the command is not executed and a clear error is written to the
   log.**
9. Configure `PersonalMusic` and `StreamMusic` and check the music: `выключи музыку` toggles personal
   music and shows the actual state, `выключи музыку стрима` / `включи звук стрима` perform explicit
   mute/unmute. `Выключи стрим` still stops the broadcast, not the music.
10. Launch the application twice and make sure no two instances and no two global Caps Lock handlers
    are created.
11. Run the self-contained publish on Windows without Python.
12. Run the recognition benchmark on both target configurations.
13. Stop a recording and save a replay: the name window appears. After entering `Тест`, the file gets
    the name `Тест_<имя OBS>.<ext>`, and an empty field leaves the OBS name. Check the same for a
    manual recording stop and replay save inside OBS itself.
14. Say `ол мут`: all OBS audio sources are muted, video-only sources do not interfere, and the
    notification shows the number of muted sources. Make sure the stream and recording are not
    stopped, and the microphone can be restored with `мут` or `включи микрофон`.
15. Say `Бочи` without a command: the compact panel `Слушаю` appears, then say `включи микрофон`
    within 6 seconds — the command is executed.
16. Say `Бочи, начни запись` as one phrase: the command is executed without a separate waiting window.
17. Say a command without the wake word, for example `включи микрофон`: nothing is executed, and the
    line `Без ключевого слова, пропущено` remains in the log.
18. Hold Caps Lock and say `Бочи, начни запись`: the command is executed once, with no repeated
    execution after the key is released. The “Бочи” waiting window is closed afterwards.
19. Say `Бочи`, then immediately hold Caps Lock and execute another command: after the key is
    released, the background phrase is no longer treated as a command.
20. Say `Бочи, громкость музыки плюс 10`: the personal music volume rises by 10 percentage points,
    and the notification shows the actual value. Repeat `громкость музыки минус 10`.
    Check `громкость +10` without a target (microphone), `громкость браузера минус 5`,
    `громкость музыки стрима плюс 5`, `громкость стрима минус 10`, and clamping at 0% and 100%.
    Separately check correction of typical Vosk errors: `громкость музыки стрела минус десять`
    and `громкость плюс стал`.
21. Start Yandex Music and say `выключи музыку`: the `PersonalMusic` source is muted, the music is
    paused, and the notification shows both the source state and the pause result. Then say
    `включи музыку`: the source is unmuted, and the music stays paused.
22. Say `следующий трек` with Yandex Music running: the track switches. Repeat with Yandex Music
    closed: the command does not crash and notifies that the player was not found.
23. Stop a recording with the MP4 copy enabled: the OBS folder contains the original `.mkv` and the
    `.mp4` copy with the same base name, and the original file is neither deleted nor renamed. Repeat
    for saving a replay.
24. Change the stream title when the current title contains `!tg` and `!exitlag`: the confirmation
    shows the final title with the tags, and after confirmation the tags are in place and not
    duplicated. Then set a title that, together with the tags, is longer than 140 characters: the
    command is rejected and the Twitch title does not change.
25. Enable the viewer overlay and start the stream: the window shows the viewer count and updates.
    After the stream stops, the window hides; the window position persists after a restart.

## 7. Risks and Questions Before the Relevant Stages

- The exact Vosk latency depends on the model size, recording length, and CPU load; on
  `vosk-model-small-ru-0.22` the measured RTF is ≈ 0.20–0.23, but the larger models require a
  separate measurement.
- Caps Lock interception and Windows Toast actions require manual verification in a real Windows
  session.
- Consider the .NET version, speech engine, and test framework chosen: .NET 10.0.12, Vosk 0.3.38,
  xUnit.
- Twitch OAuth scopes must be agreed before implementing Twitch authorization.
- The autostart format and distribution method must be confirmed before adding installer/startup
  integration.
- Do not add SQLite just for temporary history: persistent history is not needed in the MVP.
- Yandex Music may not publish a Windows media session or may publish it under a different
  application name. Then a fallback is needed (global media keys); decide based on verification
  results, not in advance.
- The viewer overlay appears in the frame only if OBS captures this window or the whole screen.
  When capturing a game or another window, the streamer sees the counter on their screen, but the
  stream viewers do not.
- Helix `GET /streams` returns the viewer count with a delay and caching on the Twitch side, so
  “real time” is approximate, accurate to tens of seconds.
- Title tags are recognized only by the `!` pattern; tags without `!` are not preserved until a
  configurable list appears.
- The final title with tags may exceed the Twitch limit of 140 characters; the command is rejected
  rather than truncating the user's text.

## 8. Definition of Done for the MVP

The MVP is considered ready when:

- all mandatory commands and vertical slice criteria are passed;
- unit and relevant integration tests pass;
- the manual test plan for Windows features is completed;
- the self-contained publish is verified on Windows without Python;
- OBS and Twitch reconnect are verified under temporary unavailability;
- confirmations do not allow dangerous actions without the `Подтвердить` button;
- offline processing and secret-handling requirements are met;
- the speech engine benchmark and known limitations are recorded;
- README, `AGENTS.md`, and docs are updated according to the actually chosen versions and commands.

## 9. Additional Tasks (slice 7, accepted 2026-10-27)

Source: the customer email `docs/customer-questions/answers/reply-20261027-043724-a15e4e8515.md`,
decision registry — `docs/customer-questions/QUESTIONS.md`. The user accepted all four wishes and
chose the implementation: the viewer count — a separate always-on-top window, Yandex Music control —
through Windows media sessions, title tags — words starting with `!`.

The slice is performed after slice 6. The tasks are independent and can go as separate PRs in the
order 9.1 → 9.2 → 9.3 → 9.4.

### 9.1. Yandex Music control

Goal: the command that turns off personal music pauses playback in Yandex Music, and a separate
command switches to the next track.

Decisions:

- The control source is Windows Media Control
  (`GlobalSystemMediaTransportControlsSessionManager`); the session is selected by the Yandex Music
  application, not by the “currently active” session.
- No new NuGet package is needed: the project TFM `net10.0-windows10.0.22621.0` already provides the
  WinRT projections.
- If the Yandex Music session is not found or the application does not publish a media session, the
  OBS command is still executed, and the notification separately reports that the pause was not
  performed. This is not a command error and does not cancel the source toggle.
- Control is moved into a separate adapter `Integrations/Media` (`IMediaPlaybackControl`);
  `CommandDispatcher` works only with the interface.

Work:

1. Media session adapter: find the Yandex session by application name, `PauseAsync`,
   `NextTrackAsync`, and an availability flag. The calls are asynchronous, and WinRT errors are
   handled and not thrown into the UI without explanation.
2. Dispatcher: after a successful mute of `PersonalMusic`, call pause. When the source is unmuted,
   the pause is not released: resuming remains up to the user.
3. New intent `NextMusicTrack`: phrases `следующий трек`, `переключи трек`, `следующая песня`. It
   works through the same `VoiceCommandPipeline` as the other commands; no confirmation is required.
4. The notification shows both the actual source state and the result of the pause or track switch.

Readiness criteria:

- `выключи музыку` mutes `PersonalMusic` and pauses Yandex Music;
- `включи музыку` unmutes the source and does not start the music;
- `следующий трек` switches the track in Yandex Music;
- when there is no session, the commands do not crash, the OBS part is executed, and the reason is
  visible in the notification;
- unit tests cover session selection, the absence of a session, and notification texts;
- manual verification with Yandex Music running.

### 9.2. Recording and replay: both mkv and mp4

The wish is already implemented in `Recordings/FfmpegMp4CopyService`: an MP4 copy is created next to
the original OBS file without re-encoding (`-c copy`), and the original file stays in place.

The task is to lock in the behavior, not to change it:

- after a recording stop and a replay save, both files exist: the original `.mkv`/`.flv` and the
  `.mp4` copy with the same base name (if the name is taken — with the ` (2)` suffix);
- an ffmpeg error does not delete or rename the original file;
- when the setting is disabled, no copy is created;
- if OBS already writes `.mp4`, no copy is created;
- manual test 23 confirms both files on a real recording.

No code is required if the tests and documentation already match. Change the implementation only if
a discrepancy is found.

### 9.3. Tags in the Twitch title

Goal: when the stream title changes, the tags that are already in the current title are preserved.

Decisions:

- A tag is a word starting with `!` (`!tg`, `!exitlag`); case is ignored when comparing.
- Before confirmation, the current title is read from Twitch
  (`GET /channels?broadcaster_id=...`). Tags are appended to the end of the new title in the order
  they appear in the old one and without duplicates. If a tag is already present in the new title, it
  is not duplicated.
- The confirmation shows the final title in full: the user sees exactly what will go to Twitch.
- If the result is longer than 140 characters, the command is rejected with a clear message; the
  user's text is not truncated, and no external call is made.
- Reading the title requires no new scope: the existing token is used.

Work:

1. `ITwitchApiClient.GetChannelAsync` (the current channel title) and tests for parsing the response.
2. `TwitchTitleComposer` in `Commands` or `Integrations/Twitch`: tag extraction, assembly, and length
   validation. The logic is deterministic and covered by unit tests.
3. `TwitchCommandAdapter.PrepareAsync` for `SetStreamTitle` substitutes the final title before
   confirmation; `ExecuteAsync` sends the already validated value.
4. Update the confirmation and notification texts.

Readiness criteria:

- tags from the old title are preserved, and duplicates do not appear;
- a new title without tags receives the tags from the old one;
- tags already present in the new title keep their position;
- a result longer than 140 characters is rejected without a Twitch call;
- a title read error rejects the command and the title does not change;
- unit tests: extraction, case, duplicates, limit, no tags, empty title.

### 9.4. Viewer overlay

Goal: a small always-on-top window shows the viewer count of the current broadcast.

Decisions:

- WPF window `Overlay/ViewerCountOverlayWindow`: `Topmost`, no border, transparent background, not
  shown in the taskbar, and does not take focus; the position is saved in settings.
- Data — Helix `GET /streams?user_login=<login>`; with an empty `data` the window hides, because the
  broadcast is not running. No new scope is needed.
- Polling runs only when the overlay is enabled and Twitch is connected; the default interval is
  30 seconds, the minimum is 15 seconds.
- Network errors are not shown as notifications: the window keeps the last value and dims, so as not
  to spam during the stream.
- The overlay is not part of notifications and lives in the `Overlay` module; the window does not
  call Helix directly, and the data comes through the `IViewerCountProvider` port.

Work:

1. Port and adapter: `GetViewerCountAsync` → `(viewers, isLive)`; tests for parsing the response and
   the empty list.
2. A polling service with cancellation, pausing when Twitch disconnects, and resuming after a
   reconnect.
3. Window: large-font number, semi-transparent background, mouse dragging, position persistence, and
   a “Hide overlay” menu item. Showing it in OBS is done by capturing the window or the screen; the
   application does not create a separate OBS source.
4. A setting for enabling the overlay and the interval in the settings window.

Readiness criteria:

- on the stream the number updates without flicker and without taking focus;
- after the broadcast ends the window hides;
- an application restart restores the position;
- when Twitch is unavailable the application does not crash and does not present zeros as fact;
- unit tests for polling and the view model; manual verification with a real stream.

### 9.5. Common criteria for section 9

- `dotnet build` and `dotnet test` pass;
- manual tests 21–25 are completed;
- `AGENTS.md` and README are updated: new commands, settings, and dependencies.
