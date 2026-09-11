namespace Tnzi.AI.Tests.Security;

/// <summary>
/// User 级权限规则的用户维度测试。
/// </summary>
/// <remarks>
/// <c>ToolPermissionRuleEntity.UserId</c> 有列、有 DTO、有写入、管理端也正确显示，但运行时评估
/// 曾经根本不看它：规则「Allow bash, Scope=User, UserId=某运维」对<b>每个</b>用户生效（fail-open），
/// 反向的 Deny 则拒绝所有人。「存了、显示了、评估时不看」是最糟的中间态 —— 管理端展示的规则集
/// 与运行时实际执行的规则集是两份东西。
/// </remarks>
public class ToolPermissionUserScopeTests
{
    private static readonly Guid Operator = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public void Evaluate_UserBoundAllow_AppliesOnlyToThatUser()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.System,
                Priority = 10,
                Reason = "Shell denied by default"
            },
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Allow,
                Scope = ToolPermissionScope.User,
                UserId = Operator,
                Priority = 100
            }
        ]);

        evaluator.Evaluate(ContextFor("bash", Operator)).Behavior.ShouldBe(PermissionBehavior.Allow);
        evaluator.Evaluate(ContextFor("bash", OtherUser)).Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void Evaluate_UserBoundAllow_DoesNotApplyWhenTheCallerIsUnknown()
    {
        // 取不到调用者时不能假定「就是那个人」：user-bound 的 Allow 必须落空，
        // 否则一条给运维开的口子会对所有没带用户身份的调用生效
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.System,
                Priority = 10
            },
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Allow,
                Scope = ToolPermissionScope.User,
                UserId = Operator,
                Priority = 100
            }
        ]);

        evaluator.Evaluate(ContextFor("bash", userId: null)).Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public void Evaluate_UserBoundDeny_DoesNotBlockEveryoneElse()
    {
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "file_write",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.User,
                UserId = OtherUser,
                Priority = 100,
                Reason = "Contractor cannot write files"
            }
        ]);

        evaluator.Evaluate(ContextFor("file_write", OtherUser)).Behavior.ShouldBe(PermissionBehavior.Deny);
        evaluator.Evaluate(ContextFor("file_write", Operator)).Behavior.ShouldBe(PermissionBehavior.Allow);
    }

    [Fact]
    public void Evaluate_RuleWithoutUserId_StaysUnbound()
    {
        // UserId 为空 = 规则不绑任何人（Scope 只影响决胜权重），与引入用户维度之前逐字相同
        var evaluator = new ToolPermissionEvaluator(
        [
            new ToolPermissionRule
            {
                ToolPattern = "bash",
                Behavior = PermissionBehavior.Deny,
                Scope = ToolPermissionScope.User,
                Priority = 100
            }
        ]);

        evaluator.Evaluate(ContextFor("bash", Operator)).Behavior.ShouldBe(PermissionBehavior.Deny);
        evaluator.Evaluate(ContextFor("bash", OtherUser)).Behavior.ShouldBe(PermissionBehavior.Deny);
        evaluator.Evaluate(ContextFor("bash", userId: null)).Behavior.ShouldBe(PermissionBehavior.Deny);
    }

    [Fact]
    public async Task Store_MapsUserId_SoUserScopedRowsStayBoundAtRuntime()
    {
        var entities = new List<ToolPermissionRuleEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                ToolPattern = "bash",
                Behavior = (int)PermissionBehavior.Allow,
                Scope = (int)ToolPermissionScope.User,
                UserId = Operator,
                Priority = 100,
                IsEnabled = true
            }
        };

        var repository = new Mock<IRepository<ToolPermissionRuleEntity, Guid>>();
        repository.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(entities.BuildMock());
        var store = new DatabaseToolPermissionRuleStore(repository.Object);

        var rules = await store.GetRulesAsync();

        rules.Single().UserId.ShouldBe(Operator);
    }

    [Fact]
    public async Task ApprovalToolWrapper_CarriesTheRunningUserIntoTheEvaluationContext()
    {
        // 运行时的用户身份来源与审批请求一致（AgentRunRequest.UserId），否则持久化的 User 级规则
        // 在真实调用路径上永远匹配不到人
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserId = Operator }
        };
        var evaluator = new CapturingEvaluator();

        var wrapped = ApprovalToolWrapper.Wrap(
            [AIFunctionFactory.Create(() => "ok", "bash")],
            approvalHandler: null,
            options: new ToolApprovalOptions { Enabled = false, Mode = ToolApprovalMode.NeverRequire },
            permissionEvaluator: evaluator,
            shellCommandAnalyzer: null,
            executionContextAccessor: accessor);

        await ((AIFunction)wrapped[0]).InvokeAsync(new AIFunctionArguments());

        evaluator.LastContext.ShouldNotBeNull();
        evaluator.LastContext!.UserId.ShouldBe(Operator);
    }

    private static ToolPermissionContext ContextFor(string toolName, Guid? userId) => new()
    {
        ToolName = toolName,
        UserId = userId
    };

    private sealed class CapturingEvaluator : IToolPermissionEvaluator
    {
        public ToolPermissionContext? LastContext { get; private set; }

        public bool HasRules => true;

        public ToolPermissionDecision Evaluate(
            ToolPermissionContext context,
            IEnumerable<ToolPermissionRule>? additionalRules = null)
        {
            LastContext = context;
            return new ToolPermissionDecision(context.ToolName, PermissionBehavior.Allow);
        }
    }
}
