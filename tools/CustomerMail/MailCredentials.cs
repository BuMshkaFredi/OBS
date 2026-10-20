namespace CustomerMail;

/// <summary>
/// Учётные данные берутся только из переменных окружения пользователя. В репозитории,
/// policy.json и журналах пароля нет.
/// </summary>
internal static class MailCredentials
{
    public const string UserVariable = "OBS_VA_MAIL_USER";

    public const string PasswordVariable = "OBS_VA_MAIL_PASSWORD";

    public static string RequireUser() => Require(UserVariable);

    public static string RequirePassword() => NormalizePassword(Require(PasswordVariable));

    /// <summary>
    /// Google показывает пароль приложения группами по 4 символа. Пробелы убираются только
    /// если без них получается ровно 16 буквенно-цифровых символов, иначе пароль не трогаем.
    /// </summary>
    public static string NormalizePassword(string value)
    {
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        return compact.Length == 16 && compact.All(char.IsLetterOrDigit) ? compact : value;
    }

    public static bool HasUser => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UserVariable));

    public static bool HasPassword => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PasswordVariable));

    private static string Require(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Не задана переменная окружения {name}. Инструкция: tools/CustomerMail/README.md.");
        }

        return value.Trim();
    }
}
