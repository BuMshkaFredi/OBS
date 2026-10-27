using System.Net;
using System.Net.Http;
using System.Text;
using ObsVoiceAssistant.Security;

namespace ObsVoiceAssistant.UnitTests;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri? Uri,
    string? Body,
    string? Authorization,
    string? ClientId);

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public int PendingResponses => responses.Count;

    public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> response) =>
        responses.Enqueue(response);

    public void EnqueueJson(HttpStatusCode status, string json) =>
        Enqueue(_ => TwitchHttp.Json(status, json));

    public void EnqueueEmpty(HttpStatusCode status) =>
        Enqueue(_ => TwitchHttp.Empty(status));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var clientId = request.Headers.TryGetValues("Client-Id", out var values)
            ? values.FirstOrDefault()
            : null;

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            body,
            request.Headers.Authorization?.ToString(),
            clientId));

        if (responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"Неожиданный запрос {request.Method} {request.RequestUri}");
        }

        return responses.Dequeue()(request);
    }
}

internal static class TwitchHttp
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage Empty(HttpStatusCode status) =>
        new(status)
        {
            Content = new StringContent(string.Empty)
        };
}

internal sealed class InMemorySecretStore : IProtectedSecretStore
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);

    public void Save(string key, string value) => values[key] = value;

    public string? Load(string key) => values.TryGetValue(key, out var value) ? value : null;

    public void Delete(string key) => values.Remove(key);
}
