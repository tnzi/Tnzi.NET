
namespace Tnzi.AI.Options;

/// <summary>
/// AI 配置选项验证器
/// </summary>
public class AIOptionsValidator : OptionsValidatorBase<AIOptions>
{
    protected override void ValidateOptions(AIOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultProvider))
        {
            errors.Add("DefaultProvider cannot be null or empty");
        }

        if (options.Providers == null || options.Providers.Count == 0)
        {
            errors.Add("At least one provider must be configured");
            return; // 如果没有提供商，后续验证无意义
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProvider) && !options.Providers.ContainsKey(options.DefaultProvider))
        {
            errors.Add($"DefaultProvider '{options.DefaultProvider}' is not found in Providers");
        }

        if (!string.IsNullOrWhiteSpace(options.DefaultProvider) && options.Providers.ContainsKey(options.DefaultProvider))
        {
            var defaultProvider = options.Providers[options.DefaultProvider];
            if (!defaultProvider.Enabled)
            {
                errors.Add($"DefaultProvider '{options.DefaultProvider}' is disabled");
            }
        }

        // MCP：Enabled 时必须有至少一个 Server；再校验各服务器配置（名称、连接方式与必填字段）
        if (options.Mcp != null && options.Mcp.Enabled && (options.Mcp.Servers == null || options.Mcp.Servers.Count == 0))
        {
            errors.Add("MCP is enabled but Servers is null or empty. Configure at least one server under AI:Mcp:Servers.");
        }

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
            }
        }

        // 验证每个启用的提供商
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

            // 验证 API Key（支持从环境变量读取）
            var apiKey = providerOptions.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                // 尝试从环境变量读取
                var envVarName = $"AI__{providerName.ToUpperInvariant()}__APIKEY";
                apiKey = Environment.GetEnvironmentVariable(envVarName);

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    errors.Add(
                        $"Provider '{providerName}' is enabled but ApiKey is missing. " +
                        $"Please set ApiKey in configuration or environment variable '{envVarName}'");
                    continue; // 如果没有API Key，跳过后续验证
                }
            }

            // 验证 API Key 格式
            ValidateApiKeyFormat(providerName, apiKey, errors);

            // 验证 BaseUrl：OpenAI 可选（未配置时使用默认 endpoint），其他提供商必填
            var isOpenAI = string.Equals(providerName, "OpenAI", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(providerOptions.BaseUrl))
            {
                if (!isOpenAI)
                {
                    errors.Add($"Provider '{providerName}' BaseUrl cannot be null or empty when enabled");
                }
            }
            else if (!Uri.TryCreate(providerOptions.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                errors.Add($"Provider '{providerName}' BaseUrl '{providerOptions.BaseUrl}' is not a valid URI");
            }
            else
            {
                if (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp)
                {
                    errors.Add($"Provider '{providerName}' BaseUrl must use HTTP or HTTPS protocol");
                }
            }

            // 验证 TimeoutSeconds
            if (providerOptions.TimeoutSeconds <= 0 || providerOptions.TimeoutSeconds > 600)
            {
                errors.Add($"Provider '{providerName}' TimeoutSeconds must be between 1 and 600");
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
}