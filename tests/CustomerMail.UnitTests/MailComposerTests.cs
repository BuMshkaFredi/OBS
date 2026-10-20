using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class MailComposerTests
{
    [Fact]
    public void Compose_ContainsBatchIdTopicAndQuestionIds()
    {
        var batch = new QuestionBatch
        {
            Id = "CQ-001",
            Topic = "audio",
            Status = "approved",
            ApprovedAt = "2026-10-20",
            Questions =
            [
                new Question { Id = "CQ-001-1", Text = "Переключать ли личную музыку тем же способом, что и микрофон?" },
            ],
        };

        var email = MailComposer.Compose(batch, "аудио и источники");

        Assert.Contains("CQ-001", email.Subject, StringComparison.Ordinal);
        Assert.Contains("аудио и источники", email.Subject, StringComparison.Ordinal);
        Assert.Contains("[CQ-001-1]", email.Body, StringComparison.Ordinal);
        Assert.Contains("Переключать ли личную музыку", email.Body, StringComparison.Ordinal);
        Assert.Contains("1)", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_NumbersQuestionsInOrder()
    {
        var batch = new QuestionBatch
        {
            Id = "CQ-002",
            Topic = "ux",
            Status = "approved",
            ApprovedAt = "2026-10-20",
            Questions =
            [
                new Question { Id = "CQ-002-1", Text = "Показывать ли уведомление о непонятой команде?" },
                new Question { Id = "CQ-002-2", Text = "Сворачивать ли окно настроек в трей?" },
            ],
        };

        var email = MailComposer.Compose(batch, "удобство использования");

        var firstIndex = email.Body.IndexOf("[CQ-002-1]", StringComparison.Ordinal);
        var secondIndex = email.Body.IndexOf("[CQ-002-2]", StringComparison.Ordinal);

        Assert.True(firstIndex > 0);
        Assert.True(secondIndex > firstIndex);
    }
}
