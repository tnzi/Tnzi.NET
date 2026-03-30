namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// ClarificationTools 内置工具功能测试
/// </summary>
public class ClarificationToolsTests
{
    private readonly AgentExecutionContextAccessor _contextAccessor;
    private readonly ClarificationTools _tools;

    public ClarificationToolsTests()
    {
        _contextAccessor = new AgentExecutionContextAccessor();
        _tools = new ClarificationTools(_contextAccessor);
    }

    [Fact]
    public void AskClarification_ReturnsFormattedMessage()
    {
        var result = _tools.AskClarification("What color?", ClarificationType.MissingInfo);

        result.ShouldContain("Clarification requested");
        result.ShouldContain("What color?");
    }

    [Fact]
    public void AskClarification_StoresClarificationRequest_InProperties()
    {
        _tools.AskClarification("Which approach?", ClarificationType.ApproachChoice,
            context: "Two valid methods",
            options: ["Option A", "Option B"]);

        _contextAccessor.Properties.ShouldContainKey(ContextPropertyKeys.ClarificationRequest);
        var request = (ClarificationRequest)_contextAccessor.Properties[ContextPropertyKeys.ClarificationRequest];

        request.Question.ShouldBe("Which approach?");
        request.Type.ShouldBe(ClarificationType.ApproachChoice);
        request.Context.ShouldBe("Two valid methods");
        request.Options.ShouldNotBeNull();
        request.Options.Count.ShouldBe(2);
    }

    [Fact]
    public void AskClarification_WithNullOptionalParams_Works()
    {
        var result = _tools.AskClarification("Continue?", ClarificationType.RiskConfirmation);

        result.ShouldNotBeNullOrWhiteSpace();

        var request = (ClarificationRequest)_contextAccessor.Properties[ContextPropertyKeys.ClarificationRequest];
        request.Context.ShouldBeNull();
        request.Options.ShouldBeNull();
    }

    [Theory]
    [InlineData(ClarificationType.MissingInfo)]
    [InlineData(ClarificationType.AmbiguousRequirement)]
    [InlineData(ClarificationType.ApproachChoice)]
    [InlineData(ClarificationType.RiskConfirmation)]
    [InlineData(ClarificationType.Suggestion)]
    public void AskClarification_AllTypes_Work(ClarificationType type)
    {
        var result = _tools.AskClarification("test question", type);

        result.ShouldNotBeNullOrWhiteSpace();

        var request = (ClarificationRequest)_contextAccessor.Properties[ContextPropertyKeys.ClarificationRequest];
        request.Type.ShouldBe(type);
    }

    [Fact]
    public void Constructor_NullAccessor_Throws()
    {
        Should.Throw<ArgumentNullException>(() => new ClarificationTools(null!));
    }
}
