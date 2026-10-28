namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 受管 MCP 配置的合并与 fail-closed 行为。
/// </summary>
public class CliMcpConfigComposerTests
{
    private static CliMcpConfigComposer Composer() => new(NullLogger<CliMcpConfigComposer>.Instance);

    /// <summary>在本机目标上组装（回写通道是直连的 HTTP 条目）。</summary>
    private static string? Compose(string? bindingMcpConfigJson, string? writeBackToken, CliWriteBackOptions options)
        => Composer().Compose(bindingMcpConfigJson, writeBackToken, options, LocalHosts.Create());

    private static CliWriteBackOptions WriteBack(bool enabled = true) => new()
    {
        Enabled = enabled,
        McpEndpoint = "https://api.example.com/mcp",
        ServerName = "tnzi"
    };

    [Fact]
    public void Compose_WithNothingConfigured_ReturnsNullSoTheCliInheritsItsOwnConfig()
    {
        Compose(null, null, WriteBack(enabled: false)).ShouldBeNull();
    }

    [Fact]
    public void Compose_InjectsWriteBackChannelWithRunScopedBearer()
    {
        var json = Compose(null, "tnzi-run_secret", WriteBack());

        json.ShouldNotBeNull();
        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["tnzi"]!["url"]!.GetValue<string>().ShouldBe("https://api.example.com/mcp");
        servers["tnzi"]!["headers"]!["Authorization"]!.GetValue<string>()
            .ShouldBe("Bearer tnzi-run_secret");
    }

    [Fact]
    public void Compose_DeclaresTheTransportTheMcpServerActuallyServes()
    {
        // Tnzi.AI.Mcp 只提供 Streamable HTTP，在 Claude 风格的 MCP 配置里它叫 "http"；"sse" 是旧版 SSE 传输，
        // 写成它时 CLI 连不上回写端点，agent 手里一个回写工具都没有，而运行照常成功
        var json = Compose(null, "tnzi-run_secret", WriteBack());

        JsonNode.Parse(json!)!["mcpServers"]!["tnzi"]!["type"]!.GetValue<string>().ShouldBe("http");
    }

    [Fact]
    public void Compose_KeepsBindingServersAlongsideTheWriteBackChannel()
    {
        var binding = """{"mcpServers":{"github":{"command":"gh-mcp","args":["serve"]}}}""";

        var json = Compose(binding, "tnzi-run_secret", WriteBack());

        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["github"]!["command"]!.GetValue<string>().ShouldBe("gh-mcp");
        servers["tnzi"].ShouldNotBeNull();
    }

    [Fact]
    public void Compose_WithWriteBackDisabled_DoesNotInjectAnyCredential()
    {
        var binding = """{"mcpServers":{"github":{"command":"gh-mcp"}}}""";

        var json = Compose(binding, "tnzi-run_secret", WriteBack(enabled: false));

        json.ShouldNotBeNull();
        json!.ShouldNotContain("tnzi-run_secret");
        JsonNode.Parse(json)!["mcpServers"]!["tnzi"].ShouldBeNull();
    }

    [Fact]
    public void Compose_WithMalformedBindingConfig_FailsClosedToAnEmptyServerSet()
    {
        // 「配置写错了但照样跑」会让 agent 悄悄继承宿主本机的全部 MCP server —— 那是个越权面。
        var json = Compose("{ not json", null, WriteBack(enabled: false));

        json.ShouldNotBeNull();
        JsonNode.Parse(json!)!["mcpServers"]!.AsObject().Count.ShouldBe(0);
    }

    [Fact]
    public void Compose_WithToolCallTimeout_WritesItInMillisecondsOnTheWriteBackEntry()
    {
        // claude 按这个字段限制每次调用、也按它等响应开始；不写时只等 60 秒，代理后面跑几分钟的回写工具每次都在 60 秒被掐断
        var options = WriteBack();
        options.ToolCallTimeout = TimeSpan.FromMinutes(15);

        var json = Compose(null, "tnzi-run_secret", options);

        JsonNode.Parse(json!)!["mcpServers"]!["tnzi"]!["timeout"]!.GetValue<long>().ShouldBe(900_000);
    }

