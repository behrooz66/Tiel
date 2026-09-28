using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LocalChat.Web.Tests.Infrastructure;

public enum FakeOllamaState
{
    Running,
    Stopped,
    Hanging,
    NotOllama,
}

/// <summary>A model as the fake reports it. A null <paramref name="Capabilities"/> omits the field, like older Ollama versions.</summary>
public sealed record FakeOllamaModel(
    string Tag,
    int? MaxContextLength = 131072,
    string[]? Capabilities = null,
    string Architecture = "phi3");

/// <summary>
/// Ollama's HTTP API in memory, with canned responses shaped like a real server's. Tests send OllamaSharp
/// through it, so they exercise the real client code and never need a running Ollama.
/// </summary>
public sealed class FakeOllama : HttpMessageHandler
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public FakeOllamaState State { get; set; } = FakeOllamaState.Running;
    public string Version { get; set; } = "0.34.4";
    public List<FakeOllamaModel> Models { get; } = [];

    /// <summary>Every request URI, oldest first.</summary>
    public IReadOnlyCollection<Uri> Requests => _requests;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        _requests.Enqueue(uri);

        switch (State)
        {
            case FakeOllamaState.Stopped:
                throw new HttpRequestException($"Connection refused ({uri.Authority})");
            case FakeOllamaState.Hanging:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                break;
            case FakeOllamaState.NotOllama:
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Router login</html>") };
        }

        var path = uri.AbsolutePath;
        if (path.EndsWith("/api/version", StringComparison.Ordinal))
        {
            return Json(new { version = Version });
        }

        if (path.EndsWith("/api/tags", StringComparison.Ordinal))
        {
            return Json(new { models = Models.Select(ToTagsEntry) });
        }

        if (path.EndsWith("/api/show", StringComparison.Ordinal))
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var tag = body.TryGetProperty("model", out var model) ? model.GetString() : body.GetProperty("name").GetString();
            var found = Models.SingleOrDefault(m => m.Tag == tag);
            return found is null
                ? Json(new { error = $"model '{tag}' not found" }, HttpStatusCode.NotFound)
                : Json(ToShowResponse(found));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static object ToTagsEntry(FakeOllamaModel model) => new
    {
        name = model.Tag,
        model = model.Tag,
        modified_at = "2026-09-28T12:00:00Z",
        size = 2_500_000_000L,
        digest = "0123456789abcdef",
        details = new { format = "gguf", family = model.Architecture, parameter_size = "3.8B", quantization_level = "Q4_K_M" },
    };

    private static Dictionary<string, object?> ToShowResponse(FakeOllamaModel model)
    {
        var modelInfo = new Dictionary<string, object> { ["general.architecture"] = model.Architecture };
        if (model.MaxContextLength is { } max)
        {
            modelInfo[$"{model.Architecture}.context_length"] = max;
        }

        // Real models also report keys like this one; only ".context_length" is the maximum.
        modelInfo[$"{model.Architecture}.rope.scaling.original_context_length"] = 4096;

        var response = new Dictionary<string, object?>
        {
            ["modelfile"] = "",
            ["template"] = "{{ .Prompt }}",
            ["details"] = new { format = "gguf", family = model.Architecture },
            ["model_info"] = modelInfo,
            ["modified_at"] = "2026-09-28T12:00:00Z",
        };
        if (model.Capabilities is not null)
        {
            response["capabilities"] = model.Capabilities;
        }

        return response;
    }

    private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value) };
}
