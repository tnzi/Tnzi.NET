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
    public void Validate_ShouldFailMcp_WhenProvidersEmpty()
    {
        var result = ValidateOptions(new AIOptions
        {
            Providers = new(),
            Mcp = new McpOptions
            {
                Enabled = true,
                Servers = []
            }
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(x => x.Contains("MCP is enabled but Servers is null or empty.", StringComparison.Ordinal));
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

    private static ValidateOptionsResult ValidateOptions(AIOptions options)
    {
        var validator = new AIOptionsValidator();
        return validator.Validate(null, options);
    }
}