    [Fact]
    public void Compose_WithoutToolCallTimeout_OmitsTheFieldSoTheCliKeepsItsDefault()
    {
        var json = Compose(null, "tnzi-run_secret", WriteBack());

        JsonNode.Parse(json!)!["mcpServers"]!["tnzi"]!.AsObject().ContainsKey("timeout").ShouldBeFalse();
    }

    [Fact]
    public void Compose_ToolCallTimeout_LeavesBindingServersAsTheirOwnJsonSaysTheyAre()
    {
        var binding = """{"mcpServers":{"slow":{"type":"http","url":"https://slow.example.com/mcp","timeout":5000},"github":{"command":"gh-mcp"}}}""";
        var options = WriteBack();
        options.ToolCallTimeout = TimeSpan.FromMinutes(15);

        var json = Compose(binding, "tnzi-run_secret", options);

        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["slow"]!["timeout"]!.GetValue<long>().ShouldBe(5000);
        servers["github"]!.AsObject().ContainsKey("timeout").ShouldBeFalse();
        servers["tnzi"]!["timeout"]!.GetValue<long>().ShouldBe(900_000);
    }

    [Fact]
    public void Compose_WithEnabledWriteBackButNoEndpoint_ProducesNoChannel()
    {
        var options = new CliWriteBackOptions { Enabled = true, McpEndpoint = null };

        var json = Compose(
            """{"mcpServers":{"github":{"command":"gh-mcp"}}}""", "tnzi-run_secret", options);

        JsonNode.Parse(json!)!["mcpServers"]!["tnzi"].ShouldBeNull();
    }

    /// <summary>
    /// 回写条目的形状由执行目标决定：连不到平台地址的远程目标写成自己的中继命令，凭据、端点与时长上限都交给它；
    /// 绑定自带的 server 不受影响。
    /// </summary>
    [Fact]
    public void Compose_TheTargetDecidesTheWriteBackEntry()
    {
        CliWriteBackChannel? received = null;
        var remote = new TestCliHost((_, _) => throw new InvalidOperationException("not started"), writeBackEntry: channel =>
        {
            received = channel;
            return new JsonObject { ["type"] = "stdio", ["command"] = "/opt/agent/bin/agent", ["args"] = new JsonArray("mcp-relay") };
        });
        var options = WriteBack();
        options.ToolCallTimeout = TimeSpan.FromMinutes(2);

        var json = Composer().Compose(@"{""mcpServers"":{""github"":{""command"":""gh-mcp""}}}", "tnzi-run_secret", options, remote);

        var servers = JsonNode.Parse(json!)!["mcpServers"]!;
        servers["tnzi"]!["command"]!.GetValue<string>().ShouldBe("/opt/agent/bin/agent");
        servers["tnzi"]!.AsObject().ContainsKey("url").ShouldBeFalse();
        servers["github"]!["command"]!.GetValue<string>().ShouldBe("gh-mcp");
        received.ShouldNotBeNull();
        received.Endpoint.ShouldBe("https://api.example.com/mcp");
        received.Token.ShouldBe("tnzi-run_secret");
        received.ToolCallTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        received.ToString().ShouldNotContain("tnzi-run_secret");
    }

    /// <summary>本机目标沿用直连平台端点的 Streamable HTTP 条目（与引入执行宿主之前逐字相同）。</summary>
    [Fact]
    public void Compose_OnTheLocalTarget_KeepsTheHttpEntry()
    {
        var json = Compose(null, "tnzi-run_secret", WriteBack());

        var entry = JsonNode.Parse(json!)!["mcpServers"]!["tnzi"]!.AsObject();
        entry["type"]!.GetValue<string>().ShouldBe("http");
        entry["url"]!.GetValue<string>().ShouldBe("https://api.example.com/mcp");
        entry["headers"]!["Authorization"]!.GetValue<string>().ShouldBe("Bearer tnzi-run_secret");
        entry.Select(p => p.Key).ShouldBe(["type", "url", "headers"]);
    }
}
