namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 认领失败不能把并发槽带走。
/// </summary>
/// <remarks>
/// 此前认领查询抛异常（连接重置、命令超时、主从切换）时占用的槽从不归还：累计 <c>MaxConcurrentRuns</c> 次之后
/// 队列循环永久阻塞在等槽上，排在它前面的租约回收也一起停摆，入队接口照样答 200，运行永远停在 Queued，
/// 而此后一条日志都没有。
/// </remarks>
public class CliRunQueueSlotTests : IntegratedTestBase<CliQueueDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliQueueDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliQueueDbContext, CliRuntime, Guid>>();
    }

    [Fact]
    public async Task ClaimQueryThrows_TheSlotIsReturned_EveryTime()
    {
        var failing = new Mock<IRepository<CliRun, Guid>>();
        failing.Setup(r => r.AsQueryable(It.IsAny<bool>())).Throws(new InvalidOperationException("connection reset"));
        var services = new ServiceCollection();
        services.AddScoped(_ => failing.Object);
        services.AddScoped(_ => new Mock<IRepository<CliRuntime, Guid>>().Object);
        using var provider = services.BuildServiceProvider();
        var processor = CreateProcessor(provider.GetRequiredService<IServiceScopeFactory>());

        using var slots = new SemaphoreSlim(2, 2);

        // 比槽数多得多的失败：任何一次漏还，都会让后面的 WaitAsync 永远等下去
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Should.ThrowAsync<InvalidOperationException>(
                () => processor.TryStartNextAsync(slots, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        }

        slots.CurrentCount.ShouldBe(2);
    }

    [Fact]
    public async Task NothingToClaim_ReturnsFalse_AndReturnsTheSlot()
    {
        var processor = CreateProcessor(ServiceProvider.GetRequiredService<IServiceScopeFactory>());
        using var slots = new SemaphoreSlim(1, 1);

        // 限时：漏还槽时第二次调用会永远等在 WaitAsync 上，测试应当失败而不是挂住整个测试进程
        (await processor.TryStartNextAsync(slots, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();
        (await processor.TryStartNextAsync(slots, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeFalse();

        slots.CurrentCount.ShouldBe(1);
    }

    private static CliRunQueueProcessor CreateProcessor(IServiceScopeFactory scopeFactory)
    {
        var options = new Mock<IOptionsMonitor<CliAgentOptions>>();
        options.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });

        return new CliRunQueueProcessor(
            scopeFactory,
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            options.Object,
            NullLogger<CliRunQueueProcessor>.Instance);
    }
}
