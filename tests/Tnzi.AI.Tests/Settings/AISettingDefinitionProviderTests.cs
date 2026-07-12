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
        foreach (var type in AllAiRuntimeSettingTypes)
        {
            var g = RuntimeSettingMetadataExtractor.Extract(type);
            if (g == null) continue;
            foreach (var field in g.Fields)
                Assert.StartsWith("AI:", field.Key);
        }
    }

    // --- Phase 2A additions ---------------------------------------------------

    /// <summary>All AI Options classes carrying [RuntimeSetting] fields (Phase 2A expanded set).</summary>
    private static readonly Type[] AllAiRuntimeSettingTypes =
    [
        typeof(AIOptions), typeof(AiUtilityOptions), typeof(ThreadOptions),
        typeof(BudgetOptions), typeof(CostTrackingOptions), typeof(SubAgentOptions),
        typeof(McpOptions), typeof(ToolApprovalOptions), typeof(OpenApiToolsOptions),
        typeof(ToolResultBudgetOptions), typeof(ToolPermissionOptions),
        typeof(SummarizationOptions), typeof(ContextRetention), typeof(SummarizationTrigger),
        typeof(LoopDetectionOptions), typeof(TodoOptions), typeof(SuggestionOptions),
        typeof(GuardrailsOptions), typeof(AllowlistGuardrailOptions), typeof(LlmJudgeOptions),
        typeof(HistoryStoreOptions), typeof(HistoryReductionOptions), typeof(PruneOptions), typeof(SummarizeOptions),
        typeof(MemoryOptions), typeof(EntityMemoryOptions), typeof(TextSearchOptions), typeof(ChatHistoryMemoryOptions),
        typeof(RetryOptions), typeof(QuotaOptions),
    ];

    [Fact]
    public void GuardrailsOptions_yields_ai_guardrails_group_with_expected_fields()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(GuardrailsOptions))!;

        Assert.Equal("ai-guardrails", g.Key);
        Assert.Equal("AI", g.ModuleName);
        Assert.Contains(g.Fields, f => f.Key == "AI:Guardrails:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:Guardrails:MaxInputLength");
        Assert.Contains(g.Fields, f => f.Key == "AI:Guardrails:InspectToolArguments");

        var mode = g.Fields.Single(f => f.Key == "AI:Guardrails:ExecutionMode");
        Assert.Equal(SettingFieldType.Select, mode.Type);
        Assert.NotNull(mode.Options);
        Assert.Contains("Sequential", mode.Options!);
        Assert.Contains("Parallel", mode.Options!);

        // List fields (BlockedOutputKeywords) must be skipped.
        Assert.DoesNotContain(g.Fields, f => f.Key == "AI:Guardrails:BlockedOutputKeywords");
    }

    [Fact]
    public void GuardrailsNestedOptions_merge_into_ai_guardrails_group()
    {
        var allow = RuntimeSettingMetadataExtractor.Extract(typeof(AllowlistGuardrailOptions))!;
        var judge = RuntimeSettingMetadataExtractor.Extract(typeof(LlmJudgeOptions))!;

        Assert.Equal("ai-guardrails", allow.Key);
        Assert.Contains(allow.Fields, f => f.Key == "AI:Guardrails:Allowlist:MatchExact");

        Assert.Equal("ai-guardrails", judge.Key);
        Assert.Contains(judge.Fields, f => f.Key == "AI:Guardrails:LlmJudge:Enabled");
        Assert.Equal(SettingFieldType.Text, judge.Fields.Single(f => f.Key == "AI:Guardrails:LlmJudge:InputJudgePrompt").Type);
    }

    [Fact]
    public void HistoryNestedOptions_merge_into_ai_history_and_skip_dead_store_enabled()
    {
        var store = RuntimeSettingMetadataExtractor.Extract(typeof(HistoryStoreOptions))!;
        var reduction = RuntimeSettingMetadataExtractor.Extract(typeof(HistoryReductionOptions))!;
        var prune = RuntimeSettingMetadataExtractor.Extract(typeof(PruneOptions))!;
        var summarize = RuntimeSettingMetadataExtractor.Extract(typeof(SummarizeOptions))!;

        Assert.Equal("ai-history", store.Key);
        Assert.Contains(store.Fields, f => f.Key == "AI:History:Store:MaxLoadedMessages");
        // Store.Enabled has no runtime consumer → deliberately not exposed.
        Assert.DoesNotContain(store.Fields, f => f.Key == "AI:History:Store:Enabled");

        Assert.Equal("ai-history", reduction.Key);
        Assert.Equal(SettingFieldType.Select, reduction.Fields.Single(f => f.Key == "AI:History:Reduction:Mode").Type);

        Assert.Equal("ai-history", prune.Key);
        Assert.Contains(prune.Fields, f => f.Key == "AI:History:Reduction:Prune:KeepLastTurns");

        Assert.Equal("ai-history", summarize.Key);
        Assert.Contains(summarize.Fields, f => f.Key == "AI:History:Reduction:Summarize:MaxSummaryTokens");
        Assert.Equal(SettingFieldType.Text, summarize.Fields.Single(f => f.Key == "AI:History:Reduction:Summarize:SummaryPrompt").Type);
    }

    [Fact]
    public void MemoryOptions_yields_ai_memory_group_with_duration_expiration()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(MemoryOptions))!;

        Assert.Equal("ai-memory", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:ContextProviders:Memory:Enabled");
        Assert.Contains(g.Fields, f => f.Key == "AI:ContextProviders:Memory:RetrievalTopK");

        var expiration = g.Fields.Single(f => f.Key == "AI:ContextProviders:Memory:EntryExpiration");
        Assert.Equal(SettingFieldType.Duration, expiration.Type);

        // HashSet / nested Scoring must be skipped.
        Assert.DoesNotContain(g.Fields, f => f.Key == "AI:ContextProviders:Memory:ValidCategories");
    }

    [Fact]
    public void MemorySubToggles_merge_into_ai_memory_group()
    {
        var entity = RuntimeSettingMetadataExtractor.Extract(typeof(EntityMemoryOptions))!;
        var text = RuntimeSettingMetadataExtractor.Extract(typeof(TextSearchOptions))!;
        var chat = RuntimeSettingMetadataExtractor.Extract(typeof(ChatHistoryMemoryOptions))!;

        Assert.Equal("ai-memory", entity.Key);
        Assert.Contains(entity.Fields, f => f.Key == "AI:ContextProviders:EntityMemory:Enabled");
        Assert.Equal("ai-memory", text.Key);
        Assert.Contains(text.Fields, f => f.Key == "AI:ContextProviders:TextSearch:Enabled");
        Assert.Equal("ai-memory", chat.Key);
        Assert.Contains(chat.Fields, f => f.Key == "AI:ContextProviders:ChatHistoryMemory:Enabled");
    }

    [Fact]
    public void RetryOptions_yields_ai_retry_group_with_only_Enabled()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(RetryOptions))!;

        Assert.Equal("ai-retry", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Retry:Enabled");
        // Tuning knobs are frozen by the singleton-cached Polly pipeline → not exposed.
        Assert.DoesNotContain(g.Fields, f => f.Key == "AI:Retry:MaxRetries");
        Assert.DoesNotContain(g.Fields, f => f.Key == "AI:Retry:CircuitBreakerDuration");
    }

    [Fact]
    public void QuotaOptions_yields_ai_quota_group_with_two_limits()
    {
        var g = RuntimeSettingMetadataExtractor.Extract(typeof(QuotaOptions))!;

        Assert.Equal("ai-quota", g.Key);
        Assert.Contains(g.Fields, f => f.Key == "AI:Quota:DefaultDailyTokenLimit");
        Assert.Contains(g.Fields, f => f.Key == "AI:Quota:DefaultMonthlyTokenLimit");
        Assert.Equal("1000000", g.Fields.Single(f => f.Key == "AI:Quota:DefaultDailyTokenLimit").DefaultValueAccessor!());
    }

    [Fact]
    public void SummarizationTrigger_and_ToolFills_merge_into_expected_groups()
    {
        var trigger = RuntimeSettingMetadataExtractor.Extract(typeof(SummarizationTrigger))!;
        Assert.Equal("ai-summarization", trigger.Key);
        Assert.Equal(SettingFieldType.Select, trigger.Fields.Single(f => f.Key == "AI:Summarization:Trigger:Type").Type);

        var budget = RuntimeSettingMetadataExtractor.Extract(typeof(ToolResultBudgetOptions))!;
        Assert.Equal("ai-tools", budget.Key);
        Assert.Contains(budget.Fields, f => f.Key == "AI:ToolResultBudget:Enabled");

        var perms = RuntimeSettingMetadataExtractor.Extract(typeof(ToolPermissionOptions))!;
        Assert.Equal("ai-tools", perms.Key);
        Assert.Contains(perms.Fields, f => f.Key == "AI:Permissions:Enabled");
    }

    [Fact]
    public void All_ai_runtime_setting_field_keys_are_unique_across_groups()
    {
        // The settings-center provider fails startup (ValidateNoConflicts) on any duplicate
        // field key. Guard the whole expanded AI set here without depending on Tnzi.System.
        var groups = AllAiRuntimeSettingTypes
            .Select(RuntimeSettingMetadataExtractor.Extract)
            .Where(g => g != null)
            .ToList();

        var allKeys = groups.SelectMany(g => g!.Fields.Select(f => f.Key)).ToList();
        var duplicates = allKeys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        Assert.Empty(duplicates);

        // Expanded set of AI group keys is present.
        var groupKeys = groups.Select(g => g!.Key).Distinct().ToList();
        foreach (var key in new[] { "ai-general", "ai-tools", "ai-summarization", "ai-conversation",
                     "ai-guardrails", "ai-history", "ai-memory", "ai-retry", "ai-quota" })
            Assert.Contains(key, groupKeys);
    }
}
