# CustomerMail

Утилита почтового канала: отправляет заказчику только вопросы о предпочтениях по реализации
и забирает его ответы. Отдельного приложения это не касается, в installer и publish не входит.

## Гарантии, зашитые в код

- Получатель один: `policy.json` → `customerAddress`, и он обязан совпадать с PIN-хешем
  `AddressPin.CustomerAddressSha256`. Параметра «кому» не существует.
- Писем без `status: approved` в файле партии не бывает; свободный текст в письмо не попадает.
- Вложений, `CC`, `BCC`, HTML и нескольких получателей в коде нет.
- Фильтр содержимого (`ContentGuard.cs`) запрещает ссылки, адреса почты, пути к файлам, IP,
  код, GUID, длинные токены, слова про пароли/токены/секреты и упоминания внутренностей проекта.
- Лимит писем в сутки и одна отправка на партию.
- Журнал `docs/customer-questions/audit/mail-log.jsonl` пишется append-only.
- Приём только от закреплённого адреса; чужие письма не читаются и не помечаются прочитанными.

## Команды

```powershell
# Проверить policy, PIN адреса и наличие учётных данных
dotnet run --project tools/CustomerMail -- check

# Показать письмо, ничего не отправляя
dotnet run --project tools/CustomerMail -- send --batch CQ-001 --preview

# Отправить после разрешения пользователя
dotnet run --project tools/CustomerMail -- send --batch CQ-001

# Забрать новые ответы заказчика
dotnet run --project tools/CustomerMail -- fetch --limit 20

# Посчитать SHA-256 адреса для PIN
dotnet run --project tools/CustomerMail -- pin-hash zakazchik@example.com
```

Коды возврата: `0` — успех, `1` — ошибка, `2` — действие отклонено проверками.

## Разовая настройка

### 1. Выделенный ящик Gmail

1. Используется отдельный ящик только для переписки с заказчиком (адрес задаётся в `policy.json`, настоящий адрес в репозиторий не попадает).
2. Включите двухэтапную аутентификацию: без неё Google не выдаёт пароли приложений.
3. Создайте пароль приложения: https://myaccount.google.com/apppasswords → «Другое» → любое имя,
   например `obs-voice-assistant`. Получите 16-символьный пароль.
4. Обычный пароль Google для SMTP/IMAP не подходит, а «доступ для менее безопасных приложений»
   Google отключил.

### 2. Переменные окружения

Пароль хранится только в переменных окружения пользователя, не в репозитории и не в `policy.json`.

```powershell
[Environment]::SetEnvironmentVariable('OBS_VA_MAIL_USER', 'your-mail@gmail.com', 'User')

# Пароль приложения вводится скрыто, чтобы не попасть в историю команд:
$secure = Read-Host -AsSecureString 'Пароль приложения Gmail'
[Environment]::SetEnvironmentVariable(
    'OBS_VA_MAIL_PASSWORD',
    [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)),
    'User')
```

После этого перезапустите терминал и OpenCode: новые процессы читают переменные при старте.
Не записывайте пароль в скрипты, задачи планировщика, логи или историю команд.

### 3. Закрепление адреса заказчика

В репозитории лежит шаблон `docs/customer-questions/policy.example.json`. Скопируйте его
в `policy.json`, укажите реальные адреса и посчитайте PIN. Файл `policy.json` добавлен
в `.gitignore`, потому что содержит персональные данные и не должен попадать в историю.
Порядок такой:

```powershell
dotnet run --project tools/CustomerMail -- pin-hash zakazchik@example.com
```

Полученный хеш нужно вписать в двух местах, и они обязаны совпадать:

- `docs/customer-questions/policy.json` → `customerAddress` (сам адрес), `fromAddress` (ящик отправителя);
- `tools/CustomerMail/AddressPin.cs` → `CustomerAddressSha256` (хеш).

Пока PIN пуст, `send` и `fetch` отказываются работать. Это намеренно: адрес нельзя задать
одним движением, изменение видно в diff и требует ревью пользователя.

Проверка: `dotnet run --project tools/CustomerMail -- check`.

## Что делать при проблемах

| Симптом | Причина и действие |
| --- | --- |
| `PIN адреса не настроен` | Пустой `AddressPin.CustomerAddressSha256`, см. шаг 3 |
| `Не задана переменная окружения OBS_VA_MAIL_USER` | Переменные не заданы или терминал не перезапущен |
| `Адрес в policy.json не совпадает с закреплённым PIN` | Адрес меняли без пересчёта хеша |
| `Отклонено: ... запрещённый фрагмент` | В вопросе есть ссылка, путь, почта, токен или код — переформулируйте вопрос |
| `Отклонено: исчерпан лимит` | Дневной лимит писем в `policy.json` |
| `Отклонено: партия уже была отправлена` | Идемпотентность: отправка на партию одна |
| `SSL/TLS handshake` или `server has unexpectedly disconnected` на 465/587 | Сеть или провайдер режет исходящий SMTP на этих портах. Для Gmail рабочий обход — `port: 25` + `security: starttls`: порт 25 отдаёт STARTTLS и AUTH. Проверяется сменой `smtp` в `policy.json` |
