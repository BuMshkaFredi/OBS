# CustomerMail

Mail-channel utility: it sends the customer only questions about implementation preferences
and fetches their replies. It is not part of the application itself and is not included in the
installer or publish output.

## Guarantees built into the code

- A single recipient: `policy.json` → `customerAddress`, and it must match the PIN hash in
  `AddressPin.CustomerAddressSha256`. There is no “to” parameter.
- No email is sent without `status: approved` in the batch file; free-form text never goes into
  the email.
- The code has no attachments, `CC`, `BCC`, HTML, or multiple recipients.
- The content filter (`ContentGuard.cs`) forbids links, email addresses, file paths, IPs, code,
  GUIDs, long tokens, words about passwords/tokens/secrets, and mentions of project internals.
- A daily email limit and one send per batch.
- The log `docs/customer-questions/audit/mail-log.jsonl` is written append-only.
- Only the pinned address is accepted; other people's emails are neither read nor marked as read.

## Commands

```powershell
# Check the policy, the address PIN, and the presence of credentials
dotnet run --project tools/CustomerMail -- check

# Show the email without sending anything
dotnet run --project tools/CustomerMail -- send --batch CQ-001 --preview

# Send after user permission
dotnet run --project tools/CustomerMail -- send --batch CQ-001

# Fetch new customer replies
dotnet run --project tools/CustomerMail -- fetch --limit 20

# Compute the SHA-256 of the address for the PIN
dotnet run --project tools/CustomerMail -- pin-hash zakazchik@example.com
```

Exit codes: `0` — success, `1` — error, `2` — action rejected by the checks.

## One-time setup

### 1. Dedicated Gmail mailbox

1. Use a separate mailbox only for correspondence with the customer (the address is set in
   `policy.json`; the real address never enters the repository).
2. Enable two-step authentication: without it Google does not issue app passwords.
3. Create an app password: https://myaccount.google.com/apppasswords → “Other” → any name,
   for example `obs-voice-assistant`. You will get a 16-character password.
4. A regular Google password does not work for SMTP/IMAP, and Google has disabled
   “access for less secure apps”.

### 2. Environment variables

The password is stored only in the user environment variables, not in the repository and not in
`policy.json`.

```powershell
[Environment]::SetEnvironmentVariable('OBS_VA_MAIL_USER', 'your-mail@gmail.com', 'User')

# The app password is entered hidden so it does not end up in the command history:
$secure = Read-Host -AsSecureString 'Gmail app password'
[Environment]::SetEnvironmentVariable(
    'OBS_VA_MAIL_PASSWORD',
    [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)),
    'User')
```

Then restart the terminal and OpenCode: new processes read the variables at startup.
Do not write the password into scripts, scheduled tasks, logs, or the command history.

### 3. Pinning the customer address

The repository contains the template `docs/customer-questions/policy.example.json`. Copy it to
`policy.json`, set the real addresses, and compute the PIN. The `policy.json` file is added to
`.gitignore`, because it contains personal data and must not enter the history. The procedure is:

```powershell
dotnet run --project tools/CustomerMail -- pin-hash zakazchik@example.com
```

The resulting hash must be written in two places, and they must match:

- `docs/customer-questions/policy.json` → `customerAddress` (the address itself), `fromAddress`
  (the sender mailbox);
- `tools/CustomerMail/AddressPin.cs` → `CustomerAddressSha256` (the hash).

While the PIN is empty, `send` and `fetch` refuse to work. This is intentional: the address cannot
be set in one move, and the change is visible in the diff and requires user review.

Check: `dotnet run --project tools/CustomerMail -- check`.

## What to do when something fails

| Symptom | Cause and action |
| --- | --- |
| `PIN адреса не настроен` (address PIN is not configured) | Empty `AddressPin.CustomerAddressSha256`, see step 3 |
| `Не задана переменная окружения OBS_VA_MAIL_USER` (environment variable is not set) | The variables are not set or the terminal was not restarted |
| `Адрес в policy.json не совпадает с закреплённым PIN` (address does not match the pinned PIN) | The address was changed without recomputing the hash |
| `Отклонено: ... запрещённый фрагмент` (rejected: forbidden fragment) | The question contains a link, path, email, token, or code — rephrase the question |
| `Отклонено: исчерпан лимит` (rejected: limit exhausted) | The daily email limit in `policy.json` |
| `Отклонено: партия уже была отправлена` (rejected: batch was already sent) | Idempotency: one send per batch |
| `SSL/TLS handshake` or `server has unexpectedly disconnected` on 465/587 | The network or provider cuts outgoing SMTP on these ports. For Gmail, a working workaround is `port: 25` + `security: starttls`: port 25 offers STARTTLS and AUTH. Verified by changing `smtp` in `policy.json` |
