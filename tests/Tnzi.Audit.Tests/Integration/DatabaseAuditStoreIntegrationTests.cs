
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

    #endregion
}
