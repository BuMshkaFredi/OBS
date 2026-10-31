# Testing Guide

> Topic document for coding agents. Read it before adding or running tests, or when deciding which
> checks a change needs. The mandatory constraints and the Definition of Done in the root
> `AGENTS.md` take precedence over this document.

- Test framework(s): xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4.
- Where the unit tests live: `tests/ObsVoiceAssistant.UnitTests/`.
- Where the integration tests live: `tests/ObsVoiceAssistant.IntegrationTests/`.
- Where the e2e tests live: not provided in the first version; store manual scenarios in `docs/testing/`.
- Where the contract tests live: `tests/ObsVoiceAssistant.IntegrationTests/` for OBS/Twitch boundary checks, if they are needed.
- Naming patterns: `<ClassName>Tests.cs`, methods descriptive, for example `StartRecording_WhenObsIsConnected_SendsStartRecordRequest`.
- Where the CI workflow lives: currently absent; add it after a stable build/test pipeline appears.

## Test Matrix

| Test type | Path / Scope | Command | When to run |
| --- | --- | --- | --- |
| Unit | `tests/ObsVoiceAssistant.UnitTests/` | `dotnet test tests/ObsVoiceAssistant.UnitTests` | When changing the parser, dispatcher, confirmation policy, or configuration logic |
| Integration | `tests/ObsVoiceAssistant.IntegrationTests/` | `dotnet test tests/ObsVoiceAssistant.IntegrationTests` | When changing OBS/Twitch adapters and reconnect behavior |
| E2E | `docs/testing/` manual plan | Not automated in the first version | When changing the hotkey, tray, notifications, and startup |
| Contract | `tests/ObsVoiceAssistant.IntegrationTests/` | To be determined together with the adapter tests | When changing external API contracts |
| Performance | `docs/testing/` benchmark plan | To be determined | When changing the model, audio pipeline, or inference settings |
| Unit (mail channel) | `tests/CustomerMail.UnitTests/` | `dotnet test tests/CustomerMail.UnitTests` | When changing the policy, address PIN, batch validator, release validator, or content filter |

When changing the Windows hotkey, tray, notifications, or startup, add or update the manual test plan
in `docs/testing/`.

## Mandatory MVP Scenarios

- The configured push-to-talk key (Caps Lock by default) down/up correctly bounds the recording; the Caps Lock state never toggles.
- «Бочи» alone opens a waiting window, while «Бочи, начни запись» (Bochi, start recording) executes the command immediately; background speech without the wake word executes nothing.
- During push-to-talk recording, continuous listening is paused: the same command is not executed twice.
- A short Russian command is recognized offline.
- `начни запись` (start recording), `останови запись` (stop recording), `начни стрим` (start stream), `сохрани повтор` (save replay) invoke the correct OBS requests.
- Replay Buffer is enabled after connecting to OBS but is not re-enabled if the user has disabled it manually.
- Toggling the microphone and personal music, and mute/unmute of the browser and stream music, work only with the source selected by the user.
- «Мут» toggles the microphone, «ол мут» mutes all OBS audio sources, skips video-only sources without audio, and does not stop the stream or recording.
- Starting and stopping a stream, stopping a recording, and changing the Twitch category require button confirmation.
- Turning personal music off pauses Yandex Music, turning it back on resumes playback from the pause, and a missing media session never cancels the OBS action.
- A custom push-to-talk combination such as Ctrl+Shift+F9 works when the user presses it on a second keyboard or a macro key, and a single key trigger does not break that key in other applications.
- On an OBS/Twitch disconnection, the connection is restored without the application crashing.
- Self-contained publish runs on Windows without Python installed.
