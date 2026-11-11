using System.Net;
using System.Net.Http.Json;
using Delily.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Delily.IntegrationTests;

public sealed class HttpSubmissionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HttpSubmissionEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SendMessage_ReturnsNotImplementedUntilPhase3b()
    {
        var request = new SendMessageRequest
        {
            From = new EmailAddress("sender@example.com"),
            To = [new EmailAddress("recipient@example.net")],
            Subject = "hello",
            Text = "body"
        };

        using var response = await _client.PostAsJsonAsync("/api/v1/messages", request);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetMessageStatus_ReturnsNotImplementedUntilPhase3b()
    {
        using var response = await _client.GetAsync("/api/v1/messages/msg_123");

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
