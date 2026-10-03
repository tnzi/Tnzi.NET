namespace Tnzi.AI.Options;

/// <summary>
/// AI 配置选项验证器
/// </summary>
public class AIOptionsValidator : OptionsValidatorBase<AIOptions>
{
    /// <summary><c>AI:Providers:*:TimeoutSeconds</c> 允许的上限。</summary>
    internal const int MaxProviderTimeoutSeconds = 600;

    protected override void ValidateOptions(AIOptions options, List<string> errors)
    {
        // Providers 声明为非空且带默认实例，配置绑定不会把它置 null；多余的 != null 检查会让
        // 编译器把后续读取标记为「可能为 null」（CS8602）。
        //
        // ★判据是「有没有已启用的提供商」，与核心的 AiProviderRegistryOptionsValidator 同源，不是「字典非空」：
        // 配置绑定合并所有配置源，同机器为别的应用设的用户级环境变量 AI__Providers__<Name>__ApiKey
        // 会让每个应用的字典非空，条目 Enabled 停在默认 false。只在 admin 登记提供商（DB Provider）
        // 或只用 quota / thread 能力的应用，配置里没有任何 Enabled=true 的条目，DefaultProvider 默认
        // "OpenAI" 找不到 → ValidateOnStart 让宿主起不来，而那个变量应用自己改不掉。
        // 没有已启用 provider 时 DefaultProvider 指向谁都解析不出东西，校验它没有运行时后果。
        var hasProviders = options.Providers.Values.Any(provider => provider.Enabled);

        // 允许零已启用 provider 配置（AI 功能降级为不可用，但模块正常加载），
        // 但仍需继续校验 Permissions / MCP / Guardrails 等其他子模块。
        if (hasProviders)
        {
            if (string.IsNullOrWhiteSpace(options.DefaultProvider))
            {
                errors.Add("DefaultProvider cannot be null or empty");
            }
            else if (!options.Providers.ContainsKey(options.DefaultProvider))
            {
                errors.Add(
                    $"DefaultProvider '{options.DefaultProvider}' is not found in Providers " +
                    $"(configured providers: {string.Join(", ", options.Providers.Keys)}). " +
                    "AI:Providers merges every configuration source, including environment variables " +
                    "named AI__Providers__<Name>__<Field>.");
            }
            else if (!options.Providers[options.DefaultProvider].Enabled)
            {
                errors.Add($"DefaultProvider '{options.DefaultProvider}' is disabled");
            }
        }

        ValidatePermissionRules(options.Permissions, errors);
        ValidateMemoryOptions(options.ContextProviders?.Memory, errors);
        ValidateAdHocTools(options.AdHocTools, errors);
        ValidateCostTracking(options.CostTracking, errors);

        // ToolCacheSeconds 与部署配置的服务器清单无关（它是 [RuntimeSetting]，服务器只经数据库
        // 注册表提供时依然生效），因此不能锁在 Servers != null 分支里。
        if (options.Mcp is { Enabled: true }
            && (options.Mcp.ToolCacheSeconds < 0 || options.Mcp.ToolCacheSeconds > 3600))
        {
            errors.Add("MCP ToolCacheSeconds must be between 0 and 3600.");
        }

        // MCP：Enabled 时允许 Servers 为空，服务器也可由数据库注册表（McpServerRegistration，
        // admin 运行时录入，经 IMcpServerCatalog 物化）提供；下方仅校验已声明的部署配置服务器。
        if (options.Mcp != null && options.Mcp.Enabled && options.Mcp.Servers != null)
        {
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in options.Mcp.Servers)
            {
                if (string.IsNullOrWhiteSpace(server.Name))
                {
                    errors.Add("MCP server Name cannot be null or empty");
                    continue;
                }
                if (!seenNames.Add(server.Name))
                {
                    errors.Add($"MCP server name '{server.Name}' is duplicated");
                }
                switch (server.ConnectionType)
                {
                    case McpConnectionType.Stdio:
                        var hasCommand = !string.IsNullOrWhiteSpace(server.Command);
                        var hasArgs = server.Arguments != null && server.Arguments.Count > 0;
                        if (!hasCommand && !hasArgs)
                        {
                            errors.Add($"MCP server '{server.Name}': Stdio requires Command or non-empty Arguments");
                        }
                        break;
                    case McpConnectionType.Http:
                        if (string.IsNullOrWhiteSpace(server.Endpoint))
                        {
                            errors.Add($"MCP server '{server.Name}': Endpoint is required for Http connection");
                        }
                        else if (!Uri.TryCreate(server.Endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        {
                            errors.Add($"MCP server '{server.Name}': Endpoint must be a valid HTTP or HTTPS URL");
                        }
                        if (server.Headers != null)
                        {
                            foreach (var key in server.Headers.Keys)
                            {
                                if (string.IsNullOrWhiteSpace(key))
                                {
                                    errors.Add($"MCP server '{server.Name}': Header key cannot be empty");
                                    break;
                                }
                            }
                        }
                        break;
                    default:
                        errors.Add($"MCP server '{server.Name}': connection type '{server.ConnectionType}' is not supported");
                        break;
                }

                if (server.OAuth != null)
                {
                    if (string.IsNullOrWhiteSpace(server.OAuth.ClientId))
                    {
                        errors.Add($"MCP server '{server.Name}': OAuth ClientId cannot be empty");
                    }

                    var hasTokenEndpoint = !string.IsNullOrWhiteSpace(server.OAuth.TokenEndpoint);
                    var hasDiscoverySource = !string.IsNullOrWhiteSpace(server.OAuth.MetadataUrl)
                        || !string.IsNullOrWhiteSpace(server.OAuth.AuthorizationServer);
                    if (!hasTokenEndpoint && !(server.OAuth.EnableMetadataDiscovery && hasDiscoverySource))
                    {
                        errors.Add($"MCP server '{server.Name}': OAuth requires TokenEndpoint or metadata discovery configuration");
                    }
                }
            }
        }

        // Guardrails options validation
        if (options.Guardrails is { Enabled: true })
        {
            if (options.Guardrails.StreamingOverlapSize <= 0)
                errors.Add("Guardrails StreamingOverlapSize must be greater than 0");
        }

        // Retry options validation
        if (options.Retry is { Enabled: true })
        {
            if (options.Retry.MaxRetries < 0 || options.Retry.MaxRetries > 10)
                errors.Add("Retry MaxRetries must be between 0 and 10");
        }

        // History reduction validation
        if (options.History?.Reduction is { Mode: HistoryReductionMode.Summarize or HistoryReductionMode.PruneThenSummarize })
        {
            if (options.History.Reduction.Summarize.MaxSummaryTokens <= 0)
                errors.Add("History Summarize MaxSummaryTokens must be greater than 0");
        }

        // 验证每个启用的提供商
        if (!hasProviders)
        {
            return;
        }

        foreach (var (providerName, providerOptions) in options.Providers)
        {
            if (!providerOptions.Enabled)
            {
                continue;
            }

            // 验证 DefaultModel
            if (string.IsNullOrWhiteSpace(providerOptions.DefaultModel))
            {
                errors.Add($"Provider '{providerName}' DefaultModel cannot be null or empty when enabled");
            }

            // 验证 API Key（PostConfigure 已处理环境变量注入，此处直接检查最终值）
            var apiKey = providerOptions.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                var envVarName = $"AI__{providerName.ToUpperInvariant()}__APIKEY";
                errors.Add(
                    $"Provider '{providerName}' is enabled but ApiKey is missing. " +
                    $"Set ApiKey in configuration or environment variable '{envVarName}'");
                continue; // 如果没有 API Key，跳过后续验证
            }

            // 验证 API Key 格式
            ValidateApiKeyFormat(providerName, apiKey, errors);

            // 验证 BaseUrl：提供时校验格式；未提供时由具体 IChatClientProvider 实现决定（如内置 OpenAI provider 有默认 endpoint，自定义 provider 可能不需要）
            if (!string.IsNullOrWhiteSpace(providerOptions.BaseUrl))
            {
                if (!Uri.TryCreate(providerOptions.BaseUrl, UriKind.Absolute, out var baseUri))
                {
                    errors.Add($"Provider '{providerName}' BaseUrl '{providerOptions.BaseUrl}' is not a valid URI");
                }
                else if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
                {
                    errors.Add($"Provider '{providerName}' BaseUrl must use HTTP or HTTPS protocol");
                }
            }

            // 验证 TimeoutSeconds（可空：未设置时跳过，显式设置时校验范围）
            if (providerOptions.TimeoutSeconds.HasValue &&
                (providerOptions.TimeoutSeconds.Value <= 0 || providerOptions.TimeoutSeconds.Value > MaxProviderTimeoutSeconds))
            {
                errors.Add($"Provider '{providerName}' TimeoutSeconds must be between 1 and {MaxProviderTimeoutSeconds}");
            }

            // 验证 Prompt Caching：Anthropic 每请求最多 4 个 cache_control 块，中间件按价值封顶；
            // 一个永远兑现不了的「最近 N 条」配置直接拒绝，而不是每次请求都安静地少给
            if (providerOptions.PromptCaching is { } caching)
            {
                if (caching.CacheFirstNMessages < 0)
                {
                    errors.Add($"Provider '{providerName}' PromptCaching.CacheFirstNMessages cannot be negative");
                }
                if (caching.CacheRecentUserMessages < 0 || caching.CacheRecentUserMessages > PromptCachingMiddleware.MaxAnthropicBreakpoints)
                {
                    errors.Add($"Provider '{providerName}' PromptCaching.CacheRecentUserMessages must be between 0 and {PromptCachingMiddleware.MaxAnthropicBreakpoints} (Anthropic allows at most {PromptCachingMiddleware.MaxAnthropicBreakpoints} cache breakpoints per request)");
                }
            }

            // 验证 Models 别名字典（如有）
            if (providerOptions.Models != null)
            {
                foreach (var (alias, modelName) in providerOptions.Models)
                {
                    if (string.IsNullOrWhiteSpace(modelName))
                    {
                        errors.Add($"Provider '{providerName}' Models alias '{alias}' has empty model name");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 验证 API Key 格式
    /// </summary>
    private static void ValidateApiKeyFormat(string providerName, string apiKey, List<string> errors)
    {
        switch (providerName.ToLowerInvariant())
        {
            case "openai":
                ValidateOpenAIApiKey(apiKey, errors);
                break;
            case "azureopenai":
                // Azure OpenAI API Key 格式多样，暂不严格验证
                break;
            default:
                // 其他提供商暂不验证格式
                break;
        }
    }

    /// <summary>
    /// 验证 OpenAI API Key 格式
    /// </summary>
    private static void ValidateOpenAIApiKey(string apiKey, List<string> errors)
    {
        // OpenAI API Key 格式: sk-... 或 sk-proj-...
        if (!apiKey.StartsWith("sk-", StringComparison.Ordinal))
        {
            errors.Add(
                "OpenAI API Key must start with 'sk-'. " +
                "Please check your API Key at https://platform.openai.com/api-keys");
        }

        // 检查长度（OpenAI API Key 通常有最小长度）
        if (apiKey.Length < 20)
        {
            errors.Add("OpenAI API Key appears to be too short");
        }
    }

    private static void ValidatePermissionRules(ToolPermissionOptions? permissions, List<string> errors)
    {
        if (permissions == null || !permissions.Enabled)
        {
            return;
        }

        ValidateRuleGroup(permissions.SystemRules, "SystemRules", errors);
        ValidateRuleGroup(permissions.ProjectRules, "ProjectRules", errors);
        ValidateRuleGroup(permissions.UserRules, "UserRules", errors);
        ValidateRuleGroup(permissions.SessionRules, "SessionRules", errors);
    }

    private static void ValidateRuleGroup(
        IEnumerable<ToolPermissionRuleOptions>? rules,
        string groupName,
        List<string> errors)
    {
        if (rules == null)
        {
            return;
        }

        var index = 0;
        foreach (var rule in rules)
        {
            if (rule == null)
            {
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ToolPattern))
            {
                errors.Add($"AI:Permissions:{groupName}[{index}] ToolPattern cannot be empty.");
            }

            if (!string.IsNullOrWhiteSpace(rule.SubAgentName)
                && rule.SubAgentName.Length > 200)
            {
                errors.Add($"AI:Permissions:{groupName}[{index}] SubAgentName is too long.");
            }

            if (!string.IsNullOrWhiteSpace(rule.WorkflowNodeName)
                && rule.WorkflowNodeName.Length > 200)
            {
                errors.Add($"AI:Permissions:{groupName}[{index}] WorkflowNodeName is too long.");
            }

            index++;
        }
    }

    private static void ValidateMemoryOptions(MemoryOptions? memory, List<string> errors)
    {
        if (memory == null || !memory.Enabled)
        {
            return;
        }

        if (memory.EnableProjectSnapshot
            && string.IsNullOrWhiteSpace(memory.ProjectSnapshotScopePrefix))
        {
            errors.Add("AI:ContextProviders:Memory:ProjectSnapshotScopePrefix cannot be empty when project snapshot is enabled.");
        }

        if (!string.IsNullOrWhiteSpace(memory.ProjectSnapshotScopePrefix)
            && memory.ProjectSnapshotScopePrefix.Length > 64)
        {
            errors.Add("AI:ContextProviders:Memory:ProjectSnapshotScopePrefix is too long.");
        }
    }

    /// <summary>
    /// 成本追踪开着时，每条费率都得是真的费率。
    /// </summary>
    /// <remarks>
    /// 一条 Input 与 Output 同时为 0 的费率没有合法用途（免费模型应当不配，计算器答 null 而不是 $0），
    /// 却正是「键名写错」的唯一症状：绑定器对未知键静默忽略，<c>ModelCostRate</c> 被建出来但字段全 0，
    /// 每条用量都记成 $0（非 null），<c>BudgetService</c> 的 Indeterminate 判据只认 null，预算永不触发，
    /// 界面显示「已启用、$0 / 上限」。docs/modules/ai.md 的 Budget 示例就这样写了几个月。
    /// 启动即拒而不是 warning：没有任何部署需要一条 0/0 的费率。
    /// </remarks>
    private static void ValidateCostTracking(CostTrackingOptions? costTracking, List<string> errors)
    {
        if (costTracking is not { Enabled: true })
        {
            return;
        }

        if (costTracking.DefaultCostRate != null)
        {
            ValidateCostRate("AI:CostTracking:DefaultCostRate", costTracking.DefaultCostRate, errors);
        }

        foreach (var (provider, models) in costTracking.ModelCosts)
        {
            foreach (var (model, rate) in models)
            {
                ValidateCostRate($"AI:CostTracking:ModelCosts:{provider}:{model}", rate, errors);
            }
        }
    }

    private static void ValidateCostRate(string path, ModelCostRate rate, List<string> errors)
    {
        if (rate.InputCostPer1MTokens < 0 || rate.OutputCostPer1MTokens < 0 || rate.CachedInputCostPer1MTokens < 0)
        {
            errors.Add($"{path}: InputCostPer1MTokens, OutputCostPer1MTokens and CachedInputCostPer1MTokens cannot be negative.");
            return;
        }

        if (rate.InputCostPer1MTokens == 0 && rate.OutputCostPer1MTokens == 0)
        {
            errors.Add(
                $"{path}: InputCostPer1MTokens and OutputCostPer1MTokens are both 0. " +
                "The rate keys are InputCostPer1MTokens / OutputCostPer1MTokens (USD per million tokens); " +
                "a zero rate usually means the key names did not bind. " +
                "A model name containing ':' (an Ollama tag such as qwen3:8b) cannot be a configuration key at all, " +
                "because ':' is the configuration path separator; price such models through the provider's \"*\" wildcard entry. " +
                "Remove the entry for a free model instead of configuring 0.");
        }
    }

    private static void ValidateAdHocTools(AdHocToolsOptions? adHocTools, List<string> errors)
    {
        if (adHocTools is null)
        {
            errors.Add("AdHocTools cannot be null");
            return;
        }

        if (adHocTools.AllowedGroups is null || adHocTools.AllowedGroups.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("AdHocTools.AllowedGroups cannot be null or contain blank entries");
        }

        if (adHocTools.AllowedTools is null || adHocTools.AllowedTools.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("AdHocTools.AllowedTools cannot be null or contain blank entries");
        }
    }
}
