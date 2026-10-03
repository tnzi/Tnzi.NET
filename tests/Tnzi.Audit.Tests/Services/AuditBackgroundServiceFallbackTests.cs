using Tnzi.Audit.Tests.TestSupport;

namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// 一批审计操作的批量 INSERT 被数据库拒绝时，不能让整批一起消失。
/// </summary>
/// <remarks>
/// 批量 INSERT 是一条语句：一行的列宽 / 约束问题会让 SQL Server / PostgreSQL 拒绝整条语句，
/// 而通道基类的既定取舍是「批处理失败记日志后丢弃这一批」。两条规则叠在一起，
/// 一行坏数据就带走了同一时间窗里其他所有人的审计记录。
/// 修法：批量失败后<b>逐行重试</b>，每行一个干净的 DI 作用域（失败的那个 DbContext
/// 还跟踪着整批实体，不能复用），只有真正坏的那一行被记录并放弃。
/// </remarks>
public class AuditBackgroundServiceFallbackTests
{
    private sealed class Probe : AuditBackgroundService
    {
        public Probe(IServiceProvider serviceProvider, IAuditConsumer consumer, IOptionsMonitor<AuditOptions> options)
            : base(serviceProvider, NullLogger<AuditBackgroundService>.Instance, consumer, options)
        {
        }

        public Task RunAsync(IReadOnlyList<AuditOperation> batch, IServiceProvider scopedServices)
            => ProcessBatchAsync(batch, scopedServices, CancellationToken.None);
    }

    /// <summary>批量必失败；逐行时 FunctionName 为 "bad" 的那一行失败，其余落库。</summary>
    private sealed class PoisonedStore : IAuditStore
    {
        public List<AuditOperation> Saved { get; } = [];
        public int BatchAttempts { get; private set; }
        public Exception BatchFailure { get; init; } = new DbUpdateException("String or binary data would be truncated.");

        public Task SaveOperationAsync(AuditOperation operation)
        {
            if (operation.FunctionName == "bad")
            {
                throw new DbUpdateException("String or binary data would be truncated.");
            }

            Saved.Add(operation);
            return Task.CompletedTask;
        }

        public Task SaveOperationBatchAsync(IEnumerable<AuditOperation> operations)
        {
            BatchAttempts++;
            throw BatchFailure;
        }

        public Task SaveEntityEntriesAsync(IEnumerable<AuditEntityEntry> entries) => Task.CompletedTask;

        public Task<int> DeleteExpiredAsync(int days, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private static AuditOperation Operation(string functionName) => new()
    {
        FunctionName = functionName,
        StartTime = DateTime.UtcNow,
        CreationTime = DateTime.UtcNow
    };

    private static (Probe Service, IServiceProvider Root) CreateService(PoisonedStore store)
    {
        var services = new ServiceCollection();
        services.AddScoped<IAuditStore>(_ => store);
        var root = services.BuildServiceProvider();

        var options = new StaticOptionsMonitor<AuditOptions>(new AuditOptions());
        var service = new Probe(root, new AuditSender(options, NullLogger<AuditSender>.Instance), options);
        return (service, root);
    }

    [Fact]
    public async Task WhenTheBatchInsertIsRejected_TheOtherRowsStillLand_AndOnlyTheBadRowIsDropped()
    {
        var store = new PoisonedStore();
        var (service, root) = CreateService(store);
        var batch = new List<AuditOperation> { Operation("good-1"), Operation("bad"), Operation("good-2") };

        using var scope = root.CreateScope();
        await service.RunAsync(batch, scope.ServiceProvider);

        store.BatchAttempts.ShouldBe(1);
        store.Saved.Select(o => o.FunctionName).ShouldBe(["good-1", "good-2"]);
    }

    [Fact]
    public async Task WhenTheFailureIsNotARowLevelRejection_TheBatchIsHandedBackToTheBaseClass()
    {
        // 连接断了这类故障逐行重试只会把一次失败放大成一百次；交还基类记日志、退避、丢弃。
        var store = new PoisonedStore { BatchFailure = new InvalidOperationException("connection refused") };
        var (service, root) = CreateService(store);
        var batch = new List<AuditOperation> { Operation("good-1"), Operation("good-2") };

        using var scope = root.CreateScope();
        await Should.ThrowAsync<InvalidOperationException>(() => service.RunAsync(batch, scope.ServiceProvider));

        store.Saved.ShouldBeEmpty();
    }
}
