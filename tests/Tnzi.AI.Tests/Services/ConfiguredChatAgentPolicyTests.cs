namespace Tnzi.AI.Tests.Services;

/// <summary>
/// Which agents users may chat with, from <c>AI:Chat</c>. With nothing configured every agent stays available,
/// as before.
/// </summary>
public class ConfiguredChatAgentPolicyTests
{
    private static readonly Guid Allowed = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public async Task NothingConfigured_EveryAgentAndAgentlessChatAreAllowed()
    {
        var policy = Policy(new AiChatOptions());

        (await policy.CheckAsync(Other, Guid.NewGuid())).Succeeded.ShouldBeTrue();
        (await policy.CheckAsync(null, Guid.NewGuid())).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AnAllowList_AdmitsOnlyItsAgents()
    {
        var policy = Policy(new AiChatOptions { AllowedAgentIds = [Allowed] });

        (await policy.CheckAsync(Allowed, Guid.NewGuid())).Succeeded.ShouldBeTrue();

        var refused = await policy.CheckAsync(Other, Guid.NewGuid());
        refused.Succeeded.ShouldBeFalse();
        refused.Code.ShouldBe(403);
        refused.ErrorCode.ShouldBe(ErrorCodes.ChatAgentNotAllowed);
    }

    [Fact]
    public async Task AgentlessChat_CanBeTurnedOff_SoTheAllowListCannotBeSidestepped()
    {
        var policy = Policy(new AiChatOptions { AllowedAgentIds = [Allowed], AllowAgentlessChat = false });

        var refused = await policy.CheckAsync(null, Guid.NewGuid());
        refused.Code.ShouldBe(403);
        refused.ErrorCode.ShouldBe(ErrorCodes.ChatAgentNotAllowed);
    }

    [Fact]
    public void TheValidator_RejectsEmptyIdsAndNonPositiveLimits()
    {
        var validator = new AiChatOptionsValidator();

        validator.Validate(null, new AiChatOptions { AllowedAgentIds = [Guid.Empty] }).Failed.ShouldBeTrue();
        validator.Validate(null, new AiChatOptions { Attachments = new ChatAttachmentOptions { MaxBytes = 0 } }).Failed.ShouldBeTrue();
        validator.Validate(null, new AiChatOptions { Attachments = new ChatAttachmentOptions { MaxCount = 0 } }).Failed.ShouldBeTrue();
        validator.Validate(null, new AiChatOptions()).Succeeded.ShouldBeTrue();
    }

    private static ConfiguredChatAgentPolicy Policy(AiChatOptions options)
        => new(new StaticOptionsMonitor<AiChatOptions>(options));
}
