namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Batch delete is the admin page's only delete (single rows go through it too), so it must
/// behave like <c>DeleteAsync</c>: a soft delete that keeps the row and its version history.
/// </summary>
public class WorkflowServiceBatchDeleteTests : WorkflowServiceSqliteTestBase
{
    [Fact]
    public async Task BatchDeleteAsync_SoftDeletes_AndKeepsTheVersionHistory()
    {
        var definition = await InsertDefinitionAsync("[]", WorkflowExecutionMode.Sequential, name: "to-delete");
        var service = CreateService();

        // An update snapshots the previous definition as a version.
        (await service.UpdateAsync(definition.Id, new UpdateWorkflowDefinitionDto { Description = "edited" })).Succeeded.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<WorkflowDefinitionVersion>().CountAsync(v => v.WorkflowDefinitionId == definition.Id)).ShouldBe(1);

        var deleted = await service.BatchDeleteAsync([definition.Id]);

        deleted.Succeeded.ShouldBeTrue(deleted.Message);
        deleted.Data.ShouldBe(1);

        DbContext.ChangeTracker.Clear();
        var row = await DbContext.Set<WorkflowDefinition>().IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == definition.Id);
        row.ShouldNotBeNull("batch delete must not remove the definition for good");
        row!.IsDeleted.ShouldBeTrue();
        row.DeleterId.ShouldBe(TestHelper.DefaultTestUserId);
        row.DeletionTime.ShouldNotBeNull();

        (await DbContext.Set<WorkflowDefinitionVersion>().CountAsync(v => v.WorkflowDefinitionId == definition.Id))
            .ShouldBe(1, "the version history survives a soft delete");
        (await service.GetByIdAsync(definition.Id)).Code.ShouldBe(404);
    }
}
