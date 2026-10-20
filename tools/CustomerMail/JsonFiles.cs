using System.Text.Json;
using System.Text.Json.Serialization;

namespace CustomerMail;

internal static class JsonFiles
{
    /// <summary>
    /// Неизвестные поля запрещены: файл вопросов не может протащить "to", "cc", "attachments"
    /// или любое другое поле, которого нет в схеме.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
}
