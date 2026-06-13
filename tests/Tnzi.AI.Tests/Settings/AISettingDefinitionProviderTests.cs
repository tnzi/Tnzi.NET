namespace Tnzi.AI.Tests.Settings;

public class AISettingDefinitionProviderTests
{
    [Fact]
    public void GetGroups_Should_Define_Six_Groups_In_Order()
    {
        var groups = new AISettingDefinitionProvider().GetGroups();

        groups.Select(g => g.Key).ShouldBe(
        [
            "ai-general",
            "ai-budget",
            "ai-subagent",
            "ai-tools",
            "ai-summarization",
            "ai-conversation"
        ]);
        groups.ShouldAllBe(g => g.ModuleName == "AI");
        groups.SelectMany(g => g.Fields).ShouldAllBe(f => f.Key.StartsWith("AI:"));
    }

    [Fact]
    public void Budget_Group_Contains_CostTracking_Enabled_Field_With_Correct_Default()
    {
        var groups = new AISettingDefinitionProvider().GetGroups();
        var budget = groups.Single(g => g.Key == "ai-budget");

        budget.Fields.Select(f => f.Key).ShouldContain("AI:Budget:DefaultMonthlyBudgetUsd");
        budget.Fields.Single(f => f.Key == "AI:Budget:DefaultMonthlyBudgetUsd")
            .DefaultValueAccessor!().ShouldBe("100");

        budget.Fields.Select(f => f.Key).ShouldContain("AI:CostTracking:Enabled");
        budget.Fields.Single(f => f.Key == "AI:CostTracking:Enabled")
            .DefaultValueAccessor!().ShouldBe("false");
    }

    [Fact]
    public void Summarization_Group_Contains_Expected_Fields_With_Defaults()
    {
        var groups = new AISettingDefinitionProvider().GetGroups();
        var summarization = groups.Single(g => g.Key == "ai-summarization");

        summarization.Fields.Count.ShouldBeGreaterThanOrEqualTo(4);
        summarization.Fields.Select(f => f.Key).ShouldContain("AI:Summarization:Enabled");
        summarization.Fields.Single(f => f.Key == "AI:Summarization:Enabled")
            .DefaultValueAccessor!().ShouldBe("false");
        summarization.Fields.Select(f => f.Key).ShouldContain("AI:Summarization:EnableMicroCompact");
        summarization.Fields.Single(f => f.Key == "AI:Summarization:EnableMicroCompact")
            .DefaultValueAccessor!().ShouldBe("true");
    }

    [Fact]
    public void Conversation_Group_Contains_LoopDetection_Todo_Suggestion_Fields()
    {
        var groups = new AISettingDefinitionProvider().GetGroups();
        var conversation = groups.Single(g => g.Key == "ai-conversation");

        // Suggestions:AutoGenerate 已移除（无后端消费者）
        conversation.Fields.Count.ShouldBe(6);

        var loopEnabled = conversation.Fields.Single(f => f.Key == "AI:LoopDetection:Enabled");
        loopEnabled.DefaultValueAccessor!().ShouldBe("true");

        var warnThreshold = conversation.Fields.Single(f => f.Key == "AI:LoopDetection:WarnThreshold");
        warnThreshold.DefaultValueAccessor!().ShouldBe("3");

        var todoEnabled = conversation.Fields.Single(f => f.Key == "AI:Todo:Enabled");
        todoEnabled.DefaultValueAccessor!().ShouldBe("true");

        var todoMaxItems = conversation.Fields.Single(f => f.Key == "AI:Todo:MaxItems");
        todoMaxItems.DefaultValueAccessor!().ShouldBe("50");

        var suggestionsCount = conversation.Fields.Single(f => f.Key == "AI:Suggestions:Count");
        suggestionsCount.DefaultValueAccessor!().ShouldBe("3");
    }
}
