namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// Agent 工具限制分层测试
/// </summary>
public class AgentToolRestrictionsTests
{
    [Fact]
    public void IsToolAllowed_GlobalDisallowed_ReturnsFalse()
    {
        var restrictions = new AgentToolRestrictions
        {
            GlobalDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "dangerous_tool" }
        };

        restrictions.IsToolAllowed("dangerous_tool").ShouldBeFalse();
        restrictions.IsToolAllowed("dangerous_tool", isSubAgent: true).ShouldBeFalse();
    }

    [Fact]
    public void IsToolAllowed_SubAgentDisallowed_BlocksSubAgentOnly()
    {
        var restrictions = new AgentToolRestrictions
        {
            SubAgentDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "bash" }
        };

        // 主 Agent 可以用
        restrictions.IsToolAllowed("bash", isSubAgent: false).ShouldBeTrue();
        // 子 Agent 不可以
        restrictions.IsToolAllowed("bash", isSubAgent: true).ShouldBeFalse();
    }

    [Fact]
    public void IsToolAllowed_AsyncAllowedList_WhitelistMode()
    {
        var restrictions = new AgentToolRestrictions
        {
            AsyncAgentAllowedTools = new(StringComparer.OrdinalIgnoreCase) { "read_file", "grep" }
        };

        // 非异步 Agent 不受限制
        restrictions.IsToolAllowed("bash", isAsync: false).ShouldBeTrue();

        // 异步 Agent 仅允许白名单内的工具
        restrictions.IsToolAllowed("read_file", isAsync: true).ShouldBeTrue();
        restrictions.IsToolAllowed("grep", isAsync: true).ShouldBeTrue();
        restrictions.IsToolAllowed("bash", isAsync: true).ShouldBeFalse();
    }

    [Fact]
    public void IsToolAllowed_AsyncEmptyAllowedList_NoRestriction()
    {
        var restrictions = new AgentToolRestrictions();

        // 空白名单 → 不限制
        restrictions.IsToolAllowed("anything", isAsync: true).ShouldBeTrue();
    }

    [Fact]
    public void IsToolAllowed_McpTools_AlwaysAllowed()
    {
        var restrictions = new AgentToolRestrictions
        {
            GlobalDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "mcp_server_tool" }
        };

        // MCP 工具始终放行（即使在全局禁止列表中）
        restrictions.IsToolAllowed("mcp_server_tool").ShouldBeTrue();
        restrictions.IsToolAllowed("mcp__slack_send_message").ShouldBeTrue();
    }

    [Fact]
    public void IsToolAllowed_DenyTakesPrecedence()
    {
        var restrictions = new AgentToolRestrictions
        {
            GlobalDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "special_tool" },
            AsyncAgentAllowedTools = new(StringComparer.OrdinalIgnoreCase) { "special_tool" }
        };

        // 即使在 async 白名单中，全局禁止仍生效
        restrictions.IsToolAllowed("special_tool", isAsync: true).ShouldBeFalse();
    }

    [Fact]
    public void FilterTools_RemovesDisallowedTools()
    {
        var restrictions = new AgentToolRestrictions
        {
            SubAgentDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "bash", "write_file" }
        };

        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(() => "ok", "read_file"),
            AIFunctionFactory.Create(() => "ok", "bash"),
            AIFunctionFactory.Create(() => "ok", "write_file"),
            AIFunctionFactory.Create(() => "ok", "grep")
        };

        var filtered = restrictions.FilterTools(tools, isSubAgent: true);

        filtered.Count.ShouldBe(2);
        filtered.Select(t => t.Name).ShouldContain("read_file");
        filtered.Select(t => t.Name).ShouldContain("grep");
    }

    [Fact]
    public void IsToolAllowed_CaseInsensitive()
    {
        var restrictions = new AgentToolRestrictions
        {
            GlobalDisallowedTools = new(StringComparer.OrdinalIgnoreCase) { "Bash" }
        };

        restrictions.IsToolAllowed("bash").ShouldBeFalse();
        restrictions.IsToolAllowed("BASH").ShouldBeFalse();
    }
}
