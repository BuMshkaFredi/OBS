# Architecture and Module Rules

> Topic document for coding agents. Read it before changing code in `src/`, adding files, or moving
> logic between modules. The mandatory constraints in the root `AGENTS.md` take precedence over this
> document.

## Overview

- Architectural style: Modular layered architecture with ports and adapters for external integrations.
- High-level description: UI/tray, audio capture, speech recognition, command parsing, and application orchestration must not depend on a specific OBS or Twitch implementation. OBS WebSocket, the Twitch API, Windows notifications, and the speech engine are plugged in through separate adapters.
- Main modules / bounded contexts: `App`, `Tray`, `Audio`, `Speech`, `Commands`, `Integrations/Obs`, `Integrations/Twitch`, `Integrations/Media`, `Configuration`, `Security`, `Notifications`, `Recordings`, `Overlay`.
- Main data flow: `Caps Lock down -> microphone capture -> Caps Lock up -> offline speech recognition -> command parsing -> optional confirmation -> command dispatch -> OBS/Twitch adapter -> state update -> Windows notification`.
- Second data flow: `continuous microphone capture -> segmentation by silence -> wake word «Бочи» -> command text -> offline speech recognition/parse -> optional confirmation -> dispatch -> notification`. The command after the wake word and push-to-talk use a single `VoiceCommandPipeline`.
- State management approach: keep OBS/Twitch runtime state in memory and periodically synchronize it with external systems; save settings locally; do not persist command history between runs.
- Integration boundaries: OBS WebSocket 5.x, Twitch OAuth/Helix API, Windows global hotkey/input, Windows toast notifications, local speech inference engine.
- Migration areas: None at the start. Add the future settings panel on top of the existing typed services and do not move business logic into the UI.

## Agreed Product Requirements

- Only Windows is supported. Target OBS Studio version: 32.2.2.
- The application is a separate desktop application and runs in the system tray. It starts together with Windows.
- Voice commands are in Russian only. Speech recognition is fully offline.
- Push-to-talk: hold the configured key or combination (`Caps Lock` by default) → speak → release. The
  application must not perform the normal Caps Lock toggle.
- Alongside push-to-talk, activation by the wake word «Бочи» (Bochi) works: the word can be said on its own (in which case the next phrase within 6 seconds is treated as a command) or immediately before the command. Background speech without the wake word is not executed.
- One recognition error is allowed in the wake word, but the first letter must remain «б» (b): «бачи» (bachi), «боци» (botsi), and «бочу» (bochu) trigger, while «ночи» (nochi) does not. Two errors are not allowed. The observed Vosk forms «мочи» and «больше/большие/большая/большим» are accepted only together with a command, because in one phrase the model hears «Бочи» that way; alone they do not open the waiting window.
- While the push-to-talk key is held down, wake-word listening is suspended and the current phrase is
  discarded so the command is not executed twice.
- Target processing time for a short command after the key is released: no more than 1–1.5 seconds, if possible on the target hardware.
- Target computers: Ryzen 5 5500H / 16 GB RAM and Ryzen 5 3600 / RTX 5060 / 16 GB RAM.
- Primary streaming platform: Twitch.
- Replay Buffer is enabled when connecting to OBS, but if the user has disabled it manually, the application does not re-enable it until restart.
- Replays are saved by OBS itself into the standard OBS folder.
- If OBS or Twitch is temporarily unavailable, the application must reconnect.
- Persistent command history is not required in the first version.
- User commands are edited in a future settings panel, not manually in JSON.
- When personal music is turned off, the Yandex Music app is paused through Windows media sessions; when it is turned back on, playback is resumed from the pause through the same session, and a separate voice command switches to the next track. If the player has no media session, the OBS action still runs and the notification reports that the pause or resume failed.
- The Twitch stream title is edited manually in the Twitch dashboard. The application changes only the stream category, after showing the exact found category and asking for confirmation; the category is re-read after the update so a silent failure is reported.
- The viewer count is shown in a separate always-on-top window; it does not take focus and is hidden while the stream is offline.
- Recordings and replays keep the original file and get an MP4 copy next to it; the original is never deleted or renamed.

