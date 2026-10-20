using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ObsVoiceAssistant.Security;

public interface IProtectedSecretStore
{
    void Save(string key, string value);
    string? Load(string key);
    void Delete(string key);
}

public sealed class DpapiSecretStore
{
    private readonly string directory;

    public DpapiSecretStore(string? directory = null)
    {
        this.directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ObsVoiceAssistant",
            "secrets");
    }

    public void Save(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        Directory.CreateDirectory(directory);

        var protectedValue = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        File.WriteAllBytes(GetPath(key), protectedValue);
    }

    public string? Load(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        var protectedValue = File.ReadAllBytes(path);
        var value = ProtectedData.Unprotect(
            protectedValue,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        return Encoding.UTF8.GetString(value);
    }

    public void Delete(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetPath(string key)
    {
        var safeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(directory, $"{safeKey}.bin");
    }
}
