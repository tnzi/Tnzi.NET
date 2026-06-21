namespace Tnzi.AI.Tests.Settings;

/// <summary>
/// Verifies that all AI Options classes are correctly decorated with attribute-driven
/// setting definitions. Default value format: bool→"True"/"False" (scanner uses ToString()),
/// numeric→invariant string.
/// </summary>
public class AISettingDefinitionProviderTests
{
    [Fact]
    public void AIOptions_yields_ai_general_group_with_DefaultProvider_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(AIOptions))!;

        Assert.Equal("ai-general", g.Key);
        Assert.Equal("AI", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "AI:DefaultProvider" && f.Label == "Default Provider");
        Assert.Equal("OpenAI", g.Fields.Single(f => f.Key == "AI:DefaultProvider").DefaultValueAccessor!());
    }

    [Fact]
    public void AiUtilityOptions_yields_ai_general_group_with_utility_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(AiUtilityOptions))!;

        Assert.Equal("ai-general", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Utility:Model");
        Assert.Contains(g.Fields, f => f.Key == "AI:Utility:MaxTokens");
        Assert.Contains(g.Fields, f => f.Key == "AI:Utility:Temperature");
        Assert.Equal("100", g.Fields.Single(f => f.Key == "AI:Utility:MaxTokens").DefaultValueAccessor!());
        Assert.Equal("0.3", g.Fields.Single(f => f.Key == "AI:Utility:Temperature").DefaultValueAccessor!());
    }

    [Fact]
    public void ThreadOptions_yields_ai_general_group_with_thread_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ThreadOptions))!;

        Assert.Equal("ai-general", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Thread:AutoGenerateTitle");
        Assert.Contains(g.Fields, f => f.Key == "AI:Thread:TitleMaxLength");
        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:Thread:AutoGenerateTitle").DefaultValueAccessor!());
        Assert.Equal("50", g.Fields.Single(f => f.Key == "AI:Thread:TitleMaxLength").DefaultValueAccessor!());
    }

    [Fact]
    public void BudgetOptions_yields_ai_budget_group_with_expected_fields_and_defaults()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(BudgetOptions))!;

        Assert.Equal("ai-budget", g.Key);
        Assert.Equal("AI", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "AI:Budget:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:Budget:DefaultMonthlyBudgetUsd");
        Assert.Contains(g.Fields, f => f.Key == "AI:Budget:WarningThreshold");
        Assert.Contains(g.Fields, f => f.Key == "AI:Budget:CacheTtlSeconds");

        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:Budget:Enabled").DefaultValueAccessor!());
        Assert.Equal("100", g.Fields.Single(f => f.Key == "AI:Budget:DefaultMonthlyBudgetUsd").DefaultValueAccessor!());
        Assert.Equal("0.8", g.Fields.Single(f => f.Key == "AI:Budget:WarningThreshold").DefaultValueAccessor!());
        Assert.Equal("60", g.Fields.Single(f => f.Key == "AI:Budget:CacheTtlSeconds").DefaultValueAccessor!());
    }

    [Fact]
    public void CostTrackingOptions_yields_ai_budget_group_with_Enabled_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(CostTrackingOptions))!;

        Assert.Equal("ai-budget", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:CostTracking:Enabled");
        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:CostTracking:Enabled").DefaultValueAccessor!());
    }

    [Fact]
    public void SubAgentOptions_yields_ai_subagent_group_with_all_five_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(SubAgentOptions))!;

        Assert.Equal("ai-subagent", g.Key);
        Assert.Equal("AI", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "AI:SubAgent:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:SubAgent:MaxConcurrentSubAgents");
        Assert.Contains(g.Fields, f => f.Key == "AI:SubAgent:TimeoutSeconds");
        Assert.Contains(g.Fields, f => f.Key == "AI:SubAgent:MaxDepth");
        Assert.Contains(g.Fields, f => f.Key == "AI:SubAgent:MaxDescendantsPerRoot");

        Assert.Equal("True", g.Fields.Single(f => f.Key == "AI:SubAgent:Enabled").DefaultValueAccessor!());
        Assert.Equal("3", g.Fields.Single(f => f.Key == "AI:SubAgent:MaxConcurrentSubAgents").DefaultValueAccessor!());
        Assert.Equal("900", g.Fields.Single(f => f.Key == "AI:SubAgent:TimeoutSeconds").DefaultValueAccessor!());
        Assert.Equal("5", g.Fields.Single(f => f.Key == "AI:SubAgent:MaxDepth").DefaultValueAccessor!());
        Assert.Equal("25", g.Fields.Single(f => f.Key == "AI:SubAgent:MaxDescendantsPerRoot").DefaultValueAccessor!());
    }

    [Fact]
    public void McpOptions_yields_ai_tools_group_with_Enabled_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(McpOptions))!;
        Assert.Equal("ai-tools", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Mcp:Enabled");
        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:Mcp:Enabled").DefaultValueAccessor!());
    }

    [Fact]
    public void ToolApprovalOptions_yields_ai_tools_group_with_Enabled_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ToolApprovalOptions))!;
        Assert.Equal("ai-tools", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:ToolApproval:Enabled");
        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:ToolApproval:Enabled").DefaultValueAccessor!());
    }

    [Fact]
    public void OpenApiToolsOptions_yields_ai_tools_group_with_Enabled_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(OpenApiToolsOptions))!;
        Assert.Equal("ai-tools", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:OpenApiTools:Enabled");
        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:OpenApiTools:Enabled").DefaultValueAccessor!());
    }

    [Fact]
    public void SummarizationOptions_yields_ai_summarization_group_with_expected_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(SummarizationOptions))!;

        Assert.Equal("ai-summarization", g.Key);
        Assert.Equal("AI", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:ModelContextWindow");
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:TrimTokensToSummarize");
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:EnableMicroCompact");
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:KeepRecentToolResults");

        Assert.Equal("False", g.Fields.Single(f => f.Key == "AI:Summarization:Enabled").DefaultValueAccessor!());
        Assert.Equal("True", g.Fields.Single(f => f.Key == "AI:Summarization:EnableMicroCompact").DefaultValueAccessor!());
        Assert.Equal("128000", g.Fields.Single(f => f.Key == "AI:Summarization:ModelContextWindow").DefaultValueAccessor!());
    }

    [Fact]
    public void ContextRetention_yields_ai_summarization_group_with_KeepLastMessages_field()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(ContextRetention))!;

        Assert.Equal("ai-summarization", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Summarization:Keep:KeepLastMessages");
        Assert.Equal("6", g.Fields.Single(f => f.Key == "AI:Summarization:Keep:KeepLastMessages").DefaultValueAccessor!());
    }

    [Fact]
    public void LoopDetectionOptions_yields_ai_conversation_group_with_three_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(LoopDetectionOptions))!;

        Assert.Equal("ai-conversation", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:LoopDetection:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:LoopDetection:WarnThreshold");
        Assert.Contains(g.Fields, f => f.Key == "AI:LoopDetection:HardLimit");

        Assert.Equal("True", g.Fields.Single(f => f.Key == "AI:LoopDetection:Enabled").DefaultValueAccessor!());
        Assert.Equal("3", g.Fields.Single(f => f.Key == "AI:LoopDetection:WarnThreshold").DefaultValueAccessor!());
        Assert.Equal("5", g.Fields.Single(f => f.Key == "AI:LoopDetection:HardLimit").DefaultValueAccessor!());
    }

    [Fact]
    public void TodoOptions_yields_ai_conversation_group_with_Enabled_and_MaxItems()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(TodoOptions))!;

        Assert.Equal("ai-conversation", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Todo:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:Todo:MaxItems");
        Assert.Equal("True", g.Fields.Single(f => f.Key == "AI:Todo:Enabled").DefaultValueAccessor!());
        Assert.Equal("50", g.Fields.Single(f => f.Key == "AI:Todo:MaxItems").DefaultValueAccessor!());
    }

    [Fact]
    public void SuggestionOptions_yields_ai_conversation_group_with_Count_field_only()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(SuggestionOptions))!;

        Assert.Equal("ai-conversation", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Suggestions:Count");
        Assert.DoesNotContain(g.Fields, f => f.Key == "AI:Suggestions:AutoGenerate");
        Assert.Equal("3", g.Fields.Single(f => f.Key == "AI:Suggestions:Count").DefaultValueAccessor!());
    }

    [Fact]
    public void All_field_keys_start_with_AI_colon()
    {
        var types = new[]
        {
            typeof(AIOptions), typeof(AiUtilityOptions), typeof(ThreadOptions),
            typeof(BudgetOptions), typeof(CostTrackingOptions), typeof(SubAgentOptions),
            typeof(McpOptions), typeof(ToolApprovalOptions), typeof(OpenApiToolsOptions),
            typeof(SummarizationOptions), typeof(ContextRetention),
            typeof(LoopDetectionOptions), typeof(TodoOptions), typeof(SuggestionOptions)
        };

        foreach (var type in types)
        {
            var g = RuntimeSettingMetadataExtractor.Extract(type);
            if (g == null) continue;
            foreach (var field in g.Fields)
                Assert.StartsWith("AI:", field.Key);
        }
    }
}
