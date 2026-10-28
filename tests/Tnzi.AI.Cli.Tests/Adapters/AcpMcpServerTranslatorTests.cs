namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// Claude 格式的 MCP 配置翻成 ACP 的 <c>McpServer</c> 时形状要写全：http / sse 的 <c>headers</c> 是必填的
/// <c>{name, value}</c> 数组，stdio 的 <c>args</c> / <c>env</c> 都是数组。
/// </summary>
/// <remarks>
/// 此前请求头原样作为对象发出、没有请求头就干脆不给这个键：按 schema 校验参数的运行时以「参数无效」拒掉
/// <c>session/new</c>，每次运行都失败；宽松的运行时丢掉对象形的请求头，回写端点的 Authorization 就这样没了。
/// </remarks>
public class AcpMcpServerTranslatorTests
{
    private static List<Dictionary<string, object?>> Translate(string json)
        => AcpMcpServerTranslator.Translate(json, NullLogger.Instance);

    /// <summary>按线缆上的样子读：session/new 的参数就是这样序列化出去的。</summary>
    private static JsonElement OnTheWire(Dictionary<string, object?> server)
        => JsonSerializer.SerializeToElement(server);

    [Fact]
    public void SseServerWithHeaders_SendsHeadersAsANameValueArray()
    {
        var server = OnTheWire(Translate("""
            {"mcpServers":{"tnzi":{"type":"sse","url":"https://host/mcp","headers":{"Authorization":"Bearer t0k"}}}}
            """).Single());

        server.GetProperty("type").GetString().ShouldBe("sse");
        var headers = server.GetProperty("headers");
        headers.ValueKind.ShouldBe(JsonValueKind.Array);
        var header = headers.EnumerateArray().ShouldHaveSingleItem();
        header.GetProperty("name").GetString().ShouldBe("Authorization");
        header.GetProperty("value").GetString().ShouldBe("Bearer t0k");
    }

    [Fact]
    public void HttpServerWithoutHeaders_SendsAnEmptyHeadersArray()
    {
        var server = OnTheWire(Translate("""
            {"mcpServers":{"docs":{"type":"http","url":"https://host/mcp"}}}
            """).Single());

        server.GetProperty("headers").ValueKind.ShouldBe(JsonValueKind.Array);
        server.GetProperty("headers").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void HttpServerWithAClaudeStyleTimeout_DoesNotCarryItIntoTheAcpShape()
    {
        // 回写条目的 timeout 是 Claude 风格配置的字段，ACP 的 McpServer 里没有它：
        // 带着它，按 schema 校验参数的运行时会拒掉整个 session/new
        var server = OnTheWire(Translate("""
            {"mcpServers":{"tnzi":{"type":"http","url":"https://host/mcp","headers":{"Authorization":"Bearer t0k"},"timeout":900000}}}
            """).Single());

        server.TryGetProperty("timeout", out _).ShouldBeFalse();
        server.GetProperty("url").GetString().ShouldBe("https://host/mcp");
    }

    [Fact]
    public void StdioServerWithoutArgsOrEnv_SendsEmptyArrays()
    {
        var server = OnTheWire(Translate("""
            {"mcpServers":{"fs":{"command":"mcp-fs"}}}
            """).Single());

        server.GetProperty("command").GetString().ShouldBe("mcp-fs");
        server.GetProperty("args").ValueKind.ShouldBe(JsonValueKind.Array);
        server.GetProperty("args").GetArrayLength().ShouldBe(0);
        server.GetProperty("env").ValueKind.ShouldBe(JsonValueKind.Array);
        server.GetProperty("env").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public void StdioServerWithArgsAndEnv_SendsArgsAndANameValueEnvArray()
    {
        var server = OnTheWire(Translate("""
            {"mcpServers":{"fs":{"command":"mcp-fs","args":["--root","/data"],"env":{"LOG":"debug"}}}}
            """).Single());

        server.GetProperty("args").EnumerateArray().Select(a => a.GetString()).ShouldBe(["--root", "/data"]);
        var env = server.GetProperty("env").EnumerateArray().ShouldHaveSingleItem();
        env.GetProperty("name").GetString().ShouldBe("LOG");
        env.GetProperty("value").GetString().ShouldBe("debug");
    }

    [Fact]
    public void NonStringHeaderValue_IsDropped_TheRestIsKept()
    {
        var server = OnTheWire(Translate("""
            {"mcpServers":{"x":{"type":"http","url":"https://host/mcp","headers":{"X-Retries":3,"X-Key":"k"}}}}
            """).Single());

        var header = server.GetProperty("headers").EnumerateArray().ShouldHaveSingleItem();
        header.GetProperty("name").GetString().ShouldBe("X-Key");
    }

    [Fact]
    public void WriteBackEntry_AsComposed_KeepsItsAuthorizationHeaderOnTheWire()
    {
        // 端到端到翻译这一步：回写通道由组装器写成 http + Authorization，ACP 一侧必须原样带上传输类型与那个请求头
        var composed = new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance).Compose(
            bindingMcpConfigJson: null,
            writeBackToken: "run-token",
            new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://host/mcp" },
            LocalHosts.Create());

        var server = OnTheWire(Translate(composed!).Single());

        server.GetProperty("type").GetString().ShouldBe("http");
        var header = server.GetProperty("headers").EnumerateArray().ShouldHaveSingleItem();
        header.GetProperty("name").GetString().ShouldBe("Authorization");
        header.GetProperty("value").GetString().ShouldBe("Bearer run-token");
    }
}
