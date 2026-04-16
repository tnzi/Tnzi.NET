using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Tnzi.AI.Channels.Adapters.Dingtalk;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

public class DingtalkChannelAdapterTests
{
    [Fact]
    public void Name_ReturnsDingtalk()
    {
        var adapter = CreateAdapter();
        adapter.Name.ShouldBe("dingtalk");
    }

    [Fact]
    public void SupportsStreaming_ReturnsFalse()
    {
        var adapter = CreateAdapter();
        adapter.SupportsStreaming.ShouldBeFalse();
    }

    [Fact]
    public void Constructor_NullAppKey_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions { Enabled = true, AppKey = null, AppSecret = "secret" }
        });

        Should.Throw<ArgumentException>(() => new DingtalkChannelAdapter(
            NullLogger<DingtalkChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void Constructor_NullAppSecret_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions { Enabled = true, AppKey = "key", AppSecret = null }
        });

        Should.Throw<ArgumentException>(() => new DingtalkChannelAdapter(
            NullLogger<DingtalkChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void Constructor_EmptyAppKey_ThrowsArgumentException()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions { Enabled = true, AppKey = "   ", AppSecret = "secret" }
        });

        Should.Throw<ArgumentException>(() => new DingtalkChannelAdapter(
            NullLogger<DingtalkChannelAdapter>.Instance,
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
    public void AllowedUsers_Null_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedUsers: null);
        adapter.IsUserAllowed("U12345").ShouldBeTrue();
    }

    [Fact]
    public void AllowedUsers_Empty_MeansNoRestriction()
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
    public void AllowedOrganizations_Null_MeansNoRestriction()
    {
        var adapter = CreateAdapter(allowedOrganizations: null);
        adapter.IsOrganizationAllowed("org123").ShouldBeTrue();
    }

    [Fact]
    public void AllowedOrganizations_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedOrganizations: ["org1", "org2"]);
        adapter.IsOrganizationAllowed("org1").ShouldBeTrue();
        adapter.IsOrganizationAllowed("org2").ShouldBeTrue();
        adapter.IsOrganizationAllowed("org999").ShouldBeFalse();
    }

    [Fact]
    public async Task HandleEventAsync_ValidTextMessage_PublishesToBus()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            senderCorpId = "corp001",
            msgtype = "text",
            text = new { content = "Hello DingTalk" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChannelName.ShouldBe("dingtalk");
        received.ChatId.ShouldBe("conv001");
        received.UserId.ShouldBe("staff001");
        received.Text.ShouldBe("Hello DingTalk");
        received.Type.ShouldBe(InboundMessageType.Chat);
    }

    [Fact]
    public async Task HandleEventAsync_CommandMessage_DetectedCorrectly()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "/help" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.Type.ShouldBe(InboundMessageType.Command);
    }

    [Fact]
    public async Task HandleEventAsync_NonTextMessage_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "image",
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_EmptyText_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "   " },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedUser_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedUsers: ["staff999"]);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "Hello" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_NonAllowedOrganization_Ignored()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedOrganizations: ["corp999"]);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            senderCorpId = "corp001",
            msgtype = "text",
            text = new { content = "Hello" },
            conversationType = "1"
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
    public async Task HandleEventAsync_VerifySignatureEnabled_NoHeaders_RejectsEvent()
    {
        // 2026-04-15 audit fix: secure-by-default. When VerifyWebhookSignature is on
        // (the production default), the no-headers overload must reject events because
        // signature verification cannot be performed.
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, verifyWebhookSignature: true);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "should be rejected" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public void DingtalkOptions_ToString_RedactsSecrets()
    {
        var options = new DingtalkAdapterOptions
        {
            Enabled = true,
            AppKey = "public-key",
            AppSecret = "super-secret-value",
            RobotCode = "robot-1"
        };

        var text = options.ToString();

        text.ShouldNotContain("super-secret-value");
        text.ShouldContain("REDACTED");
        text.ShouldContain("robot-1");
    }

    [Fact]
    public async Task SendAsync_Success_PostsToDingtalkApi()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, new { processQueryKey = "abc123" });
        var tokenHandler = CreateMockHandler(HttpStatusCode.OK, new { accessToken = "test-token", expireIn = 7200 });
        var adapter = CreateAdapter(handler: handler, tokenHandler: tokenHandler);

        var message = new OutboundMessage(
            ChannelName: "dingtalk",
            ChatId: "conv001",
            ThreadId: Guid.NewGuid(),
            Text: "Hello DingTalk!");

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("oToMessages")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_LongMessage_ChunksCorrectly()
    {
        var handler = CreateMockHandler(HttpStatusCode.OK, new { processQueryKey = "abc123" });
        var tokenHandler = CreateMockHandler(HttpStatusCode.OK, new { accessToken = "test-token", expireIn = 7200 });
        var adapter = CreateAdapter(handler: handler, tokenHandler: tokenHandler, maxMessageLength: 10);

        var message = new OutboundMessage(
            ChannelName: "dingtalk",
            ChatId: "conv001",
            ThreadId: Guid.NewGuid(),
            Text: "12345678901234567890"); // 20 chars, split into 2 chunks

        await adapter.SendAsync(message);

        handler.Protected().Verify("SendAsync",
            Times.Exactly(2),
            ItExpr.Is<HttpRequestMessage>(r =>
                r.Method == HttpMethod.Post &&
                r.RequestUri!.ToString().Contains("oToMessages")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ApiError_Throws()
    {
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError, new { errcode = 500 });
        var tokenHandler = CreateMockHandler(HttpStatusCode.OK, new { accessToken = "test-token", expireIn = 7200 });
        var adapter = CreateAdapter(handler: handler, tokenHandler: tokenHandler, maxRetries: 0);

        var message = new OutboundMessage(
            ChannelName: "dingtalk",
            ChatId: "conv001",
            ThreadId: Guid.NewGuid(),
            Text: "Test");

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(message));
    }

    [Fact]
    public async Task SendFileAsync_AlwaysReturnsFalse()
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
            ChannelName: "dingtalk",
            ChatId: "conv001",
            ThreadId: Guid.NewGuid(),
            Text: "");

        var result = await adapter.SendFileAsync(message, attachment);
        result.ShouldBeFalse();
    }

    [Fact]
    public void ChannelsModule_RegistersDingtalk()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/ChannelsModule.cs");
        source.ShouldContain("DingtalkChannelAdapter");
        source.ShouldContain("options.Dingtalk.Enabled");
    }

    [Fact]
    public void DingtalkOptions_HasRequiredFields()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Dingtalk/DingtalkAdapterOptions.cs");
        source.ShouldContain("AppKey");
        source.ShouldContain("AppSecret");
        source.ShouldContain("RobotCode");
        source.ShouldContain("AllowedUsers");
        source.ShouldContain("AllowedOrganizations");
        source.ShouldContain("MaxMessageLength");
    }

    [Fact]
    public void ChannelsModuleOptions_HasDingtalkProperty()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptions.cs");
        source.ShouldContain("DingtalkAdapterOptions Dingtalk");
    }

    [Fact]
    public void OptionsValidator_RequiresDingtalkCredentials()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Options/ChannelsModuleOptionsValidator.cs");
        source.ShouldContain("options.Dingtalk.Enabled");
        source.ShouldContain("Dingtalk.AppKey");
        source.ShouldContain("Dingtalk.AppSecret");
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

    private static DingtalkChannelAdapter CreateAdapter(
        InMemoryChannelMessageBus? bus = null,
        List<string>? allowedUsers = null,
        List<string>? allowedOrganizations = null,
        Mock<HttpMessageHandler>? handler = null,
        Mock<HttpMessageHandler>? tokenHandler = null,
        int maxMessageLength = 20000,
        int maxRetries = 3,
        bool verifyWebhookSignature = false)
    {
        // Tests exercise the no-headers overload directly; default to
        // VerifyWebhookSignature=false so existing behavioral tests continue
        // to work. The production default is true (secure-by-default).
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions
            {
                Enabled = true,
                AppKey = "test-app-key",
                AppSecret = "test-app-secret",
                RobotCode = "test-robot-code",
                AllowedUsers = allowedUsers ?? [],
                AllowedOrganizations = allowedOrganizations ?? [],
                MaxMessageLength = maxMessageLength,
                MaxRetries = maxRetries,
                VerifyWebhookSignature = verifyWebhookSignature
            }
        });

        var factory = CreateMockHttpClientFactory(handler, tokenHandler).Object;

        return new DingtalkChannelAdapter(
            NullLogger<DingtalkChannelAdapter>.Instance,
            bus ?? new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            factory,
            options);
    }

    private static Mock<IHttpClientFactory> CreateMockHttpClientFactory(
        Mock<HttpMessageHandler>? handler = null,
        Mock<HttpMessageHandler>? tokenHandler = null)
    {
        // 使用组合 handler：token 请求走 tokenHandler，其他走 handler
        var combinedHandler = new Mock<HttpMessageHandler>();
        var defaultHandler = handler ?? CreateMockHandler(HttpStatusCode.OK, new { processQueryKey = "ok" });
        var defaultTokenHandler = tokenHandler ?? CreateMockHandler(HttpStatusCode.OK,
            new { accessToken = "test-access-token", expireIn = 7200 });

        combinedHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                var uri = req.RequestUri?.ToString() ?? "";
                if (uri.Contains("oauth2/accessToken") || uri.Contains("gettoken"))
                {
                    // 委托给 token handler
                    var method = defaultTokenHandler.Object.GetType()
                        .GetMethod("SendAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    return (Task<HttpResponseMessage>)method.Invoke(defaultTokenHandler.Object, [req, ct])!;
                }

                // 委托给 message handler
                var msgMethod = defaultHandler.Object.GetType()
                    .GetMethod("SendAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                return (Task<HttpResponseMessage>)msgMethod.Invoke(defaultHandler.Object, [req, ct])!;
            });

        var client = new HttpClient(combinedHandler.Object);

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Tnzi.AI.Dingtalk")).Returns(client);
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
