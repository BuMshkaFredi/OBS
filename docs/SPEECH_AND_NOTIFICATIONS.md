# Local Russian speech recognition

## Recognition engine

There is one engine — **Vosk** (`Speech/VoskSpeechRecognizer.cs`), and it runs fully locally behind
the `Speech/ISpeechRecognizer.cs` interface. Audio is never sent over the network.

- designed for CPU: the small Russian model takes about 45 MB;
- a short command is processed in fractions of a second, no GPU is required;
- chosen because Whisper on the target laptop (Ryzen 5 5600H, integrated AMD Radeon only) took
  28–48 seconds per segment, which is incompatible with the target of 1–1.5 seconds.

The engine choice is written to the log with the line `Движок распознавания: ...`, and the time of
each attempt with the line `Распознавание заняло ... мс`.

A second engine is deliberately not connected: it would pull a second model and a second native
runtime and create uncertainty about what actually works. If the model directory is not found,
recognition is disabled and the application stays in the tray with the status
`нет модели распознавания` (no recognition model).

## Vosk model

Model directories in `models/` are not included in the repository (`models/*` in `.gitignore`).
The application tries them in priority order: the larger the model, the higher the accuracy and the
lower the speed.

| Priority | Directory | Size | WER: books / YouTube / devices |
| --- | --- | --- | --- |
| 1 | `vosk-model-ru-0.42` | 1.8 GB | 4.5 / 19.5 / 17.9 |
| 2 | `vosk-model-ru-0.22` | 1.5 GB | 5.7 / 20.7 / 19.7 |
| 3 | `vosk-model-small-ru-0.22` | 45 MB | 22.7 / 32.0 / 29.9 |

The search follows the priority list and, within it, looks next to the application, upward through the
directory tree, and in the current directory. Therefore a model in the repository root takes priority
over a copy in `bin`. The actually selected directory and the load time are written to the log with
the line `Модель распознавания: ... (Vosk), загрузка заняла ... мс`.

If the model directory is missing, the application writes an error to the log listing the expected
directories, disables recognition, and shows the status `нет модели распознавания`.

Installing the model:

```powershell
# Resume in a loop: the connection to alphacephei.com drops on large files.
$url = 'https://alphacephei.com/vosk/models/vosk-model-ru-0.42.zip'
$out = '.\models\vosk-model-ru-0.42.zip'
for ($i = 1; $i -le 500; $i++) {
  curl.exe -C - -L --retry 5 -o $out $url
  if ((Get-Item $out).Length -ge 1937602113) { break }
}
Expand-Archive -Path $out -DestinationPath '.\models' -Force
```

Compatibility: the NuGet package `Vosk` ends at version `0.3.38`, so only Kaldi-format models from
this list are suitable. Zipformer2 models from HuggingFace (`alphacep/vosk-model-small-ru`,
`alphacep/vosk-model-small-streaming-ru`) require Vosk 0.54+ and will not load.

The native libraries `libvosk.dll` and its dependencies are supplied by the `Vosk` package version
`0.3.38`.

## Command vocabulary and recognition robustness

- The phrasing dictionary and canonical words are stored in one place —
  `Commands/CommandVocabulary.cs`. The list is deliberately wide (direct and reverse word order) so
  that the user does not have to adapt speech to the application.
- First, the phrase is compared as a whole with the dictionary (after normalizing case, `ё→е`, and
  punctuation). An exact match is the primary and safest path.
- Then a deterministic correction of a **single word** against the dictionary of canonical words is
  allowed: a word of 4–5 letters — at most 1 error, 6–7 letters — at most 2, 8+ letters — at most 3;
  words of 3 letters and shorter are not corrected. This turns «остонови запись» into
  «останови запись», and «выключи бразуер» into «выключи браузер».
- If correcting one word is not enough, a character-by-character comparison of the whole phrase is
  enabled (Levenshtein distance). It catches transpositions, merges, and strong distortions:
  «нашли запись» → «начни запись», «начни с три» → «начни стрим», «основании запись» →
  «останови запись». The limits are:
  - up to 4 edits for the whole phrase;
  - more than 4 (up to 6) edits — only if the first verb differs, all other words match exactly, and
    the command is unambiguous («основании запись»);
  - one leading or trailing extra word of 4+ letters may be dropped if the rest of the phrase is
    recognized unambiguously: «блин включи микрофон», «начни запись видео». Direction and intensity
    words (`потише`, `громче`, `тише`, `совсем`, `меньше`, `больше`) are not dropped, so
    «включи музыку потише» stays unrecognized;
  - merging or splitting adjacent words is allowed only when the rest of the phrase matches exactly:
    «с три» → «стрим», «навстречу» → «на стриме»;
  - the phrase must contain at least one canonical word, and a negation rejects the whole phrase;
  - changed words must be similar: the first verb — at least 5 letters or one letter different, the
    others — must share a common three-letter fragment. Therefore «выключи окно» does not become
    «выключи микро», and «это запись» does not become «стоп запись».
