using Tnzi.AI.Mcp.Options;

namespace Tnzi.AI.Tests.Mcp;

public class McpServerOptionsValidatorTests
{
    // ─── Gate-on-Enabled：禁用的服务器不得阻塞启动 ───────────────────────────

    [Fact]
    public void Validate_Disabled_IncompleteConfig_Passes()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = false,
            Endpoint = "",
            RequireAuthentication = true,
            AllowedApiKeys = [],
            RateLimitTrackingMaxEntries = 0
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Enabled_MissingEndpoint_Fails()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            Endpoint = "",
            RequireAuthentication = false
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Endpoint is required.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Enabled_AuthWithoutKeys_Fails()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = true,
            AllowedApiKeys = []
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains(
            "RequireAuthentication is enabled but no AllowedApiKeys are configured.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EndpointWithoutLeadingSlash_FailsEvenWhenDisabled()
    {
        // 格式校验不受 Enabled 门控 - 只要给了值就检查
        var result = Validate(new McpServerOptions
        {
            Enabled = false,
            Endpoint = "mcp"
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Endpoint must start with '/'.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeRateLimit_Fails()
    {
        var result = Validate(new McpServerOptions
        {
            RateLimitPerMinute = -1
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains("Rate limit must be >= 0.", StringComparison.Ordinal));
    }

    // ─── RateLimitTrackingMaxEntries 下限 ────────────────────────────────────

    [Fact]
    public void Validate_Enabled_RateLimitOn_MaxEntriesZero_Fails()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            RateLimitPerMinute = 600,
            RateLimitTrackingMaxEntries = 0
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains(
            "RateLimitTrackingMaxEntries must be >= 1 when rate limiting is enabled.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Enabled_RateLimitOn_NegativeMaxEntries_Fails()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            RateLimitPerMinute = 600,
            RateLimitTrackingMaxEntries = -5
        });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(f => f.Contains(
            "RateLimitTrackingMaxEntries must be >= 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_Enabled_RateLimitOn_MaxEntriesOne_Passes()
    {
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            RateLimitPerMinute = 600,
            RateLimitTrackingMaxEntries = 1
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Enabled_RateLimitDisabled_MaxEntriesZero_Passes()
    {
        // 限流关闭（RateLimitPerMinute=0）时跟踪表不工作，不强制下限
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false,
            RateLimitPerMinute = 0,
            RateLimitTrackingMaxEntries = 0
        });

        result.Succeeded.ShouldBeTrue();
    }

    // ─── Warnings（不阻塞启动）─────────────────────────────────────────────

    [Fact]
    public void Warnings_EnabledWithoutAuthentication_EmitsWarning()
    {
        var warnings = CollectWarnings(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = false
        });

        warnings.ShouldContain(w => w.Contains("MCP server is enabled without authentication", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_EnabledWithAuthentication_NoAuthWarning()
    {
        var warnings = CollectWarnings(new McpServerOptions
        {
            Enabled = true,
            RequireAuthentication = true,
            AllowedApiKeys = ["secret"]
        });

        warnings.ShouldNotContain(w => w.Contains("without authentication", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_DisabledWithoutAuthentication_NoWarning()
    {
        var warnings = CollectWarnings(new McpServerOptions
        {
            Enabled = false,
            RequireAuthentication = false
        });

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Warnings_AllowApiKeyInQuery_EmitsWarning()
    {
        var warnings = CollectWarnings(new McpServerOptions
        {
            AllowApiKeyInQuery = true
        });

        warnings.ShouldContain(w => w.Contains("AllowApiKeyInQuery is enabled.", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WarningsOnly_DoesNotFail()
    {
        // 警告（裸奔 + query key）只记日志，不阻塞启动
        var result = Validate(new McpServerOptions
        {
            Enabled = true,
            Endpoint = "/mcp",
            RequireAuthentication = false,
            AllowApiKeyInQuery = true
        });

        result.Succeeded.ShouldBeTrue();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ValidateOptionsResult Validate(McpServerOptions options)
    {
        var validator = new McpServerOptionsValidator();
        return validator.Validate(null, options);
    }

    private static List<string> CollectWarnings(McpServerOptions options)
    {
        var validator = new TestableMcpServerOptionsValidator();
        return validator.CollectWarningsPublic(options);
    }

    private sealed class TestableMcpServerOptionsValidator : McpServerOptionsValidator
    {
        public List<string> CollectWarningsPublic(McpServerOptions options)
        {
            var warnings = new List<string>();
            CollectWarnings(options, warnings);
            return warnings;
        }
    }
}
