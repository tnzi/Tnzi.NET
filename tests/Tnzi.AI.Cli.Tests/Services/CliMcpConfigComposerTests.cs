namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 受管 MCP 配置的合并与 fail-closed 行为。
/// </summary>
public class CliMcpConfigComposerTests
{
    private static CliMcpConfigComposer Composer() => new(NullLogger<CliMcpConfigComposer>.Instance);

    private static CliWriteBackOptions WriteBack(bool enabled = true) => new()
    {
        Enabled = enabled,
        McpEndpoint = "https://api.example.com/mcp",
        ServerName = "tnzi"
    };

    [Fact]
    public void Compose_WithNothingConfigured_ReturnsNullSoTheCliInheritsItsOwnConfig()
    {
        Composer().Compose(null, null, WriteBack(enabled: false)).ShouldBeNull();
    }

    [Fact]
    public void Compose_InjectsWriteBackChannelWithRunScopedBearer()
    {
        var json = Composer().Compose(null, "tnzi-run_secret", WriteBack());

        json.ShouldNotBeNull();
        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["tnzi"]!["url"]!.GetValue<string>().ShouldBe("https://api.example.com/mcp");
        servers["tnzi"]!["headers"]!["Authorization"]!.GetValue<string>()
            .ShouldBe("Bearer tnzi-run_secret");
    }

    [Fact]
    public void Compose_KeepsBindingServersAlongsideTheWriteBackChannel()
    {
        var binding = """{"mcpServers":{"github":{"command":"gh-mcp","args":["serve"]}}}""";

        var json = Composer().Compose(binding, "tnzi-run_secret", WriteBack());

        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["github"]!["command"]!.GetValue<string>().ShouldBe("gh-mcp");
        servers["tnzi"].ShouldNotBeNull();
    }

    [Fact]
    public void Compose_WithWriteBackDisabled_DoesNotInjectAnyCredential()
    {
        var binding = """{"mcpServers":{"github":{"command":"gh-mcp"}}}""";

        var json = Composer().Compose(binding, "tnzi-run_secret", WriteBack(enabled: false));

        json.ShouldNotBeNull();
        json!.ShouldNotContain("tnzi-run_secret");
        JsonNode.Parse(json)!["mcpServers"]!["tnzi"].ShouldBeNull();
    }

    [Fact]
    public void Compose_WithMalformedBindingConfig_FailsClosedToAnEmptyServerSet()
    {
        // 「配置写错了但照样跑」会让 agent 悄悄继承宿主本机的全部 MCP server —— 那是个越权面。
        var json = Composer().Compose("{ not json", null, WriteBack(enabled: false));

        json.ShouldNotBeNull();
        JsonNode.Parse(json!)!["mcpServers"]!.AsObject().Count.ShouldBe(0);
    }

    [Fact]
    public void Compose_WithEnabledWriteBackButNoEndpoint_ProducesNoChannel()
    {
        var options = new CliWriteBackOptions { Enabled = true, McpEndpoint = null };

        var json = Composer().Compose(
            """{"mcpServers":{"github":{"command":"gh-mcp"}}}""", "tnzi-run_secret", options);

        JsonNode.Parse(json!)!["mcpServers"]!["tnzi"].ShouldBeNull();
    }
}
