using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

public class FeishuChannelAdapterTests
{
    private const string TestEncryptKey = "test-encrypt-key-for-unit-tests";

    [Fact]
    public void Name_ReturnsFeishu()
    {
        var adapter = CreateAdapter();
        adapter.Name.ShouldBe("feishu");
    }

    [Fact]
    public void SupportsStreaming_ReturnsTrue()
    {
        var adapter = CreateAdapter();
        adapter.SupportsStreaming.ShouldBeTrue();
    }

    [Fact]
    public void Constructor_EmptyAppId_Throws()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "", AppSecret = "s" }
        });

        Should.Throw<ArgumentException>(() => new FeishuChannelAdapter(
            NullLogger<FeishuChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public void Constructor_EmptyAppSecret_Throws()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "a", AppSecret = "" }
        });

        Should.Throw<ArgumentException>(() => new FeishuChannelAdapter(
            NullLogger<FeishuChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options));
    }

    [Fact]
    public async Task HandleEventAsync_NoEncryptKey_NoHeaders_ProcessesEvent()
    {
        // Without EncryptKey configured, signature check is skipped (back-compat path).
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: null);

        var json = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        await adapter.HandleEventAsync(json);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task HandleEventAsync_EncryptKeyConfigured_NoHeaders_RejectsEvent()
    {
        // 2026-04-15 audit fix: when EncryptKey is set but the no-headers overload
        // is invoked, the adapter must reject the event because signature verification
        // cannot be performed.
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var json = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_ValidSignature_ProcessesEvent()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var body = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        var headers = BuildValidHeaders(body, TestEncryptKey);

        await adapter.HandleEventAsync(body, headers);

        var received = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        received.ShouldNotBeNull();
        received.ChannelName.ShouldBe("feishu");
        received.ChatId.ShouldBe("oc_chat_1");
        received.UserId.ShouldBe("ou_user_1");
        received.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task HandleEventAsync_InvalidSignature_RejectsEvent()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var body = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        var headers = BuildValidHeaders(body, TestEncryptKey);
        headers["X-Lark-Signature"] = "0000000000000000000000000000000000000000000000000000000000000000";

        await adapter.HandleEventAsync(body, headers);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_WrongEncryptKey_RejectsEvent()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var body = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        // Sign using a DIFFERENT key to simulate forged request
        var headers = BuildValidHeaders(body, "attacker-key");

        await adapter.HandleEventAsync(body, headers);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_StaleTimestamp_RejectsEvent()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var body = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        // 10 minutes ago — exceeds 5 minute window
        var staleTimestamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 600).ToString();
        var nonce = "abc123";
        var signature = ComputeSha256Hex(staleTimestamp + nonce + TestEncryptKey + body);

        var headers = new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = staleTimestamp,
            ["X-Lark-Request-Nonce"] = nonce,
            ["X-Lark-Signature"] = signature
        };

        await adapter.HandleEventAsync(body, headers);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_MissingSignatureHeader_RejectsEvent()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var body = SerializeTextMessage("oc_chat_1", "ou_user_1", "hello");
        var headers = BuildValidHeaders(body, TestEncryptKey);
        headers.Remove("X-Lark-Signature");

        await adapter.HandleEventAsync(body, headers);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_ReplayWithDifferentBody_RejectsEvent()
    {
        // Attacker replays a valid header set but swaps the body
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(bus: bus, encryptKey: TestEncryptKey);

        var originalBody = SerializeTextMessage("oc_chat_1", "ou_user_1", "original");
        var headers = BuildValidHeaders(originalBody, TestEncryptKey);

        var tamperedBody = SerializeTextMessage("oc_chat_1", "ou_user_1", "tampered");
        await adapter.HandleEventAsync(tamperedBody, headers);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public async Task HandleEventAsync_AllowedUserEnforced()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateAdapter(
            bus: bus,
            encryptKey: null,
            allowedUsers: ["ou_allowed"]);

        var json = SerializeTextMessage("oc_chat_1", "ou_not_allowed", "hi");
        await adapter.HandleEventAsync(json);

        var consumed = await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(100));
        consumed.ShouldBeNull();
    }

    [Fact]
    public void ChannelsModule_RegistersFeishu()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/AIChannelsModule.cs");
        source.ShouldContain("FeishuChannelAdapter");
        source.ShouldContain("options.Feishu.Enabled");
    }

    [Fact]
    public void FeishuOptions_HasRequiredFields()
    {
        var source = File.ReadAllText("C:/src/Tnzi.NET/src/Tnzi.AI.Channels/Adapters/Feishu/FeishuAdapterOptions.cs");
        source.ShouldContain("AppId");
        source.ShouldContain("AppSecret");
        source.ShouldContain("EncryptKey");
    }

    [Fact]
    public void ToString_RedactsSecrets()
    {
        var options = new FeishuAdapterOptions
        {
            Enabled = true,
            AppId = "cli_public_app_id",
            AppSecret = "super-sensitive-secret",
            VerificationToken = "verify-token",
            EncryptKey = "encrypt-key"
        };

        var text = options.ToString();

        text.ShouldContain("cli_public_app_id");
        text.ShouldNotContain("super-sensitive-secret");
        text.ShouldNotContain("verify-token");
        text.ShouldNotContain("encrypt-key");
        text.ShouldContain("REDACTED");
    }

    #region Helpers

    private static string SerializeTextMessage(string chatId, string openId, string text)
    {
        return JsonSerializer.Serialize(new
        {
            @event = new
            {
                sender = new
                {
                    sender_id = new { open_id = openId }
                },
                message = new
                {
                    chat_id = chatId,
                    message_type = "text",
                    content = JsonSerializer.Serialize(new { text })
                }
            }
        });
    }

    private static Dictionary<string, string> BuildValidHeaders(string body, string encryptKey)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..8];
        var signature = ComputeSha256Hex(timestamp + nonce + encryptKey + body);

        return new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = timestamp,
            ["X-Lark-Request-Nonce"] = nonce,
            ["X-Lark-Signature"] = signature
        };
    }

    private static string ComputeSha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash);
    }

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

    private static FeishuChannelAdapter CreateAdapter(
        InMemoryChannelMessageBus? bus = null,
        string? encryptKey = null,
        List<string>? allowedUsers = null)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions
            {
                Enabled = true,
                AppId = "cli_test_app",
                AppSecret = "test-secret",
                EncryptKey = encryptKey,
                AllowedUserIds = allowedUsers ?? []
            }
        });

        return new FeishuChannelAdapter(
            NullLogger<FeishuChannelAdapter>.Instance,
            bus ?? new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            CreateMockHttpClientFactory().Object,
            options);
    }

    private static Mock<IHttpClientFactory> CreateMockHttpClientFactory()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tenant_access_token\":\"t\",\"expire\":7200}")
            });

        var client = new HttpClient(handler.Object);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory;
    }

    #endregion
}
