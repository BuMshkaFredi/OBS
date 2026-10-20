using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class MailPolicyTests
{
    [Fact]
    public void ValidateRecipient_EmptyAddress_IsRejected()
    {
        var policy = new MailPolicy { CustomerAddress = string.Empty };

        var error = policy.ValidateRecipient(MailPolicy.ComputeAddressHash("customer@example.com"));

        Assert.NotNull(error);
        Assert.Contains("customerAddress", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRecipient_InvalidAddress_IsRejected()
    {
        var policy = new MailPolicy { CustomerAddress = "не-адрес" };

        var error = policy.ValidateRecipient(MailPolicy.ComputeAddressHash("не-адрес"));

        Assert.NotNull(error);
        Assert.Contains("корректным email", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRecipient_EmptyPin_IsRejected()
    {
        var policy = new MailPolicy { CustomerAddress = "customer@example.com" };

        var error = policy.ValidateRecipient(string.Empty);

        Assert.NotNull(error);
        Assert.Contains("PIN", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRecipient_MatchingPin_IsAccepted()
    {
        var policy = new MailPolicy { CustomerAddress = "customer@example.com" };

        var error = policy.ValidateRecipient(MailPolicy.ComputeAddressHash("customer@example.com"));

        Assert.Null(error);
    }

    [Fact]
    public void ValidateRecipient_OtherAddressWithSamePin_IsRejected()
    {
        var policy = new MailPolicy { CustomerAddress = "attacker@example.com" };

        var error = policy.ValidateRecipient(MailPolicy.ComputeAddressHash("customer@example.com"));

        Assert.NotNull(error);
        Assert.Contains("не совпадает с закреплённым PIN", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeAddressHash_IgnoresCaseAndSurroundingSpaces()
    {
        Assert.Equal(
            MailPolicy.ComputeAddressHash("customer@example.com"),
            MailPolicy.ComputeAddressHash("  Customer@Example.COM  "));
    }

    [Fact]
    public void ValidateSender_AddressDifferentFromAccount_IsRejected()
    {
        var policy = new MailPolicy { FromAddress = "assistant@mail.ru" };

        var error = policy.ValidateSender("other@mail.ru");

        Assert.NotNull(error);
        Assert.Contains("не совпадает с учётной записью", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSender_MatchingAccount_IsAccepted()
    {
        var policy = new MailPolicy { FromAddress = "assistant@mail.ru" };

        var error = policy.ValidateSender("Assistant@Mail.RU");

        Assert.Null(error);
    }

    [Fact]
    public void ValidateEndpoints_DefaultEndpoints_AreAccepted()
    {
        var policy = new MailPolicy
        {
            Smtp = new MailEndpoint { Host = "smtp.mail.ru", Port = 465 },
            Imap = new MailEndpoint { Host = "imap.mail.ru", Port = 993 },
        };

        Assert.Null(policy.ValidateEndpoints());
    }

    [Fact]
    public void ValidateEndpoints_EmptyHost_IsRejected()
    {
        var policy = new MailPolicy
        {
            Smtp = new MailEndpoint { Host = string.Empty, Port = 465 },
            Imap = new MailEndpoint { Host = "imap.mail.ru", Port = 993 },
        };

        var error = policy.ValidateEndpoints();

        Assert.NotNull(error);
        Assert.Contains("smtp", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateEndpoints_UnknownSecurity_IsRejected()
    {
        var policy = new MailPolicy
        {
            Smtp = new MailEndpoint { Host = "smtp.gmail.com", Port = 587, Security = "plain" },
            Imap = new MailEndpoint { Host = "imap.gmail.com", Port = 993 },
        };

        var error = policy.ValidateEndpoints();

        Assert.NotNull(error);
        Assert.Contains("security", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToSecureSocketOptions_Default_IsImplicitTls()
    {
        var endpoint = new MailEndpoint { Host = "smtp.gmail.com", Port = 465 };

        Assert.Equal(MailKit.Security.SecureSocketOptions.SslOnConnect, endpoint.ToSecureSocketOptions());
    }

    [Fact]
    public void ToSecureSocketOptions_StartTls_IsStartTls()
    {
        var endpoint = new MailEndpoint { Host = "smtp.gmail.com", Port = 587, Security = "starttls" };

        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, endpoint.ToSecureSocketOptions());
    }
}
