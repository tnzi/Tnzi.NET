using Moq.Protected;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Adapters.Slack;
using Tnzi.AI.Channels.Bus;
using Tnzi.AI.Channels.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Channels;

public class ChannelsModuleOptionsValidatorTests
{
    // ─── Slack SigningSecret ─────────────────────────────────────────────────

    [Fact]
    public void Validate_SlackEnabled_NoSigningSecret_Fails()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = "xoxb-valid", SigningSecret = null }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Slack.SigningSecret is required when the Slack adapter is enabled",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SlackEnabled_EmptySigningSecret_Fails()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = "xoxb-valid", SigningSecret = "   " }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Slack.SigningSecret is required when the Slack adapter is enabled",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SlackEnabled_SigningSecretPresent_Passes()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = "xoxb-valid", SigningSecret = "secret123" }
        });

        result.Failed.ShouldBeFalse();
    }

    [Fact]
    public void Validate_SlackDisabled_NoSigningSecret_Passes()
    {
        // Disabled adapter — no webhook traffic, so no requirement.
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Slack = new SlackAdapterOptions { Enabled = false, BotToken = null, SigningSecret = null }
        });

        result.Failed.ShouldBeFalse();
    }

    // ─── Feishu EncryptKey ───────────────────────────────────────────────────

    [Fact]
    public void Validate_FeishuEnabled_NoEncryptKey_Fails()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "cli_app", AppSecret = "s", EncryptKey = null }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Feishu.EncryptKey is required when the Feishu adapter is enabled",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_FeishuEnabled_EmptyEncryptKey_Fails()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "cli_app", AppSecret = "s", EncryptKey = "  " }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Feishu.EncryptKey is required when the Feishu adapter is enabled",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_FeishuEnabled_EncryptKeyPresent_Passes()
    {
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "cli_app", AppSecret = "s", EncryptKey = "enc-key-abc" }
        });

        result.Failed.ShouldBeFalse();
    }

    [Fact]
    public void Validate_FeishuDisabled_NoEncryptKey_Passes()
    {
        // Disabled adapter — no webhook traffic, so no requirement.
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = true,
            Feishu = new FeishuAdapterOptions { Enabled = false, AppId = "cli_app", AppSecret = "s", EncryptKey = null }
        });

        result.Failed.ShouldBeFalse();
    }

    // ─── Module-level disabled guard ────────────────────────────────────────

    [Fact]
    public void Validate_ModuleDisabled_SlackFeishuNoSecrets_Passes()
    {
        // When the whole module is disabled, no platform validations run.
        var result = Validate(new ChannelsModuleOptions
        {
            Enabled = false,
            Slack = new SlackAdapterOptions { Enabled = true, BotToken = null, SigningSecret = null },
            Feishu = new FeishuAdapterOptions { Enabled = true, AppId = "a", AppSecret = "s", EncryptKey = null }
        });

        result.Succeeded.ShouldBeTrue();
    }

    // ─── FIX 2: Feishu challenge JSON escaping ───────────────────────────────

    [Fact]
    public async Task FeishuChallenge_WithSpecialChars_ProducesValidJson()
    {
        // Regression: challenge value containing a quote or backslash must be JSON-escaped.
        const string tricky = @"abc""def\ghi";

        var json = BuildChallengeBody(tricky);
        var adapter = CreateFeishuAdapterNoEncryptKey();
        var result = await adapter.ProcessWebhookAsync(json, new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);
        result.ChallengeResponse.ShouldNotBeNull();

        // Must deserialize cleanly — string-interpolation bug would throw here.
        using var doc = JsonDocument.Parse(result.ChallengeResponse);
        var roundTripped = doc.RootElement.GetProperty("challenge").GetString();
        roundTripped.ShouldBe(tricky);
    }

    [Fact]
    public async Task FeishuChallenge_PlainValue_ProducesValidJson()
    {
        const string plain = "challenge_token_abc123";

        var json = BuildChallengeBody(plain);
        var adapter = CreateFeishuAdapterNoEncryptKey();
        var result = await adapter.ProcessWebhookAsync(json, new Dictionary<string, string>());

        result.Outcome.ShouldBe(WebhookOutcome.Challenge);

        using var doc = JsonDocument.Parse(result.ChallengeResponse!);
        doc.RootElement.GetProperty("challenge").GetString().ShouldBe(plain);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ValidateOptionsResult Validate(ChannelsModuleOptions options)
    {
        var validator = new ChannelsModuleOptionsValidator();
        return validator.Validate(null, options);
    }

    private static string BuildChallengeBody(string challengeValue)
        => JsonSerializer.Serialize(new
        {
            type = "url_verification",
            token = (string?)null,
            challenge = challengeValue
        });

    private static FeishuChannelAdapter CreateFeishuAdapterNoEncryptKey()
    {
        var options = MsOptions.Create(new ChannelsModuleOptions
        {
            Feishu = new FeishuAdapterOptions
            {
                Enabled = true,
                AppId = "cli_test_app",
                AppSecret = "test-secret",
                EncryptKey = null
            }
        });

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

        return new FeishuChannelAdapter(
            NullLogger<FeishuChannelAdapter>.Instance,
            new InMemoryChannelMessageBus(NullLogger<InMemoryChannelMessageBus>.Instance),
            factory.Object,
            options);
    }
}
