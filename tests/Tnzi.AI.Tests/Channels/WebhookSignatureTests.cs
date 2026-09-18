using System.Security.Cryptography;
using System.Text;
using Tnzi.AI.Channels.Adapters.Dingtalk;
using Tnzi.AI.Channels.Adapters.Discord;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Adapters.Slack;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Models;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

/// <summary>
/// D2 - signature-gated webhook inbound. Per platform: valid signature accepted + dispatched,
/// invalid/missing signature rejected (NOT dispatched), challenge/handshake handled.
/// </summary>
public class WebhookSignatureTests
{
    private static async Task<InboundMessage?> TryConsumeAsync(InMemoryChannelMessageBus bus, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try { return await bus.ConsumeInboundAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
    }

    private static IHttpClientFactory HttpFactory()
    {
        var handler = new Mock<HttpMessageHandler>();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
        return factory.Object;
    }

    // ---------------- Slack ----------------

    private static string MessageEventJson(string text = "hello") => JsonSerializer.Serialize(new
    {
        type = "event_callback",
        @event = new { type = "message", channel = "C001", user = "U001", text, ts = "1700000000.000100" }
    });

    private static (string sig, string ts) SlackSign(string secret, string body, long? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var baseString = $"v0:{ts}:{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        return ("v0=" + Convert.ToHexStringLower(hash), ts);
    }

    private static SlackChannelAdapter CreateSlack(InMemoryChannelMessageBus bus, string? signingSecret)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = "xoxb-test", SigningSecret = signingSecret }
        });
        return new SlackChannelAdapter(NullLogger<SlackChannelAdapter>.Instance, bus, HttpFactory(), options);
    }

    [Fact]
    public async Task Slack_ValidSignature_Accepted_AndDispatched()
    {
        const string secret = "slack-signing-secret";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, secret);

        var body = MessageEventJson();
        var (sig, ts) = SlackSign(secret, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Slack-Signature"] = sig,
            ["X-Slack-Request-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Accepted);
        var msg = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        msg.ShouldNotBeNull();
        msg.Text.ShouldBe("hello");
    }

    [Fact]
    public async Task Slack_InvalidSignature_Rejected_NotDispatched()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, "slack-signing-secret");

        var body = MessageEventJson();
        var headers = new Dictionary<string, string>
        {
            ["X-Slack-Signature"] = "v0=deadbeef",
            ["X-Slack-Request-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task Slack_MissingSignature_Rejected_WhenSecretConfigured()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, "slack-signing-secret");

        var result = await adapter.ProcessWebhookAsync(MessageEventJson(), new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task Slack_UrlVerificationChallenge_Echoed_OnlyAfterTheSignatureChecks()
    {
        const string secret = "slack-signing-secret";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, secret);

        var body = JsonSerializer.Serialize(new { type = "url_verification", challenge = "abc123" });
        var (sig, ts) = SlackSign(secret, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Slack-Signature"] = sig,
            ["X-Slack-Request-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        result.ChallengeResponse.ShouldBe("abc123");
    }

    [Fact]
    public async Task Slack_UnsignedUrlVerificationChallenge_Rejected()
    {
        // Slack 对 url_verification 请求同样签名，所以"先回显 challenge 再验签"不是规范要求，
        // 而是一个免验证的回显器：任何人都能拿它确认端点在线并让它回声任意字符串。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, "slack-signing-secret");

        var body = JsonSerializer.Serialize(new { type = "url_verification", challenge = "abc123" });
        var result = await adapter.ProcessWebhookAsync(body, new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        result.ChallengeResponse.ShouldBeNull();
    }

    [Fact]
    public async Task Slack_NoSigningSecretConfigured_Rejected_NotDispatched()
    {
        // ★ 此前验签块没有 else 分支：漏配一个密钥，端点就从"验签的"变成"谁都能投递的"，
        // 而日志、返回码、监控全部正常。Discord 一直是拒绝的，Slack/Feishu 不是。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateSlack(bus, signingSecret: null);

        var result = await adapter.ProcessWebhookAsync(MessageEventJson(), new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    // ---------------- DingTalk ----------------

    private static (string sign, string ts) DingSign(string secret, long? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).ToString();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}\n{secret}"));
        return (Convert.ToBase64String(hash), ts);
    }

    private static DingtalkChannelAdapter CreateDing(InMemoryChannelMessageBus bus, string secret, bool verify = true)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Dingtalk = new DingtalkAdapterOptions
            {
                Enabled = true, AppKey = "ak", AppSecret = secret, RobotCode = "rc", VerifyWebhookSignature = verify
            }
        });
        return new DingtalkChannelAdapter(NullLogger<DingtalkChannelAdapter>.Instance, bus, HttpFactory(), options);
    }

    private static string DingEventJson() => JsonSerializer.Serialize(new
    {
        conversationId = "cid1",
        senderStaffId = "staff1",
        msgtype = "text",
        text = new { content = "hi from ding" }
    });

    [Fact]
    public async Task DingTalk_ValidSignature_Accepted_AndDispatched()
    {
        const string secret = "ding-app-secret";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateDing(bus, secret);

        var (sign, ts) = DingSign(secret);
        var headers = new Dictionary<string, string> { ["timestamp"] = ts, ["sign"] = sign };

        var result = await adapter.ProcessWebhookAsync(DingEventJson(), headers);

        result.Outcome.ShouldBe(WebhookOutcome.Accepted);
        var msg = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        msg.ShouldNotBeNull();
        msg.Text.ShouldBe("hi from ding");
    }

    [Fact]
    public async Task DingTalk_MissingSignature_Rejected()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateDing(bus, "ding-app-secret");

        var result = await adapter.ProcessWebhookAsync(DingEventJson(), new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task DingTalk_InvalidSignature_Rejected()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateDing(bus, "ding-app-secret");

        var headers = new Dictionary<string, string>
        {
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            ["sign"] = "wrong-signature"
        };

        var result = await adapter.ProcessWebhookAsync(DingEventJson(), headers);

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
    }

    // ---------------- Feishu ----------------

    private static string FeishuSign(string encryptKey, string ts, string nonce, string body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(ts + nonce + encryptKey + body));
        return Convert.ToHexStringLower(hash);
    }

    private static FeishuChannelAdapter CreateFeishu(InMemoryChannelMessageBus bus, string? encryptKey, string? verificationToken = null)
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions
            {
                Enabled = true, AppId = "app", AppSecret = "secret",
                EncryptKey = encryptKey, VerificationToken = verificationToken
            }
        });
        return new FeishuChannelAdapter(NullLogger<FeishuChannelAdapter>.Instance, bus, HttpFactory(), options);
    }

    private static string FeishuEventJson() => JsonSerializer.Serialize(new
    {
        @event = new
        {
            sender = new { sender_id = new { open_id = "ou_1" } },
            message = new { chat_id = "oc_1", message_type = "text", content = "{\"text\":\"feishu hi\"}" }
        }
    });

    [Fact]
    public async Task Feishu_ValidSignature_Accepted_AndDispatched()
    {
        const string key = "feishu-encrypt-key";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateFeishu(bus, key);

        // 配置了 Encrypt Key 的飞书应用推送的是密文信封，签名算在信封上
        var body = FeishuTestCrypto.Envelope(FeishuEventJson(), key);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = "nonce-1";
        var headers = new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = ts,
            ["X-Lark-Request-Nonce"] = nonce,
            ["X-Lark-Signature"] = FeishuSign(key, ts, nonce, body)
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Accepted);
        var msg = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        msg.ShouldNotBeNull();
        msg.Text.ShouldBe("feishu hi");
    }

    [Fact]
    public async Task Feishu_InvalidSignature_Rejected_NotDispatched()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateFeishu(bus, "feishu-encrypt-key");

        var headers = new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["X-Lark-Request-Nonce"] = "n",
            ["X-Lark-Signature"] = "00ff"
        };

        var result = await adapter.ProcessWebhookAsync(FeishuEventJson(), headers);

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    /// <summary>为一条飞书 body 造一组通过验签的请求头。</summary>
    private static Dictionary<string, string> FeishuHeaders(string key, string body)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        const string nonce = "nonce-1";
        return new Dictionary<string, string>
        {
            ["X-Lark-Request-Timestamp"] = ts,
            ["X-Lark-Request-Nonce"] = nonce,
            ["X-Lark-Signature"] = FeishuSign(key, ts, nonce, body)
        };
    }

    [Fact]
    public async Task Feishu_UrlVerificationChallenge_Echoed()
    {
        const string key = "feishu-encrypt-key";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateFeishu(bus, encryptKey: key);

        var body = FeishuTestCrypto.Envelope(
            JsonSerializer.Serialize(new { type = "url_verification", challenge = "lark-challenge", token = "vtok" }), key);
        var result = await adapter.ProcessWebhookAsync(body, FeishuHeaders(key, body));

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        result.ChallengeResponse!.ShouldContain("lark-challenge");
    }

    [Fact]
    public async Task Feishu_ChallengeWithWrongToken_Rejected()
    {
        // 刻意配上 EncryptKey 并签名：否则这条会因为"无法验签"而被拒，
        // 它想验的"token 不对要拒"就再也没被跑到过。
        const string key = "feishu-encrypt-key";
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateFeishu(bus, encryptKey: key, verificationToken: "expected-token");

        var body = FeishuTestCrypto.Envelope(
            JsonSerializer.Serialize(new { type = "url_verification", challenge = "c", token = "wrong-token" }), key);
        var result = await adapter.ProcessWebhookAsync(body, FeishuHeaders(key, body));

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
    }

    [Fact]
    public async Task Feishu_NoEncryptKeyConfigured_Rejected_NotDispatched()
    {
        // ★ 与 Slack 同一处缺陷：验签块没有 else 分支，漏配密钥就等于关掉验签，
        // 而外观与配置正确时完全一致。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var adapter = CreateFeishu(bus, encryptKey: null);

        var result = await adapter.ProcessWebhookAsync(FeishuEventJson(), new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    // ---------------- Discord (Ed25519) ----------------

    private static (DiscordChannelAdapter adapter, NSec.Cryptography.Key key) CreateDiscord(
        InMemoryChannelMessageBus bus, out string publicKeyHex, List<string>? allowedUsers = null)
    {
        var algo = NSec.Cryptography.SignatureAlgorithm.Ed25519;
        var key = NSec.Cryptography.Key.Create(algo, new NSec.Cryptography.KeyCreationParameters
        {
            ExportPolicy = NSec.Cryptography.KeyExportPolicies.AllowPlaintextExport
        });
        var pub = key.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        publicKeyHex = Convert.ToHexStringLower(pub);

        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Discord = new DiscordAdapterOptions { Enabled = true, BotToken = "bot", PublicKey = publicKeyHex, AllowedUsers = allowedUsers ?? [] }
        });
        var adapter = new DiscordChannelAdapter(NullLogger<DiscordChannelAdapter>.Instance, bus, HttpFactory(), options);
        return (adapter, key);
    }

    private static (string sig, string ts) DiscordSign(NSec.Cryptography.Key key, string body, long? timestamp = null)
    {
        var ts = (timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString();
        var algo = NSec.Cryptography.SignatureAlgorithm.Ed25519;
        var sig = algo.Sign(key, Encoding.UTF8.GetBytes(ts + body));
        return (Convert.ToHexStringLower(sig), ts);
    }

    [Fact]
    public async Task Discord_ValidSignature_ApplicationCommand_DeferredAck_AndDispatched()
    {
        // Discord 的 HTTP 回调只会投递 Interaction（斜杠命令等），三秒内必须先答 type=5（延迟应答），
        // 真正的回复之后经交互令牌 PATCH 回去。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, key) = CreateDiscord(bus, out _);

        var body = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C1", guild_id = "G1",
            member = new { user = new { id = "U1" } },
            data = new { name = "ask", type = 1, options = new[] { new { name = "prompt", type = 3, value = "discord hi" } } }
        });
        var (sig, ts) = DiscordSign(key, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Signature-Ed25519"] = sig,
            ["X-Signature-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        result.ChallengeResponse!.ShouldContain("\"type\":5");
        var msg = await TryConsumeAsync(bus, TimeSpan.FromSeconds(1));
        msg.ShouldNotBeNull();
        msg.Text.ShouldBe("discord hi");
        msg.ChatId.ShouldBe("C1");
        msg.UserId.ShouldBe("U1");
        msg.Metadata.ShouldNotBeNull();
        msg.Metadata[DiscordInteractionMetadata.Token].ShouldBe("itok");
        msg.Metadata[DiscordInteractionMetadata.ApplicationId].ShouldBe("app1");
    }

    [Fact]
    public async Task Discord_ValidSignature_ApplicationCommand_NotAllowed_AnsweredImmediatelyNotDeferred()
    {
        // 不在允许名单里的交互此前也答 type=5（延迟应答）却从不跟进：用户看到 "thinking..." 十五分钟直到令牌过期。
        // 拒绝要当场答 type=4 的临时消息（flags 64，只有本人可见）。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, key) = CreateDiscord(bus, out _, allowedUsers: ["U-someone-else"]);

        var body = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C1", guild_id = "G1",
            member = new { user = new { id = "U1" } },
            data = new { name = "ask", type = 1, options = new[] { new { name = "prompt", type = 3, value = "discord hi" } } }
        });
        var (sig, ts) = DiscordSign(key, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Signature-Ed25519"] = sig,
            ["X-Signature-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        var challenge = result.ChallengeResponse.ShouldNotBeNull();
        challenge.ShouldContain("\"type\":4");
        challenge.ShouldContain("\"flags\":64");
        challenge.ShouldNotContain("\"type\":5");
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task Discord_ValidSignature_GatewayDispatchShape_NotDispatchedViaWebhook()
    {
        // MESSAGE_CREATE 是 WebSocket Gateway 的分发帧，Discord 从不会 POST 它；签过名的 HTTP 回调只会是 Interaction。
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, key) = CreateDiscord(bus, out _);

        var body = JsonSerializer.Serialize(new
        {
            t = "MESSAGE_CREATE",
            d = new { id = "m1", channel_id = "C1", content = "discord hi", author = new { id = "U1" } }
        });
        var (sig, ts) = DiscordSign(key, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Signature-Ed25519"] = sig,
            ["X-Signature-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Accepted);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task Discord_ValidSignature_Ping_PongChallenge()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, key) = CreateDiscord(bus, out _);

        var body = JsonSerializer.Serialize(new { type = 1 });
        var (sig, ts) = DiscordSign(key, body);
        var headers = new Dictionary<string, string>
        {
            ["X-Signature-Ed25519"] = sig,
            ["X-Signature-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        result.ChallengeResponse!.ShouldContain("\"type\":1");
    }

    [Fact]
    public async Task Discord_InvalidSignature_Rejected_NotDispatched()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, key) = CreateDiscord(bus, out _);

        var body = JsonSerializer.Serialize(new
        {
            type = 2, id = "i1", application_id = "app1", token = "itok", channel_id = "C1",
            user = new { id = "U1" },
            data = new { name = "ask", type = 1, options = new[] { new { name = "prompt", type = 3, value = "x" } } }
        });
        // Sign a DIFFERENT body → signature won't verify against the real body.
        var (sig, ts) = DiscordSign(key, "tampered");
        var headers = new Dictionary<string, string>
        {
            ["X-Signature-Ed25519"] = sig,
            ["X-Signature-Timestamp"] = ts
        };

        var result = await adapter.ProcessWebhookAsync(body, headers);

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
        (await TryConsumeAsync(bus, TimeSpan.FromMilliseconds(150))).ShouldBeNull();
    }

    [Fact]
    public async Task Discord_MissingSignature_Rejected()
    {
        var bus = new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance);
        var (adapter, _) = CreateDiscord(bus, out _);

        var result = await adapter.ProcessWebhookAsync(
            JsonSerializer.Serialize(new { type = 1 }), new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Rejected);
    }
}
