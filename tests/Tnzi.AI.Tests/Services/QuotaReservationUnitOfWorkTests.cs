using Tnzi.AI.Tests.Integration;

namespace Tnzi.AI.Tests.Services;

/// <summary>
/// 全局工作单元下的配额预留回归测试（真实 SQLite + 真实 EFCoreRepository + UnitOfWorkManager）。
/// </summary>
/// <remarks>
/// <para>
/// 启用事务（AspNetCore 的 <c>EnableGlobalUnitOfWork</c>）时仓储默认延迟保存，新建的配额行只停在
/// 变更跟踪器里；而 <c>ReserveQuotaAsync</c> 紧接着走 <c>ExecuteUpdateAsync</c>（绕过变更跟踪器的
/// 裸 SQL），匹配不到那一行、返回受影响行数 0，而 0 被读成「配额不足」。
/// 症状是<b>每个用户的首次 AI 请求必定失败，且失败原因写着配额耗尽</b>。
/// </para>
/// <para>
/// 这些用例必须跑在真实 EF + UnitOfWorkManager 上：Mock 仓储既不会延迟保存，也不会执行
/// <c>ExecuteUpdate</c>，这个缺陷在 Mock 下根本不存在。
/// </para>
/// </remarks>
public class QuotaReservationUnitOfWorkTests : IntegratedTestBase<AiIntegrationDbContext>
{
    private const long DailyLimit = 1_000_000;
    private const long MonthlyLimit = 20_000_000;

