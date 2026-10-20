namespace CustomerMail;

/// <summary>
/// Закреплённый адрес заказчика в виде SHA-256. Пустая строка означает "не настроено":
/// тогда ни отправка, ни приём не работают. Изменение требует ревью diff пользователем.
/// </summary>
internal static class AddressPin
{
    public const string CustomerAddressSha256 = "4199494014d4ad53adfe0384664b26ee78435524c6ceb2be68999d8e5d70f63b";
}