## Architectural Rules

### Module boundaries

- Place phrase recognition and normalization logic in `Speech` and `Commands`, not in tray/UI.
- Place OBS logic in `Integrations/Obs` and Twitch logic in `Integrations/Twitch`; commands must not call the SDK or HTTP client directly.
- `CommandDispatcher` must work with integration abstractions and return an explicit operation result.
- Do not mix the global key hook, audio recording, and recognition in a single class.
- Before creating a new abstraction, reuse the existing contracts in the corresponding module.
- A connection error with OBS/Twitch must be handled by a retry/reconnect service, not by infinite loops in the UI.

### Voice phrase matching

- Phrasings for microphone, browser, personal music, and stream music are generated from verb lists and source lists (`AudioTargets`, `OffVerbs`, `OnVerbs`) rather than duplicated by hand: manual enumeration of combinations leaves gaps such as a missing «замуть браузер» (mute the browser).
- Matching a voice phrase to a command starts with an exact full match. Then a deterministic correction of an individual word against the dictionary of canonical words (`CommandVocabulary.Tokens`) is allowed: 1 error for a word of 4–5 letters, up to 2 for a word of 6–7 letters, up to 3 for a word of 8+ letters; short words are not corrected. Ambiguity is resolved by command, not by word: if all correction variants yield the same action, it is executed; if they differ, the phrase is rejected.
- If word corrections are insufficient, fuzzy matching of the whole phrase by Levenshtein distance is allowed: up to 4 edits; more than 4 (up to 6) — only when the first verb differs, the remaining words match exactly, and the command is unambiguous; merging or splitting adjacent words — only when the rest of the phrase matches exactly. One leading or trailing extra word of 4+ letters may be dropped if the rest of the phrase is unambiguous; trailing direction and intensity words (`потише` — a bit quieter, `громче` — louder, `тише` — quieter, `совсем` — completely) are not dropped, and a trailing canonical command word is not dropped either (otherwise «больше выключи музыку» lost its object and became «браузер выключи»). A missing canonical word, negation, different commands at the minimum distance, and dissimilar changed words reject the phrase. Opposite verbs (`включи`/`выключи` — turn on/turn off, `верни`/`заглуши` — restore/mute) are not substituted by fuzzy matching. OBS source names are not subject to these rules.

### Audio commands

- Real OBS source names are set by settings; do not hardcode `Mic/Aux`, `Desktop Audio`, or other names in code.
- Microphone commands («включи микрофон» — turn on the microphone, «выключи микрофон» — turn off the microphone, short «мут» — mute) mean one action — toggling the state, not choosing "enable" or "disable". The actual state is reported to the user by notification, because the word in the command does not predict the result. Personal music works the same way: «выключи музыку» (turn off the music) and «выключи мою музыку» (turn off my music) toggle the `PersonalMusic` source, and the notification reports the actual state.
- The browser keeps explicit mute/unmute. Stream music works the same way but is recognized only by the word «стрим» (stream): «выключи музыку стрима» (turn off the stream music), «включи звук стрима» (turn on the stream sound). A phrasing without «музыки»/«звука» (music/sound) remains broadcast control: «выключи стрим» (stop the stream) — stopping the stream.
- The command «ол мут» (all mute; variants «all mute», «выключи весь звук» — turn off all sound, «заглуши все» — mute everything) mutes all OBS audio sources, not only the configured roles. Video-only sources without audio are skipped on OBS error 604; a partial failure is listed in the notification. The command does not stop the stream or the recording.
- Volume is changed with the command «громкость <target> plus/minus N» (N — percentage points 0–100; without a number the step is 10, and the forms «громче»/«тише» equal plus/minus 10). A direction before the word «громкость» (volume) is allowed: «меньше громкость микрофоном» (lower the microphone volume), «больше громкость браузером» (raise the browser volume); one extra word may precede the command if the remaining tail is unambiguous. Targets are the same four roles; «громкость +10» without a target means the microphone. The result is clamped to 0–100%, no confirmation is required, and the notification shows the actual value. An error in the numeral is corrected only when there is an unambiguous nearest number; if ambiguous, the phrase is rejected. For persistent Vosk errors in volume commands, targeted substitutions `стрела → стрима` and `стал → сто` are allowed; they do not enable general fuzzy matching.

