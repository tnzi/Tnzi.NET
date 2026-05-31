using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.AI.Channels.Abstractions;
using Tnzi.AI.Channels.Controllers;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// D2 — DefaultChannelWebhookController: routes to the matching platform adapter by name,
/// reads the raw body, returns the adapter's outcome as HTTP (challenge / 200 / 401),
/// and 404 for unknown platforms.
/// </summary>
public class ChannelWebhookControllerTests
{
    private sealed class StubWebhookAdapter : IInboundWebhookAdapter
    {
        private readonly Func<string, IReadOnlyDictionary<string, string>, WebhookProcessResult> _handler;
        public string Platform { get; }
        public string? LastBody { get; private set; }
        public int CallCount { get; private set; }

        public StubWebhookAdapter(string platform, Func<string, IReadOnlyDictionary<string, string>, WebhookProcessResult> handler)
        {
            Platform = platform;
            _handler = handler;
        }

        public Task<WebhookProcessResult> ProcessWebhookAsync(
            string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
        {
            CallCount++;
            LastBody = rawBody;
            return Task.FromResult(_handler(rawBody, headers));
        }
    }

    private static DefaultChannelWebhookController CreateController(
        IEnumerable<IInboundWebhookAdapter> adapters, string body, IDictionary<string, string>? headers = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.ContentLength = body.Length;
        if (headers != null)
        {
            foreach (var (k, v) in headers) httpContext.Request.Headers[k] = v;
        }

        return new DefaultChannelWebhookController(adapters)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task Receive_ValidEvent_DispatchesAndReturns200()
    {
        var stub = new StubWebhookAdapter("slack", (_, _) => WebhookProcessResult.Accepted());
        var controller = CreateController([stub], """{"type":"event_callback"}""");

        var result = await controller.Receive("slack");

        stub.CallCount.ShouldBe(1);
        stub.LastBody.ShouldBe("""{"type":"event_callback"}""");
        var content = result.ShouldBeOfType<ContentResult>();
        content.StatusCode.ShouldBeNull(); // ContentResult defaults to 200
    }

    [Fact]
    public async Task Receive_InvalidSignature_Returns401_AndStillDispatchedToAdapterOnly()
    {
        // The adapter decides rejection; the controller surfaces it as 401 and does NOT dispatch downstream.
        var stub = new StubWebhookAdapter("discord", (_, _) => WebhookProcessResult.Rejected("bad sig"));
        var controller = CreateController([stub], """{"type":2}""");

        var result = await controller.Receive("discord");

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Receive_Challenge_EchoesChallengeBody()
    {
        var stub = new StubWebhookAdapter("feishu", (_, _) => WebhookProcessResult.Challenge("""{"challenge":"x"}"""));
        var controller = CreateController([stub], """{"type":"url_verification","challenge":"x"}""");

        var result = await controller.Receive("feishu");

        var content = result.ShouldBeOfType<ContentResult>();
        content.Content.ShouldBe("""{"challenge":"x"}""");
    }

    [Fact]
    public async Task Receive_UnknownPlatform_Returns404_NoAdapterInvoked()
    {
        var stub = new StubWebhookAdapter("slack", (_, _) => WebhookProcessResult.Accepted());
        var controller = CreateController([stub], "{}");

        var result = await controller.Receive("telegram");

        result.ShouldBeOfType<NotFoundResult>();
        stub.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Receive_NoAdaptersLoaded_Returns404()
    {
        var controller = CreateController([], "{}");

        var result = await controller.Receive("slack");

        result.ShouldBeOfType<NotFoundResult>();
    }
}