- The decision is made by the meaning of the command, not by a word: the parser iterates over all
  allowed corrections and executes the action only if all variants produce the same command.
  «Запису» is equally close to «записи» and «запись», but «останови запись» is one command, so
  «останови запису» is executed.
- Fuzzy matching does not flip the verb direction: «включи весь звук» does not become
  «выключи весь звук», even if the rest of the phrase matches. Opposite verbs
  (`включи`/`выключи`, `верни`/`заглуши`) are excluded from the candidates before the command is
  chosen.
- If the variants produce different commands, the phrase is rejected as a whole. «Ыключи браузер» is
  equally close to «включи браузер» and «выключи браузер», and these are opposite actions, so the
  browser is not toggled. It is better not to execute a command than to execute the opposite one.
- The microphone is an exception to the previous point: `включи микрофон` and `выключи микрофон`
  mean the same action `ToggleInput`. The word in the command does not choose the state, so the
  ambiguity is safe here: any variant simply toggles the microphone. The short form `мут` (as well as
  `муть` and `мьют`) does the same.
- The toggle is performed by OBS itself (`ToggleInputMute`), so there is no “read state — write the
  opposite” race. The new state is returned to the application and shown to the user with the
  notification `Микрофон выключен` / `Микрофон включён`.
- Personal music works like the microphone: `выключи музыку`, `выключи мою музыку`,
  `включи личную музыку` toggle the `PersonalMusic` source. The word in the command does not predict
  the result, so the notification shows the actual state: `Личная музыка выключена` /
  `Личная музыка включена`.
- Stream music works like the browser: `выключи музыку стрима`, `включи звук стрима` are explicit
  mute/unmute of the `StreamMusic` source. The key word is “стрим”: without “музыки” or “звука”,
  `выключи стрим` remains stopping the broadcast, and `выключи музыку` refers to personal music.
- `ол мут` (as well as `all mute`, `выключи весь звук`, `заглуши все`) mutes every OBS audio source,
  not only the four configured roles. Video-only sources are skipped: OBS answers them with error
  604, and this is not considered a failure. The notification shows the number of muted sources, and
  if some sources could not be muted — their names.
- Filler words (`пожалуйста`, `давай`, `ну`, `сейчас`, and similar) are removed before matching.
- A phrase with a negation (`не`, `ни`, `нельзя`) is not executed at all, so «не начни запись» does
  not become «начни запись».
- If a correction worked, the log gets `Исправлено распознавание: «остонови»→«останови»`. For a
  whole-phrase correction, the full pair is written: `«нашли запись»→«начни запись»`. The original
  text also stays in the log with the line `Vosk распознал: ...`.
- OBS source names are **not** subject to corrections: they are taken only from settings and must
  match OBS exactly.
- The single word `стрим` performs no action: this protects against accidentally starting or
  stopping the broadcast.
- Vosk does not guess commands: on silence and music it produces no text, and random words are cut
  off by the threshold and parser checks before any OBS/Twitch call. Verified: 2 seconds of silence
  produce the empty result `{"text": ""}`; on a real log of 313 unrecognized phrases, fuzzy matching
  added no false positives on background speech.
- Dangerous commands still require confirmation through Windows Toast.

## Volume

The volume command has a separate form because the number and the target vary:

- `громкость микрофона плюс 10`, `громкость музыки минус 5`,
  `громкость браузера плюс пятнадцать`;
- `громкость музыки стрима плюс 10`, `громкость звука стрима минус 5`,
  `громкость стрима минус 10`;
- `громкость +10` / `громкость -5` without a target — the microphone;
- `громкость музыки плюс` or `громкость музыки тише` without a number — step 10;
- `меньше громкость микрофоном`, `тише громкость музыки`, `больше громкость браузером` — a
  direction before the word `громкость`, without a number the step is 10;
- an extra word before the command does not interfere: `чем громкость плюс десять` — microphone +10.

The number can be spoken as digits or words, with or without `процентов`. A numeral error is
corrected only if the nearest number is unambiguous: «десеть» is 10, while a word equally close to
«десять» and «девять» rejects the phrase. For persistent Vosk errors, targeted substitutions are
added: `стрела → стрима` and `стал → сто`. They apply only in volume commands and do not enable
general fuzzy matching. The range is 0–100%: at 95% the command `плюс 10` gives 100%, at 3% the
command `минус 10` gives 0%. There are no absolute values (`громкость микрофона 50`) and no
arbitrary sources by name: the target is one of the four roles, and “музыка” without a qualifier
means personal music. The command requires no confirmation, and the notification shows the actual
value: `Громкость микрофона 60%`.

