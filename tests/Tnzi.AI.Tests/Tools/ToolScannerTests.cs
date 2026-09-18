using Tnzi.AI.Permissions;
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

    [Fact]
    public void ScanAssembly_GroupLevelRequiredPermissions_ApplyToEveryToolAndMergeWithMethodLevel()
    {
        // 组级声明覆盖整组：敏感工具组（task / sandbox / a2a）一条声明门住全部方法，
        // 逐方法声明会在新增方法时漏掉一条而毫无症状
        var tools = _scanner.ScanAssembly(typeof(TestGatedToolGroup).Assembly)
            .Where(t => t.GroupName == "test-gated").ToList();

        tools.Count.ShouldBe(2);
        tools.First(t => t.Name == "gated_plain").RequiredPermissions
            .ShouldBe(["ai.tools.test"]);
        tools.First(t => t.Name == "gated_extra").RequiredPermissions
            .ShouldBe(["ai.tools.test", "ai.tools.extra"], ignoreOrder: true);
    }

    [Fact]
    public void SensitiveBuiltInToolGroups_DeclareRequiredPermissions()
    {
        // 子 Agent 生命周期（task）与远端 agent 调用（a2a）能读/取消别人的运行、能向外打请求，
        // 必须由权限码门住，否则 RequiredPermissions 门控对框架自带的每个工具都是空转
        var tools = _scanner.ScanAssembly(typeof(A2ATools).Assembly).ToList();

        tools.Where(t => t.GroupName == "a2a").ShouldNotBeEmpty();
        tools.Where(t => t.GroupName == "a2a").ShouldAllBe(t => t.RequiredPermissions.Contains(AIToolPermissions.A2A));

        // task 组必须真的经扫描器进注册表（此前四个提供者类只带特性不带 IAIToolProvider，
        // 扫描器按「接口 AND 特性」过滤，整组从未登记；旧用例改读特性自我安慰，恰好把这条漏洞盖住）
        tools.Where(t => t.GroupName == "task").ShouldNotBeEmpty();
        tools.Where(t => t.GroupName == "task").ShouldAllBe(t => t.RequiredPermissions.Contains(AIToolPermissions.Task));
    }

    /// <summary>
    /// 框架自带的四个「模型侧协议」工具组（task / todo / clarification / artifact）必须经扫描器可达：
    /// TodoMiddleware 等 write_todos、ClarificationMiddleware 等 ask_clarification、
    /// SubAgentRegistry 排除 task、ai.tools.task 权限码门 task —— 它们都以「这些工具在注册表里」为前提。
    /// </summary>
    [Fact]
    public void ScanAssembly_FrameworkAssembly_RegistersProtocolToolGroups()
    {
        var tools = _scanner.ScanAssembly(typeof(AIModule).Assembly).ToList();

        new[] { "task", "todo", "clarification", "artifact" }
            .ShouldBeSubsetOf(tools.Select(t => t.GroupName).Distinct());

        new[]
            {
                "spawn_agent", "list_agent_runs", "get_agent_run", "send_agent_input", "wait_agent", "kill_agent",
                "list_sub_agent_types", "write_todos", "ask_clarification", "present_files"
            }
            .ShouldBeSubsetOf(tools.Select(t => t.Name));

        // 无权限码的三组只写属性包 / IAgentArtifactService，不需要门；task 整组受 ai.tools.task 门控
        tools.Where(t => t.GroupName is "todo" or "clarification" or "artifact")
            .ShouldAllBe(t => t.RequiredPermissions.Count == 0);
    }

    #endregion

    #region 测试用工具组

    [AIToolGroup("test-gated", RequiredPermissions = "ai.tools.test")]
    private class TestGatedToolGroup : IAIToolProvider
    {
        [AIFunction("gated_plain", "Inherits the group permission")]
        public Task<string> GatedPlainAsync() => Task.FromResult("ok");

        [AIFunction("gated_extra", "Group permission plus its own", RequiredPermissions = "ai.tools.extra")]
        public Task<string> GatedExtraAsync() => Task.FromResult("ok");
    }

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
