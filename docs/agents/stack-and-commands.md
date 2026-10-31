# Stack, Environment, Commands, and Code Style

> Topic document for coding agents. Read it before building, running, publishing, changing
> dependencies or project files, or when you need a command or style convention. The mandatory
> constraints in the root `AGENTS.md` take precedence over this document.

## Core Stack

- Language(s): C# 12
- Runtime(s): .NET 10.0.12 (SDK 10.0.401; target framework `net10.0-windows`)
- Framework(s): WPF
- Package manager(s): NuGet via the `dotnet` CLI
- Build tool(s): `dotnet build`, `dotnet publish`
- Database(s): none in the first version; settings are stored in typed configuration, and no persistent history is stored
- Messaging / queueing: none
- Cache / storage: local application settings; Windows DPAPI for sensitive tokens
- Hosting / infrastructure: local Windows computer; OBS Studio 32.2.2; Twitch API

## Key Libraries and Services

| Area | Library / service | Version | Purpose | Notes / limitations |
| --- | --- | --- | --- | --- |
| Desktop UI | WPF | Version follows the chosen .NET runtime | System tray and the future settings panel | Do not add another UI framework without a separate decision |
| OBS integration | `obs-websocket-dotnet` | 5.7.0 | Typed OBS WebSocket 5.x client for controlling recording, streaming, Replay Buffer, and inputs | Supports `net10.0`; verify the actual connection with OBS Studio 32.2.2 |
| Audio capture | `NAudio` | 3.2.0 | Microphone audio capture via Windows audio input | Use short recordings; the device and capture errors must be diagnosable |
| Speech recognition | `Vosk` | 0.3.38 | Local Russian speech recognition engine | CPU, no GPU; the only supported model is `vosk-model-small-ru-0.22` (~45 MB), downloaded separately into `models/`; larger Russian models load for minutes and take gigabytes of RAM, so they are not supported |
| Notifications | Windows toast notifications | Pin the API/package at implementation time | Notifications and confirmation buttons | Do not perform confirmation actions without an explicit button press |
| Streaming platform | Twitch Helix API | The API version current at implementation time | Changing the Twitch category; reading the live viewer count | Do not store OAuth tokens in plain text |
| Media playback | Windows Media Control (WinRT) | Windows SDK projection from the `net10.0-windows10.0.22621.0` target | Pause and switch tracks in Yandex Music | No NuGet package; works only when the player publishes a Windows media session, and a missing session must not fail the OBS command |
| Secrets | Windows DPAPI / Credential Manager | Windows API | Secure storage of Twitch and OBS credentials | Do not put tokens, passwords, or keys into source code or plain JSON |
| Video conversion | ffmpeg (external program) | Version from PATH or the path in settings | MP4 copy of a recording and a replay without re-encoding | Not a NuGet dependency: the path is set in settings, the copy is made with the `-c copy` command; without ffmpeg no copy is created and a notification is shown |

## Version Policy

- Mandatory versions: Windows; OBS Studio 32.2.2; .NET 10.0.12; SDK 10.0.401; C# 12; target framework `net10.0-windows`; NuGet package versions must be explicitly pinned in `.csproj` and restore metadata.
- Source of truth for versions: `.csproj`, `Directory.Build.props`, `Directory.Packages.props` once it appears, as well as the OBS WebSocket compatibility documentation.
- Dependency update policy: manually, after compatibility checks and tests.
- `System.Drawing.Common` comes transitively from `CommunityToolkit.WinUI.Notifications` version 4.7.0 with the critical vulnerability NU1904 (GHSA-rxg9-xrhp-64gj). The version is pinned explicitly in the test projects (`10.0.12`, matching the runtime); in the application the package is replaced by the `Microsoft.WindowsDesktop.App` assembly. After changing dependencies, run `dotnet list package --vulnerable --include-transitive`.
- Compatibility requirements: the application must run on the target configurations Ryzen 5 5500H/16 GB RAM and Ryzen 5 3600/RTX 5060/16 GB RAM; CPU recognition mode is mandatory, and GPU acceleration must not be mandatory.

## Environment Setup

