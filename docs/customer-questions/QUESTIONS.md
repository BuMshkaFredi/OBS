# Customer question registry

Batch statuses: `draft` — draft, `approved` — allowed to send, `sent` — sent, `answered` — reply
received and recorded, `closed` — decision made, `rejected` — question withdrawn.

The user makes decisions on the replies received. The agent only records replies and does not change
requirements, code, or configuration.

| Batch | Topic | Status | Sent | Reply | User decision |
| --- | --- | --- | --- | --- | --- |
| CQ-001 | OBS behavior | closed | 2026-10-20 | 2026-10-20 | accepted: items 1–3; item 4 postponed |

## Release letters

Statuses: `draft` — draft, `approved` — allowed to send, `sent` — sent, `rejected` — withdrawn.

| Letter | Build | Status | Sent | Subject |
| --- | --- | --- | --- | --- |
| REL-001 | 0.1.0 MVP | sent | 2026-10-27 | OBS Voice Assistant — сборка 0.1.0 для проверки |
| REL-002 | 0.2.0 | sent | 2026-10-31 | OBS Voice Assistant — сборка 0.2.0 для проверки |
| REL-003 | 0.2.1 | sent | 2026-10-31 | OBS Voice Assistant — сборка 0.2.1 для проверки |

Build feedback on `REL-001` arrived on 2026-10-31 and is saved in
`answers/reply-20261031-060720-ec263136ec.md`. It contains no answers to the batch questions, only
the three wishes listed below.

`REL-002` describes that feedback being addressed: the configurable push-to-talk key, resuming Yandex
Music from the pause, the fixed category command, and the removed title command. The build was
published as `v0.2.0`; the letter was sent on 2026-10-31. The first SMTP attempts failed because
Gmail's SMTP ports were unreachable from the working network; the send succeeded from another
network, and the audit log contains four attempts, one of them `sent`.

`REL-003` describes build 0.2.1, prepared from the customer's `assistant.log` supplied on 2026-10-31:
the wake-word variants `больше`/`большие`/`большая`/`большим` and `бачив`, the Russian Twitch
category aliases (`пас оф экзайл`, `пасов экзамен`, `путь изгнанника` → `Path of Exile`,
`дота дс` → `Dota 2`), the soft-sign transliteration fix, and the fuzzy-match guard that previously
muted the browser on «больше выключи музыку». The build was published as `v0.2.1`; the letter was
sent on 2026-10-31.

## Unscheduled wishes

Wishes that the customer added on their own, outside the question list. Each is recorded as a
separate row and awaits the user's decision; nothing is implemented automatically.

| Date | Wish | Source | User decision |
| --- | --- | --- | --- |
| 2026-10-27 | When personal music is turned off, pause the music playing in the “Yandex Music” app; additionally, switch to the next track by a voice command | `answers/reply-20261027-043724-a15e4e8515.md`, item 1 | accepted: pause through Windows media sessions, command `следующий трек` (next track) |
| 2026-10-27 | Automatic conversion of a saved recording or replay from mkv to mp4 | `answers/reply-20261027-043724-a15e4e8515.md`, item 2 | accepted: both files are kept — the original mkv and the mp4 copy (already implemented) |
| 2026-10-27 | When renaming the stream, keep the tags in the title (for example, `!tg`, `!exitlag`, and similar) | `answers/reply-20261027-043724-a15e4e8515.md`, item 3 | accepted: words starting with `!` are kept, without duplicates |
| 2026-10-27 | If possible, show the number of viewers in real time | `answers/reply-20261027-043724-a15e4e8515.md`, item 4 | accepted: a separate always-on-top window |
| 2026-10-31 | Rebind the push-to-talk key from Caps Lock to any other key or key combination (F9, F12, Ctrl/Shift combinations; the customer plans to use Ctrl+Shift+F9) | `answers/reply-20261031-060720-ec263136ec.md`, item 1 | accepted: a configurable key or combination, Caps Lock stays the default; the settings window captures the real key |
| 2026-10-31 | After personal music is turned back on, the Yandex Music app is not resumed from pause (pausing works, resuming does not); the customer asks to fix it | `answers/reply-20261031-060720-ec263136ec.md`, item 2 | accepted: fixed through the same Windows media session, a missing player does not cancel the OBS action |
| 2026-10-31 | The Twitch category change does not work in the current build; the customer asks to finish it and to remove the stream title change, because the title is better edited manually | `answers/reply-20261031-060720-ec263136ec.md`, item 3 | accepted: the category is fixed and verified after the update; the title command, its confirmation, and tag composition are removed |
| 2026-10-31 | The customer's log shows that «Бочи» in one phrase is heard as «больше», «большие», «большая», or «большим»; the category command fails on Path of Exile names and sets «Dota DS» for «дота дс» | customer log supplied on 2026-10-31 | accepted: the wake-word variants, category aliases, soft-sign fix, and parser guard are implemented in build 0.2.1 |

User decision on 2026-10-31: all three wishes are accepted and implemented in slice 8 of
[`docs/MVP_PLAN.md`](../MVP_PLAN.md), section 10. The email contains no answers to batch questions;
`CQ-001` is closed. Manual verification of the new hotkey requires the customer's second keyboard,
and manual test 24 now checks the category instead of the title.

User decision on 2026-10-27: all four wishes are accepted. The tasks are added to
[`docs/MVP_PLAN.md`](../MVP_PLAN.md), section 9 “Additional tasks (slice 7)”; manual tests 21–25 are
there as well. The email of 2026-10-27 does not contain answers to the batch questions: `CQ-001` is
already closed, and the email contains only unscheduled wishes. The original is saved in
`answers/reply-20261027-043724-a15e4e8515.md`.

## Batch log

### CQ-001 — OBS behavior

Sent on 2026-10-20 from the sender address to the customer address (the values are in `policy.json`).
The reply was received on 2026-10-20 and saved in `answers/reply-20261020-080806-075327ae0a.md`.

- `CQ-001-1` Is button confirmation needed to start a recording and start a stream, or is a result
  notification enough? — answer: «уведом на запись, кнопку подтверждения на стриме» (a notification
  for recording, a confirmation button for the stream)
- `CQ-001-2` What should happen if a phrase is recognized with low confidence: stay silent or show a
  notification about an unrecognized command? — answer: «уведомление бы маленькое» (a small
  notification would be fine)
- `CQ-001-3` Should Replay Buffer be enabled on every connection to OBS if you disabled it manually?
  — answer: «нет» (no)
- `CQ-001-4` How should the application determine which audio source to toggle if the current scene
  has several suitable sources? — answer: «в таком случае программа может дополнительно спрашивать,
  какая по счету аудиодорожка именно нужна если считать слева направо» (in that case the program can
  additionally ask which audio track number is needed, counting from left to right)

User decision (2026-10-20):

- `CQ-001-1` accepted: starting a recording shows a notification, starting a stream requires button
  confirmation.
- `CQ-001-2` accepted: an unrecognized phrase shows a small tray notification.
- `CQ-001-3` accepted with a clarification: Replay Buffer is enabled on connection, but after a manual
  shutdown it is not re-enabled until the application restarts.
- `CQ-001-4` postponed, not implemented.
