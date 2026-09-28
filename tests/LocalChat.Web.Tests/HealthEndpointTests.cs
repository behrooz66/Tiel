using System.Net;
using LocalChat.Web.Tests.Infrastructure;

namespace LocalChat.Web.Tests;

public sealed class HealthEndpointTests(LocalChatWebFactory factory) : IClassFixture<LocalChatWebFactory>
{
    [Fact]
    public async Task Health_returns_json()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
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