### Wake word and push-to-talk

- The wake word «Бочи» logic lives in `Speech/WakeWordSession`, not in `App`: `App` only passes the recognized text and receives the decision `Ignored`/`Armed`/`Command`.
- Push-to-talk takes priority over the wake word: while recording via the push-to-talk key, continuous listening is paused, the current phrase is discarded, and the «Бочи» waiting window closes.
- Both modes use a single `ISpeechRecognizer` instance; Vosk calls are serialized because the engine is not designed for parallel recognition.

### Media, overlay, and Twitch

- Yandex Music control lives in `Integrations/Media` behind `IMediaPlaybackControl`; the dispatcher never calls WinRT directly, and a missing media session must not cancel the OBS action. Pausing happens when personal music becomes muted and resuming when it becomes audible. The player session is selected by an `AppId` containing `yandex` or `яндекс`, preferring a playing session; the next-track intent is `NextMusicTrack` with the phrases «следующий трек», «следующая песня», «переключи трек», «переключить трек», «смени трек», «сменить трек».
- The viewer count lives in `Overlay` and is read through `IViewerCountProvider`; the overlay window never calls Twitch directly, polling stops when Twitch is disconnected or the overlay is disabled, and network errors do not produce toast spam. The overlay is disabled by default, is toggled by the «Счётчик зрителей» tray item or the settings window, polls every 30 seconds by default (minimum 15), hides while offline, and keeps the last value dimmed to 45% on a network error.
- The Twitch stream title is not changed by voice: the customer edits it manually. Do not reintroduce title phrases, title confirmation, or title composition.
- For Twitch categories, first retrieve and verify the exact `game_id`, then show the result to the user and require confirmation. The first Helix search result is used, the exact name is shown before confirmation; after a rejection or the notification being closed, no external call is made. The spoken name may be Russian: for known games a deterministic alias table (`TwitchCategoryAliases`) gives the Latin search name directly, otherwise the search tries the original query, a deterministic transliteration, and the transliteration with numerals written as digits. After the update the category is re-read and a mismatch is reported instead of being treated as success.
- OAuth, scope, and token rules are mandatory and listed in the root `AGENTS.md`; details are in `docs/integrations/twitch.md`.
- When integrations change, update the OBS source map and the documented Twitch scopes.

The step-by-step implementation order and readiness criteria are in `docs/MVP_PLAN.md`. Until a separate confirmation timeout is agreed, do not add automatic time-based cancellation without configuration and tests.

## Repository Structure

