
namespace Tnzi.AI.Options;

/// <summary>
/// AI 配置选项验证器
/// </summary>
public class AIOptionsValidator : OptionsValidatorBase<AIOptions>
{
    protected override void ValidateOptions(AIOptions options, List<string> errors)
    {
        var hasProviders = options.Providers != null && options.Providers.Count > 0;

        // 允许零 provider 配置（AI 功能降级为不可用，但模块正常加载），
        // 但仍需继续校验 Permissions / MCP / Guardrails 等其他子模块。
        if (hasProviders)
        {
            if (string.IsNullOrWhiteSpace(options.DefaultProvider))
            {
                errors.Add("DefaultProvider cannot be null or empty");
            }
            else if (!options.Providers!.ContainsKey(options.DefaultProvider))
            {
                errors.Add($"DefaultProvider '{options.DefaultProvider}' is not found in Providers");
            }
            else if (!options.Providers[options.DefaultProvider].Enabled)
            {
                errors.Add($"DefaultProvider '{options.DefaultProvider}' is disabled");
            }
        }

        // MCP：Enabled 时必须有至少一个 Server；再校验各服务器配置（名称、连接方式与必填字段）
        if (options.Mcp != null && options.Mcp.Enabled && (options.Mcp.Servers == null || options.Mcp.Servers.Count == 0))
        {
            errors.Add("MCP is enabled but Servers is null or empty. Configure at least one server under AI:Mcp:Servers.");
        }

        ValidatePermissionRules(options.Permissions, errors);
        ValidateMemoryOptions(options.ContextProviders?.Memory, errors);

        if (options.Mcp != null && options.Mcp.Enabled && options.Mcp.Servers != null)
        {
            if (options.Mcp.ToolCacheSeconds < 0 || options.Mcp.ToolCacheSeconds > 3600)
            {
                errors.Add("MCP ToolCacheSeconds must be between 0 and 3600.");
            }

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
        if (options.History?.Reduction is { Mode: HistoryReductionMode.Summarize })
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
                (providerOptions.TimeoutSeconds.Value <= 0 || providerOptions.TimeoutSeconds.Value > 600))
            {
                errors.Add($"Provider '{providerName}' TimeoutSeconds must be between 1 and 600");
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
        if (!apiKey.StartsWith("sk-"))
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
}
