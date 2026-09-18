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

    [Fact]
    public void Evaluate_WithoutRegistry_UsesBodyFlagOnly()
    {
        var controller = Build(new ToolPermissionEvaluator([]), registry: null);

        controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "delete_memory" }).Data!.Behavior.ShouldBe(PermissionBehavior.Allow);
        controller.Evaluate(new PermissionEvaluateRequestDto { ToolName = "delete_memory", IsDestructive = true }).Data!.Behavior.ShouldBe(PermissionBehavior.Deny);
    }
}
