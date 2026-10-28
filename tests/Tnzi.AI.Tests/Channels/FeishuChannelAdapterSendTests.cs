using System.Net;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// Feishu reports most send failures (bot removed from the chat, missing scope) as HTTP 200 with a
/// non-zero <c>code</c>. A send that the API rejected must fail, the way every other adapter's does,
/// so the shared sender retries it and the outbound pipeline logs it, instead of being dropped.
/// </summary>
public class FeishuChannelAdapterSendTests
{
    [Fact]
    public async Task SendAsync_BusinessErrorCode_Throws()
    {
        var handler = new FeishuApiStub(HttpStatusCode.OK, """{"code":230002,"msg":"Bot is not in the chat"}""");
        var adapter = CreateAdapter(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, "hello")));

        ex.Message.ShouldContain("230002");
        handler.MessagePosts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_HttpError_Throws()
    {
        var handler = new FeishuApiStub(HttpStatusCode.BadRequest, """{"code":99991663,"msg":"invalid receive_id"}""");
        var adapter = CreateAdapter(handler);

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, "hello")));

        ex.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SendAsync_Accepted_PostsTheTextToTheChat()
    {
        var handler = new FeishuApiStub(HttpStatusCode.OK, """{"code":0,"msg":"success","data":{}}""");
        var adapter = CreateAdapter(handler);

        await adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, "hello"));

        var post = handler.MessagePosts.ShouldHaveSingleItem();
        using var doc = JsonDocument.Parse(post);
        doc.RootElement.GetProperty("receive_id").GetString().ShouldBe("oc_chat");
        using var content = JsonDocument.Parse(doc.RootElement.GetProperty("content").GetString()!);
        content.RootElement.GetProperty("text").GetString().ShouldBe("hello");
    }

    [Fact]
    public async Task SendAsync_LongText_IsSentInChunks()
    {
        var handler = new FeishuApiStub(HttpStatusCode.OK, """{"code":0,"msg":"success"}""");
        var adapter = CreateAdapter(handler, maxMessageLength: 4);

        await adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, "abcdefghij"));

        var texts = handler.MessagePosts.Select(p =>
        {
            using var doc = JsonDocument.Parse(p);
            using var content = JsonDocument.Parse(doc.RootElement.GetProperty("content").GetString()!);
            return content.RootElement.GetProperty("text").GetString();
        }).ToList();
        texts.ShouldBe(["abcd", "efgh", "ij"]);
    }

    // Each chunk is its own message, so a cut inside a surrogate pair (an emoji) would send two halves that
    // are not valid text. The chunk ends before the pair instead; at length 1 the whole pair goes out so the
    // loop still advances.
    [Theory]
    [InlineData(4, "abc\U0001F600def", new[] { "abc", "\U0001F600de", "f" })]
    [InlineData(1, "\U0001F600a", new[] { "\U0001F600", "a" })]
    public async Task SendAsync_ChunkBoundaryInsideASurrogatePair_KeepsThePairTogether(int maxMessageLength, string text, string[] expected)
    {
        var handler = new FeishuApiStub(HttpStatusCode.OK, """{"code":0,"msg":"success"}""");
        var adapter = CreateAdapter(handler, maxMessageLength: maxMessageLength);

        await adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, text));

        var texts = handler.MessagePosts.Select(p =>
        {
            using var doc = JsonDocument.Parse(p);
            using var content = JsonDocument.Parse(doc.RootElement.GetProperty("content").GetString()!);
            return content.RootElement.GetProperty("text").GetString();
        }).ToList();
        texts.ShouldBe(expected);
    }

    // The shared chunked sender guards the chunk length itself: a length of 0 used to cut empty
    // chunks forever without consuming the text. Startup validation covers configuration; this
    // covers any caller that gets past it.
    [Fact]
    public async Task SendAsync_ZeroChunkLength_FailsWithoutSending()
    {
        var handler = new FeishuApiStub(HttpStatusCode.OK, """{"code":0,"msg":"success"}""");
        var adapter = CreateAdapter(handler, maxMessageLength: 0);

        var send = adapter.SendAsync(new OutboundMessage("feishu", "oc_chat", Guid.Empty, "hello"));
        var finished = await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(5)));

        finished.ShouldBe(send, "a zero chunk length must not loop");
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => send);
        handler.MessagePosts.ShouldBeEmpty();
    }

    private static FeishuChannelAdapter CreateAdapter(FeishuApiStub handler, int maxMessageLength = 10000)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions
            {
                Enabled = true,
                AppId = "cli_test_app",
                AppSecret = "test-secret",
                MaxMessageLength = maxMessageLength,
                // No retries: a rejected send must surface at once (the retry delays are real time).
                MaxRetries = 0
            }
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));

        return new FeishuChannelAdapter(
            NullLogger<FeishuChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            factory.Object,
            options);
    }

    /// <summary>Answers the token endpoint with a token and every message post with the given response.</summary>
    private sealed class FeishuApiStub(HttpStatusCode messageStatus, string messageBody) : HttpMessageHandler
    {
        private const int MaxPosts = 100;

        public List<string> MessagePosts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/tenant_access_token/internal", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"code":0,"tenant_access_token":"t","expire":7200}""")
                };
            }

            // A sender that stops making progress would post forever; fail it instead of hanging the run.
            if (MessagePosts.Count >= MaxPosts)
            {
                throw new InvalidOperationException("Runaway send loop: too many message posts.");
            }

            MessagePosts.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(messageStatus) { Content = new StringContent(messageBody) };
        }
    }
}
