using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class ContentGuardTests
{
    [Theory]
    [InlineData("Посмотрите пример на https://obsproject.com/wiki и скажите, так удобнее?")]
    [InlineData("Пишите мне на streamer@example.com, если что-то непонятно?")]
    [InlineData("Файл лежит в D:\\Записи, так удобнее?")]
    [InlineData("Сервер 192.168.1.10 подойдёт?")]
    [InlineData("Использовать `obs-websocket` для этого?")]
    [InlineData("Токен 3f9a1c7e5b2d4a8f6c0e7b3d9a1f5c8e2b6d4a7f9c1e3b5d8a0f2c4e6b8d1a3f подойдёт?")]
    [InlineData("Указать в конфиге пароль от OBS?")]
    [InlineData("Приложить скриншот из репозитория с кодом?")]
    public void FindViolations_ForbiddenContent_IsReported(string text)
    {
        var violations = ContentGuard.FindViolations(text);

        Assert.NotEmpty(violations);
    }

    [Theory]
    [InlineData("Нужно ли подтверждение для остановки записи?")]
    [InlineData("Должен ли Replay Buffer включаться автоматически?")]
    [InlineData("Как назвать источник личной музыки в настройках приложения?")]
    public void FindViolations_OrdinaryQuestion_IsClean(string text)
    {
        var violations = ContentGuard.FindViolations(text);

        Assert.Empty(violations);
    }

    [Fact]
    public void FindViolations_ComposedEmail_IsClean()
    {
        var batch = new QuestionBatch
        {
            Id = "CQ-001",
            Topic = "obs-behavior",
            Status = "approved",
            ApprovedAt = "2026-10-20",
            Questions =
            [
                new Question { Id = "CQ-001-1", Text = "Нужно ли подтверждение для запуска записи?" },
                new Question { Id = "CQ-001-2", Text = "Должен ли Replay Buffer включаться автоматически?" },
            ],
        };

        var email = MailComposer.Compose(batch, "поведение OBS");

        Assert.Empty(ContentGuard.FindViolations(email.Subject));
        Assert.Empty(ContentGuard.FindViolations(email.Body));
    }
}
