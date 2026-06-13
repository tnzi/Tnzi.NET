namespace Tnzi.AI.Settings;

/// <summary>
/// AI 模块内置配置定义 — 仅收录经 IOptionsMonitor.CurrentValue 运行时热消费的字段
/// （消费者：ChatClientFactory / AiUtilityService / ThreadTitleGenerationHandler /
/// BudgetService / SubAgentExecutionService / McpServerCatalog / ToolResolver /
/// ConfiguredToolPermissionEvaluator）。启动期快照字段不得加入，避免死配置。
/// </summary>
public class AISettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "ai-general",
            ModuleName = "AI",
            DisplayName = "AI General",
            I18nKey = $"{I18nBase}.groups.aiGeneral",
            Icon = "mdi:robot-outline",
            Order = 100,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:DefaultProvider", Label = "Default Provider",
                    I18nKey = $"{I18nBase}.fields.defaultProvider",
                    DefaultValueAccessor = () => new AIOptions().DefaultProvider,
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Utility:Model", Label = "Utility Model",
                    I18nKey = $"{I18nBase}.fields.utilityModel",
                    Description = "Model used by lightweight utility calls (title generation etc.); falls back to the provider default when empty",
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Utility:MaxTokens", Label = "Utility Max Tokens", Type = SettingFieldType.Int, Min = 1, Max = 100_000,
                    I18nKey = $"{I18nBase}.fields.utilityMaxTokens",
                    DefaultValueAccessor = () => new AiUtilityOptions().MaxTokens.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Utility:Temperature", Label = "Utility Temperature", Type = SettingFieldType.Decimal, Min = 0, Max = 2,
                    I18nKey = $"{I18nBase}.fields.utilityTemperature",
                    DefaultValueAccessor = () => new AiUtilityOptions().Temperature.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Thread:AutoGenerateTitle", Label = "Auto-generate Thread Titles", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.autoGenerateTitle",
                    DefaultValueAccessor = () => new ThreadOptions().AutoGenerateTitle.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Thread:TitleMaxLength", Label = "Title Max Length", Type = SettingFieldType.Int, Min = 1, Max = 500,
                    I18nKey = $"{I18nBase}.fields.titleMaxLength",
                    DefaultValueAccessor = () => new ThreadOptions().TitleMaxLength.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "ai-budget",
            ModuleName = "AI",
            DisplayName = "AI Budget",
            I18nKey = $"{I18nBase}.groups.aiBudget",
            Icon = "mdi:cash-multiple",
            Order = 110,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:Budget:Enabled", Label = "Budget Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.budgetEnabled",
                    DefaultValueAccessor = () => new BudgetOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Budget:DefaultMonthlyBudgetUsd", Label = "Default Monthly Budget (USD)", Type = SettingFieldType.Decimal, Min = 0,
                    I18nKey = $"{I18nBase}.fields.defaultMonthlyBudgetUsd",
                    DefaultValueAccessor = () => new BudgetOptions().DefaultMonthlyBudgetUsd.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Budget:WarningThreshold", Label = "Warning Threshold", Type = SettingFieldType.Decimal, Min = 0, Max = 1,
                    I18nKey = $"{I18nBase}.fields.warningThreshold",
                    DefaultValueAccessor = () => new BudgetOptions().WarningThreshold.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Budget:CacheTtlSeconds", Label = "Cache TTL (s)", Type = SettingFieldType.Int, Min = 0, Max = 86_400,
                    I18nKey = $"{I18nBase}.fields.cacheTtlSeconds",
                    DefaultValueAccessor = () => new BudgetOptions().CacheTtlSeconds.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:CostTracking:Enabled", Label = "Cost Tracking Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.costTrackingEnabled",
                    Description = "Enable per-request token cost calculation (requires model cost rates configured)",
                    DefaultValueAccessor = () => new CostTrackingOptions().Enabled.ToString().ToLowerInvariant(),
                },
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "ai-subagent",
            ModuleName = "AI",
            DisplayName = "AI Sub-Agents",
            I18nKey = $"{I18nBase}.groups.aiSubagent",
            Icon = "mdi:account-group-outline",
            Order = 120,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:SubAgent:Enabled", Label = "Sub-Agents Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.subagentEnabled",
                    DefaultValueAccessor = () => new SubAgentOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:SubAgent:MaxConcurrentSubAgents", Label = "Max Concurrent", Type = SettingFieldType.Int, Min = 1, Max = 64,
                    I18nKey = $"{I18nBase}.fields.maxConcurrentSubAgents",
                    DefaultValueAccessor = () => new SubAgentOptions().MaxConcurrentSubAgents.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:SubAgent:TimeoutSeconds", Label = "Timeout (s)", Type = SettingFieldType.Int, Min = 1, Max = 86_400,
                    I18nKey = $"{I18nBase}.fields.timeoutSeconds",
                    DefaultValueAccessor = () => new SubAgentOptions().TimeoutSeconds.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:SubAgent:MaxDepth", Label = "Max Depth", Type = SettingFieldType.Int, Min = 1, Max = 32,
                    I18nKey = $"{I18nBase}.fields.maxDepth",
                    DefaultValueAccessor = () => new SubAgentOptions().MaxDepth.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:SubAgent:MaxDescendantsPerRoot", Label = "Max Descendants", Type = SettingFieldType.Int, Min = 1, Max = 1_000,
                    I18nKey = $"{I18nBase}.fields.maxDescendantsPerRoot",
                    DefaultValueAccessor = () => new SubAgentOptions().MaxDescendantsPerRoot.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "ai-tools",
            ModuleName = "AI",
            DisplayName = "AI Tools",
            I18nKey = $"{I18nBase}.groups.aiTools",
            Icon = "mdi:tools",
            Order = 130,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:Mcp:Enabled", Label = "MCP Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.mcpEnabled",
                    DefaultValueAccessor = () => new McpOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:ToolApproval:Enabled", Label = "Tool Approval Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.toolApprovalEnabled",
                    DefaultValueAccessor = () => new ToolApprovalOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:OpenApiTools:Enabled", Label = "OpenAPI Tools Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.openApiToolsEnabled",
                    DefaultValueAccessor = () => new OpenApiToolsOptions().Enabled.ToString().ToLowerInvariant(),
                },
            ],
        },
        new SettingDefinitionGroup
        {
            Key = "ai-summarization",
            ModuleName = "AI",
            DisplayName = "AI Summarization",
            I18nKey = $"{I18nBase}.groups.aiSummarization",
            Icon = "mdi:text-short",
            Order = 140,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:Enabled", Label = "Summarization Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.summarizationEnabled",
                    DefaultValueAccessor = () => new SummarizationOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:ModelContextWindow", Label = "Model Context Window", Type = SettingFieldType.Int, Min = 1_000, Max = 2_000_000,
                    I18nKey = $"{I18nBase}.fields.summarizationModelContextWindow",
                    Description = "Context window size in tokens used for fraction-based trigger calculation",
                    DefaultValueAccessor = () => new SummarizationOptions().ModelContextWindow.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:TrimTokensToSummarize", Label = "Min Tokens to Summarize", Type = SettingFieldType.Int, Min = 100, Max = 100_000,
                    I18nKey = $"{I18nBase}.fields.summarizationTrimTokensToSummarize",
                    Description = "Minimum token count before summarization is triggered",
                    DefaultValueAccessor = () => new SummarizationOptions().TrimTokensToSummarize.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:Keep:KeepLastMessages", Label = "Keep Last Messages", Type = SettingFieldType.Int, Min = 1, Max = 50,
                    I18nKey = $"{I18nBase}.fields.summarizationKeepLastMessages",
                    Description = "Number of recent messages preserved verbatim (not included in summary)",
                    DefaultValueAccessor = () => new ContextRetention().KeepLastMessages.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:EnableMicroCompact", Label = "Enable Micro-Compact", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.summarizationEnableMicroCompact",
                    Description = "Trim stale tool results before each execution to reduce context size",
                    DefaultValueAccessor = () => new SummarizationOptions().EnableMicroCompact.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Summarization:KeepRecentToolResults", Label = "Keep Recent Tool Results", Type = SettingFieldType.Int, Min = 1, Max = 50,
                    I18nKey = $"{I18nBase}.fields.summarizationKeepRecentToolResults",
                    Description = "Number of recent tool result messages retained during micro-compact",
                    DefaultValueAccessor = () => new SummarizationOptions().KeepRecentToolResults.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
        // ai-prompt-caching: SKIPPED — PromptCachingOptions is configured per-provider at
        // AI:Providers:{providerName}:PromptCaching (dynamic key); cannot be represented as
        // a static setting definition path.
        new SettingDefinitionGroup
        {
            Key = "ai-conversation",
            ModuleName = "AI",
            DisplayName = "AI Conversation",
            I18nKey = $"{I18nBase}.groups.aiConversation",
            Icon = "mdi:message-cog-outline",
            Order = 160,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "AI:LoopDetection:Enabled", Label = "Loop Detection Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.loopDetectionEnabled",
                    DefaultValueAccessor = () => new LoopDetectionOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:LoopDetection:WarnThreshold", Label = "Loop Warn Threshold", Type = SettingFieldType.Int, Min = 1, Max = 20,
                    I18nKey = $"{I18nBase}.fields.loopDetectionWarnThreshold",
                    Description = "Number of repeated tool call patterns before a warning is injected",
                    DefaultValueAccessor = () => new LoopDetectionOptions().WarnThreshold.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:LoopDetection:HardLimit", Label = "Loop Hard Limit", Type = SettingFieldType.Int, Min = 1, Max = 50,
                    I18nKey = $"{I18nBase}.fields.loopDetectionHardLimit",
                    Description = "Number of repeated tool call patterns that forces the agent to stop using tools",
                    DefaultValueAccessor = () => new LoopDetectionOptions().HardLimit.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Todo:Enabled", Label = "Todo Mode Enabled", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.todoEnabled",
                    DefaultValueAccessor = () => new TodoOptions().Enabled.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "AI:Todo:MaxItems", Label = "Todo Max Items", Type = SettingFieldType.Int, Min = 1, Max = 200,
                    I18nKey = $"{I18nBase}.fields.todoMaxItems",
                    DefaultValueAccessor = () => new TodoOptions().MaxItems.ToString(CultureInfo.InvariantCulture),
                },
                // Suggestions:AutoGenerate 不收录：声明后无任何后端消费者（SuggestionService 只读 Count）。
                new SettingFieldDefinition
                {
                    Key = "AI:Suggestions:Count", Label = "Suggestion Count", Type = SettingFieldType.Int, Min = 1, Max = 10,
                    I18nKey = $"{I18nBase}.fields.suggestionsCount",
                    DefaultValueAccessor = () => new SuggestionOptions().Count.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
    ];
}
