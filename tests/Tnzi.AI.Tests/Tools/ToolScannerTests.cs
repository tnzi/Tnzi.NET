using Tnzi.AI.Tools.Attributes;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Tools;

/// <summary>
/// ToolScanner 工具扫描测试 - 验证安全元数据提取
/// </summary>
public class ToolScannerTests
{
    private readonly ToolScanner _scanner;

    public ToolScannerTests()
    {
        _scanner = new ToolScanner(NullLogger<ToolScanner>.Instance);
    }

    #region 安全元数据提取

    [Fact]
    public void ScanAssembly_ToolWithSafetyAttributes_ExtractsAllSafetyMetadata()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var readTool = tools.First(t => t.Name == "safe_read");
        readTool.IsReadOnly.ShouldBeTrue();
        readTool.IsConcurrencySafe.ShouldBeTrue();
        readTool.IsDestructive.ShouldBeFalse();
        readTool.MaxResultSizeChars.ShouldBe(5000);
        readTool.SearchHint.ShouldBe("read file content");
    }

    [Fact]
    public void ScanAssembly_ToolWithoutSafetyAttributes_UsesFailClosedDefaults()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var defaultTool = tools.First(t => t.Name == "default_safety");
        // fail-closed: 未标记的工具默认不可并发、非只读、非破坏性
        defaultTool.IsReadOnly.ShouldBeFalse();
        defaultTool.IsConcurrencySafe.ShouldBeFalse();
        defaultTool.IsDestructive.ShouldBeFalse();
        defaultTool.MaxResultSizeChars.ShouldBe(0); // 0 = 不限制
        defaultTool.SearchHint.ShouldBeNull();
    }

    [Fact]
    public void ScanAssembly_DestructiveTool_ExtractsIsDestructive()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var deleteTool = tools.First(t => t.Name == "destructive_delete");
        deleteTool.IsDestructive.ShouldBeTrue();
        deleteTool.IsReadOnly.ShouldBeFalse();
        deleteTool.IsConcurrencySafe.ShouldBeFalse();
    }

    [Fact]
    public void ScanAssembly_ToolWithAliases_ExtractsAliases()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var aliasTool = tools.First(t => t.Name == "safe_read");
        aliasTool.Aliases.ShouldNotBeEmpty();
        aliasTool.Aliases.ShouldContain("read");
        aliasTool.Aliases.ShouldContain("cat");
    }

    [Fact]
    public void ScanAssembly_ToolWithInterruptBehavior_ExtractsInterruptBehavior()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var gracefulTool = tools.First(t => t.Name == "graceful_tool");
        gracefulTool.InterruptBehavior.ShouldBe(ToolInterruptBehavior.GracefulShutdown);

        // 未标记的默认 Cancel
        var defaultTool = tools.First(t => t.Name == "default_safety");
        defaultTool.InterruptBehavior.ShouldBe(ToolInterruptBehavior.Cancel);
    }

    #endregion

    #region 现有功能回归

    [Fact]
    public void ScanAssembly_ExistingAttributes_StillWorkCorrectly()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        var readTool = tools.First(t => t.Name == "safe_read");
        readTool.Description.ShouldBe("Read a file safely");
        readTool.GroupName.ShouldBe("test-safety");
        readTool.Category.ShouldBe("io");
    }

    [Fact]
    public void ScanAssembly_AsyncSuffix_IsRemovedCorrectly()
    {
        var tools = _scanner.ScanAssembly(typeof(TestSafetyToolGroup).Assembly).ToList();

        // "GracefulToolAsync" → "graceful_tool"
        tools.ShouldContain(t => t.Name == "graceful_tool");
        tools.ShouldNotContain(t => t.Name == "graceful_toolAsync");
    }

    #endregion

    #region 测试用工具组

    [AIToolGroup("test-safety", "Test Safety Tools", "Tools for testing safety metadata")]
    private class TestSafetyToolGroup : IAIToolProvider
    {
        [AIFunction("safe_read", "Read a file safely",
            IsReadOnly = true, IsConcurrencySafe = true, IsDestructive = false,
            MaxResultSizeChars = 5000, SearchHint = "read file content",
            Aliases = "read,cat", Category = "io")]
        public Task<string> SafeReadAsync() => Task.FromResult("content");

        [AIFunction("default_safety", "Tool with default safety settings")]
        public Task<string> DefaultSafetyAsync() => Task.FromResult("ok");

        [AIFunction("destructive_delete", "Delete something destructively",
            IsDestructive = true)]
        public Task<string> DestructiveDeleteAsync() => Task.FromResult("deleted");

        [AIFunction("graceful_tool", "Tool with graceful shutdown",
            InterruptBehavior = ToolInterruptBehavior.GracefulShutdown)]
        public Task<string> GracefulToolAsync() => Task.FromResult("done");
    }

    #endregion
}
