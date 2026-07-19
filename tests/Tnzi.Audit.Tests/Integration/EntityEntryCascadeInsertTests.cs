namespace Tnzi.Audit.Tests.Integration;

/// <summary>
/// 验证带导航子集合的 AuditOperation 新实体图经 InsertMany 级联插入：
/// AuditOperation → AuditEntityEntry → AuditPropertyEntry 三级全部落库且外键正确。
/// 这是实体级审计管道的入库前提（AuditBackgroundService → IAuditStore.SaveOperationBatchAsync）。
/// </summary>
public class EntityEntryCascadeInsertTests : IntegrationTestBase
{
    [Fact]
    public async Task SaveOperationBatch_WithEntityEntryGraph_CascadesAllThreeLevels()
    {
        var store = ServiceProvider.GetRequiredService<Tnzi.Audit.Services.IAuditStore>();

        var operation = new AuditOperation
        {
            FunctionName = "Product.Update",
            HttpMethod = "PUT",
            StartTime = DateTime.UtcNow
        };
        var entityEntry = new AuditEntityEntry
        {
            EntityTypeName = "Product",
            EntityTypeFullName = "Test.Product",
            EntityId = Guid.NewGuid().ToString(),
            OperationType = Tnzi.Audit.Metadata.EntityState.Modified,
            CreationTime = DateTime.UtcNow
        };
        entityEntry.PropertyEntries.Add(new AuditPropertyEntry
        {
            PropertyName = "Name",
            PropertyTypeName = "String",
            OriginalValue = "Old",
            NewValue = "New",
            CreationTime = DateTime.UtcNow
        });
        entityEntry.PropertyEntries.Add(new AuditPropertyEntry
        {
            PropertyName = "Price",
            PropertyTypeName = "Decimal",
            OriginalValue = "10",
            NewValue = "12.5",
            CreationTime = DateTime.UtcNow
        });
        operation.EntityEntries.Add(entityEntry);

        await store.SaveOperationBatchAsync([operation]);

        // 清空跟踪器，从数据库重新读取，验证真实落库与外键
        DbContext.ChangeTracker.Clear();
        var saved = await DbContext.AuditOperations
            .Include(o => o.EntityEntries)
            .ThenInclude(e => e.PropertyEntries)
            .SingleAsync(o => o.FunctionName == "Product.Update");

        var savedEntry = saved.EntityEntries.ShouldHaveSingleItem();
        savedEntry.Id.ShouldNotBe(Guid.Empty);
        savedEntry.AuditOperationId.ShouldBe(saved.Id);
        savedEntry.PropertyEntries.Count.ShouldBe(2);
        savedEntry.PropertyEntries.ShouldAllBe(p => p.AuditEntityEntryId == savedEntry.Id);
        savedEntry.PropertyEntries.Select(p => p.PropertyName).ShouldBe(["Name", "Price"], ignoreOrder: true);
    }
}
