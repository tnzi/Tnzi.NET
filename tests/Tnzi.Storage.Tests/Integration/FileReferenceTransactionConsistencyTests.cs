using Microsoft.Extensions.DependencyInjection;
using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.Security.Claims;
using Tnzi.Storage;
using Tnzi.TestBase;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 带 [FileField] 的测试实体，用于触发 DbContext 的文件引用收集与处理链路。
/// </summary>
public class TxTestDoc : FullAuditedEntity<Guid>
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? CoverImageId { get; set; }
}

/// <summary>
/// 最小 DbContext，承载 <see cref="TxTestDoc"/> 以驱动 TnziDbContextHelper 的文件引用处理。
/// </summary>
public class TxConsistencyDbContext : TnziDbContext<TxConsistencyDbContext>
{
    public TxConsistencyDbContext(DbContextOptions<TxConsistencyDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<TxTestDoc> Docs => Set<TxTestDoc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TxTestDoc>(b =>
        {
            b.ToTable("TxTestDoc");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200);
        });

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 模拟引用处理失败的处理器：ProcessChangesAsync 总是抛异常。
/// </summary>
public class ThrowingFileReferenceProcessor : IFileReferenceProcessor
{
    public bool PublishCalled { get; private set; }

    public Task ProcessChangesAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("simulated reference failure");

    public Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        PublishCalled = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// 验证文件引用处理失败不再被静默吞掉（事务一致性 P0 修复）。
/// </summary>
public class FileReferenceTransactionConsistencyTests : IntegratedTestBase<TxConsistencyDbContext>
{
    private ThrowingFileReferenceProcessor _processor = null!;

    protected override void ConfigureServices(IServiceCollection services)
    {
        _processor = new ThrowingFileReferenceProcessor();
        services.AddScoped<IFileReferenceProcessor>(_ => _processor);
    }

    [Fact]
    public async Task SaveChanges_WhenReferenceProcessingFails_PropagatesException()
    {
        // 带 [FileField] 值的实体新增 → 触发引用处理 → processor 抛异常
        var doc = new TxTestDoc { Title = "doc", CoverImageId = Guid.NewGuid() };
        DbContext.Docs.Add(doc);

        // 修复前：异常被 TnziDbContextHelper 的 catch 吞掉，SaveChanges 静默成功
        // 修复后：异常向上传播，使外层事务/调用方得以回滚主业务
        await Assert.ThrowsAsync<InvalidOperationException>(() => DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_WhenNoFileFieldChange_DoesNotInvokeProcessor()
    {
        // 文件字段为 null → 无引用变更 → processor 不应被调用 → 不抛异常
        var doc = new TxTestDoc { Title = "no-file" };
        DbContext.Docs.Add(doc);

        await DbContext.SaveChangesAsync();
    }
}