## How to add a new phrasing

An unrecognized phrase is not lost: the phrase itself and the nearest dictionary phrase stay in the
log.

```text
Команда не распознана: «стоп записи» (ближайшая фраза: «стоп запись» → StopRecording).
```

The log does not print a ready-made `new(...)` line. For short and ambiguous words, a human makes the
decision: «мод» or «не» must not automatically become commands just because they are close to «мут».

Procedure:

1. Open `src/ObsVoiceAssistant/Commands/CommandVocabulary.cs`.
2. For the microphone, browser, personal music, and stream music, phrases are not listed by hand:
   they are assembled from `AudioTargets` (sources) and `OffVerbs`/`OnVerbs` (verbs), in direct and
   reverse order. A new verb is one line in the list, and it immediately works with all sources: that
   is why `замуть браузер` needs no separate line.
3. If the form does not reduce to “verb + source” (for example, `выключи звук микрофона`), add it to
   `ExplicitPhrases` with a third source argument:
   `new("выключи звук браузера", CommandIntent.MuteInput, "browser")`.
   Mass commands such as `ол мут` are added there without a source:
   `new("ол мут", CommandIntent.MuteAllAudio)`. Actual Vosk distortions are also added there: for
   example, `new("начни стареем", CommandIntent.StartStream)`.
4. Rebuild and restart. `Tokens` is built from `Phrases` automatically.

The “nearest phrase” hint in the log line is only a hint for a human: it is not executed by itself.
Only phrases that pass the limited fuzzy threshold above are executed automatically; a human still
chooses the new phrasing.

The test `Vocabulary_DoesNotContainConflictingPhrases` fails if the same phrase is assigned to two
different actions or two different sources.

## Speech gate and pause-based segmentation

`Audio/SpeechActivityDetector.cs` computes the energy of 16-bit PCM and decides whether there is
speech. Two things are built on this:

1. **Segmentation of the recording by speech** (`Audio/ContinuousListeningService.cs`): the
   application waits for the start of a phrase and then ends the recording on a pause. A phrase is
   not cut by a fixed-segment boundary and goes to recognition right after speech ends.
   Defaults: check every 60 ms, end-of-phrase pause 1000 ms, maximum speech 12 s, minimum speech
   length 150 ms, wait for speech start 4 s. The pause and the upper bound are increased
   deliberately: a lost command costs more than a few tenths of a second of latency.
2. **Final gate** (`Speech/VoiceCommandPipeline.cs`): if the recording still contains no speech,
   recognition is skipped — the log gets `Запись без речи: распознавание пропущено`. The gate
   remains a safety net and saves CPU on silence. The loudest part of the recording is compared, not
   the average level: a long pause before a phrase no longer “blurs” a quiet command.

The RMS threshold is 200. It is deliberately low so as not to swallow quiet speech: a false positive
is safe, because the exact parser will not execute a command from random words.

Segment processing runs separately from recording: while the previous command is being recognized,
the microphone keeps listening, so a phrase said right after the previous one is not lost. Commands
are executed strictly one at a time.

### Losing a command: what you see in the log

Previously, unclear text was discarded silently. Now every such case has a line:

| Situation | Log line |
| --- | --- |
| There was speech, but the engine produced no text | `Речь была, но текст не распознан` + the compact panel `Не расслышал команду` |
| In continuous “Бочи” listening there was a segment, but no text | `Vosk не распознал сегмент «Бочи»: <длительность> мс, пиковый RMS <значение>, байт <размер>` |
| Text was received but did not match a command | `Команда не распознана: «...»` + the nearest phrase, if any; the compact panel `Команда не распознана` |
| The command was recognized, but OBS/Twitch returned an error | `Ошибка выполнения ...` + a notification panel without an icon |

The second case is the main source of “I said it, and nothing happened”. The actual phrase and the
nearest dictionary one stay in the log so that the dictionary can be extended from real data rather
than guesses. The dictionary does not change automatically: short and ambiguous variants are checked
and added by a human.

For continuous “Бочи” listening, an empty result is now also visible in the log: duration, peak RMS,
and segment size. This helps distinguish noise from missed speech: if the RMS is noticeably above the
threshold of 200 and the duration matches a phrase, the segment is worth checking and, if necessary,
repeating the command.

Note: loud music from speakers passes the energy gate. Therefore, when debugging, it is better to
pause the music.

## Activation modes: Caps Lock and the wake word “Бочи”

Both modes work simultaneously and independently. Each uses its own recording stream from the default
microphone; Windows allows several simultaneous recordings in shared mode. Verified on the target
machine: two `WaveInEvent` instances received about 65 KB of data each in 2 seconds.

