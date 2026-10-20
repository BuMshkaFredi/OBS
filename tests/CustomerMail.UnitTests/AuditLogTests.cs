using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class AuditLogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "customermail-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Append_ThenReadAll_ReturnsEntry()
    {
        AuditLog.Append(root, Sent("CQ-001"));

        var entries = AuditLog.ReadAll(root);

        Assert.Single(entries);
        Assert.Equal("CQ-001", entries[0].BatchId);
        Assert.Equal("sent", entries[0].Result);
    }

    [Fact]
    public void WasSent_IsTrueOnlyForSentEntries()
    {
        AuditLog.Append(root, Sent("CQ-001"));
        AuditLog.Append(root, new AuditEntry { Action = "send", BatchId = "CQ-002", Result = "failed" });

        Assert.True(AuditLog.WasSent(root, "CQ-001"));
        Assert.False(AuditLog.WasSent(root, "CQ-002"));
        Assert.False(AuditLog.WasSent(root, "CQ-003"));
    }

    [Fact]
    public void CountSentOnUtcDate_CountsOnlyRequestedDay()
    {
        var today = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);
        AuditLog.Append(root, Sent("CQ-001", today));
        AuditLog.Append(root, Sent("CQ-002", today.AddDays(-1)));
        AuditLog.Append(root, new AuditEntry { TimestampUtc = today, Action = "send", BatchId = "CQ-003", Result = "failed" });

        var count = AuditLog.CountSentOnUtcDate(root, new DateOnly(2026, 10, 20));

        Assert.Equal(1, count);
    }

    [Fact]
    public void ReceivedMessageIds_ReturnsSavedReceipts()
    {
        AuditLog.Append(root, new AuditEntry { Action = "receive", Result = "saved", MessageId = "<a@mail.ru>" });
        AuditLog.Append(root, new AuditEntry { Action = "send", Result = "sent", MessageId = "<b@mail.ru>" });

        var ids = AuditLog.ReceivedMessageIds(root);

        Assert.Contains("<a@mail.ru>", ids);
        Assert.DoesNotContain("<b@mail.ru>", ids);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AuditEntry Sent(string batchId, DateTimeOffset? timestamp = null) => new()
    {
        TimestampUtc = timestamp ?? DateTimeOffset.UtcNow,
        Action = "send",
        BatchId = batchId,
        Recipient = "customer@example.com",
        Result = "sent",
    };
}
