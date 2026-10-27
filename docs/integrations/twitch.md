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
  characters. A Windows notification requires pressing “Подтвердить” (Confirm); only after that is
  `PATCH /helix/channels` sent.
- `измени категорию стрима на ...` (change the stream category to ...) — before confirmation, a
  search is performed through `GET /helix/search/categories`. The first result is used; the exact
  found name is shown in the notification, and its `game_id` is sent only after confirmation.

Both commands are available through the shared voice pipeline, including Push-to-talk and the wake
word “Бочи” (Bochi). A rejection, closing the notification, or no confirmation performs no external
action.

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
credentials nor network access. Manual verification of authorization and metadata changes requires
a configured Twitch Client ID and an interactive Windows session.
