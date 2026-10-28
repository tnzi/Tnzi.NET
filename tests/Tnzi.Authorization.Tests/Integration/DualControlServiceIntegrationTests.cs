using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Options;
using Tnzi.Mapster;
using DualControlOptions = Tnzi.Authorization.Options.DualControlOptions;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 双人授权（四眼原则）：真实 SQLite 仓储 + 真实 <see cref="DualControlService"/>，
/// 当前用户可在测试中切换（发起人 / 批准人 / 第三方）。
/// </summary>
/// <remarks>
/// 这些用例钉的是四条不变量：决定的人不能是发起人、批准的是一份参数而不是一个操作名、
/// 一张许可只换一次执行、许可只能由发起人取用。每一条都能被「看起来很合理」的实现绕过去。
/// 另钉一条：发起去重按「动作 + 目标 + 发起人 + 参数」，别人的待批请求不会被递给你。
/// </remarks>
public class DualControlServiceIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid Requester = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Approver = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdParty = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private Guid _actingUser = Requester;

    public DualControlServiceIntegrationTests()
    {
        // 服务层用 MapTo 出 DTO，静态映射器在测试宿主里没人初始化过。
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    /// <summary>批准人持有的权限码；测试可清空以模拟「没被授权批准」。</summary>
    private readonly HashSet<string> _grantedPermissions = ["payrun.void.approve", "payrun.post.approve", "account.delete.approve"];

    /// <summary>是否注册权限检查器；false 用来验证「查不通就拒绝」。</summary>
    private bool _permissionCheckerAvailable = true;

    /// <summary>批准权限码后缀；空串 = 由调用方自己把关。</summary>
    private string _approvalPermissionSuffix = ".approve";

    /// <summary>是否注入用户仓储；false 模拟「未加载 Identity 模块」。</summary>
    private bool _identityLoaded = true;

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<DualControlRequest>(services);

        // 覆盖基类那个固定用户：四眼原则的全部用例都要在两个身份之间切换。
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(() => _actingUser);
        services.AddScoped(_ => currentUser.Object);

        // 批准要过 {Operation}.approve 这道码。用真实的服务契约而不是绕开它 ——
        // 绕开等于把被测的那半换掉。
        var permissionChecker = new Mock<IPermissionChecker>();
        permissionChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string>()))
            .ReturnsAsync((string code) => _grantedPermissions.Contains(code));
        services.AddScoped(_ => _permissionCheckerAvailable ? permissionChecker.Object : null!);

        // 待办面把发起人/审批人 Guid 换成用户名，走的是可选注入的用户仓储。
        // 注册它才测得到解析这一半；不注册的那条路径由 Resolves_nothing_when_identity_is_absent 钉。
        AddRepo<Tnzi.Identity.Entities.User>(services);

        services.AddScoped(sp => new DualControlService(
            sp,
            sp.GetRequiredService<IRepository<DualControlRequest, Guid>>(),
            sp.GetRequiredService<IOptionsMonitor<DualControlOptions>>(),
            _identityLoaded ? sp.GetRequiredService<IRepository<Tnzi.Identity.Entities.User, Guid>>() : null));

        services.AddSingleton<IOptionsMonitor<DualControlOptions>>(
            new LiveOptionsMonitor<DualControlOptions>(() => new DualControlOptions
            {
                ApprovalPermissionSuffix = _approvalPermissionSuffix
            }));
    }

    private DualControlService Service => ServiceProvider.GetRequiredService<DualControlService>();

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private void ActAs(Guid userId) => _actingUser = userId;

    private async Task<Guid> RequestAsync(string operation = "payrun.void", string? payload = "{\"amount\":100}")
    {
        ActAs(Requester);
        var request = await Service.RequestAsync(new DualControlRequestInput(operation, "PR-1", payload));
        return request.Data!.Id;
    }

    [Fact]
    public async Task ARequesterCannotApproveTheirOwnRequest()
    {
        // ★★★ 这一句就是四眼原则的全部内容。
        var id = await RequestAsync();

        var approved = await Service.ApproveAsync(id);

        Assert.False(approved.Succeeded);
        Assert.Equal(403, approved.Code);
    }

    [Fact]
    public async Task ARequesterCannotRejectTheirOwnRequestEither()
    {
        // 允许自己拒自己看似无害，但它让「决定人 ≠ 发起人」出现例外，
        // 而例外一旦存在，下一个人就会问「那批准是不是也能有例外」。
        var id = await RequestAsync();

        var rejected = await Service.RejectAsync(id, "never mind");

        Assert.False(rejected.Succeeded);
    }

    [Fact]
    public async Task ASecondPersonCanApproveAndTheRequesterThenConsumes()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        var approved = await Service.ApproveAsync(id, "checked");
        Assert.True(approved.Succeeded);
        Assert.Equal(DualControlStatus.Approved, approved.Data!.Status);
        Assert.Equal(Approver, approved.Data.ApproverId);

        ActAs(Requester);
        var permit = await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}");
        Assert.True(permit.Succeeded);
    }

    [Fact]
    public async Task ApprovingWithoutThePerOperationPermissionIsRejected()
    {
        // ★ 类级「能不能用这个功能」之外，还要按动作分权。少了这一层，
        //   一个被授权批准报销的人就顺带能批准删账户。
        var id = await RequestAsync("payrun.void");
        _grantedPermissions.Clear();

        ActAs(Approver);
        var approved = await Service.ApproveAsync(id);

        Assert.False(approved.Succeeded);
        Assert.Equal(403, approved.Code);
    }

    [Fact]
    public async Task RejectingAlsoRequiresThePermission()
    {
        // 拒绝与批准同权：给拒绝开一个更松的口子，等于让没有审批权的人能阻断别人的动作。
        var id = await RequestAsync("payrun.void");
        _grantedPermissions.Clear();

        ActAs(Approver);
        Assert.False((await Service.RejectAsync(id)).Succeeded);
    }

    [Fact]
    public async Task TheCheckCanBeTurnedOffForCallersThatGateItThemselves()
    {
        _approvalPermissionSuffix = string.Empty;
        var id = await RequestAsync("payrun.void");
        _grantedPermissions.Clear();

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);
    }

    [Fact]
    public async Task AnUnavailablePermissionCheckerRejectsRatherThanWavesThrough()
    {
        // ★ fail-closed，与 ILoginGuard 同一判据：一条查不通的准入策略应当拒绝。
        //   放行的话，漏装权限运行时的部署会得到一个「双人授权已启用」的假象。
        _permissionCheckerAvailable = false;
        var id = await RequestAsync("payrun.void");

        ActAs(Approver);
        Assert.False((await Service.ApproveAsync(id)).Succeeded);
    }

    [Fact]
    public async Task ConsumingWithADifferentPayloadIsRejected()
    {
        // ★★★ 批准的是一份参数，不是一个操作名。少了这一比，
        //     批下来的「转 100 元」可以改成「转 100 万」再执行，
        //     而审计上看到的是一次完全合规的双人授权。
        var id = await RequestAsync(payload: "{\"amount\":100}");

        ActAs(Approver);
        await Service.ApproveAsync(id);

        ActAs(Requester);
        var permit = await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":1000000}");

        Assert.False(permit.Succeeded);
        Assert.Equal(409, permit.Code);
    }

    [Fact]
    public async Task ConsumingUnderADifferentOperationIsRejected()
    {
        // 一张为「作废工资单」批的许可，不该能用来「删除账户」。
        var id = await RequestAsync("payrun.void");

        ActAs(Approver);
        await Service.ApproveAsync(id);

        ActAs(Requester);
        var permit = await Service.ConsumeAsync(id, "account.delete", "{\"amount\":100}");

        Assert.False(permit.Succeeded);
    }

    [Fact]
    public async Task APermitIsGoodForExactlyOneExecution()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        await Service.ApproveAsync(id);

        ActAs(Requester);
        Assert.True((await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}")).Succeeded);

        // 第二次必须失败，否则「批一次、跑十遍」。
        Assert.False((await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}")).Succeeded);
    }

    [Fact]
    public async Task APendingRequestCannotBeConsumed()
    {
        var id = await RequestAsync();

        ActAs(Requester);
        var permit = await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}");

        Assert.False(permit.Succeeded);
    }

    [Fact]
    public async Task ARejectedRequestCannotBeConsumed()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        await Service.RejectAsync(id, "not this one");

        ActAs(Requester);
        Assert.False((await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}")).Succeeded);
    }

    [Fact]
    public async Task AnExpiredApprovalCannotBeConsumed()
    {
        ActAs(Requester);
        var request = await Service.RequestAsync(
            new DualControlRequestInput("payrun.void", "PR-1", "{}", Lifetime: TimeSpan.FromMilliseconds(1)));
        var id = request.Data!.Id;

        ActAs(Approver);
        // 过期后连批准都不该受理 —— 一张已经作古的请求不该在批准那一刻复活。
        var approved = await Service.ApproveAsync(id);

        Assert.False(approved.Succeeded);
    }

    [Fact]
    public async Task RequestingTheSameActionTwiceReusesThePendingOne()
    {
        // 连点两下不该造出两张许可 —— 两张许可等于这个动作被批准了两次。
        var first = await RequestAsync();
        var second = await RequestAsync();

        Assert.Equal(first, second);
    }

    /// <summary>
    /// ★ 审批人批的是「<b>这个人</b>做这件事」，所以许可只能由发起人取用。
    /// 少了这一比，任何知道 requestId 的调用方都能把别人批下来的许可换成自己的执行。
    /// </summary>
    [Fact]
    public async Task OnlyTheRequesterCanConsumeThePermit()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);

        // 审批人自己、以及一个碰巧知道 id 的第三方，都不能拿这张许可去执行。
        var byApprover = await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}");
        Assert.False(byApprover.Succeeded);
        Assert.Equal(403, byApprover.Code);

        ActAs(ThirdParty);
        var byStranger = await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}");
        Assert.False(byStranger.Succeeded);
        Assert.Equal(403, byStranger.Code);

        // 被拒的尝试不能把许可烧掉：发起人回来照常能用。
        ActAs(Requester);
        Assert.True((await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}")).Succeeded);
    }

    /// <summary>
    /// ★ 去重键必须含发起人：否则 B 发起同一动作会拿回 A 那张待批请求 ——
    /// 连同 A 的参数原文 —— 而 B 既批不了它也用不了它。
    /// </summary>
    [Fact]
    public async Task AnotherPersonsPendingRequestIsNeverHandedBackAsYours()
    {
        var alices = await RequestAsync(payload: "{\"amount\":100}");

        ActAs(ThirdParty);
        var bobs = await Service.RequestAsync(new DualControlRequestInput("payrun.void", "PR-1", "{\"amount\":100}"));

        Assert.True(bobs.Succeeded);
        Assert.NotEqual(alices, bobs.Data!.Id);
        Assert.Equal(ThirdParty, bobs.Data.RequesterId);
    }

    /// <summary>
    /// ★ 去重键必须含参数：同一个人对同一目标改了参数再发起，那是一次新的请求，
    /// 不能静默地把旧参数那张递回来 —— 「保存成功」与「你的参数被换掉了」在响应上分不出来。
    /// </summary>
    [Fact]
    public async Task ADifferentPayloadIsANewRequestNotTheOldOneInDisguise()
    {
        var small = await RequestAsync(payload: "{\"amount\":100}");
        var large = await RequestAsync(payload: "{\"amount\":1000000}");

        Assert.NotEqual(small, large);

        var stored = await Service.GetAsync(large);
        Assert.Equal("{\"amount\":1000000}", stored.Data!.PayloadJson);
    }

    [Fact]
    public async Task OnlyTheRequesterCanCancel()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        Assert.False((await Service.CancelAsync(id)).Succeeded);

        ActAs(Requester);
        Assert.True((await Service.CancelAsync(id)).Succeeded);
    }

    [Fact]
    public async Task ADecidedRequestCannotBeDecidedAgain()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);
        Assert.False((await Service.ApproveAsync(id)).Succeeded);
        Assert.False((await Service.RejectAsync(id)).Succeeded);
    }

    [Fact]
    public async Task UsableOnlyFiltersOutWhatCannotBeUsed()
    {
        var approvedId = await RequestAsync("payrun.void");
        ActAs(Approver);
        await Service.ApproveAsync(approvedId);

        ActAs(Requester);
        await Service.RequestAsync(new DualControlRequestInput("payrun.post", "PR-2", "{}"));

        var usable = await Service.QueryAsync(new DualControlQueryDto { UsableOnly = true, PageSize = 50 });

        Assert.True(usable.Succeeded);
        Assert.Single(usable.Data!.Items);
        Assert.Equal(approvedId, usable.Data.Items.First().Id);
    }

    /// <summary>
    /// 待办面必须能回答「<b>谁</b>要做这件事」。
    /// </summary>
    /// <remarks>
    /// 四眼原则的第二只眼睛要判断的是这个人该不该做这件事，而一列 Guid 回答不了它。
    /// 姓名解析走可选注入的用户仓储，两个方向都要钉：解析得出来（本条），
    /// 以及未加载 Identity 时安静留空而不是整页 500（<see cref="Names_are_left_empty_when_identity_is_absent"/>）。
    /// </remarks>
    [Fact]
    public async Task Query_and_get_resolve_requester_and_approver_names()
    {
        await SeedUserAsync(Requester, "alice");
        await SeedUserAsync(Approver, "bob");

        var id = await RequestAsync();
        ActAs(Approver);
        await Service.ApproveAsync(id, "checked");

        var listed = await Service.QueryAsync(new DualControlQueryDto { PageSize = 50 });
        Assert.True(listed.Succeeded);
        var row = listed.Data!.Items.Single(i => i.Id == id);
        Assert.Equal("alice", row.RequesterName);
        Assert.Equal("bob", row.ApproverName);

        // 单条读取与列表必须同源：详情抽屉是审批人真正做决定的地方。
        var single = await Service.GetAsync(id);
        Assert.True(single.Succeeded);
        Assert.Equal("alice", single.Data!.RequesterName);
        Assert.Equal("bob", single.Data.ApproverName);
    }

    /// <summary>未决请求还没有审批人，姓名列必须是空的而不是编一个出来。</summary>
    [Fact]
    public async Task A_pending_request_has_a_requester_name_but_no_approver_name()
    {
        await SeedUserAsync(Requester, "alice");

        var id = await RequestAsync();

        var single = await Service.GetAsync(id);

        Assert.Equal("alice", single.Data!.RequesterName);
        Assert.Null(single.Data.ApproverId);
        Assert.Null(single.Data.ApproverName);
    }

    /// <summary>
    /// 未加载 Identity 模块时，待办面照常可用：姓名留空，其余字段一字不差。
    /// </summary>
    [Fact]
    public async Task Names_are_left_empty_when_identity_is_absent()
    {
        // 服务是用工厂 lambda 注册的（求值发生在**解析**那一刻，不是注册那一刻），
        // 所以在首次触碰 Service 之前翻这个开关是有效的。
        _identityLoaded = false;

        await SeedUserAsync(Requester, "alice");
        var id = await RequestAsync();

        var single = await Service.GetAsync(id);

        Assert.True(single.Succeeded);
        Assert.Equal(Requester, single.Data!.RequesterId);
        Assert.Null(single.Data.RequesterName);
    }

    /// <summary>
    /// ★★★ 第二位审批人的拒绝是在第一位批准之前读到 Pending 的，落库却在批准之后：
    /// 先读后整行回写会把 Approved 覆盖成 Rejected，两条相反的决定事件先后发出，
    /// 而总线上先到的那条批准可能已经让发起人执行了动作。
    /// </summary>
    [Fact]
    public async Task A_decision_made_on_a_stale_read_does_not_overwrite_a_concurrent_one()
    {
        var id = await RequestAsync();
        var stale = await ReadInConcurrentRequestAsync(id);

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);

        ActAs(ThirdParty);
        var rejected = await stale.Service.RejectAsync(id, "no");

        Assert.False(rejected.Succeeded);
        Assert.Equal(409, rejected.Code);
        var row = await ReadCommittedAsync(id);
        Assert.Equal(DualControlStatus.Approved, row.Status);
        Assert.Equal(Approver, row.ApproverId);
    }

    /// <summary>反方向：拒绝先落库，带着旧快照的批准后到，不得把一张被否决的许可变成可用。</summary>
    [Fact]
    public async Task A_stale_approval_cannot_turn_a_rejection_into_a_usable_permit()
    {
        var id = await RequestAsync();
        var stale = await ReadInConcurrentRequestAsync(id);

        ActAs(Approver);
        Assert.True((await Service.RejectAsync(id, "no")).Succeeded);

        ActAs(ThirdParty);
        var approved = await stale.Service.ApproveAsync(id);

        Assert.False(approved.Succeeded);
        Assert.Equal(409, approved.Code);
        Assert.Equal(DualControlStatus.Rejected, (await ReadCommittedAsync(id)).Status);
    }

    /// <summary>
    /// 整行回写还会把 <c>IsConsumed</c> 一起写回读到的旧值：许可已经被执行过，审计上却成了「从未使用」。
    /// </summary>
    [Fact]
    public async Task A_stale_decision_cannot_reset_a_consumed_permit()
    {
        var id = await RequestAsync();
        var stale = await ReadInConcurrentRequestAsync(id);

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);
        ActAs(Requester);
        Assert.True((await Service.ConsumeAsync(id, "payrun.void", "{\"amount\":100}")).Succeeded);

        ActAs(ThirdParty);
        Assert.False((await stale.Service.ApproveAsync(id)).Succeeded);

        var row = await ReadCommittedAsync(id);
        Assert.True(row.IsConsumed);
        Assert.NotNull(row.ConsumedAt);
        Assert.Equal(Approver, row.ApproverId);
    }

    /// <summary>发起人的撤回与审批人的决定并发：只能有一个成立，后到的那个不得覆盖先到的。</summary>
    [Fact]
    public async Task A_cancel_made_on_a_stale_read_does_not_overwrite_a_decision()
    {
        var id = await RequestAsync();
        var stale = await ReadInConcurrentRequestAsync(id);

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);

        ActAs(Requester);
        var cancelled = await stale.Service.CancelAsync(id);

        Assert.False(cancelled.Succeeded);
        Assert.Equal(409, cancelled.Code);
        Assert.Equal(DualControlStatus.Approved, (await ReadCommittedAsync(id)).Status);
    }

    /// <summary>决定落库时审计列照常记下是谁、何时改的（条件更新绕过了保存拦截器）。</summary>
    [Fact]
    public async Task A_decision_stamps_the_modification_audit_columns()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        Assert.True((await Service.ApproveAsync(id)).Succeeded);

        var row = await ReadCommittedAsync(id);
        Assert.Equal(Approver, row.LastModifierId);
        Assert.NotNull(row.LastModificationTime);
    }

    // ── 发起与决定的输入边界 ────────────────────────────────────────────────

    /// <summary>
    /// 逐请求覆盖的有效期与配置默认值受同一个 30 天上限约束：再长，「两个人当场同意」就不再成立。
    /// 此前只钳配置值，覆盖值只看是否为正：十年有效的许可照签不误。
    /// </summary>
    [Fact]
    public async Task A_lifetime_override_beyond_thirty_days_is_refused()
    {
        ActAs(Requester);

        var result = await Service.RequestAsync(
            new DualControlRequestInput("payrun.void", "PR-1", "{}", Lifetime: TimeSpan.FromDays(3650)));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Empty(await DbContext.DualControlRequests.AsNoTracking().ToListAsync());
    }

    /// <summary>超出 <see cref="DateTime"/> 能表示的有效期答 400，而不是在算到期时刻时抛出成 500。</summary>
    [Fact]
    public async Task An_unrepresentable_lifetime_is_a_validation_error_not_an_exception()
    {
        ActAs(Requester);

        var result = await Service.RequestAsync(
            new DualControlRequestInput("payrun.void", "PR-1", "{}", Lifetime: TimeSpan.MaxValue));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    [Fact]
    public async Task A_lifetime_of_exactly_thirty_days_is_accepted()
    {
        ActAs(Requester);

        var result = await Service.RequestAsync(
            new DualControlRequestInput("payrun.void", "PR-1", "{}", Lifetime: TimeSpan.FromMinutes(43200)));

        Assert.True(result.Succeeded, result.Message);
    }

    /// <summary>
    /// 列宽之外的说明、动作标识与目标标识在服务层答 400：PostgreSQL 在保存时才拒（500），
    /// SQLite 根本不拒（落库的值超出约定的列宽）。
    /// </summary>
    [Theory]
    [InlineData("operation")]
    [InlineData("target")]
    [InlineData("description")]
    public async Task Over_length_request_fields_are_refused(string field)
    {
        ActAs(Requester);
        var operation = field == "operation" ? new string('o', DualControlRequest.OperationMaxLength + 1) : "payrun.void";
        var target = field == "target" ? new string('t', DualControlRequest.TargetIdMaxLength + 1) : "PR-1";
        var description = field == "description" ? new string('d', DualControlRequest.DescriptionMaxLength + 1) : "why";

        var result = await Service.RequestAsync(new DualControlRequestInput(operation, target, "{}", description));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Empty(await DbContext.DualControlRequests.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Fields_at_their_length_limit_are_accepted()
    {
        ActAs(Requester);

        var result = await Service.RequestAsync(new DualControlRequestInput(
            new string('o', DualControlRequest.OperationMaxLength),
            new string('t', DualControlRequest.TargetIdMaxLength),
            "{}",
            new string('d', DualControlRequest.DescriptionMaxLength)));

        Assert.True(result.Succeeded, result.Message);
    }

    /// <summary>审批意见同样受列宽约束；超长时请求保持待批，而不是条件更新在库里失败。</summary>
    [Fact]
    public async Task An_over_length_decision_comment_is_refused_and_the_request_stays_pending()
    {
        var id = await RequestAsync();

        ActAs(Approver);
        var result = await Service.RejectAsync(id, new string('r', DualControlRequest.DecisionCommentMaxLength + 1));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Equal(DualControlStatus.Pending, (await ReadCommittedAsync(id)).Status);
    }

    /// <summary>
    /// 在另一个作用域（另一个 DbContext）里先把请求读进来，像一个与当前请求并发的 HTTP 请求那样：
    /// 之后对它的决定拿到的是这份读时的快照。
    /// </summary>
    private async Task<(IServiceScope Scope, DualControlService Service)> ReadInConcurrentRequestAsync(Guid id)
    {
        var scope = ServiceProvider.CreateScope();
        _scopes.Add(scope);
        var snapshot = await scope.ServiceProvider.GetRequiredService<IRepository<DualControlRequest, Guid>>().GetAsync(id);
        Assert.Equal(DualControlStatus.Pending, snapshot!.Status);
        return (scope, scope.ServiceProvider.GetRequiredService<DualControlService>());
    }

    /// <summary>绕过任何上下文的跟踪副本，读库里此刻的那一行。</summary>
    private async Task<DualControlRequest> ReadCommittedAsync(Guid id)
        => await DbContext.DualControlRequests.AsNoTracking().SingleAsync(r => r.Id == id);

    private readonly List<IServiceScope> _scopes = [];

    public override void Dispose()
    {
        foreach (var scope in _scopes)
        {
            scope.Dispose();
        }

        base.Dispose();
    }

    private async Task SeedUserAsync(Guid id, string userName)
    {
        DbContext.Set<Tnzi.Identity.Entities.User>().Add(new Tnzi.Identity.Entities.User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            CreationTime = DateTime.UtcNow
        });

        await DbContext.SaveChangesAsync();
    }

    /// <summary>
    /// 每次读取都重新构造的 <see cref="IOptionsMonitor{T}"/>：
    /// 用例要在服务已经建好之后改配置（服务是 <c>CurrentValue</c> 的消费者，不是快照的）。
    /// </summary>
    private sealed class LiveOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly Func<T> _factory;

        public LiveOptionsMonitor(Func<T> factory) => _factory = factory;

        public T CurrentValue => _factory();

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
