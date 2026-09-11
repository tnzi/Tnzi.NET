using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.EventBus;
using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 带 [FileField] 的测试实体：驱动 <c>TnziDbContextHelper</c> 的文件引用处理链路；
/// 领域事件派发链路由基类的 <c>AddDomainEvent</c> 驱动。
/// </summary>
public class FinalCommitDoc : FullAuditedEntity<Guid>
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? CoverImageId { get; set; }
}

/// <summary>
/// 测试用领域事件。
/// </summary>
public class FinalCommitDocCreatedEvent : EventBase
{
}

/// <summary>
/// 承载 <see cref="FinalCommitDoc"/> 的最小 DbContext。
/// </summary>
public class FinalCommitDbContext : TnziDbContext<FinalCommitDbContext>
{
    public FinalCommitDbContext(DbContextOptions<FinalCommitDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FinalCommitDoc> Docs => Set<FinalCommitDoc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FinalCommitDoc>(b =>
        {
            b.ToTable("FinalCommitDoc");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200);
        });

        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// 观测文件引用处理发生的那一刻是否存在物理事务；<see cref="ThrowOnProcess"/> 为真时抛异常，
/// 模拟「主实体已保存、引用处理失败」这一 <c>TnziDbContextHelper</c> 承诺要整体回滚的场景。
/// </summary>
public class ObservingFileReferenceProcessor : IFileReferenceProcessor
{
    private readonly Func<DbContext> _dbContextAccessor;

    public ObservingFileReferenceProcessor(Func<DbContext> dbContextAccessor)
    {
        _dbContextAccessor = dbContextAccessor;
    }

    public bool ThrowOnProcess { get; set; }

    public bool ProcessCalled { get; private set; }

    public bool HadPhysicalTransaction { get; private set; }

    public Task ProcessChangesAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        ProcessCalled = true;
        HadPhysicalTransaction = _dbContextAccessor().Database.CurrentTransaction != null;

        if (ThrowOnProcess)
        {
            throw new InvalidOperationException("simulated reference failure");
        }

        return Task.CompletedTask;
    }

    public Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// 记录每一次 SaveChanges 发生时是否存在物理事务（与文件字段无关的通用观测点）。
/// </summary>
public class TransactionObservingInterceptor : SaveChangesInterceptor
{
    public List<bool> SaveHadPhysicalTransaction { get; } = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        SaveHadPhysicalTransaction.Add(eventData.Context?.Database.CurrentTransaction != null);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
