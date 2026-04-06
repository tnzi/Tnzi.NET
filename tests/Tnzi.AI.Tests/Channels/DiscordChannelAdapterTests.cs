using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
            new InMemoryChannelMessageBus(),
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
            new InMemoryChannelMessageBus(),
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
        var bus = new InMemoryChannelMessageBus();
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new { type = 1 });
        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_BotMessage_Ignored()
    {
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ThreadTs.ShouldBe("T001");
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedChannel_Ignored()
    {
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
        var bus = new InMemoryChannelMessageBus();
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
    public async Task SendFileAsync_FileNotFound_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        var attachment = new ResolvedAttachment(
            VirtualPath: "/test.txt",
            ActualPath: "/nonexistent/test.txt",
            FileName: "test.txt",
            ContentType: "text/plain",
            Size: 100,
            IsImage: false);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "");

        var result = await adapter.SendFileAsync(message, attachment);
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task SendFileAsync_TooLarge_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        var attachment = new ResolvedAttachment(
            VirtualPath: "/large.bin",
            ActualPath: "/nonexistent/large.bin",
            FileName: "large.bin",
            ContentType: "application/octet-stream",
            Size: 50 * 1024 * 1024, // 50MB, exceeds default 25MB
            IsImage: false);

        var message = new OutboundMessage(
            ChannelName: "discord",
            ChatId: "C001",
            ThreadId: Guid.NewGuid(),
            Text: "");

        var result = await adapter.SendFileAsync(message, attachment);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ChannelsModule_RegistersDiscord()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/ChannelsModule.cs");
        source.ShouldContain("DiscordChannelAdapter");
        source.ShouldContain("options.Discord.Enabled");
    }

    [Fact]
    public void DiscordOptions_HasRequiredFields()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Discord/DiscordAdapterOptions.cs");
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
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptions.cs");
        source.ShouldContain("DiscordAdapterOptions Discord");
    }

    [Fact]
    public void OptionsValidator_RequiresDiscordBotToken()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptionsValidator.cs");
        source.ShouldContain("options.Discord.Enabled");
        source.ShouldContain("Discord.BotToken");
    }

    [Fact]
    public void DiscordAdapter_UsesCorrectAuthScheme()
    {
        // Discord uses "Bot" prefix, not "Bearer" like Slack
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Discord/DiscordChannelAdapter.cs");
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
            bus ?? new InMemoryChannelMessageBus(),
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
