namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// ApprovalNode 单元测试 — 覆盖 NodeType、AwaitingApproval 标志、上游输出传递、元数据包含审批状态
/// </summary>
public class ApprovalNodeTests
{
    private readonly ApprovalNode _node;

    public ApprovalNodeTests()
    {
        _node = new ApprovalNode(
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<ApprovalNode>>());
    }

    [Fact]
    public void NodeType_ReturnsApproval()
    {
        _node.NodeType.ShouldBe(WorkflowNodeTypes.Approval);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsAwaitingApprovalTrue()
    {
        // Arrange
        var context = CreateContext();

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.AwaitingApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PassesUpstreamOutputInResult()
    {
        // Arrange
        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["review-step"] = new WorkflowStepOutput
            {
                Text = "Review completed: looks good",
                Metadata = new Dictionary<string, string> { ["verdict"] = "accept" }
            }
        };

        var context = CreateContext(dependencyOutputs: depOutputs);

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldContain("Review completed: looks good");
    }

    [Fact]
    public async Task ExecuteAsync_MetadataContainsApprovalStatus()
    {
        // Arrange
        var context = CreateContext(stepId: "approval-step-1");

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.Output.Metadata.ShouldNotBeNull();
        result.Output.Metadata.ShouldContainKeyAndValue("status", "awaiting_approval");
        result.Output.Metadata.ShouldContainKeyAndValue("step_id", "approval-step-1");
    }

    #region Helpers

    private static WorkflowNodeContext CreateContext(
        string? stepId = null,
        Dictionary<string, WorkflowStepOutput>? dependencyOutputs = null)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = stepId ?? "approval-test"
            },
            State = new WorkflowState("initial input"),
            DependencyOutputs = dependencyOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }

    #endregion
}
