namespace Tnzi.AI.Tests.Services;

/// <summary>
/// Which thread origins a client may declare, from <c>AI:Chat:AllowedThreadOrigins</c>. With nothing configured a
/// client can declare none: an origin a client could set freely would let any conversation pose as one a rule started.
/// The default policy never gives an origin of its own.
/// </summary>
public class ConfiguredThreadOriginPolicyTests
{
    [Fact]
    public async Task NothingConfigured_NoDeclaredOriginIsAllowed()
    {
        var refused = await Policy(new AiChatOptions()).ResolveAsync(Declared("Card"));

        refused.Succeeded.ShouldBeFalse();
        refused.Code.ShouldBe(400);
        refused.ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
    }

    [Fact]
    public async Task AnAllowList_AdmitsExactlyItsValues_AsTheThreadsOrigin()
    {
        var policy = Policy(new AiChatOptions { AllowedThreadOrigins = ["Card", "Desktop"] });

        (await policy.ResolveAsync(Declared("Card"))).Data.ShouldBe("Card");
        (await policy.ResolveAsync(Declared("Desktop") with { AgentId = Guid.NewGuid() })).Data.ShouldBe("Desktop");
        (await policy.ResolveAsync(Declared("card"))).ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
        (await policy.ResolveAsync(Declared("Email"))).ErrorCode.ShouldBe(ErrorCodes.ThreadOriginNotAllowed);
    }

    [Fact]
    public async Task NothingDeclared_GivesNoOrigin_AndIsNotRefused()
    {
        var resolved = await Policy(new AiChatOptions()).ResolveAsync(Declared(null));

        resolved.Succeeded.ShouldBeTrue();
        resolved.Data.ShouldBeNull();
    }

    [Fact]
    public void TheValidator_RejectsAMalformedAllowedOrigin()
    {
        var validator = new AiChatOptionsValidator();

        validator.Validate(null, new AiChatOptions { AllowedThreadOrigins = ["Card", "not ok"] }).Failed.ShouldBeTrue();
        validator.Validate(null, new AiChatOptions { AllowedThreadOrigins = [new string('a', ThreadOrigin.MaxLength + 1)] }).Failed.ShouldBeTrue();
        validator.Validate(null, new AiChatOptions { AllowedThreadOrigins = ["Card", "LocalAgent", "app:notes"] }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Email", true)]
    [InlineData("app:notes.v2", true)]
    [InlineData("Local_Agent-1", true)]
    [InlineData("1Email", false)]
    [InlineData("_x", false)]
    [InlineData("has space", false)]
    [InlineData("emoji😀", false)]
    [InlineData("", false)]
    public void ThreadOrigin_Shape(string origin, bool wellFormed)
        => ThreadOrigin.IsWellFormed(origin).ShouldBe(wellFormed);

    [Fact]
    public void ThreadOrigin_MaxLength_IsTheColumnWidth()
    {
        ThreadOrigin.IsWellFormed("a" + new string('b', ThreadOrigin.MaxLength - 1)).ShouldBeTrue();
        ThreadOrigin.IsWellFormed("a" + new string('b', ThreadOrigin.MaxLength)).ShouldBeFalse();
    }

    private static ThreadOriginRequest Declared(string? origin)
        => new(origin, null, Guid.NewGuid(), StartsThread: true, new Dictionary<string, JsonElement>());

    private static ConfiguredThreadOriginPolicy Policy(AiChatOptions options)
        => new(new StaticOptionsMonitor<AiChatOptions>(options));
}
