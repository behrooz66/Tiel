using System.Net;
using System.Net.Http.Json;
using LocalChat.Web.Services;
using LocalChat.Web.Tests.Infrastructure;

namespace LocalChat.Web.Tests;

public sealed class HealthEndpointTests(LocalChatWebFactory factory) : IClassFixture<LocalChatWebFactory>
{
    [Fact]
    public async Task Health_returns_the_status_as_json()
    {
        factory.Ollama.State = FakeOllamaState.Running;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var status = await response.Content.ReadFromJsonAsync<HealthStatus>(TestContext.Current.CancellationToken);
        Assert.Equal(new HealthStatus(true, "0.34.4", "http://localhost:11434"), status);
    }

    [Fact]
    public async Task The_app_starts_without_ollama_and_health_reports_it_unreachable()
    {
        await using var app = new LocalChatWebFactory();
        app.Ollama.State = FakeOllamaState.Stopped;
        using var client = app.CreateClient();

        var status = await client.GetFromJsonAsync<HealthStatus>("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(new HealthStatus(false, null, "http://localhost:11434"), status);
    }

    [Fact]
    public async Task Requests_for_other_hosts_are_rejected()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        request.Headers.Host = "example.com";

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
