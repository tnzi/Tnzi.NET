using System.Net;
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
        adapter.IsOrganizationAllowed(null).ShouldBeTrue();
    }

    [Fact]
    public void AllowedOrganizations_WithList_FiltersCorrectly()
    {
        var adapter = CreateAdapter(allowedOrganizations: ["org1", "org2"]);
        adapter.IsOrganizationAllowed("org1").ShouldBeTrue();
        adapter.IsOrganizationAllowed("org2").ShouldBeTrue();
        adapter.IsOrganizationAllowed("org999").ShouldBeFalse();
        adapter.IsOrganizationAllowed(null).ShouldBeFalse();
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
    public async Task HandleEventAsync_MissingCorpId_WithOrganizationAllowlist_Ignored()
    {
        // 配了 AllowedOrganizations 时，说不出自己属于哪个组织的消息不属于任何允许的组织
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedOrganizations: ["corp001"]);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "Hello" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100))).ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_MissingCorpId_NoOrganizationAllowlist_Published()
    {
        // 正控制：没配组织名单时缺 senderCorpId 照常放行
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, allowedOrganizations: []);

        var json = JsonSerializer.Serialize(new
        {
            conversationId = "conv001",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "Hello" },
            conversationType = "1"
        });

        await adapter.HandleEventAsync(json);

        (await TryConsumeAsync(bus, TimeSpan.FromSeconds(1))).ShouldNotBeNull();
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
    public async Task HandleEventAsync_CarriesConversationTypeAndSenderStaffIdForTheReply()
    {
        // 回复要知道走单聊还是群聊接口、单聊发给谁：ChatId（conversationId）不是任何员工的 id
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        await adapter.HandleEventAsync(JsonSerializer.Serialize(new
        {
            conversationId = "cidGroup==",
            senderStaffId = "manager123",
            msgtype = "text",
            text = new { content = "hello" },
            conversationType = "2"
        }));

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChatId.ShouldBe("cidGroup==");
        received.Metadata.ShouldNotBeNull();
        received.Metadata![DingtalkConversationMetadata.ConversationType].ShouldBe("2");
        received.Metadata[DingtalkConversationMetadata.SenderStaffId].ShouldBe("manager123");
    }

    [Fact]
    public async Task SendAsync_SingleChatReply_GoesToTheSenderStaffId_NotTheConversationId()
    {
        var http = new CapturingHandler();
        var adapter = CreateAdapter(http: http);

        await adapter.SendAsync(Reply("cidXXX==", "1", "manager123", "Hello DingTalk!"));

        var sent = http.Sends.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/v1.0/robot/oToMessages/batchSend");
        using var body = JsonDocument.Parse(sent.Body);
        body.RootElement.GetProperty("userIds").EnumerateArray().Select(e => e.GetString()).ShouldBe(["manager123"]);
        body.RootElement.GetProperty("robotCode").GetString().ShouldBe("test-robot-code");
        body.RootElement.TryGetProperty("openConversationId", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_GroupReply_GoesToTheGroupApiWithTheConversationId()
    {
        var http = new CapturingHandler();
        var adapter = CreateAdapter(http: http);

        await adapter.SendAsync(Reply("cidGroup==", "2", "manager123", "Hello group!"));

        var sent = http.Sends.ShouldHaveSingleItem();
        sent.Path.ShouldBe("/v1.0/robot/groupMessages/send");
        using var body = JsonDocument.Parse(sent.Body);
        body.RootElement.GetProperty("openConversationId").GetString().ShouldBe("cidGroup==");
        body.RootElement.GetProperty("robotCode").GetString().ShouldBe("test-robot-code");
        body.RootElement.TryGetProperty("userIds", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_ReplyWithoutConversationMetadata_SendsNothing_AndFails()
    {
        // 失败关闭：不知道会话类型时绝不再把会话 id 当员工 id 发（钉钉回 200 却什么都不投递）
        var http = new CapturingHandler();
        var adapter = CreateAdapter(http: http);

        var message = new OutboundMessage(ChannelName: "dingtalk", ChatId: "cidXXX==", ThreadId: Guid.NewGuid(), Text: "Hello");

        await Should.ThrowAsync<InvalidOperationException>(() => adapter.SendAsync(message));
        http.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_SingleChatReplyWithoutSenderStaffId_SendsNothing_AndFails()
    {
        var http = new CapturingHandler();
        var adapter = CreateAdapter(http: http);

        await Should.ThrowAsync<InvalidOperationException>(() => adapter.SendAsync(Reply("cidXXX==", "1", staffId: null, "Hello")));
        http.Sends.ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_RecipientReportedAsInvalidStaffId_IsAFailure()
    {
        // 200 + invalidStaffIdList：只看状态码会把「一条都没投递」记成成功
        var http = new CapturingHandler(sendResponse: """{"processQueryKey":"k","invalidStaffIdList":["manager123"],"flowControlledStaffIdList":[]}""");
        var adapter = CreateAdapter(http: http, maxRetries: 0);

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(Reply("cidXXX==", "1", "manager123", "Hello")));
    }

    [Fact]
    public async Task SendAsync_RecipientThrottled_IsAFailure()
    {
        var http = new CapturingHandler(sendResponse: """{"processQueryKey":"k","invalidStaffIdList":[],"flowControlledStaffIdList":["manager123"]}""");
        var adapter = CreateAdapter(http: http, maxRetries: 0);

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(Reply("cidXXX==", "1", "manager123", "Hello")));
    }

    [Fact]
    public async Task SendAsync_LongMessage_ChunksCorrectly()
    {
        var http = new CapturingHandler();
        var adapter = CreateAdapter(http: http, maxMessageLength: 10);

        await adapter.SendAsync(Reply("cidXXX==", "1", "manager123", "12345678901234567890")); // 20 chars, split into 2 chunks

        http.Sends.Count.ShouldBe(2);
        http.Sends.ShouldAllBe(s => s.Path == "/v1.0/robot/oToMessages/batchSend");
    }

    [Fact]
    public async Task SendAsync_ApiError_Throws()
    {
        var http = new CapturingHandler(sendStatus: HttpStatusCode.InternalServerError, sendResponse: """{"code":"500"}""");
        var adapter = CreateAdapter(http: http, maxRetries: 0);

        await Should.ThrowAsync<HttpRequestException>(() => adapter.SendAsync(Reply("cidGroup==", "2", "manager123", "Test")));
    }

    [Fact]
    public void Constructor_NullRobotCode_ThrowsArgumentException()
    {
        // 两条回复接口都以 robotCode 指明发送方机器人，缺了它每一条回复都会被拒
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions { Enabled = true, AppKey = "key", AppSecret = "secret", RobotCode = null }
        });

        Should.Throw<ArgumentException>(() => new DingtalkChannelAdapter(
            NullLogger<DingtalkChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void ChannelsModule_RegistersDingtalk()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/AIChannelsModule.cs");
        source.ShouldContain("DingtalkChannelAdapter");
        source.ShouldContain("options.Dingtalk.Enabled");
    }

    [Fact]
    public void DingtalkOptions_HasRequiredFields()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Adapters/Dingtalk/DingtalkAdapterOptions.cs");
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
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Options/ChannelsModuleOptions.cs");
        source.ShouldContain("DingtalkAdapterOptions Dingtalk");
    }

    [Fact]
    public void OptionsValidator_RequiresDingtalkCredentials()
    {
        var source = RepoRoot.ReadText("src/Tnzi.AI.Channels/Options/ChannelsModuleOptionsValidator.cs");
        source.ShouldContain("options.Dingtalk.Enabled");
        source.ShouldContain("Dingtalk.AppKey");
        source.ShouldContain("Dingtalk.AppSecret");
        source.ShouldContain("Dingtalk.RobotCode");
    }

    [Theory]
    [InlineData("1", "user")]
    [InlineData("2", "group")]
    public async Task HandleEventAsync_ReportsThePeerKindFromConversationType(string conversationType, string expected)
    {
        // 绑定规则的 PeerKind 按它匹配：不报出来，带 PeerKind 的规则永远不会命中
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus);

        await adapter.HandleEventAsync(JsonSerializer.Serialize(new
        {
            conversationId = "cid001==",
            senderStaffId = "staff001",
            msgtype = "text",
            text = new { content = "hi" },
            conversationType
        }));

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.PeerKind.ShouldBe(expected);
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

    /// <summary>一条带着入站元数据的回复（与 ChannelManager 构造回复的方式一致）。</summary>
    private static OutboundMessage Reply(string chatId, string conversationType, string? staffId, string text)
    {
        var metadata = new Dictionary<string, object> { [DingtalkConversationMetadata.ConversationType] = conversationType };
        if (staffId != null)
        {
            metadata[DingtalkConversationMetadata.SenderStaffId] = staffId;
        }

        return new OutboundMessage(ChannelName: "dingtalk", ChatId: chatId, ThreadId: Guid.NewGuid(), Text: text, Metadata: metadata);
    }

    /// <summary>记下每次发送的路径与请求体；令牌请求单独应答，不计入发送。</summary>
    private sealed class CapturingHandler(
        HttpStatusCode sendStatus = HttpStatusCode.OK,
        string sendResponse = """{"processQueryKey":"abc123"}""") : HttpMessageHandler
    {
        public List<(string Path, string Body)> Sends { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("oauth2/accessToken", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"accessToken":"test-token","expireIn":7200}""")
                };
            }

            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Sends.Add((path, body));
            return new HttpResponseMessage(sendStatus) { Content = new StringContent(sendResponse) };
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
        bool verifyWebhookSignature = false,
        HttpMessageHandler? http = null)
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

        IHttpClientFactory factory;
        if (http != null)
        {
            var capturing = new Mock<IHttpClientFactory>();
            capturing.Setup(f => f.CreateClient("Tnzi.AI.Dingtalk")).Returns(() => new HttpClient(http, disposeHandler: false));
            factory = capturing.Object;
        }
        else
        {
            factory = CreateMockHttpClientFactory(handler, tokenHandler).Object;
        }

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
