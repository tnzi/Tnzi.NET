
namespace Tnzi.Audit.Tests.Services;

/// <summary>
/// DatabaseAuditStore 单元测试
/// </summary>
public class DatabaseAuditStoreTests
{
    private readonly Mock<IRepository<AuditOperation, Guid>> _operationRepositoryMock;
    private readonly Mock<IRepository<AuditEntityEntry, Guid>> _entityEntryRepositoryMock;
    private readonly DatabaseAuditStore _store;

    public DatabaseAuditStoreTests()
    {
        _operationRepositoryMock = new Mock<IRepository<AuditOperation, Guid>>();
        _entityEntryRepositoryMock = new Mock<IRepository<AuditEntityEntry, Guid>>();

        _store = new DatabaseAuditStore(
            _operationRepositoryMock.Object,
            _entityEntryRepositoryMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_Should_Throw_When_OperationRepository_Is_Null()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new DatabaseAuditStore(
            null!,
            _entityEntryRepositoryMock.Object));
    }

    [Fact]
    public void Constructor_Should_Throw_When_EntityEntryRepository_Is_Null()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new DatabaseAuditStore(
            _operationRepositoryMock.Object,
            null!));
    }

    #endregion

    #region SaveOperationAsync Tests

    [Fact]
    public async Task SaveOperationAsync_Should_Insert_Operation()
    {
        // Arrange
        var operation = new AuditOperation
        {
            Id = Guid.NewGuid(),
            FunctionName = "TestFunction"
        };

        _operationRepositoryMock.Setup(r => r.InsertAsync(operation, default))
            .Returns(Task.CompletedTask);

        // Act
        await _store.SaveOperationAsync(operation);

        // Assert
        _operationRepositoryMock.Verify(r => r.InsertAsync(operation, default), Times.Once);
    }

    #endregion

    #region SaveOperationBatchAsync Tests

    [Fact]
    public async Task SaveOperationBatchAsync_Should_Insert_Multiple_Operations()
    {
        // Arrange
        var operations = new List<AuditOperation>
        {
            new AuditOperation { Id = Guid.NewGuid(), FunctionName = "Function1" },
            new AuditOperation { Id = Guid.NewGuid(), FunctionName = "Function2" }
        };

        _operationRepositoryMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditOperation>>(), default))
            .Returns(Task.CompletedTask);

        // Act
        await _store.SaveOperationBatchAsync(operations);

        // Assert
        _operationRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditOperation>>(), default), Times.Once);
    }

    [Fact]
    public async Task SaveOperationBatchAsync_Should_Not_Insert_When_Empty()
    {
        // Arrange
        var operations = new List<AuditOperation>();

        // Act
        await _store.SaveOperationBatchAsync(operations);

        // Assert
        _operationRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditOperation>>(), default), Times.Never);
    }

    #endregion

    #region SaveEntityEntriesAsync Tests

    [Fact]
    public async Task SaveEntityEntriesAsync_Should_Insert_Entries()
    {
        // Arrange
        var entries = new List<AuditEntityEntry>
        {
            new AuditEntityEntry { Id = Guid.NewGuid(), EntityTypeName = "Entity1" },
            new AuditEntityEntry { Id = Guid.NewGuid(), EntityTypeName = "Entity2" }
        };

        _entityEntryRepositoryMock.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditEntityEntry>>(), default))
            .Returns(Task.CompletedTask);

        // Act
        await _store.SaveEntityEntriesAsync(entries);

        // Assert
        _entityEntryRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditEntityEntry>>(), default), Times.Once);
    }

    [Fact]
    public async Task SaveEntityEntriesAsync_Should_Not_Insert_When_Empty()
    {
        // Arrange
        var entries = new List<AuditEntityEntry>();

        // Act
        await _store.SaveEntityEntriesAsync(entries);

        // Assert
        _entityEntryRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<AuditEntityEntry>>(), default), Times.Never);
    }

    #endregion

    #region DeleteExpiredAsync Tests

    [Fact(Skip = "Requires IQueryable mock which Moq doesn't support")]
    public async Task DeleteExpiredAsync_Should_Delete_Old_Operations()
    {
        // 此测试需要集成测试
    }

    #endregion
}