- Mandatory tools: Windows, the .NET SDK of the chosen supported version, OBS Studio 32.2.2.
- Dependency installation: `dotnet restore`.
- Starting the local environment: `dotnet run --project src/ObsVoiceAssistant/ObsVoiceAssistant.csproj`.
- Starting only dependent services: launch OBS Studio manually and enable OBS WebSocket; no separate local services are required.
- Data seed / bootstrap: not required; on first launch, configure the OBS connection and sources.
- Where to load env variables from: secrets must not be loaded from `.env`; store local credentials via Windows DPAPI/Credential Manager.
- Required local services: OBS Studio 32.2.2 for integration/manual tests; Twitch OAuth for Twitch tests.
- Start OBS first and configure WebSocket, then run the application's integration/manual checks.
- Python, Node.js, and Docker must not be mandatory for building or running the application.
- The installer and self-contained publish must include the runtime and the required native speech libraries; the model must be a separate, explicitly verifiable artifact.
- Real Twitch credentials and the OBS password are entered manually through the UI and do not end up in git.

## Development Commands

| Task | Command | Scope | Notes |
| --- | --- | --- | --- |
| Install dependencies | `dotnet restore` | repo | |
| Start development | `dotnet run --project src/ObsVoiceAssistant/ObsVoiceAssistant.csproj` | app | Requires OBS only for integration scenarios |
| Build | `dotnet build --configuration Release` | repo | |
| Publish self-contained | `dotnet publish src/ObsVoiceAssistant/ObsVoiceAssistant.csproj --configuration Release --runtime win-x64 --self-contained true` | app | The final runtime RID must be confirmed |
| Lint | Not assigned | repo | Do not add a tool without a need |
| Format | `dotnet format` | repo | |
| Typecheck | `dotnet build --no-restore` | repo | The C# compiler performs type checking |
| Vulnerability check | `dotnet list package --vulnerable --include-transitive` | repo | After any dependency change |
| Run all tests | `dotnet test` | repo | |
| Run tests by filter | `dotnet test tests/ObsVoiceAssistant.UnitTests --filter "FullyQualifiedName~<ClassName>"` | tests | xUnit filter syntax |
| Run integration tests | `dotnet test tests/ObsVoiceAssistant.IntegrationTests` | tests | Requires running OBS and isolated credentials |
| Run e2e tests | Not provided in the first version | repo | Use the manual test plan for Windows notifications and the global hotkey |
| Regenerate code | Not required | repo | Do not add codegen without a real need |

Customer mail commands are documented in `docs/agents/customer-mail.md`.

Do not consider a command that depends on OBS or Twitch verified until it has actually been run
against them.

## Code Style and Naming

- Formatter: `dotnet format`.
- Linter: standard .NET analyzers; add additional analyzers only after a decision and a fixed version.
- Typing policy: strict static typing in C#; enable nullable reference types.
- Comment policy: comments only for non-obvious decisions, external API limitations, and security reasons; do not comment obvious code.
- Import policy: regular `using` directives at the top of the file, grouped according to the chosen formatter's conventions; do not use global using unless it benefits readability.
- Error handling approach: return expected errors through an explicit result/operation type or a typed exception boundary; show a safe message in the UI and diagnostic context without secrets in the logs.
- Logging approach: structured Debug/Information/Warning/Error levels; do not log audio, OAuth tokens, OBS passwords, or full sensitive payloads.
- Configuration approach: typed options and a single settings service; secrets only via Windows DPAPI/Credential Manager.
- Naming: PascalCase for types and public members, camelCase for local variables and parameters, interfaces with the `I` prefix, async methods with the `Async` suffix.

## Git and Release Conventions

- Branch naming scheme: `feat/<short-description>`, `fix/<short-description>`, `chore/<short-description>`.
- Commit message format: Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `chore:`).
- PR title format: a Conventional Commit summary with a brief description of the user-facing effect.
- Changelog policy: add an entry for user-facing changes once `CHANGELOG.md` appears; do not create it solely for internal refactoring.
- Release notes policy: describe changes in OBS/Twitch behavior, runtime/model requirements, and breaking changes.
