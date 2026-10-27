namespace CustomerMail;

internal static class RepoLayout
{
    private const string SolutionMarker = "ObsVoiceAssistant.slnx";

    public static string FindRoot(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Не найден корень репозитория: вверх по дереву нет {SolutionMarker}. Запустите команду из папки проекта.");
    }

    public static string CustomerQuestions(string root) => Path.Combine(root, "docs", "customer-questions");

    public static string PolicyPath(string root) => Path.Combine(CustomerQuestions(root), "policy.json");

    public static string BatchesDirectory(string root) => Path.Combine(CustomerQuestions(root), "batches");

    public static string ReleasesDirectory(string root) => Path.Combine(CustomerQuestions(root), "releases");

    public static string AnswersDirectory(string root) => Path.Combine(CustomerQuestions(root), "answers");

    public static string AuditDirectory(string root) => Path.Combine(CustomerQuestions(root), "audit");
}
