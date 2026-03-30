namespace Tnzi.AI.Tests;

public class ClarificationToolsTests
{
    [Fact]
    public void AskClarification_SetsPropertyOnAccessor()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new ClarificationTools(accessor);

        var result = tools.AskClarification(
            "Which approach do you prefer?",
            ClarificationType.ApproachChoice,
            "We need to choose between two architectures.",
            ["Microservices", "Monolith"]);

        // Tool returns placeholder text
        Assert.Contains("clarification", result, StringComparison.OrdinalIgnoreCase);

        // Tool writes ClarificationRequest to accessor Properties
        Assert.True(accessor.Properties.ContainsKey("ClarificationRequest"));
        var request = accessor.Properties["ClarificationRequest"] as ClarificationRequest;
        Assert.NotNull(request);
        Assert.Equal("Which approach do you prefer?", request.Question);
        Assert.Equal(ClarificationType.ApproachChoice, request.Type);
        Assert.Equal(2, request.Options!.Count);
    }

    [Fact]
    public void AskClarification_WithoutOptions_SetsNullOptions()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new ClarificationTools(accessor);

        tools.AskClarification(
            "What is the target?",
            ClarificationType.MissingInfo);

        var request = accessor.Properties["ClarificationRequest"] as ClarificationRequest;
        Assert.NotNull(request);
        Assert.Null(request.Options);
        Assert.Equal(ClarificationType.MissingInfo, request.Type);
    }

    [Fact]
    public void AskClarification_WithContext_SetsContext()
    {
        var accessor = new AgentExecutionContextAccessor();
        var tools = new ClarificationTools(accessor);

        tools.AskClarification(
            "Confirm deletion?",
            ClarificationType.RiskConfirmation,
            "This will delete all data permanently.");

        var request = accessor.Properties["ClarificationRequest"] as ClarificationRequest;
        Assert.NotNull(request);
        Assert.Equal("This will delete all data permanently.", request.Context);
    }
}