    private IUnitOfWorkManager Manager => ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var mapperConfig = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(mapperConfig));

        // 让 UnitOfWorkManager 能发现测试 DbContext
        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(AiIntegrationDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IRepository<UserQuota, Guid>,
            EFCoreRepository<AiIntegrationDbContext, UserQuota, Guid>>();

        var options = new AIOptions
        {
            Quota = new QuotaOptions
            {
                DefaultDailyTokenLimit = DailyLimit,
                DefaultMonthlyTokenLimit = MonthlyLimit
            }
        };
        services.AddSingleton<IOptionsMonitor<AIOptions>>(new StaticOptionsMonitor<AIOptions>(options));

        services.AddScoped<IQuotaService, QuotaService>();
    }

    private IQuotaService CreateService() => ServiceProvider.GetRequiredService<IQuotaService>();

    private async Task<UserQuota?> ReadQuotaAsync(Guid userId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<UserQuota>().AsNoTracking()
            .FirstOrDefaultAsync(q => q.UserId == userId);
    }

    [Fact]
    public async Task ReserveQuota_FirstEverRequest_UnderGlobalUnitOfWork_Succeeds()
    {
        // Arrange：全新用户，库里没有配额行；模拟 EnableGlobalUnitOfWork = true
        var userId = Guid.NewGuid();
        var service = CreateService();
        Manager.EnableTransaction();

        // Act
        var result = await service.ReserveQuotaAsync(userId, 1_000);

        // Assert：首次请求必须成功，且预留真的被记下了
        result.Succeeded.ShouldBeTrue(
            $"first-ever request must not fail; got [{result.ErrorCode}] {result.Message}");
        result.Data.ShouldNotBeNull();
        result.Data!.ReservedTokens.ShouldBe(1_000);

        // 必须 no-tracking 读：ExecuteUpdate 是绕过变更跟踪器的裸 SQL，
        // 跟踪查询会返回跟踪器里那份没有被它更新过的副本
        var persisted = await DbContext.Set<UserQuota>().AsNoTracking()
            .FirstOrDefaultAsync(q => q.UserId == userId);
        persisted.ShouldNotBeNull();
        persisted!.CurrentDailyUsage.ShouldBe(1_000);
        persisted.CurrentMonthlyUsage.ShouldBe(1_000);

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task ReserveQuota_FirstEverRequest_NeverReportsQuotaExceeded()
    {
        // 全新账号默认额度 100 万 / 2000 万 Token，"已耗尽" 根本讲不通 ——
        // 一旦这样报，排查会被指向配额配置，而真正的原因在别处。
        var userId = Guid.NewGuid();
        var service = CreateService();
        Manager.EnableTransaction();

        var result = await service.ReserveQuotaAsync(userId, 1_000);

        result.ErrorCode.ShouldNotBe(ErrorCodes.QuotaExceeded);
        (result.Message ?? string.Empty).ShouldNotContain("quota exceeded", Case.Insensitive);

        await Manager.CommitTransactionAsync();
    }

    [Fact]
    public async Task ReserveQuota_FirstEverRequest_WritesInsideTransaction_SoRollbackUndoesIt()
    {
        // 让新行可见的正确做法是经工作单元 flush，不是绕开事务落库：
        // 后者会让一次失败的请求留下无法回滚的配额扣减。
        var userId = Guid.NewGuid();
        var service = CreateService();
        Manager.EnableTransaction();

        var result = await service.ReserveQuotaAsync(userId, 1_000);
        result.Succeeded.ShouldBeTrue();

        // 裸 SQL 与 flush 都发生在物理事务内（延迟事务已被强开）
        DbContext.Database.CurrentTransaction.ShouldNotBeNull();

        await Manager.RollbackTransactionAsync();

        (await ReadQuotaAsync(userId)).ShouldBeNull();
    }

    [Fact]
    public async Task ReserveQuota_ExistingRow_WritesInsideTransaction_SoRollbackUndoesIt()
    {
        // ★ 这条守的是 ReserveQuotaAsync 里的 EnsureTransactionStartedAsync，
        //   而上面那条首次请求的回滚用例守不住它：新建路径的 flush 会顺手 BEGIN 事务，
        //   所以去掉那行前置，首次请求依然全绿。
        //   配额行已存在时（第二次及以后的每一次请求）没有任何 flush 先发生，
        //   扣减就落在自动提交模式下：请求回滚了，用量却永久留在库里。
        var userId = Guid.NewGuid();
        DbContext.Set<UserQuota>().Add(new UserQuota
        {
            UserId = userId,
            DailyTokenLimit = DailyLimit,
            MonthlyTokenLimit = MonthlyLimit,
            CurrentDailyUsage = 0,
            CurrentMonthlyUsage = 0,
            LastResetDate = DateTime.UtcNow,
            IsEnabled = true,
            WarningThreshold = 0.8m,
            CriticalThreshold = 0.95m
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var service = CreateService();
        Manager.EnableTransaction();

        (await service.ReserveQuotaAsync(userId, 1_000)).Succeeded.ShouldBeTrue();

        await Manager.RollbackTransactionAsync();

        var persisted = await ReadQuotaAsync(userId);
        persisted.ShouldNotBeNull();
        persisted!.CurrentDailyUsage.ShouldBe(0);
    }

    [Fact]
    public async Task ReserveQuota_SecondRequestInSameTransaction_AccumulatesOnTheSameRow()
    {
        // 第一次请求建行 + 预留，第二次必须落在同一行上累加，
        // 而不是再建一行或匹配不到行
        var userId = Guid.NewGuid();
        var service = CreateService();
        Manager.EnableTransaction();

        (await service.ReserveQuotaAsync(userId, 1_000)).Succeeded.ShouldBeTrue();
        (await service.ReserveQuotaAsync(userId, 2_500)).Succeeded.ShouldBeTrue();

        await Manager.CommitTransactionAsync();

        var persisted = await ReadQuotaAsync(userId);
        persisted.ShouldNotBeNull();
        persisted!.CurrentDailyUsage.ShouldBe(3_500);
        persisted.CurrentMonthlyUsage.ShouldBe(3_500);
    }

    [Fact]
    public async Task ReserveQuota_WhenDailyLimitGenuinelyExhausted_ReportsExceededWithRealNumbers()
    {
        // 真的超限时仍须报超限，且带上具体数字（原实现在缺行时会插值出空白：
        // "Monthly quota exceeded. Current: , Limit: "）
        var userId = Guid.NewGuid();
        DbContext.Set<UserQuota>().Add(new UserQuota
        {
            UserId = userId,
            DailyTokenLimit = 10_000,
            MonthlyTokenLimit = MonthlyLimit,
            CurrentDailyUsage = 9_999,
            CurrentMonthlyUsage = 9_999,
            LastResetDate = DateTime.UtcNow,
            IsEnabled = true,
            WarningThreshold = 0.8m,
            CriticalThreshold = 0.95m
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var service = CreateService();
        Manager.EnableTransaction();

        var result = await service.ReserveQuotaAsync(userId, 1_000);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.QuotaExceeded);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("9999");
        result.Message.ShouldContain("10000");

        await Manager.RollbackTransactionAsync();
    }

    [Fact]
    public async Task GetQuota_FirstEverRequest_UnderGlobalUnitOfWork_ReturnsPersistedId()
    {
        // 同一条延迟保存的推论：ID 在 SaveChanges 中赋值，不 flush 就是 Guid.Empty，
        // 调用方拿着它去查什么都查不到
        var userId = Guid.NewGuid();
        var service = CreateService();
        Manager.EnableTransaction();

        var result = await service.GetQuotaAsync(userId);

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data!.Id.ShouldNotBe(Guid.Empty);

        await Manager.CommitTransactionAsync();
    }
}
