using System.Net;

namespace Tnzi.AI.Tests;

/// <summary>
/// HttpA2AClient 单元测试 - 验证 SSRF 防护：私有端点在发送 HTTP 前返回错误结果，
/// 以及 302 重定向不被跟随（防止通过 Location 头绕过 EgressGuard）
/// </summary>
public class HttpA2AClientTests
{
    // A fake HttpClientFactory that should never be called for blocked endpoints
    private static IHttpClientFactory CreateNeverCalledFactory()
    {
        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Callback<string>(_ => throw new InvalidOperationException("HttpClient.CreateClient must not be called for blocked endpoints"));
        return mock.Object;
    }

    /// <summary>
    /// Creates an IHttpClientFactory that returns an HttpClient backed by a fake handler.
    /// The handler records all request URIs so tests can assert no secondary request was made.
    /// </summary>
    private static (IHttpClientFactory Factory, List<Uri> CapturedUris) CreateFactoryWithHandler(HttpResponseMessage response)
    {
        var capturedUris = new List<Uri>();
        var handler = new RecordingHandler(capturedUris, response);
        var client = new HttpClient(handler) { BaseAddress = null };

        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return (mock.Object, capturedUris);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://192.168.1.1/")]
    public async Task SendTaskAsync_PrivateEndpoint_ReturnsErrorWithoutSendingRequest(string endpoint)
    {
        // Arrange - HttpClientFactory that asserts it is never called
        var factory = CreateNeverCalledFactory();
        var client = new HttpA2AClient(factory, NullLogger<HttpA2AClient>.Instance);
        var request = new A2ATaskRequest { Input = "test" };

        // Act - must not throw; must return failed status
        var result = await client.SendTaskAsync(endpoint, request);

        // Assert
        result.Status.ShouldBe("failed");
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("blocked");
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://169.254.169.254/")]
    public async Task GetTaskStatusAsync_PrivateEndpoint_ReturnsErrorWithoutSendingRequest(string endpoint)
    {
        var factory = CreateNeverCalledFactory();
        var client = new HttpA2AClient(factory, NullLogger<HttpA2AClient>.Instance);

        var result = await client.GetTaskStatusAsync(endpoint, "task-1");

        result.Status.ShouldBe("failed");
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("blocked");
    }

    // ------------------------------------------------------------------
    // FIX 1: SSRF redirect-follow prevention - 302 must not be followed
    // ------------------------------------------------------------------

    [Fact]
    public async Task SendTaskAsync_302RedirectToPrivateIp_ReturnsFailureAndNoSecondRequest()
    {
        // Arrange: server responds with 302 → private metadata IP
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirectResponse.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");

        var (factory, capturedUris) = CreateFactoryWithHandler(redirectResponse);
        var a2aClient = new HttpA2AClient(factory, NullLogger<HttpA2AClient>.Instance);
        var request = new A2ATaskRequest { Input = "test" };

        // Act - endpoint is a public-looking host (passes EgressGuard for initial check)
        var result = await a2aClient.SendTaskAsync("https://8.8.8.8", request);

        // Assert: failure returned, redirect NOT followed
        result.Status.ShouldBe("failed");
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("redirect");

        // Only ONE request was made (to the original endpoint) - no second request to the private IP
        capturedUris.Count.ShouldBe(1);
        capturedUris[0].Host.ShouldBe("8.8.8.8");
    }

    [Fact]
    public async Task GetTaskStatusAsync_302RedirectToPrivateIp_ReturnsFailureAndNoSecondRequest()
    {
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirectResponse.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");

        var (factory, capturedUris) = CreateFactoryWithHandler(redirectResponse);
        var a2aClient = new HttpA2AClient(factory, NullLogger<HttpA2AClient>.Instance);

        var result = await a2aClient.GetTaskStatusAsync("https://8.8.8.8", "task-1");

        result.Status.ShouldBe("failed");
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("redirect");

        capturedUris.Count.ShouldBe(1);
        capturedUris[0].Host.ShouldBe("8.8.8.8");
    }

    [Fact]
    public async Task DiscoverAsync_302RedirectToPrivateIp_ThrowsAndNoSecondRequest()
    {
        var redirectResponse = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirectResponse.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");

        var (factory, capturedUris) = CreateFactoryWithHandler(redirectResponse);
        var a2aClient = new HttpA2AClient(factory, NullLogger<HttpA2AClient>.Instance);

        // Discover throws on redirect (matching the pre-existing error-shape for Discover)
        await Should.ThrowAsync<InvalidOperationException>(
            async () => await a2aClient.DiscoverAsync("https://8.8.8.8"));

        capturedUris.Count.ShouldBe(1);
        capturedUris[0].Host.ShouldBe("8.8.8.8");
    }

    // ------------------------------------------------------------------
    // Helper: fake DelegatingHandler that records requests and returns a preset response
    // ------------------------------------------------------------------

    private sealed class RecordingHandler(List<Uri> capturedUris, HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capturedUris.Add(request.RequestUri!);
            return Task.FromResult(response);
        }
    }
}
