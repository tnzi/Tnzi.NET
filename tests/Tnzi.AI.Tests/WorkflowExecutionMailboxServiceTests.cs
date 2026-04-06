namespace Tnzi.AI.Tests;

[ExperimentalApi(Reason = "Workflow mailbox and signals are in preview")]
public class WorkflowExecutionMailboxServiceTests
{
    [Fact]
    public async Task EnqueueAndAcknowledgeSignal_UpdatesPendingSignalCount()
    {
        var entity = new WorkflowExecution
        {
            ExecutionId = "wf-mailbox-1",
            PendingSignalsJson = "[]"
        };

        var repository = new Mock<IRepository<WorkflowExecution, Guid>>();
        repository.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Expression<Func<WorkflowExecution, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repository.Setup(x => x.UpdateAsync(It.IsAny<WorkflowExecution>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mailbox = new WorkflowExecutionMailboxService(repository.Object);

        await mailbox.EnqueueSignalAsync(entity.ExecutionId, new WorkflowExecutionSignal
        {
            SignalId = "sig-1",
            Type = WorkflowExecutionSignalTypes.Cancel
        });

        entity.PendingSignalCount.ShouldBe(1);
        var pending = await mailbox.GetPendingSignalsAsync(entity.ExecutionId);
        pending.Count.ShouldBe(1);
        pending[0].SignalId.ShouldBe("sig-1");

        await mailbox.AcknowledgeSignalsAsync(entity.ExecutionId, ["sig-1"]);

        entity.PendingSignalCount.ShouldBe(0);
        (await mailbox.GetPendingSignalsAsync(entity.ExecutionId)).ShouldBeEmpty();
    }
}
