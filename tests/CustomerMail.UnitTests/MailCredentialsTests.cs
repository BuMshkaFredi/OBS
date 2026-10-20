using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class MailCredentialsTests
{
    [Fact]
    public void NormalizePassword_RemovesSpacesFromAppPassword()
    {
        Assert.Equal("abcdefghijklmnop", MailCredentials.NormalizePassword("abcd efgh ijkl mnop"));
    }

    [Fact]
    public void NormalizePassword_KeepsCompactAppPassword()
    {
        Assert.Equal("abcdefghijklmnop", MailCredentials.NormalizePassword("abcdefghijklmnop"));
    }

    [Theory]
    [InlineData("пароль с пробелом")]
    [InlineData("abcd efgh ijkl mnopq")]
    [InlineData("short pass")]
    public void NormalizePassword_KeepsOtherPasswordsUnchanged(string value)
    {
        Assert.Equal(value, MailCredentials.NormalizePassword(value));
    }
}
