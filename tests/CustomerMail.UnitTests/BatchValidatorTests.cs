using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class BatchValidatorTests
{
    private const string ValidQuestion = "Нужно ли подтверждение для запуска записи?";

    [Fact]
    public void Validate_ApprovedBatch_IsValid()
    {
        var result = BatchValidator.Validate(CreateBatch("approved", ValidQuestion), "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.True(result.IsValid);
        Assert.Equal("поведение OBS", result.TopicLabel);
    }

    [Fact]
    public void Validate_DraftBatch_WhenApprovalRequired_IsRejected()
    {
        var result = BatchValidator.Validate(CreateBatch("draft", ValidQuestion), "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("status должен быть approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DraftBatch_Preview_IsAllowed()
    {
        var result = BatchValidator.Validate(CreateBatch("draft", ValidQuestion), "CQ-001", CreatePolicy(), requireApproved: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ApprovedBatchWithoutApprovedAt_IsRejected()
    {
        var batch = CreateBatch("approved", ValidQuestion);
        var withoutDate = new QuestionBatch
        {
            Id = batch.Id,
            Topic = batch.Topic,
            Status = batch.Status,
            ApprovedAt = null,
            Questions = batch.Questions,
        };

        var result = BatchValidator.Validate(withoutDate, "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("approvedAt", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_QuestionWithoutQuestionMark_IsRejected()
    {
        var result = BatchValidator.Validate(CreateBatch("approved", "Нужно ли подтверждение для записи"), "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("заканчиваться знаком", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_QuestionWithLink_IsRejected()
    {
        var result = BatchValidator.Validate(
            CreateBatch("approved", "Смотрите ли вы примеры вида https://example.com/guide перед настройкой?"),
            "CQ-001",
            CreatePolicy(),
            requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("запрещённый фрагмент: ссылка", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_QuestionWithWindowsPath_IsRejected()
    {
        var result = BatchValidator.Validate(
            CreateBatch("approved", "Открываете ли вы записи напрямую из C:\\Users\\Viteok при монтаже?"),
            "CQ-001",
            CreatePolicy(),
            requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("путь к файлу", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_QuestionWithoutCyrillic_IsRejected()
    {
        var result = BatchValidator.Validate(
            CreateBatch("approved", "Do you want a confirmation dialog before recording?"),
            "CQ-001",
            CreatePolicy(),
            requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("на русском языке", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TooManyQuestions_IsRejected()
    {
        var result = BatchValidator.Validate(
            CreateBatch("approved", ValidQuestion, ValidQuestion, ValidQuestion, ValidQuestion),
            "CQ-001",
            CreatePolicy(),
            requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Вопросов больше лимита", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_DuplicateQuestionId_IsRejected()
    {
        var batch = new QuestionBatch
        {
            Id = "CQ-001",
            Topic = "obs-behavior",
            Status = "approved",
            ApprovedAt = "2026-10-20",
            Questions =
            [
                new Question { Id = "CQ-001-1", Text = ValidQuestion },
                new Question { Id = "CQ-001-1", Text = ValidQuestion },
            ],
        };

        var result = BatchValidator.Validate(batch, "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Повторяющийся id вопроса", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_UnknownTopic_IsRejected()
    {
        var batch = CreateBatch("approved", ValidQuestion);
        var unknownTopic = new QuestionBatch
        {
            Id = batch.Id,
            Topic = "personal-data",
            Status = batch.Status,
            ApprovedAt = batch.ApprovedAt,
            Questions = batch.Questions,
        };

        var result = BatchValidator.Validate(unknownTopic, "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Неизвестная тема", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_BatchIdMismatchWithFileName_IsRejected()
    {
        var result = BatchValidator.Validate(CreateBatch("approved", ValidQuestion), "CQ-002", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("не совпадает с именем файла", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MissingBatch_IsRejected()
    {
        var result = BatchValidator.Validate(null, "CQ-001", CreatePolicy(), requireApproved: false);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("не найден", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TooLongQuestion_IsRejected()
    {
        var longQuestion = new string('а', 250) + "?";

        var result = BatchValidator.Validate(CreateBatch("approved", longQuestion), "CQ-001", CreatePolicy(), requireApproved: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("длиннее", StringComparison.Ordinal));
    }

    private static MailPolicy CreatePolicy() => new()
    {
        CustomerAddress = "customer@example.com",
        FromAddress = "assistant@mail.ru",
        Limits = new MailLimits
        {
            MaxEmailsPerDay = 3,
            MaxQuestionsPerBatch = 3,
            MaxQuestionCharacters = 200,
            MaxBodyCharacters = 4000,
        },
    };

    private static QuestionBatch CreateBatch(string status, params string[] questions) => new()
    {
        Id = "CQ-001",
        Topic = "obs-behavior",
        Status = status,
        CreatedAt = "2026-10-20",
        ApprovedAt = status == "approved" ? "2026-10-20" : null,
        Questions = questions
            .Select((text, index) => new Question { Id = $"CQ-001-{index + 1}", Text = text })
            .ToList(),
    };
}
