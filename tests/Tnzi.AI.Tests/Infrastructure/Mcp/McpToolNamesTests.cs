using System.Text.RegularExpressions;
using Tnzi.AI.Infrastructure.Mcp;

namespace Tnzi.AI.Tests.Infrastructure.Mcp;

public class McpToolNamesTests
{
    [Theory]
    [InlineData("github", "search", "github__search")]
    [InlineData("my server.v2", "read/file", "my_server_v2__read_file")]
    [InlineData("files-eu", "list_dir", "files-eu__list_dir")]
    public void Prefixed_UsesOnlyProviderSafeCharacters(string server, string tool, string expected)
    {
        McpToolNames.Prefixed(server, tool).ShouldBe(expected);
    }

    /// <summary>OpenAI / Anthropic reject tool names longer than 64 characters; two long names must not collapse into one.</summary>
    [Fact]
    public void Prefixed_LongNames_AreCappedAndStayDistinct()
    {
        var server = new string('s', 40);
        var first = McpToolNames.Prefixed(server, new string('t', 40) + "_alpha");
        var second = McpToolNames.Prefixed(server, new string('t', 40) + "_beta");

        first.Length.ShouldBe(McpToolNames.MaxLength);
        second.Length.ShouldBe(McpToolNames.MaxLength);
        first.ShouldNotBe(second);
        first.ShouldMatch("^[a-zA-Z0-9_-]+$");
        Regex.IsMatch(second, "^[a-zA-Z0-9_-]+$").ShouldBeTrue();
    }

    [Fact]
    public void OriginalName_NonMcpTool_IsTheToolName()
    {
        McpToolNames.OriginalName(AIFunctionFactory.Create(() => "x", "plain_tool")).ShouldBe("plain_tool");
    }
}
