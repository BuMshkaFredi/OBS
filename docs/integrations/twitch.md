# Twitch Integration

## Setup

1. Create an application at [dev.twitch.tv/console/apps](https://dev.twitch.tv/console/apps).
2. Copy the `Client ID` into the Twitch section of the “Settings” window. The application does not
   need a `Client secret`.
3. Click “Connect Twitch”, open the activation page, and enter the displayed code.
4. Confirm access to the only requested permission, `channel:manage:broadcast`.

The Client ID is stored in `settings.json`: it is a public application identifier. The access token
and refresh token are stored separately through Windows DPAPI for the current user. On disconnect,
the local tokens are deleted; the application also tries to revoke the access token at Twitch.
Secrets are never written to the log.

## Commands

- `измени название стрима на ...` (change the stream title to ...) — a title of up to 140
  characters. Before confirmation, the current title is read through
  `GET /helix/channels?broadcaster_id=...`. Words starting with `!` (for example, `!tg`,
  `!exitlag`) are appended to the end of the new title in the order they appear in the old one,
  without duplicates; case is ignored when comparing. The confirmation shows the final title in
  full. A Windows notification requires pressing “Подтвердить” (Confirm); only after that is
  `PATCH /helix/channels` sent. If the final title is longer than 140 characters, the command is
  rejected before any external call. Only trailing punctuation is removed from the spoken title;
  the rest of the text and its case are kept.
- `измени категорию стрима на ...` (change the stream category to ...) — before confirmation, a
  search is performed through `GET /helix/search/categories`. The first result is used; the exact
  found name is shown in the notification, and its `game_id` is sent only after confirmation.

Both commands are available through the shared voice pipeline, including Push-to-talk and the wake
word “Бочи” (Bochi). A rejection, closing the notification, or no confirmation performs no external
action.

## Viewer count

The viewer overlay reads `GET /helix/streams?user_id=...`. An empty `data` array means the
broadcast is offline: the overlay hides. A non-empty item provides `viewer_count`, which is shown in
the always-on-top window. The same `channel:manage:broadcast` token is used; no additional scope is
required.

Polling runs only when the overlay is enabled and Twitch is connected. The default interval is
30 seconds; the settings window accepts 15–3600 seconds. On `401`/`403` the polling pauses until the
user connects Twitch again; a network error is not shown as a toast, and the window keeps the last
value dimmed. Helix caches `/streams`, so the displayed number may lag behind the real audience by
tens of seconds.

## Session and recovery

Before Helix calls, the application validates the access token through `/oauth2/validate`. A token
close to expiry is refreshed through the Device Grant refresh; on HTTP 401, one refresh and one
retry are performed. Twitch issues a rotating refresh token to a public client, so after a refresh
the new pair is saved through DPAPI immediately. If the refresh token is revoked or expired, the
tokens are deleted and the notification suggests connecting Twitch again.

Helix requests use `Client-Id` and `Authorization: Bearer`. Title and category calls use the
broadcaster ID of the currently authorized user. On a temporary network error, 429, or 5xx, the
Helix request is retried once; there is no infinite retry loop.

## Checks

OAuth and Helix are tested through a replaceable `HttpMessageHandler`; unit tests need neither real
credentials nor network access. Title composition, viewer count parsing, polling pause/resume, and
the overlay view model are covered by unit tests. Manual verification of authorization, metadata
changes, and the overlay requires a configured Twitch Client ID and an interactive Windows session.
