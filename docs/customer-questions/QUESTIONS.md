# Customer question registry

Batch statuses: `draft` — draft, `approved` — allowed to send, `sent` — sent, `answered` — reply
received and recorded, `closed` — decision made, `rejected` — question withdrawn.

The user makes decisions on the replies received. The agent only records replies and does not change
requirements, code, or configuration.

| Batch | Topic | Status | Sent | Reply | User decision |
| --- | --- | --- | --- | --- | --- |
| CQ-001 | OBS behavior | closed | 2026-10-20 | 2026-10-20 | accepted: items 1–3; item 4 postponed |

## Unscheduled wishes

Wishes that the customer added on their own, outside the question list. Each is recorded as a
separate row and awaits the user's decision; nothing is implemented automatically.

| Date | Wish | Source | User decision |
| --- | --- | --- | --- |
| 2026-10-27 | When personal music is turned off, pause the music playing in the “Yandex Music” app; additionally, switch to the next track by a voice command | `answers/reply-20261027-043724-a15e4e8515.md`, item 1 | accepted: pause through Windows media sessions, command `следующий трек` (next track) |
| 2026-10-27 | Automatic conversion of a saved recording or replay from mkv to mp4 | `answers/reply-20261027-043724-a15e4e8515.md`, item 2 | accepted: both files are kept — the original mkv and the mp4 copy (already implemented) |
| 2026-10-27 | When renaming the stream, keep the tags in the title (for example, `!tg`, `!exitlag`, and similar) | `answers/reply-20261027-043724-a15e4e8515.md`, item 3 | accepted: words starting with `!` are kept, without duplicates |
| 2026-10-27 | If possible, show the number of viewers in real time | `answers/reply-20261027-043724-a15e4e8515.md`, item 4 | accepted: a separate always-on-top window |

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
