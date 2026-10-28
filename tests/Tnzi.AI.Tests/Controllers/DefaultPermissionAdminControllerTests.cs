using Tnzi.AI.Controllers.Admin;
using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Controllers;

/// <summary>
/// 管理端 evaluate 试算必须与运行时 <c>ApprovalToolWrapper</c> 给出同一个答案：
/// 工具经 <c>[AIFunction(IsDestructive = true)]</c> 声明的破坏性由注册表补入，不能只看请求体。
/// </summary>
public class DefaultPermissionAdminControllerTests
{
    private static DefaultPermissionAdminController Build(IToolPermissionEvaluator evaluator, IToolRegistry? registry)
        => new(evaluator, Mock.Of<IToolPermissionRuleService>(), registry);

    [Fact]
    public void Evaluate_DefaultsIsDestructiveFromRegistry_WhenBodyOmitsIt()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.Register(new ToolDefinition { Name = "delete_memory", Description = "d", IsDestructive = true, ProviderType = typeof(object), GroupName = "memory" });

        var controller = Build(new ToolPermissionEvaluator([]), registry);

        var result = controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "delete_memory" });

        result.Data!.Behavior.ShouldBe(PermissionBehavior.Deny);
        result.Data.Reason.ShouldBe("Destructive tool requires explicit allow");
    }

    [Fact]
    public void Evaluate_NonDestructiveRegisteredTool_NoRules_Allows()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.Register(new ToolDefinition { Name = "search_memory", Description = "s", IsDestructive = false, ProviderType = typeof(object), GroupName = "memory" });

        var controller = Build(new ToolPermissionEvaluator([]), registry);

        var result = controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "search_memory" });

        result.Data!.Behavior.ShouldBe(PermissionBehavior.Allow);
    }

    /// <summary>
    /// 带 CommandPrefix 的放行规则要求每个 shell 片段都命中：试算必须用运行时同一个分析器拆片段，
    /// 否则这类规则在试算里永远不命中、运行时却命中；拆出的破坏性判断也要并进来。
    /// </summary>
    [Theory]
    [InlineData("git status", PermissionBehavior.Allow)]
    [InlineData("git status; rm -rf data", PermissionBehavior.Deny)]
    [InlineData("echo hi\nrm -rf data", PermissionBehavior.Deny)]
    public void Evaluate_ShellCommand_IsAnalyzedLikeTheRuntime(string command, PermissionBehavior expected)
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule { ToolPattern = "bash", CommandPrefix = "git", Behavior = PermissionBehavior.Allow }
        ]);
        var controller = new DefaultPermissionAdminController(
            evaluator, Mock.Of<IToolPermissionRuleService>(), toolRegistry: null, new ShellCommandAnalyzer());

        var result = controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "bash", ShellCommand = command });

        result.Data!.Behavior.ShouldBe(expected, command);
    }

    [Fact]
    public void Evaluate_ScopedAllowForADestructiveCommand_MatchesLikeTheRuntime()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule { ToolPattern = "bash", CommandPrefix = "rm", Behavior = PermissionBehavior.Allow }
        ]);
        var controller = new DefaultPermissionAdminController(
            evaluator, Mock.Of<IToolPermissionRuleService>(), toolRegistry: null, new ShellCommandAnalyzer());

        var result = controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "bash", ShellCommand = "rm -rf build" });

        result.Data!.Behavior.ShouldBe(PermissionBehavior.Allow);
        result.Data.MatchedRulePattern.ShouldBe("bash");
    }

    [Fact]
    public void Evaluate_WithoutRegistry_UsesBodyFlagOnly()
    {
        var controller = Build(new ToolPermissionEvaluator([]), registry: null);

        controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "delete_memory" }).Data!.Behavior.ShouldBe(PermissionBehavior.Allow);
        controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "delete_memory", IsDestructive = true }).Data!.Behavior.ShouldBe(PermissionBehavior.Deny);
    }
}