```text
repo-root/
├─ AGENTS.md                         # Mandatory rules for coding agents and topic index
├─ README.md                         # Installation, OBS/Twitch setup, and launch
├─ .gitignore                        # Windows, IDE, build artifacts, and secrets
├─ src/
│  └─ ObsVoiceAssistant/
│     ├─ App/                        # Composition root and application lifecycle
│     ├─ Tray/                       # Tray icon, menu, and application status
│     ├─ Audio/                      # Push-to-talk key hook and microphone capture
│     ├─ Speech/                     # Local model and speech recognition
│     ├─ Commands/                   # Intents, parser, dispatcher, confirmations
│     ├─ Integrations/
│     │  ├─ Media/                   # Windows media session control (Yandex Music)
│     │  ├─ Obs/                     # OBS WebSocket adapter and state sync
│     │  └─ Twitch/                  # OAuth and Twitch Helix adapter
│     ├─ Configuration/              # Typed settings and their persistence
│     ├─ Security/                   # DPAPI/Credential Manager wrappers
│     ├─ Notifications/              # Windows toast and notification panel
│     ├─ Overlay/                    # Viewer count overlay window
│     └─ Recordings/                 # Recording and replay file names
├─ tests/
│  ├─ ObsVoiceAssistant.UnitTests/   # Fast tests for parser and domain/application logic
│  ├─ ObsVoiceAssistant.IntegrationTests/ # OBS/Twitch boundary tests with mocks/test doubles
│  └─ CustomerMail.UnitTests/        # Mail channel policy and validators
├─ models/                           # Local speech models; do not commit large models without a decision
├─ tools/                            # Helper utilities, including CustomerMail for customer emails
├─ docs/                             # Agent topic docs, ADRs, protocols, troubleshooting, manual test plans
└─ installer/                        # Packaging/installer scripts, if they are added
```

### Directory Responsibilities

| Path | Responsibility | Typical contents | What must not be there |
| --- | --- | --- | --- |
| `src/ObsVoiceAssistant/App` | Application startup and composition | composition root, single-instance guard | command business logic and HTTP code |
| `src/ObsVoiceAssistant/Tray` | System tray | tray icon, menu, status view model | direct OBS/Twitch calls |
| `src/ObsVoiceAssistant/Audio` | Push-to-talk, continuous listening, and audio capture | global key handling, microphone buffer, speech segmentation by pauses | parser and integration code |
| `src/ObsVoiceAssistant/Speech` | Local recognition | engine wrapper, model manager, recognition result | OAuth, OBS settings |
| `src/ObsVoiceAssistant/Commands` | Turning speech into action | intent, parser, dispatcher, confirmation policy | platform-specific HTTP/SDK details |
| `src/ObsVoiceAssistant/Integrations` | External adapters | OBS WebSocket, Twitch API/OAuth, Windows media sessions | UI and storage of arbitrary secrets |
| `src/ObsVoiceAssistant/Configuration` | Application settings | typed options, settings service | access tokens in plain text |
| `src/ObsVoiceAssistant/Recordings` | Names of finished recording and replay files, MP4 copy | name entry window, renaming the OBS file, creating an MP4 copy via ffmpeg | direct OBS WebSocket calls and UI outside the dispatcher |
| `src/ObsVoiceAssistant/Overlay` | Viewer count overlay | topmost WPF window, view model, saved position | direct Twitch HTTP calls and command business logic |
| `tests` | Automated checks | unit/integration tests | real production secrets |
| `models` | Speech model files | local models and metadata | secrets, logs, temporary files |

### File Placement Rules

- Place new features in the corresponding `src/ObsVoiceAssistant` module, not in `App` or `Tray` merely for convenience.
- Place shared reusable code in the narrowest module that truly owns it; do not create a shared `Utils` without two real use cases.
- Keep one-off scripts in `tools/` and document their purpose.
- Keep generated artifacts and publish output in `bin/`, `obj/`, `publish/` outside git; models by a separate decision; do not commit large binary files by default.
- Store application configuration through typed settings; user configuration templates in `docs` or `config.example.*`, without secrets.
- Store integration contracts and manual protocols in `docs/integrations/`.
- Documentation is kept in English only. Do not add or restore Russian duplicates with the `.ru.md` suffix next to the English files.

## Data, Contracts, Codegen, and Migrations

- Where the schemas live: typed C# options/records in `Configuration` and command contracts in `Commands`; add separate schemas only when necessary.
- Where the migrations live: none in the first version; no persistent database is used.
- Where the API contracts live: adapters and DTOs in `Integrations/Obs` and `Integrations/Twitch`; external specifications in `docs/integrations/`.
- Where the event contracts live: internal command/result records in `Commands`; no separate event bus is used.
- Where the generated code lives: not used.
- Regeneration command: not required.
