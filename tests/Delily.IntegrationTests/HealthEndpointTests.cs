using Microsoft.AspNetCore.Mvc.Testing;

namespace Delily.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Endpoint_ReturnsSuccess(string path)
    {
        using var response = await _client.GetAsync(path);

        response.EnsureSuccessStatusCode();
    }
}
