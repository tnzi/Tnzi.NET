using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// AgentExecutor 工具执行安全守卫测试
/// </summary>
public class AgentExecutorSafetyTests
{
    #region CanExecuteConcurrently

    [Fact]
    public void CanExecuteConcurrently_AllConcurrencySafeAndNotDestructive_ReturnsTrue()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = new() { Name = "read_file", IsConcurrencySafe = true, IsDestructive = false },
            ["grep"] = new() { Name = "grep", IsConcurrencySafe = true, IsDestructive = false }
        };

        var result = AgentExecutor.CanExecuteConcurrently(
            ["read_file", "grep"], toolDefs);

        result.ShouldBeTrue();
    }

    [Fact]
    public void CanExecuteConcurrently_HasNonConcurrencySafe_ReturnsFalse()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = new() { Name = "read_file", IsConcurrencySafe = true },
            ["write_file"] = new() { Name = "write_file", IsConcurrencySafe = false }
        };

        var result = AgentExecutor.CanExecuteConcurrently(
            ["read_file", "write_file"], toolDefs);

        result.ShouldBeFalse();
    }

    [Fact]
    public void CanExecuteConcurrently_HasDestructive_ReturnsFalse()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = new() { Name = "read_file", IsConcurrencySafe = true },
            ["delete_file"] = new() { Name = "delete_file", IsConcurrencySafe = true, IsDestructive = true }
        };

        var result = AgentExecutor.CanExecuteConcurrently(
            ["read_file", "delete_file"], toolDefs);

        result.ShouldBeFalse();
    }

    [Fact]
    public void CanExecuteConcurrently_UnknownTool_ReturnsFalse_FailClosed()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["read_file"] = new() { Name = "read_file", IsConcurrencySafe = true }
        };

        // "unknown_tool" 不在定义中 → fail-closed → 返回 false
        var result = AgentExecutor.CanExecuteConcurrently(
            ["read_file", "unknown_tool"], toolDefs);

        result.ShouldBeFalse();
    }

    [Fact]
    public void CanExecuteConcurrently_SingleTool_AlwaysTrue()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["write_file"] = new() { Name = "write_file", IsConcurrencySafe = false }
        };

        // 单个工具不需要并发判断
        var result = AgentExecutor.CanExecuteConcurrently(
            ["write_file"], toolDefs);

        result.ShouldBeTrue();
    }

    [Fact]
    public void CanExecuteConcurrently_EmptyToolDefs_ReturnsFalse()
    {
        var toolDefs = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

        var result = AgentExecutor.CanExecuteConcurrently(
            ["read_file", "grep"], toolDefs);

        result.ShouldBeFalse();
    }

    #endregion

    #region NormalizeToolResult (空结果标记)

    [Fact]
    public void NormalizeToolResult_NullResult_ReturnsMarker()
    {
        var result = AgentExecutor.NormalizeToolResult("test_tool", null);
        result.ShouldBe("(test_tool completed with no output)");
    }

    [Fact]
    public void NormalizeToolResult_EmptyString_ReturnsMarker()
    {
        var result = AgentExecutor.NormalizeToolResult("test_tool", "");
        result.ShouldBe("(test_tool completed with no output)");
    }

    [Fact]
    public void NormalizeToolResult_WhitespaceOnly_ReturnsMarker()
    {
        var result = AgentExecutor.NormalizeToolResult("test_tool", " ");
        result.ShouldBe("(test_tool completed with no output)");
    }

    [Fact]
    public void NormalizeToolResult_ValidContent_ReturnsSameContent()
    {
        var result = AgentExecutor.NormalizeToolResult("test_tool", "actual data");
        result.ShouldBe("actual data");
    }

    #endregion
}
