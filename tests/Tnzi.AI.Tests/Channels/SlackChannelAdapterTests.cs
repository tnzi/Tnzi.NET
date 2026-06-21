using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Tnzi.AI.Channels.Adapters.Slack;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

public class SlackChannelAdapterTests
{
    [Fact]
    public void Name_ReturnsSlack()
    {
        var adapter = CreateAdapter();
        adapter.Name.ShouldBe("slack");
    }

    [Fact]
    public void SupportsStreaming_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        adapter.SupportsStreaming.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_NullBotToken_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = null }
        });

        Should.Throw<ArgumentException>(() => new SlackChannelAdapter(
            NullLogger<SlackChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void Constructor_EmptyBotToken_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = "   " }
        });

        Should.Throw<ArgumentException>(() => new SlackChannelAdapter(
            NullLogger<SlackChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var adapter = CreateAdapter();
        await adapter.StopAsync();
    }

    [Fact]
    public async Task DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var adapter = CreateAdapter();
        await adapter.DisposeAsync();
        await adapter.DisposeAsync();
    }

    [Fact]
    public void AllowedUsers_EmptyList_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedUsers: []);
        adapter.IsUserAllowed("U12345").ShouldBeTrue();
    }

    [Fact]
    public void AllowedUsers_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedUsers: ["U100", "U200"]);
        adapter.IsUserAllowed("U100").ShouldBeTrue();
        adapter.IsUserAllowed("U200").ShouldBeTrue();
        adapter.IsUserAllowed("U999").ShouldBeFalse();
    }

    [Fact]
    public void AllowedChannels_EmptyList_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedChannels: []);
        adapter.IsChannelAllowed("C12345").ShouldBeTrue();
    }

    [Fact]
    public void AllowedChannels_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedChannels: ["C100", "C200"]);
        adapter.IsChannelAllowed("C100").ShouldBeTrue();
        adapter.IsChannelAllowed("C200").ShouldBeTrue();
        adapter.IsChannelAllowed("C999").ShouldBeFalse();
    }

    [Fact]
    public async Task HandleEventAsync_ChallengeEvent_DoesNotPublish()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new { challenge = "test-challenge", type = "url_verification" });
        await adapter.HandleEventAsync(json);

        // ConsumeInboundAsync should timeout since nothing was published
        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_BotMessage_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                bot_id = "B123",
                channel = "C001",
                user = "U001",
                text = "Hello from bot",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_SubtypeMessage_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                subtype = "message_changed",
                channel = "C001",
                user = "U001",
                text = "edited text",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_ValidMessage_PublishesToBus()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                channel = "C001",
                user = "U001",
                text = "Hello world",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChannelName.ShouldBe("slack");
        received.ChatId.ShouldBe("C001");
        received.UserId.ShouldBe("U001");
        received.Text.ShouldBe("Hello world");
        received.Type.ShouldBe(InboundMessageType.Chat);
        received.ThreadTs.ShouldBe("1234567890.123456");
    }

    [Fact]
    public async Task HandleEventAsync_CommandMessage_DetectedCorrectly()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                channel = "C001",
                user = "U001",
                text = "/help",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.Type.ShouldBe(InboundMessageType.Command);
    }

    [Fact]
    public async Task HandleEventAsync_ThreadMessage_PreservesThreadTs()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                channel = "C001",
                user = "U001",
                text = "reply in thread",
                ts = "1234567890.999999",
                thread_ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ThreadTs.ShouldBe("1234567890.123456");
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedChannel_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedChannels: ["C999"]);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                channel = "C001",
                user = "U001",
                text = "Hello",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedUser_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedUsers: ["U999"]);

        var json = JsonSerializer.Serialize(new
        {
            @event = new
            {
                type = "message",
                channel = "C001",
                user = "U001",
                text = "Hello",
                ts = "1234567890.123456"
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_InvalidJson_DoesNotThrow()
    {
        var adapter = CreateAdapter();
        await adapter.HandleEventAsync("not valid json");
    }

    [Fact]
    public async Task SendAsync_Success_PostsToSlackApi()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, new { ok = true });
        var adapter = CreateAdapter(handler: handler);

        var message = new OutboundMessage(
            ChannelName: "slack",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Hello Slack!");

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("chat.postMessage")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_LongMessage_ChunksCorrectly()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, new { ok = true });
        var adapter = CreateAdapter(handler: handler, maxMessageLength: 10);

        var message = new OutboundMessage(
            ChannelName: "slack",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "12345678901234567890"); // 20 chars, split into 2 chunks

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("chat.postMessage")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WithThreadTs_IncludesInPayload()
    {
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { ok = true }))
            });

        var adapter = CreateAdapter(handler: handler);

        var message = new OutboundMessage(
            ChannelName: "slack",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Reply in thread",
            ThreadTs: "1234567890.123456");

        await adapter.SendAsync(message);

        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("thread_ts");
        capturedBody.ShouldContain("1234567890.123456");
    }

    [Fact]
    public async Task SendAsync_ApiReturnsOkFalse_Throws()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, new { ok = false, error = "channel_not_found" });
        var adapter = CreateAdapter(handler: handler, maxRetries: 0);

        var message = new OutboundMessage(
            ChannelName: "slack",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Test");

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(message));
    }

    [Fact]
    public void ChannelsModule_RegistersSlack()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/ChannelsModule.cs");
        source.ShouldContain("SlackChannelAdapter");
        source.ShouldContain("options.Slack.Enabled");
    }

    [Fact]
    public void SlackOptions_HasRequiredFields()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Slack/SlackAdapterOptions.cs");
        source.ShouldContain("BotToken");
        source.ShouldContain("AppToken");
        source.ShouldContain("SigningSecret");
        source.ShouldContain("AllowedChannels");
        source.ShouldContain("AllowedUsers");
        source.ShouldContain("MaxMessageLength");
    }

    [Fact]
    public void ChannelsModuleOptions_HasSlackProperty()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptions.cs");
        source.ShouldContain("SlackAdapterOptions Slack");
    }

    [Fact]
    public void OptionsValidator_RequiresSlackBotToken()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptionsValidator.cs");
        source.ShouldContain("options.Slack.Enabled");
        source.ShouldContain("Slack.BotToken");
    }

    #region Helpers

    /// <summary>
    /// 尝试从消息总线消费一条入站消息，超时返回 null
    /// </summary>
    private static async Task<InboundMessage?> TryConsumeAsync(InMemoryChannelMessageBus bus, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await bus.ConsumeInboundAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static SlackChannelAdapter CreateAdapter(
        InMemoryChannelMessageBus? bus = null,
        List<string>? allowedUsers = null,
        List<string>? allowedChannels = null,
        Mock<HttpMessageHandler>? handler = null,
        int maxMessageLength = 4000,
        int maxRetries = 3)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Slack = new SlackAdapterOptions
            {
                Enabled = true,
                BotToken = "xoxb-test-token-for-unit-tests",
                AllowedUsers = allowedUsers ?? [],
                AllowedChannels = allowedChannels ?? [],
                MaxMessageLength = maxMessageLength,
                MaxRetries = maxRetries
            }
        });

        var factory = CreateMockHttpClientFactory(handler).Object;

        return new SlackChannelAdapter(
            NullLogger<SlackChannelAdapter>.Instance,
            bus ?? new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            factory,
            options);
    }

    private static Mock<IHttpClientFactory> CreateMockHttpClientFactory(Mock<HttpMessageHandler>? handler = null)
    {
        var mockHandler = handler ?? CreateMockHandler(HttpStatusCode.OK, new { ok = true });
        var client = new HttpClient(mockHandler.Object);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Tnzi.AI.Slack")).Returns(client);
        return factory;
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, object responseBody)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(JsonSerializer.Serialize(responseBody))
            });
        return handler;
    }

    #endregion
}
