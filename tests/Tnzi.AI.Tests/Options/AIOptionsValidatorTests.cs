namespace Tnzi.AI.Tests.Options;

public class AIOptionsValidatorTests
{
    [Fact]
    public void Validate_ShouldPass_WhenProvidersEmptyAndNoOtherIssues()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new()
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_AdHocTools_BlankEntry_Fails()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            AdHocTools = new AdHocToolsOptions { AllowedGroups = ["datetime", " "] }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("AdHocTools.AllowedGroups");
    }

    [Fact]
    public void Validate_AdHocTools_DefaultsAreEmptyAndValid()
    {
        var options = new AIOptions { Providers = new() };

        options.AdHocTools.AllowedGroups.ShouldBeEmpty();
        options.AdHocTools.AllowedTools.ShouldBeEmpty();
        ValidateOptions(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ProvidersNonEmptyButAllDisabled_Succeeds()
    {
        // 同机器为别的应用导出的 AI__Providers__DeepSeek__ApiKey 会合并进每个应用的字典，
        // 条目 Enabled 停在默认 false；DefaultProvider 默认 "OpenAI" 不在字典里。
        // 没有任何已启用 provider 时，校验 DefaultProvider 没有运行时后果，不能让宿主起不来。
        var result = ValidateOptions(new AIOptions
        {
            Providers = new()
            {
                ["DeepSeek"] = new ProviderOptions { ApiKey = "leaked-from-env", Enabled = false }
            }
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EnabledProviderAndMisnamedDefault_StillFails()
    {
        var result = ValidateOptions(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new()
            {
                ["DeepSeek"] = new ProviderOptions { ApiKey = "k", Enabled = true, DefaultModel = "deepseek-chat" }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("DefaultProvider 'OpenAI' is not found in Providers", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_MisnamedDefault_MessageListsPresentKeysAndEnvShape()
    {
        var result = ValidateOptions(new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new()
            {
                ["DeepSeek"] = new ProviderOptions { ApiKey = "k", Enabled = true, DefaultModel = "deepseek-chat" }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("configured providers: DeepSeek", StringComparison.Ordinal));
        result.Failures.ShouldContain(x => x.Contains("AI__Providers__<Name>__<Field>", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailPermissions_WhenProvidersEmpty()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SystemRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = ""
                    }
                ]
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("AI:Permissions:SystemRules[0] ToolPattern cannot be empty.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_McpEnabledWithEmptyServers_DoesNotFailOnMcp()
    {
        // MCP servers may be supplied entirely by the database registry (McpServerRegistration),
        // so Enabled=true with an empty deployment server list is a valid configuration.
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers = []
            }
        });

        result.Failures?.ShouldNotContain(x => x.Contains("MCP", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailLongSubAgentName_WhenProvidersEmpty()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SessionRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = "bash",
                        SubAgentName = new string('a', 201)
                    }
                ]
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("AI:Permissions:SessionRules[0] SubAgentName is too long.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailLongWorkflowNodeName_WhenProvidersEmpty()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Permissions = new ToolPermissionOptions
            {
                Enabled = true,
                SessionRules =
                [
                    new ToolPermissionRuleOptions
                    {
                        ToolPattern = "bash",
                        WorkflowNodeName = new string('n', 201)
                    }
                ]
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("AI:Permissions:SessionRules[0] WorkflowNodeName is too long.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailDefaultProvider_WhenConfiguredProvidersExist()
    {
        var result = ValidateOptions(new AIOptions
        {
            DefaultProvider = "",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openai"] = new()
                {
                    Enabled = true,
                    DefaultModel = "gpt-4o",
                    ApiKey = "sk-test-key-1234567890"
                }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("DefaultProvider cannot be null or empty", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailMcpOAuth_WhenEndpointAndDiscoveryMissing()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers =
                [
                    new McpServerConfig
                    {
                        Name = "oauth-server",
                        ConnectionType = McpConnectionType.Http,
                        Endpoint = "https://mcp.example.com",
                        OAuth = new McpOAuthConfig
                        {
                            ClientId = "client-id"
                        }
                    }
                ]
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("OAuth requires TokenEndpoint or metadata discovery configuration", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldPassMcpOAuth_WhenMetadataDiscoveryConfigured()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers =
                [
                    new McpServerConfig
                    {
                        Name = "oauth-server",
                        ConnectionType = McpConnectionType.Http,
                        Endpoint = "https://mcp.example.com",
                        OAuth = new McpOAuthConfig
                        {
                            ClientId = "client-id",
                            EnableMetadataDiscovery = true,
                            AuthorizationServer = "https://auth.example.com"
                        }
                    }
                ]
            }
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldFailProjectSnapshotPrefix_WhenEnabledAndEmpty()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            ContextProviders = new ContextProvidersOptions
            {
                Memory = new MemoryOptions
                {
                    Enabled = true,
                    EnableProjectSnapshot = true,
                    ProjectSnapshotScopePrefix = ""
                }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains(
            "AI:ContextProviders:Memory:ProjectSnapshotScopePrefix cannot be empty when project snapshot is enabled.",
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFailProjectSnapshotPrefix_WhenTooLong()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            ContextProviders = new ContextProvidersOptions
            {
                Memory = new MemoryOptions
                {
                    Enabled = true,
                    ProjectSnapshotScopePrefix = new string('p', 65)
                }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains(
            "AI:ContextProviders:Memory:ProjectSnapshotScopePrefix is too long.",
            StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(-1)]
    public void Validate_PromptCaching_RecentUserMessagesOutsideAnthropicBudget_Fails(int recent)
    {
        // Anthropic 每请求最多 4 个 cache_control 块：要 5 条最近消息的配置永远兑现不了，不该安静地少给
        var result = ValidateOptions(ProviderWithCaching(new PromptCachingOptions { Enabled = true, CacheRecentUserMessages = recent }));

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("CacheRecentUserMessages", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PromptCaching_NegativeFirstNMessages_Fails()
    {
        var result = ValidateOptions(ProviderWithCaching(new PromptCachingOptions { Enabled = true, CacheFirstNMessages = -1 }));

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("CacheFirstNMessages", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PromptCaching_WithinBudget_Passes()
    {
        ValidateOptions(ProviderWithCaching(new PromptCachingOptions { Enabled = true, CacheRecentUserMessages = 4, CacheFirstNMessages = 2 }))
            .Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// ★ 一条全 0 的费率没有合法用途（免费模型应当不配，计算器答 null 而不是 $0），却正是
    /// 「键名写错」的唯一症状：绑定器对未知键静默忽略，ModelCostRate 被建出来但字段全 0，
    /// 每条用量都记成 $0（非 null）→ Indeterminate 判据看不见 → 预算永不触发。
    /// docs/modules/ai.md 的 Budget 示例就这样写了几个月。启动即拒，不是 warning。
    /// </summary>
    [Fact]
    public void Validate_CostTracking_AllZeroModelRate_FailsAndNamesTheRealKeys()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions
            {
                Enabled = true,
                ModelCosts = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai"] = new() { ["gpt-4o"] = new ModelCostRate() },
                }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("ModelCosts:openai:gpt-4o", StringComparison.Ordinal)
                                           && x.Contains("InputCostPer1MTokens", StringComparison.Ordinal));
    }

    /// <summary>
    /// 模型名本身带 <c>:</c>（Ollama 标签 <c>qwen3:8b</c>）在 IConfiguration 里根本写不成键：
    /// <c>"Ollama:qwen3:8b"</c> 与嵌套写法都被拆成 <c>ModelCosts:Ollama:qwen3:8b</c>，绑定器为 <c>qwen3</c>
    /// 建出一条全 0 费率、把 <c>8b</c> 丢掉。这条配置拼写完全正确却过不了启动，提示不能只让人去核对
    /// InputCostPer1MTokens 的拼写，得说出真正的出路：按 provider 的 <c>*</c> 通配价。
    /// </summary>
    [Fact]
    public void Validate_CostTracking_AllZeroModelRate_ExplainsColonInModelNames()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions
            {
                Enabled = true,
                ModelCosts = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Ollama"] = new() { ["qwen3"] = new ModelCostRate() },
                }
            }
        });

        result.Failed.ShouldBeTrue();
        var failure = result.Failures.Single(x => x.Contains("ModelCosts:Ollama:qwen3", StringComparison.Ordinal));
        failure.ShouldContain("':'");
        failure.ShouldContain("\"*\"");
    }

    [Fact]
    public void Validate_CostTracking_AllZeroDefaultRate_Fails()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions { Enabled = true, DefaultCostRate = new ModelCostRate() }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("DefaultCostRate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CostTracking_NegativeRate_Fails()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions
            {
                Enabled = true,
                ModelCosts = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["openai"] = new() { ["gpt-4o"] = new ModelCostRate { InputCostPer1MTokens = 2.5m, OutputCostPer1MTokens = 10m, CachedInputCostPer1MTokens = -1m } },
                }
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("CachedInputCostPer1MTokens", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CostTracking_RealRates_Pass()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions
            {
                Enabled = true,
                DefaultCostRate = new ModelCostRate { InputCostPer1MTokens = 3m, OutputCostPer1MTokens = 15m },
                ModelCosts = new(StringComparer.OrdinalIgnoreCase)
                {
                    // 只收输出费的模型是合法形态；两项同时为 0 才是「没绑上」。
                    ["openai"] = new() { ["gpt-4o"] = new ModelCostRate { InputCostPer1MTokens = 0m, OutputCostPer1MTokens = 10m } },
                }
            }
        });

        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>成本追踪关着时费率根本不会被读，一段遗留的旧配置不该拦住启动。</summary>
    [Fact]
    public void Validate_CostTracking_Disabled_IgnoresRates()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            CostTracking = new CostTrackingOptions { Enabled = false, DefaultCostRate = new ModelCostRate() }
        });

        result.Succeeded.ShouldBeTrue();
    }

    private static AIOptions ProviderWithCaching(PromptCachingOptions caching) => new()
    {
        DefaultProvider = "anthropic",
        Providers = new Dictionary<string, ProviderOptions>
        {
            ["anthropic"] = new()
            {
                Enabled = true,
                DefaultModel = "claude-opus-5",
                ApiKey = "sk-ant-test-key-1234567890",
                PromptCaching = caching
            }
        }
    };

    private static ValidateOptionsResult ValidateOptions(AIOptions options)
    {
        var validator = new AIOptionsValidator();
        return validator.Validate(null, options);
    }
}
