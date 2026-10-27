using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class ReleaseMailTests
{
    private const string Checksum = "A5A365F5657DE2AF9E9E7744543659B3CB1F426184AAABD40C3E740E2DC386D5";

    private const string ReleasePrefix = "https://github.com/example/repo/releases/tag/";

    private const string DownloadPrefix = "https://github.com/example/repo/releases/download/";

    [Fact]
    public void Validate_ApprovedMailWithAllowedLinks_IsAccepted()
    {
        var result = ReleaseMailValidator.Validate(Mail(), "REL-001", Policy(), requireApproved: true);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void Validate_DraftWithoutApproval_IsRejected()
    {
        var mail = Mail(status: "draft", approvedAt: null);

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("status должен быть approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ApprovedWithoutDate_IsRejected()
    {
        var mail = Mail(approvedAt: null);

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("approvedAt", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_LinkOutsideAllowlist_IsRejected()
    {
        var mail = Mail(downloadUrl: "https://evil.example/app.zip");

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("allowedUrlPrefixes", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_HttpLink_IsRejected()
    {
        var mail = Mail(releaseUrl: "http://github.com/example/repo/releases/tag/v0.1.0");

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_LinkWithQuery_IsRejected()
    {
        var mail = Mail(releaseUrl: ReleasePrefix + "v0.1.0?download=1");

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_TextWithPath_IsRejected()
    {
        var mail = Mail(intro: ["Файл лежит в D:\\Записи и ждёт вас."]);

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("путь к файлу", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WrongChecksum_IsRejected()
    {
        var mail = Mail(archiveSha256: "не-хеш");

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("archiveSha256", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TooLongBody_IsRejected()
    {
        var policy = new MailPolicy
        {
            Release = Policy().Release,
            Limits = new MailLimits { MaxBodyCharacters = 100 },
        };

        var result = ReleaseMailValidator.Validate(Mail(), "REL-001", policy, requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("длиннее 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptySections_IsRejected()
    {
        var mail = Mail(sections: []);

        var result = ReleaseMailValidator.Validate(mail, "REL-001", Policy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("нет разделов", StringComparison.Ordinal));
    }

    [Fact]
    public void Compose_ContainsLinksChecksumStepsAndSections()
    {
        var email = ReleaseMailComposer.Compose(Mail());

        Assert.Contains(ReleasePrefix + "v0.1.0", email.Body, StringComparison.Ordinal);
        Assert.Contains(DownloadPrefix + "v0.1.0/app.zip", email.Body, StringComparison.Ordinal);
        Assert.Contains(Checksum.ToLowerInvariant(), email.Body, StringComparison.Ordinal);
        Assert.Contains("Установка", email.Body, StringComparison.Ordinal);
        Assert.Contains("1. Распакуйте архив.", email.Body, StringComparison.Ordinal);
        Assert.Contains("2. Запустите приложение.", email.Body, StringComparison.Ordinal);
        Assert.Contains("Напишите, что получилось.", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReleaseConfiguration_NonHttpsPrefix_IsRejected()
    {
        var policy = Policy("http://github.com/example/repo/releases/tag/");

        var error = policy.ValidateReleaseConfiguration();

        Assert.NotNull(error);
        Assert.Contains("HTTPS", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReleaseConfiguration_EmptyList_IsAccepted()
    {
        var policy = new MailPolicy();

        Assert.Null(policy.ValidateReleaseConfiguration());
        Assert.Empty(policy.Release.AllowedUrlPrefixes);
    }

    private static MailPolicy Policy(params string[] prefixes) => new()
    {
        CustomerAddress = "customer@example.com",
        Release = new ReleaseMailPolicy
        {
            AllowedUrlPrefixes = prefixes.Length == 0 ? [ReleasePrefix, DownloadPrefix] : [.. prefixes],
        },
    };

    private static ReleaseMail Mail(
        string status = "approved",
        string? approvedAt = "2026-10-27",
        string subject = "OBS Voice Assistant: сборка 0.1.0 для проверки",
        string? releaseUrl = null,
        string? downloadUrl = null,
        string? archiveSha256 = null,
        List<string>? intro = null,
        List<ReleaseSection>? sections = null,
        List<string>? closing = null) => new()
    {
        Id = "REL-001",
        Status = status,
        ApprovedAt = approvedAt,
        Subject = subject,
        ReleaseUrl = releaseUrl ?? ReleasePrefix + "v0.1.0",
        DownloadUrl = downloadUrl ?? DownloadPrefix + "v0.1.0/app.zip",
        ArchiveSha256 = archiveSha256 ?? Checksum,
        Intro = intro ?? ["Первая сборка готова."],
        Sections = sections ??
        [
            new ReleaseSection
            {
                Title = "Установка",
                Steps = ["Распакуйте архив.", "Запустите приложение."],
            },
        ],
        Closing = closing ?? ["Напишите, что получилось."],
    };
}
