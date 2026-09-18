using System.Net;
using Moq.Protected;
using Tnzi.AI.Channels.Adapters.Discord;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

public class DiscordChannelAdapterTests
{
    [Fact]
    public void Name_ReturnsDiscord()
    {
        var adapter = CreateAdapter();
        adapter.Name.ShouldBe("discord");
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
            Discord = new DiscordAdapterOptions { Enabled = true, BotToken = null }
        });

        Should.Throw<ArgumentException>(() => new DiscordChannelAdapter(
            NullLogger<DiscordChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void Constructor_EmptyBotToken_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Discord = new DiscordAdapterOptions { Enabled = true, BotToken = "   " }
        });

        Should.Throw<ArgumentException>(() => new DiscordChannelAdapter(
            NullLogger<DiscordChannelAdapter>.Instance,
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
        adapter.IsUserAllowed("123456").ShouldBeTrue();
    }

    [Fact]
    public void AllowedUsers_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedUsers: ["100", "200"]);
        adapter.IsUserAllowed("100").ShouldBeTrue();
        adapter.IsUserAllowed("200").ShouldBeTrue();
        adapter.IsUserAllowed("999").ShouldBeFalse();
    }

    [Fact]
    public void AllowedChannels_EmptyList_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedChannels: []);
        adapter.IsChannelAllowed("123456").ShouldBeTrue();
    }

    [Fact]
    public void AllowedChannels_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedChannels: ["100", "200"]);
        adapter.IsChannelAllowed("100").ShouldBeTrue();
        adapter.IsChannelAllowed("200").ShouldBeTrue();
        adapter.IsChannelAllowed("999").ShouldBeFalse();
    }

    [Fact]
    public void AllowedGuilds_EmptyList_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedGuilds: []);
        adapter.IsGuildAllowed("123456").ShouldBeTrue();
    }

    [Fact]
    public void AllowedGuilds_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedGuilds: ["G100", "G200"]);
        adapter.IsGuildAllowed("G100").ShouldBeTrue();
        adapter.IsGuildAllowed("G200").ShouldBeTrue();
        adapter.IsGuildAllowed("G999").ShouldBeFalse();
    }

    [Fact]
    public async Task HandleEventAsync_PingInteraction_DoesNotPublish()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new { type = 1 });
        await adapter.HandleEventAsync(json);

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
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "Hello from bot",
                author = new { id = "U001", bot = true }
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_NonMessageEvent_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            t = "GUILD_MEMBER_ADD",
            d = new { user = new { id = "U001" } }
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
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "Hello world",
                author = new { id = "U001" }
            }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChannelName.ShouldBe("discord");
        received.ChatId.ShouldBe("C001");
        received.UserId.ShouldBe("U001");
        received.Text.ShouldBe("Hello world");
        received.Type.ShouldBe(InboundMessageType.Chat);
        received.ThreadTs.ShouldBe("msg001");
    }

    [Fact]
    public async Task HandleEventAsync_CommandMessage_DetectedCorrectly()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "/help",
                author = new { id = "U001" }
            }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.Type.ShouldBe(InboundMessageType.Command);
    }

    [Fact]
    public async Task HandleEventAsync_MessageWithReference_PreservesThreadId()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg002",
                channel_id = "C001",
                content = "reply in thread",
                author = new { id = "U001" },
                message_reference = new { channel_id = "T001", message_id = "msg001" }
            }
        });

        await adapter.HandleEventAsync(json);

        // message_reference.channel_id 是频道 id 不是消息 id，拿它当 message_reference.message_id 回复必错；
        // 线程频道里 channel_id 本身就是线程，回复引用只需要这条消息自己的 id。
        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ThreadTs.ShouldBe("msg002");
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedChannel_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedChannels: ["C999"]);

        var json = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "Hello",
                author = new { id = "U001" }
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
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "Hello",
                author = new { id = "U001" }
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedGuild_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedGuilds: ["G999"]);

        var json = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                guild_id = "G001",
                content = "Hello",
                author = new { id = "U001" }
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
    public async Task HandleEventAsync_EmptyContent_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new
            {
                id = "msg001",
                channel_id = "C001",
                content = "",
                author = new { id = "U001" }
            }
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_Success_PostsToDiscordApi()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler: handler);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Hello Discord!");

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("/channels/C001/messages")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_LongMessage_ChunksCorrectly()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK);
        var adapter = CreateAdapter(handler: handler, maxMessageLength: 10);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "12345678901234567890"); // 20 chars, split into 2 chunks

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("/channels/C001/messages")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WithThreadTs_IncludesMessageReference()
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
                Content = new StringContent("{}")
            });

        var adapter = CreateAdapter(handler: handler);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Reply in thread",
            ThreadTs: "msg001");

        await adapter.SendAsync(message);

        capturedBody.ShouldNotBeNull();
        capturedBody.ShouldContain("message_reference");
        capturedBody.ShouldContain("msg001");
    }

    // =====================================================================
    // Interactions（Discord 签过名的 HTTP 回调唯一会投递的形状）
    // =====================================================================

    [Fact]
    public async Task HandleEventAsync_ApplicationCommand_StringOption_PublishesChat()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C001", guild_id = "G1",
            member = new { user = new { id = "U001" } },
            data = new { name = "ask", type = 1, options = new[] { new { name = "prompt", type = 3, value = "Hello world" } } }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChatId.ShouldBe("C001");
        received.UserId.ShouldBe("U001");
        received.Text.ShouldBe("Hello world");
        received.Type.ShouldBe(InboundMessageType.Chat);
        received.Metadata.ShouldNotBeNull();
        received.Metadata[DiscordInteractionMetadata.Token].ShouldBe("itok");
        received.Metadata[DiscordInteractionMetadata.ApplicationId].ShouldBe("app1");
    }

    [Fact]
    public async Task HandleEventAsync_ApplicationCommand_NoOptions_PublishesSlashCommand()
    {
        // /new /status /help 这类无参数斜杠命令 → 按 "/{name}" 走命令路由
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C001",
            user = new { id = "U001" },
            data = new { name = "new", type = 1 }
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.Text.ShouldBe("/new");
        received.Type.ShouldBe(InboundMessageType.Command);
    }

    [Fact]
    public async Task HandleEventAsync_ApplicationCommand_NonAllowedGuild_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedGuilds: ["G999"]);

        var json = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C001", guild_id = "G1",
            member = new { user = new { id = "U001" } },
            data = new { name = "ask", type = 1, options = new[] { new { name = "prompt", type = 3, value = "hi" } } }
        });

        await adapter.HandleEventAsync(json);

        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100))).ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_WithInteractionToken_PatchesOriginalThenFollowsUp()
    {
        var requests = new List<(HttpMethod Method, string Url, string Body)>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
                requests.Add((req.Method, req.RequestUri!.ToString(), req.Content!.ReadAsStringAsync().Result)))
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var adapter = CreateAdapter(handler: handler, maxMessageLength: 10);
        var message = new OutboundMessage(
            ChannelName: "discord", ChatId: "C001", ThreadId: Guid.NewGuid(),
            Text: "0123456789abcdef",
            Metadata: new Dictionary<string, object>
            {
                [DiscordInteractionMetadata.Token] = "itok",
                [DiscordInteractionMetadata.ApplicationId] = "app1"
            });

        await adapter.SendAsync(message);

        requests.Count.ShouldBe(2);
        requests[0].Method.ShouldBe(HttpMethod.Patch);
        requests[0].Url.ShouldEndWith("/webhooks/app1/itok/messages/@original");
        requests[0].Body.ShouldContain("0123456789");
        requests[1].Method.ShouldBe(HttpMethod.Post);
        requests[1].Url.ShouldEndWith("/webhooks/app1/itok");
        requests[1].Body.ShouldContain("abcdef");
    }

    [Fact]
    public async Task SendAsync_WithInteractionToken_FirstChunkRetryStaysAPatch()
    {
        // 第一块的 PATCH 瞬时失败一次：重试必须还是 PATCH @original。此前「是不是第一块」在发送前就翻转，
        // 重试变成 POST follow-up，用户看到答案是一条后续消息，原来的斜杠命令停在 "thinking..." 直到令牌过期。
        var requests = new List<(HttpMethod Method, string Url)>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => requests.Add((req.Method, req.RequestUri!.ToString())))
            .ReturnsAsync(() => requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var adapter = CreateAdapter(handler: handler, maxMessageLength: 10, maxRetries: 1);
        var message = new OutboundMessage(
            ChannelName: "discord", ChatId: "C001", ThreadId: Guid.NewGuid(),
            Text: "0123456789abcdef",
            Metadata: new Dictionary<string, object>
            {
                [DiscordInteractionMetadata.Token] = "itok",
                [DiscordInteractionMetadata.ApplicationId] = "app1"
            });

        await adapter.SendAsync(message);

        requests.Count.ShouldBe(3);
        requests[0].Method.ShouldBe(HttpMethod.Patch);
        requests[1].Method.ShouldBe(HttpMethod.Patch, "the retried first chunk must still edit @original");
        requests[1].Url.ShouldEndWith("/messages/@original");
        requests[2].Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task SendAsync_ApiReturnsError_Throws()
    {
        var handler = CreateMockHandler(HttpStatusCode.Forbidden);
        var adapter = CreateAdapter(handler: handler, maxRetries: 0);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "Test");

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(message));
    }

    [Fact]
    public void ChannelsModule_RegistersDiscord()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/AIChannelsModule.cs");
        source.ShouldContain("DiscordChannelAdapter");
        source.ShouldContain("options.Discord.Enabled");
    }

    [Fact]
    public void DiscordOptions_HasRequiredFields()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Adapters/Discord/DiscordAdapterOptions.cs");
        source.ShouldContain("BotToken");
        source.ShouldContain("ApplicationId");
        source.ShouldContain("AllowedGuilds");
        source.ShouldContain("AllowedChannels");
        source.ShouldContain("AllowedUsers");
        source.ShouldContain("MaxMessageLength");
        source.ShouldContain("2000");
    }

    [Fact]
    public void ChannelsModuleOptions_HasDiscordProperty()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Options/ChannelsModuleOptions.cs");
        source.ShouldContain("DiscordAdapterOptions Discord");
    }

    [Fact]
    public void OptionsValidator_RequiresDiscordBotToken()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Options/ChannelsModuleOptionsValidator.cs");
        source.ShouldContain("options.Discord.Enabled");
        source.ShouldContain("Discord.BotToken");
    }

    [Fact]
    public void DiscordAdapter_UsesCorrectAuthScheme()
    {
        // Discord uses "Bot" prefix, not "Bearer" like Slack
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Adapters/Discord/DiscordChannelAdapter.cs");
        source.ShouldContain("\"Bot\"");
        source.ShouldContain("Tnzi.AI.Discord");
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

    private static DiscordChannelAdapter CreateAdapter(
        InMemoryChannelMessageBus? bus = null,
        List<string>? allowedUsers = null,
        List<string>? allowedChannels = null,
        List<string>? allowedGuilds = null,
        Mock<HttpMessageHandler>? handler = null,
        int maxMessageLength = 2000,
        int maxRetries = 3)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Discord = new DiscordAdapterOptions
            {
                Enabled = true,
                BotToken = "discord-test-bot-token-for-unit-tests",
                ApplicationId = "test-app-id",
                AllowedUsers = allowedUsers ?? [],
                AllowedChannels = allowedChannels ?? [],
                AllowedGuilds = allowedGuilds ?? [],
                MaxMessageLength = maxMessageLength,
                MaxRetries = maxRetries
            }
        });

        var factory = CreateMockHttpClientFactory(handler).Object;

        return new DiscordChannelAdapter(
            NullLogger<DiscordChannelAdapter>.Instance,
            bus ?? new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            factory,
            options);
    }

    private static Mock<IHttpClientFactory> CreateMockHttpClientFactory(Mock<HttpMessageHandler>? handler = null)
    {
        var mockHandler = handler ?? CreateMockHandler(HttpStatusCode.OK);
        var client = new HttpClient(mockHandler.Object);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Tnzi.AI.Discord")).Returns(client);
        return factory;
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{}")
            });
        return handler;
    }

    #endregion
}
