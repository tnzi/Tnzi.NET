using Tnzi.Audit.Tests.TestSupport;


namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// DatabaseAuditStore 集成测试
/// </summary>
public class DatabaseAuditStoreIntegrationTests : IntegrationTestBase
{
    private readonly IAuditStore _store;

    public DatabaseAuditStoreIntegrationTests()
    {
        _store = ServiceProvider.GetRequiredService<IAuditStore>();
    }

    #region SaveOperationAsync Tests

    [Fact]
    public async Task SaveOperationAsync_Should_Insert_Operation()
    {
        // Arrange
        var operation = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "Test.Action",
            ResultType = AuditResultType.Success,
            CreationTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            Elapsed = 100
        };

        // Act
        await _store.SaveOperationAsync(operation);

        // Assert
        var saved = await DbContext.AuditOperations.FindAsync(operation.Id);
        saved.ShouldNotBeNull();
        saved.FunctionName.ShouldBe("Test.Action");
    }

    #endregion

    #region SaveOperationBatchAsync Tests

    [Fact]
    public async Task SaveOperationBatchAsync_Should_Insert_Multiple_Operations()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "Action1",
                ResultType = AuditResultType.Success,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "Action2",
                ResultType = AuditResultType.Failed,
                CreationTime = DateTime.UtcNow,
                StartTime = DateTime.UtcNow,
                Elapsed = 200
            }
        };

        // Act
        await _store.SaveOperationBatchAsync(operations);

        // Assert
        var count = await DbContext.AuditOperations.CountAsync();
        count.ShouldBe(2);
    }

    [Fact]
    public async Task SaveOperationBatchAsync_Should_Not_Insert_When_Empty()
    {
        // Act
        await _store.SaveOperationBatchAsync(new List<AuditOperation>());

        // Assert
        var count = await DbContext.AuditOperations.CountAsync();
        count.ShouldBe(0);
    }

    #endregion

    #region SaveEntityEntriesAsync Tests

    [Fact]
    public async Task SaveEntityEntriesAsync_Should_Insert_Entries()
    {
        // Arrange - 先创建父级 Operation
        var operation = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "Test.Action",
            ResultType = AuditResultType.Success,
            CreationTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            Elapsed = 100
        };
        await DbContext.AuditOperations.AddAsync(operation);
        await DbContext.SaveChangesAsync();

        var entries = new List<AuditEntityEntry>
        {
            new AuditEntityEntry
            {
                Id = Guid.NewGuid(),
                AuditOperationId = operation.Id,
                EntityTypeName = "User",
                EntityTypeFullName = "Tnzi.Domain.User",
                EntityId = "123",
                OperationType = Tnzi.Audit.Metadata.EntityState.Modified,
                CreationTime = DateTime.UtcNow
            },
            new AuditEntityEntry
            {
                Id = Guid.NewGuid(),
                AuditOperationId = operation.Id,
                EntityTypeName = "Product",
                EntityTypeFullName = "Tnzi.Domain.Product",
                EntityId = "456",
                OperationType = Tnzi.Audit.Metadata.EntityState.Added,
                CreationTime = DateTime.UtcNow
            }
        };

        // Act
        await _store.SaveEntityEntriesAsync(entries);

        // Assert
        var count = await DbContext.AuditEntityEntries.CountAsync();
        count.ShouldBe(2);
    }

    #endregion

    #region DeleteExpiredAsync Tests

    [Fact]
    public async Task DeleteExpiredAsync_Should_Delete_Old_Operations()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var operations = new List<AuditOperation>
        {
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "OldAction",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-100),
                StartTime = now.AddDays(-100),
                Elapsed = 100
            },
            new AuditOperation
            {
                Id = Guid.NewGuid(),
                FunctionName = "RecentAction",
                ResultType = AuditResultType.Success,
                CreationTime = now.AddDays(-30),
                StartTime = now.AddDays(-30),
                Elapsed = 100
            }
        };

        await DbContext.AuditOperations.AddRangeAsync(operations);
        await DbContext.SaveChangesAsync();

        // Act
        var deletedCount = await _store.DeleteExpiredAsync(days: 90);

        // Assert
        deletedCount.ShouldBe(1);
        DbContext.AuditOperations.Count().ShouldBe(1);
        DbContext.AuditOperations.First().FunctionName.ShouldBe("RecentAction");
    }

    [Fact]
    public async Task DeleteExpiredAsync_Should_Return_Zero_When_Nothing_Expired()
    {
        // Arrange
        var operation = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "RecentAction",
            ResultType = AuditResultType.Success,
            CreationTime = DateTime.UtcNow,
            StartTime = DateTime.UtcNow,
            Elapsed = 100
        };

        await DbContext.AuditOperations.AddAsync(operation);
        await DbContext.SaveChangesAsync();

        // Act
        var deletedCount = await _store.DeleteExpiredAsync(days: 90);

        // Assert
        deletedCount.ShouldBe(0);
        DbContext.AuditOperations.Count().ShouldBe(1);
    }

    [Fact]
    public async Task DeleteExpiredAsync_DeletesInPrimaryKeyBatchesOfBatchSize_AndCascadesTheEntries()
    {
        // 一条谓词 DELETE 在积压一年的表上撞命令超时、整条回滚、每天重来一次；分批让每条语句都短。
        var now = DateTime.UtcNow;
        var expired = Enumerable.Range(0, 5).Select(i => new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = $"Old{i}",
            ResultType = AuditResultType.Success,
            CreationTime = now.AddDays(-200 + i),
            StartTime = now.AddDays(-200 + i),
            Elapsed = 1,
            EntityEntries = [new AuditEntityEntry { Id = Guid.NewGuid(), EntityTypeName = "E" }]
        }).ToList();
        var recent = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "Recent",
            ResultType = AuditResultType.Success,
            CreationTime = now,
            StartTime = now,
            Elapsed = 1
        };
        await DbContext.AuditOperations.AddRangeAsync([.. expired, recent]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var repository = new CountingDeleteRepository(DbContext, ServiceProvider);
        var store = new DatabaseAuditStore(
            repository,
            ServiceProvider.GetRequiredService<IRepository<AuditEntityEntry, Guid>>(),
            new StaticOptionsMonitor<AuditOptions>(new AuditOptions { BatchSize = 2 }));

        var deleted = await store.DeleteExpiredAsync(days: 90);

        deleted.ShouldBe(5);
        repository.PredicateDeletes.ShouldBe(3); // 2 + 2 + 1
        DbContext.ChangeTracker.Clear();
        DbContext.AuditOperations.Select(o => o.FunctionName).ToList().ShouldBe(["Recent"]);
        DbContext.AuditEntityEntries.Count().ShouldBe(0);
    }

    [Fact]
    public async Task DeleteExpiredAsync_JudgesExpiryByTheIndexedStartTime()
    {
        // CreationTime 没有索引，按它判过期每一批都是全表扫描；StartTime 有索引，两者只差采集队列的几秒。
        var now = DateTime.UtcNow;
        await DbContext.AuditOperations.AddAsync(new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "StartedLongAgo",
            ResultType = AuditResultType.Success,
            CreationTime = now,
            StartTime = now.AddDays(-100),
            Elapsed = 1
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var deleted = await _store.DeleteExpiredAsync(days: 90);

        deleted.ShouldBe(1);
        DbContext.AuditOperations.Count().ShouldBe(0);
    }

    [Fact]
    public async Task DeleteExpiredAsync_HonoursCancellation()
    {
        await DbContext.AuditOperations.AddAsync(new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "Old",
            ResultType = AuditResultType.Success,
            CreationTime = DateTime.UtcNow.AddDays(-100),
            StartTime = DateTime.UtcNow.AddDays(-100),
            Elapsed = 1
        });
        await DbContext.SaveChangesAsync();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => _store.DeleteExpiredAsync(90, cancelled.Token));
        DbContext.ChangeTracker.Clear();
        DbContext.AuditOperations.Count().ShouldBe(1);
    }

    private sealed class CountingDeleteRepository(AuditTestDbContext dbContext, IServiceProvider serviceProvider)
        : EFCoreRepository<AuditTestDbContext, AuditOperation, Guid>(dbContext, serviceProvider: serviceProvider)
    {
        public int PredicateDeletes { get; private set; }

        public override Task DeleteAsync(System.Linq.Expressions.Expression<Func<AuditOperation, bool>> predicate, CancellationToken cancellationToken = default)
        {
            PredicateDeletes++;
            return base.DeleteAsync(predicate, cancellationToken);
        }
    }

    #endregion
}