Why not one shared stream: the existing `IAudioRecorder` keeps one buffer and clears it in `Stop()`.
Push-to-talk stops recording when the key is released, while continuous listening stops on a pause in
speech. With a shared instance, `Stop()` from one mode would cut the phrase of the other, and
`Start()` during an already running recording would do nothing. Two independent streams provide
isolation: a pause or an error in one mode does not break the other. If a single stream is later
required, a separate capture service is needed that records continuously and distributes segments to
subscribers; that is a separate refactoring, not a setting.

### Push-to-talk

- The application records the microphone while `Caps Lock` is held.
- `Caps Lock down` starts recording, `Caps Lock up` stops it and starts recognition.
- The normal Caps Lock state toggle is not performed while the application is running.
- Recordings are recognized sequentially so that Vosk is not called in parallel.

### Wake word “Бочи”

- The second stream listens to the microphone continuously and splits speech by pauses with the same
  `ContinuousListeningService` as before.
- “Бочи” as a separate phrase opens a 6-second waiting window and shows the compact panel `Слушаю`.
  The next phrase within this window is considered a command.
- The wake word is recognized only at the start of a phrase; filler words such as `ну` or `давай`
  are allowed before it. This protects against false triggers on the ordinary words `бочка`,
  `бочки`, `бочке` in the middle of background speech.
- “Бочи, включи микрофон” executes the command immediately, without a separate window.
- Background speech without the wake word is not executed and shows no notifications: only the line
  `Без ключевого слова, пропущено: «...»` is written to the log.
- The waiting window closes after the command is executed, after 6 seconds, or when `Caps Lock` is
  pressed: a button command cancels the wait.
- While `Caps Lock` is held, continuous listening is paused and the current phrase is discarded.
  Therefore the same speech is not executed twice.
- Vosk hears “Бочи” differently. The exact variants are listed in `Speech/WakeWordSession.cs`
  (`SpokenForms`): `бочи`, `боче`, `бочий`, `бочии`, `бочь`, `боч`, `бочка`, `бочки`, `бочке`,
  `бочек`, `боже`. Besides an exact match, **one** recognition error (substitution, insertion, or
  deletion of a character) is allowed, but the first letter must remain `б`: therefore `бачи`,
  `боци`, `бочу`, `бочку` are recognized, while `ночи` is not. Two errors are not allowed. New exact
  variants are added from the `Без ключевого слова, пропущено: «...»` lines in the log. “Боже” is an
  ordinary interjection, so it is accepted only at the start of a phrase; if false triggers are
  frequent, it can be removed.
- Recognition runs for every phrase, so loud background speech consumes CPU. This is the price of
  activation without a separate keyword-spotting engine; if necessary, the next step can be to limit
  listening by volume or by a Vosk grammar.

## Windows Toast Notifications

Dangerous commands (`начни стрим`, `останови запись`, `останови стрим`, changing the Twitch title or
category) show a Windows Toast with two actions: `Подтвердить` and `Отмена`. The command is sent to
OBS/Twitch only after `Подтвердить`; closing the notification, an error, or `Отмена` results in a
refusal. For a category, a Twitch search is performed before the notification is shown; the
confirmation text shows the exact name of the first result. The title and category receive a separate
informational notification after a successful change. Twitch setup is described in
[`integrations/twitch.md`](integrations/twitch.md).

Separately from confirmations, there are informational notifications. They are needed where the
result cannot be understood from the command itself: a microphone toggle shows the actual state
(`Микрофон выключен` / `Микрофон включён`), `ол мут` shows how many sources were muted, and an
execution error shows the reason. Starting a recording is confirmed by a short notification
`Запись началась`, because it runs without confirmation. All such messages are shown by a panel in
the lower-right corner: dark, rounded, without an icon, without taking focus, closed by a click. The
regular panel is 380 wide, has a minimum height of 56, wraps long text, and is shown for 4 seconds.
If a phrase is not recognized or the text is empty, the compact panel `Команда не распознана` or
`Не расслышал команду` is used: 260×44, one second. The application, not Windows, sets the size, so
notifications do not inflate to balloon size and do not show a warning icon.

Windows Toast does not support text input, so the file name is requested by a separate small window.
It opens after a recording is stopped and after a replay is saved — including when the action was
performed manually in OBS. The entered text **replaces** the automatic OBS name entirely
(`Мой клип.mkv` instead of `2026-10-20 18-16-32.mkv`); an empty field, `Esc`, or closing the window
leaves the OBS name unchanged. The extension and folder stay from OBS, and renaming is performed by
the `Recordings` module: it does not overwrite existing files — if the name is taken, ` (2)`, ` (3)`
is appended.

For Toast, the application is built with the target framework `net10.0-windows10.0.22621.0`. Initial
testing must be performed in an interactive Windows user session where notifications are allowed for
the application.
