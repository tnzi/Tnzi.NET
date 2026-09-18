using Tnzi.Audit.Tests.TestSupport;
using Tnzi.MultiTenancy;

namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 审计三张表的读取面按<b>调用者的租户</b>收口：租户内的调用者只看得到本租户的行，宿主看全部。
/// </summary>
/// <remarks>
/// <para>
/// 三个实体都刻意不实现 <c>IMultiTenant</c>（审计写入不该被租户过滤器挡住），租户归属是手工写入的
/// <c>TenantId</c> 列，所以全局租户过滤器管不到它们。此前读取侧一处租户谓词都没有：多租户部署里
/// 持 <c>audit.operation.view</c> 的租户管理员一次 query 就拿到所有租户的 Url / RequestBody / Ip /
/// UserAgent，还能整包导出；记录级读审计答的正是「谁读了哪一条记录」，跨租户可读等于把别家客户的
/// 数据访问轨迹全部交出去。
/// </para>
/// <para>
/// ★ 谓词以多租户开关为闸门：<c>TenantId</c> 在多租户关闭时被 <c>builder.Ignore</c>，对被 Ignore
/// 的属性写 <c>Where</c> 会在查询翻译时抛异常 —— 关闭时必须一行不加，否则单租户部署每次审计查询 500。
/// 本类的 DbContext 以 <c>MultiTenancy:Enabled=true</c> 构造（否则列根本不存在）；
/// <see cref="MultiTenancyDisabled_QueriesDoNotReferenceTenantId"/> 在默认（关闭）的 DbContext 上守另一半。
/// </para>
/// </remarks>
public class AuditTenantScopeTests : IntegrationTestBase
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid UserOfA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid UserOfB = Guid.Parse("22222222-0000-0000-0000-000000000002");

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        // 多租户开启：TenantId 列才会被映射并建索引（见 AuditOperationConfiguration）。
        services.Configure<MultiTenancyOptions>(o => o.Enabled = true);
        services.AddScoped<IRepository<AuditRecordAccess, Guid>,
            EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>>();
    }

    public AuditTenantScopeTests()
    {
        Seed().GetAwaiter().GetResult();
    }

    // ---- 种子 ---------------------------------------------------------------

    private async Task Seed()
    {
        var now = DateTime.UtcNow;
        DbContext.Set<AuditOperation>().AddRange(
            Operation("A.One", TenantA, UserOfA, now.AddDays(-1)),
            Operation("A.Two", TenantA, UserOfA, now.AddDays(-2)),
            Operation("B.One", TenantB, UserOfB, now.AddDays(-1)),
            Operation("Host.One", null, null, now.AddDays(-1)),
            Operation("A.Old", TenantA, UserOfA, now.AddDays(-400)),
            Operation("B.Old", TenantB, UserOfB, now.AddDays(-400)));

        DbContext.Set<AuditRecordAccess>().AddRange(
            Access(1, TenantA, UserOfA, "Case", "a-1"),
            Access(2, TenantA, UserOfA, "Case", "a-2"),
            Access(1, TenantB, UserOfB, "Case", "b-1"));

        DbContext.Set<AuditDataDestruction>().AddRange(
            Certificate(1, TenantA),
            Certificate(2, TenantB),
            Certificate(3, null));

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static AuditOperation Operation(string functionName, Guid? tenantId, Guid? userId, DateTime when) => new()
    {
        Id = Guid.NewGuid(),
        FunctionName = functionName,
        TenantId = tenantId,
        UserId = userId,
        UserName = userId?.ToString(),
        ResultType = AuditResultType.Success,
        HttpMethod = "GET",
        Url = $"/api/{functionName}",
        CreationTime = when,
        StartTime = when,
        Elapsed = 10
    };

    private static AuditRecordAccess Access(long sequence, Guid tenantId, Guid userId, string resourceType, string resourceId) => new()
    {
        Id = Guid.NewGuid(),
        Sequence = sequence,
        ResourceType = resourceType,
        ResourceId = resourceId,
        UserId = userId,
        UserName = userId.ToString(),
        TenantId = tenantId,
        PreviousHash = string.Empty,
        Hash = "hash",
        CreationTime = DateTime.UtcNow.AddMinutes(-sequence)
    };

    private static AuditDataDestruction Certificate(long sequence, Guid? tenantId) => new()
    {
        Id = Guid.NewGuid(),
        Sequence = sequence,
        PolicyName = "retention",
        EntityType = "Test",
        Cutoff = DateTime.UtcNow.AddDays(-30),
        Mode = "hard-delete",
        IdentifierDigest = "digest",
        PreviousHash = string.Empty,
        Hash = "hash",
        TenantId = tenantId,
        CreationTime = DateTime.UtcNow
    };

    // ---- 构造助手：tenantId 为 null 表示宿主（不绑定租户）的调用者 ------------------

    private static ICurrentTenant Tenant(Guid? tenantId)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(tenantId);
        tenant.Setup(t => t.IsAvailable).Returns(tenantId.HasValue);
        return tenant.Object;
    }

    private static IOptions<MultiTenancyOptions> MultiTenancyOn()
        => Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = true });

    private AuditOperationService OperationService(Guid? tenantId)
    {
        var repository = new EFCoreRepository<AuditTestDbContext, AuditOperation, Guid>(DbContext, serviceProvider: ServiceProvider);
        return new AuditOperationService(
            repository,
            ServiceProvider.GetRequiredService<IAuditStore>(),
            new StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
            ServiceProvider,
            Tenant(tenantId),
            MultiTenancyOn());
    }

    private RecordAccessAuditor Auditor(Guid? tenantId)
    {
        var repository = new EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>(DbContext, serviceProvider: ServiceProvider);
        return new RecordAccessAuditor(
            repository,
            new StaticOptionsMonitor<RecordAccessAuditOptions>(new RecordAccessAuditOptions { Enabled = true }),
            ServiceProvider,
            Tenant(tenantId),
            MultiTenancyOn());
    }

    /// <param name="tenantId">调用者的租户；null 为宿主。</param>
    /// <param name="policies">要跑的保留策略；不给则不声明策略（只用读取面）。</param>
    /// <param name="currentTenant">
    /// 给了就替换按 <paramref name="tenantId"/> 造的替身：跑遍租户的用例要用真实的 <see cref="CurrentTenant"/>，
    /// 替身的 <c>Change</c> 不会改 <c>Id</c>，切进哪个租户都读不出来。
    /// </param>
    private DataDestructionService DestructionService(Guid? tenantId, IEnumerable<RetentionPolicy>? policies = null, ICurrentTenant? currentTenant = null)
    {
        var repository = new EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>(DbContext, serviceProvider: ServiceProvider);
        return new DataDestructionService(
            ServiceProvider,
            repository,
            new StaticOptionsMonitor<DataDestructionOptions>(new DataDestructionOptions { Enabled = true }),
            policies == null ? [] : [new StubPolicyProvider(policies.ToArray())],
            [],
            new HardDeleteDataDestroyer(ServiceProvider, new StubEntityManager()),
            currentTenant ?? Tenant(tenantId),
            null,
            MultiTenancyOn());
    }

    private static string[] Functions(Result<IPagedList<AuditOperationDto>> result)
    {
        result.Succeeded.ShouldBeTrue(result.Message);
        return result.Data!.Items.Select(o => o.FunctionName!).OrderBy(f => f).ToArray();
    }

    // ---- 操作审计 -------------------------------------------------------------

    [Fact]
    public async Task TenantCaller_QueryOperations_SeesOnlyOwnTenantRows()
    {
        var functions = Functions(await OperationService(TenantA).GetOperationsAsync(new AuditOperationQueryDto()));

        functions.ShouldBe(["A.Old", "A.One", "A.Two"]);
    }

    [Fact]
    public async Task HostCaller_QueryOperations_SeesEveryTenant()
    {
        var functions = Functions(await OperationService(null).GetOperationsAsync(new AuditOperationQueryDto()));

        functions.ShouldBe(["A.Old", "A.One", "A.Two", "B.Old", "B.One", "Host.One"]);
    }

    [Fact]
    public async Task TenantCaller_GetOperationOfAnotherTenant_Is404_NotRevealingExistence()
    {
        var foreign = await DbContext.Set<AuditOperation>().AsNoTracking().SingleAsync(o => o.FunctionName == "B.One");
        var own = await DbContext.Set<AuditOperation>().AsNoTracking().SingleAsync(o => o.FunctionName == "A.One");
        var service = OperationService(TenantA);

        (await service.GetAsync(foreign.Id)).Code.ShouldBe(404);
        (await service.GetAsync(own.Id)).Succeeded.ShouldBeTrue();
        (await OperationService(null).GetAsync(foreign.Id)).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task TenantCaller_UserOperationsAndStatistics_AreScoped()
    {
        // 按用户 id 查询：点名别租户的用户拿到的是空，不是对方的操作。
        var service = OperationService(TenantA);

        var ops = await service.GetUserOperationsAsync(UserOfB);
        ops.Succeeded.ShouldBeTrue();
        ops.Data!.ShouldBeEmpty();

        var stats = await service.GetUserStatisticsAsync(UserOfB);
        stats.Data!.TotalCount.ShouldBe(0);

        var functionStats = await service.GetFunctionStatisticsAsync("B.One");
        functionStats.Data!.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task TenantCaller_TopAndTrend_AreScoped()
    {
        var service = OperationService(TenantA);
        var since = DateTime.UtcNow.AddDays(-3);

        var topFunctions = await service.GetTopFunctionsAsync(10, since);
        topFunctions.Data!.Select(f => f.FunctionName).ShouldBe(["A.One", "A.Two"], ignoreOrder: true);

        var topUsers = await service.GetTopUsersAsync(10, since);
        topUsers.Data!.ShouldHaveSingleItem().UserId.ShouldBe(UserOfA);

        var trend = await service.GetAuditTrendAsync(since, DateTime.UtcNow);
        trend.Data!.Sum(p => p.TotalCount).ShouldBe(2);
    }

    [Fact]
    public async Task TenantCaller_Export_ContainsOnlyOwnTenant()
    {
        var csv = await OperationService(TenantA).ExportToCsvAsync(new AuditOperationQueryDto());

        csv.Succeeded.ShouldBeTrue(csv.Message);
        csv.Data!.ShouldContain("A.One");
        csv.Data!.ShouldNotContain("B.One");
        csv.Data!.ShouldNotContain("Host.One");

        var json = await OperationService(TenantA).ExportToJsonAsync(new AuditOperationQueryDto());
        json.Data!.ShouldNotContain("B.One");
    }

    [Fact]
    public async Task TenantCaller_DeleteExpired_IsRefused_AndTouchesNothing()
    {
        // 保留期清理是部署级的合规动作，不是租户能替宿主做的事：租户内的调用者 403，一行不删。
        var result = await OperationService(TenantA).DeleteExpiredOperationsAsync(days: 90);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await DbContext.Set<AuditOperation>().AsNoTracking().CountAsync()).ShouldBe(6);
    }

    [Fact]
    public async Task HostCaller_DeleteExpired_StillWorks()
    {
        var result = await OperationService(null).DeleteExpiredOperationsAsync(days: 90);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBe(2);
    }

    // ---- 记录级读审计 -------------------------------------------------------------

    [Fact]
    public async Task TenantCaller_RecordAccessQuery_SeesOnlyOwnTenantRows()
    {
        var result = await Auditor(TenantA).GetAccessesAsync(new RecordAccessQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.Select(a => a.ResourceId).ShouldBe(["a-1", "a-2"], ignoreOrder: true);

        var host = await Auditor(null).GetAccessesAsync(new RecordAccessQueryDto());
        host.Data!.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task TenantCaller_RecordAccessUserStatistics_AreScoped()
    {
        var result = await Auditor(TenantA).GetUserStatisticsAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ShouldHaveSingleItem().UserId.ShouldBe(UserOfA);
    }

    [Fact]
    public async Task TenantCaller_VerifyChainOfAnotherTenantsUser_Is404()
    {
        // 链要整条读才能验，所以不按租户过滤行；但别租户用户的链对租户内调用者等同于不存在。
        var foreign = await Auditor(TenantA).VerifyChainAsync(UserOfB);
        foreign.Succeeded.ShouldBeFalse();
        foreign.Code.ShouldBe(404);

        // 本租户用户的链照常验（种子哈希是假的，验证结果是 409「被改过」而不是 404）。
        (await Auditor(TenantA).VerifyChainAsync(UserOfA)).Code.ShouldBe(409);
        (await Auditor(null).VerifyChainAsync(UserOfB)).Code.ShouldBe(409);
    }

    // ---- 销毁证明 ---------------------------------------------------------------

    [Fact]
    public async Task TenantCaller_DestructionCertificates_SeesOnlyOwnTenantRows()
    {
        var result = await DestructionService(TenantA).GetCertificatesAsync(new DataDestructionQueryDto());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Items.ShouldHaveSingleItem().Sequence.ShouldBe(1);

        var host = await DestructionService(null).GetCertificatesAsync(new DataDestructionQueryDto());
        host.Data!.TotalCount.ShouldBe(3);
    }

    // ---- 销毁执行侧 ---------------------------------------------------------------
    // 读取面收口那批只收了证书查询；RunAsync 与 DELETE expired 是同一形状的部署级合规动作，
    // 却仍由租户调用者触发并遍历全部租户；证书又记在触发者名下而不是被销毁数据所属的租户。

    private sealed class StubPolicyProvider(params RetentionPolicy[] policies) : IRetentionPolicyProvider
    {
        public IEnumerable<RetentionPolicy> GetPolicies() => policies;
    }

    /// <summary>把带租户的被试实体指向测试 DbContext（生产里由 EntityManager 的注册表回答）。</summary>
    private sealed class StubEntityManager : IEntityManager
    {
        public void Initialize() { }
        public IEntityRegister[] GetEntityRegisters(Type dbContextType) => [];
        public Type GetDbContextTypeForEntity(Type entityType) => typeof(AuditTestDbContext);
        public Type[] GetAllEntityTypes() => [typeof(TenantRetentionTestRecord)];
        public Type[] GetAllDbContextTypes() => [typeof(AuditTestDbContext)];
    }

    private static RetentionPolicy<TenantRetentionTestRecord> TenantPolicy() => new()
    {
        Name = "tenant-retention",
        RetentionPeriod = TimeSpan.FromDays(30),
        Timestamp = r => r.CreationTime
    };

    private async Task SeedExpiredTenantRecordsAsync()
    {
        DbContext.Set<TenantRetentionTestRecord>().AddRange(
            new TenantRetentionTestRecord { Id = Guid.NewGuid(), TenantId = TenantA, Category = "a", CreationTime = DateTime.UtcNow.AddDays(-100) },
            new TenantRetentionTestRecord { Id = Guid.NewGuid(), TenantId = TenantB, Category = "b", CreationTime = DateTime.UtcNow.AddDays(-100) });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private Task<List<TenantRetentionTestRecord>> SurvivingTenantRecordsAsync()
        => DbContext.Set<TenantRetentionTestRecord>().IgnoreQueryFilters().AsNoTracking().ToListAsync();

    private Task<List<AuditDataDestruction>> CertificatesAsync()
        => DbContext.Set<AuditDataDestruction>().AsNoTracking().OrderBy(e => e.Sequence).ToListAsync();

    [Fact]
    public async Task TenantCaller_RunDestruction_IsRefused_AndDestroysNothing()
    {
        // 与 DELETE expired 同一口径：租户内的调用者 403，任何租户的到期数据一行不动，也不出证书。
        await SeedExpiredTenantRecordsAsync();

        var result = await DestructionService(TenantA, policies: [TenantPolicy()]).RunAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await SurvivingTenantRecordsAsync()).Count.ShouldBe(2);
        (await CertificatesAsync()).Count.ShouldBe(3);
    }

    [Fact]
    public async Task HostRun_StampsEachCertificateWithTheTenantWhoseDataWasDestroyed()
    {
        // 证书是销毁的唯一证据：宿主触发（或定时触发，两者都没有租户）跑遍租户时，
        // 每一张证书要记在被销毁数据所属的租户名下，那个租户按 query 才看得到它。
        await SeedExpiredTenantRecordsAsync();
        var service = DestructionService(null, policies: [TenantPolicy()], currentTenant: new CurrentTenant(ServiceProvider.GetRequiredService<ICurrentUser>()));

        var result = await service.RunAsync();

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.TotalDestroyed.ShouldBe(2);
        (await SurvivingTenantRecordsAsync()).ShouldBeEmpty();

        // 一租户一张、各记各的一条：切进租户后的扫描若不补租户条件，第一轮就把两家的行一起销毁，
        // 证书只有一张、记在第一个租户名下、DestroyedCount = 2（09-12 实测就是这个形态）。
        var issued = (await CertificatesAsync()).Where(c => c.Sequence > 3).ToList();
        issued.Select(c => (c.TenantId, c.DestroyedCount)).ShouldBe([(TenantA, 1), (TenantB, 1)], ignoreOrder: true);

        var seenByA = await DestructionService(TenantA).GetCertificatesAsync(new DataDestructionQueryDto());
        seenByA.Data!.Items.Select(c => c.Sequence).ShouldBe([issued.Single(c => c.TenantId == TenantA).Sequence, 1]);
    }

    // ---- 多租户关闭：谓词一行都不能加 ----------------------------------------------

    /// <summary>
    /// 默认（多租户关闭）的 DbContext 里 <c>TenantId</c> 被 Ignore；谓词若不以开关为闸门，
    /// 这里每一条查询都会在翻译时抛 <c>InvalidOperationException</c>。
    /// </summary>
    public class MultiTenancyDisabled_QueriesDoNotReferenceTenantId : IntegrationTestBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddScoped<IRepository<AuditRecordAccess, Guid>,
                EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>>();
        }

        [Fact]
        public async Task TenantBoundCaller_StillQueriesFine_WhenMultiTenancyIsOff()
        {
            // 调用者带着租户 claim，但部署没开多租户：谓词必须不出现，否则 500。
            DbContext.Set<AuditOperation>().Add(Operation("Solo", null, UserOfA, DateTime.UtcNow));
            await DbContext.SaveChangesAsync();
            DbContext.ChangeTracker.Clear();

            var tenant = Tenant(TenantA);
            var off = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = false });

            var operations = new AuditOperationService(
                new EFCoreRepository<AuditTestDbContext, AuditOperation, Guid>(DbContext, serviceProvider: ServiceProvider),
                ServiceProvider.GetRequiredService<IAuditStore>(),
                new StaticOptionsMonitor<AuditOptions>(new AuditOptions()),
                ServiceProvider,
                tenant,
                off);
            var list = await operations.GetOperationsAsync(new AuditOperationQueryDto());
            list.Succeeded.ShouldBeTrue(list.Message);
            list.Data!.TotalCount.ShouldBe(1);
            (await operations.GetAsync(list.Data.Items[0].Id)).Succeeded.ShouldBeTrue();
            (await operations.DeleteExpiredOperationsAsync(days: 3650)).Succeeded.ShouldBeTrue();

            var auditor = new RecordAccessAuditor(
                new EFCoreRepository<AuditTestDbContext, AuditRecordAccess, Guid>(DbContext, serviceProvider: ServiceProvider),
                new StaticOptionsMonitor<RecordAccessAuditOptions>(new RecordAccessAuditOptions { Enabled = true }),
                ServiceProvider,
                tenant,
                off);
            (await auditor.GetAccessesAsync(new RecordAccessQueryDto())).Succeeded.ShouldBeTrue();
            (await auditor.GetUserStatisticsAsync()).Succeeded.ShouldBeTrue();
            (await auditor.VerifyChainAsync(UserOfA)).Succeeded.ShouldBeTrue();

            var destruction = new DataDestructionService(
                ServiceProvider,
                new EFCoreRepository<AuditTestDbContext, AuditDataDestruction, Guid>(DbContext, serviceProvider: ServiceProvider),
                new StaticOptionsMonitor<DataDestructionOptions>(new DataDestructionOptions { Enabled = true }),
                [],
                [],
                new Mock<IDataDestroyer>().Object,
                tenant,
                null,
                off);
            (await destruction.GetCertificatesAsync(new DataDestructionQueryDto())).Succeeded.ShouldBeTrue();
        }
    }
}
