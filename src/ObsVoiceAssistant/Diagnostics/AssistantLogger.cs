using System.IO;
using System.Text;

namespace ObsVoiceAssistant.Diagnostics;

public sealed class AssistantLogger
{
    private readonly object sync = new();
    private readonly string path;

    public AssistantLogger(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ObsVoiceAssistant");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "assistant.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}: {exception.GetType().Name}: {exception.Message}");

    private void Write(string level, string message)
    {
        lock (sync)
        {
            File.AppendAllText(path, $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
    }
}
