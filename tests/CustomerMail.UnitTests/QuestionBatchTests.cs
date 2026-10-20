using CustomerMail;

namespace CustomerMail.UnitTests;

public sealed class QuestionBatchTests : IDisposable
{
    private readonly List<string> files = [];

    [Fact]
    public void TryLoad_ValidBatch_ReturnsBatch()
    {
        var path = Write("""
            {
              "id": "CQ-001",
              "topic": "ux",
              "status": "draft",
              "questions": [ { "id": "CQ-001-1", "text": "Удобно ли открывать настройки из трея?" } ]
            }
            """);

        var batch = QuestionBatch.TryLoad(path, out var error);

        Assert.Null(error);
        Assert.NotNull(batch);
        Assert.Equal("CQ-001", batch.Id);
        Assert.Single(batch.Questions);
    }

    [Theory]
    [InlineData("\"attachments\": [\"secret.txt\"],")]
    [InlineData("\"to\": \"attacker@example.com\",")]
    [InlineData("\"cc\": \"attacker@example.com\",")]
    public void TryLoad_FieldOutsideSchema_IsRejected(string injectedField)
    {
        var path = Write($$"""
            {
              "id": "CQ-001",
              {{injectedField}}
              "topic": "ux",
              "status": "draft",
              "questions": []
            }
            """);

        var batch = QuestionBatch.TryLoad(path, out var error);

        Assert.Null(batch);
        Assert.NotNull(error);
        Assert.Contains("не соответствует схеме", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNullWithoutError()
    {
        var batch = QuestionBatch.TryLoad(Path.Combine(Path.GetTempPath(), "нет-такого-файла.json"), out var error);

        Assert.Null(batch);
        Assert.Null(error);
    }

    public void Dispose()
    {
        foreach (var file in files)
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    private string Write(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"customermail-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        files.Add(path);
        return path;
    }
}
