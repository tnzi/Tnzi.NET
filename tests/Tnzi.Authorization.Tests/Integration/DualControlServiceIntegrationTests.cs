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
