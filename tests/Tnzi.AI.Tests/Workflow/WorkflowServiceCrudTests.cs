namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// WorkflowService CRUD and new feature tests
/// </summary>
public class WorkflowServiceCrudTests
{
    [Fact]
    public void WorkflowValidationResultDto_DefaultValues()
    {
        var result = new WorkflowValidationResultDto();
        result.IsValid.ShouldBeFalse(); // Default bool is false; service sets to true
        result.Errors.ShouldBeEmpty();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void WorkflowValidationResultDto_WhenSetValid()
    {
        var result = new WorkflowValidationResultDto { IsValid = true };
        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void WorkflowStatsDto_DefaultValues()
    {
        var stats = new WorkflowStatsDto();
        stats.TotalWorkflows.ShouldBe(0);
        stats.EnabledWorkflows.ShouldBe(0);
        stats.DisabledWorkflows.ShouldBe(0);
        stats.ByExecutionMode.ShouldBeEmpty();
        stats.TotalExecutions.ShouldBe(0);
    }

    [Fact]
    public void WorkflowExecutionSummaryDto_Properties()
    {
        var dto = new WorkflowExecutionSummaryDto
        {
            Id = Guid.NewGuid(),
            ExecutionId = "exec-123",
            WorkflowDefinitionId = Guid.NewGuid(),
            Status = WorkflowExecutionStatus.Completed,
            CompletedStepCount = 3,
            AwaitingApprovalCount = 0,
            CompletedTime = DateTime.UtcNow,
            UpdatedTime = DateTime.UtcNow
        };

        dto.ExecutionId.ShouldBe("exec-123");
        dto.Status.ShouldBe(WorkflowExecutionStatus.Completed);
        dto.CompletedStepCount.ShouldBe(3);
    }

    [Fact]
    public void WorkflowExecutionDetailDto_InheritsFromSummary()
    {
        var dto = new WorkflowExecutionDetailDto
        {
            ExecutionId = "exec-456",
            InitialInput = "test input",
            CompletedStepIds = ["step-1", "step-2"],
            StepsAwaitingApproval = [],
            StepOutputs = new() { ["step-1"] = "output 1", ["step-2"] = "output 2" }
        };

        dto.InitialInput.ShouldBe("test input");
        dto.CompletedStepIds.Count.ShouldBe(2);
        dto.StepOutputs.Count.ShouldBe(2);
    }

    [Fact]
    public void WorkflowExecutionQueryDto_DefaultValues()
    {
        var query = new WorkflowExecutionQueryDto();
        query.PageSize.ShouldBe(20);
        query.WorkflowDefinitionId.ShouldBeNull();
        query.Status.ShouldBeNull();
    }

    [Fact]
    public void WorkflowDefinitionQueryDto_DefaultValues()
    {
        var query = new WorkflowDefinitionQueryDto();
        query.Keyword.ShouldBeNull();
        query.IsEnabled.ShouldBeNull();
        query.ExecutionMode.ShouldBeNull();
    }

    [Fact]
    public void WorkflowStepDto_DefaultRetryValues()
    {
        var step = new WorkflowStepDto();
        step.MaxRetries.ShouldBe(0);
        step.RetryDelaySeconds.ShouldBe(2);
        step.RequiresApproval.ShouldBeFalse();
    }

    [Fact]
    public void WorkflowDefinitionDto_DefaultValues()
    {
        var dto = new WorkflowDefinitionDto();
        dto.Name.ShouldBe(string.Empty);
        dto.Steps.ShouldNotBeNull();
        dto.ExecutionMode.ShouldBe(WorkflowExecutionMode.Sequential);
    }

    [Fact]
    public void CreateWorkflowDefinitionDto_Defaults()
    {
        var dto = new CreateWorkflowDefinitionDto();
        dto.ExecutionMode.ShouldBe(WorkflowExecutionMode.Sequential);
        dto.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void WorkflowExecutionStatusDto_Properties()
    {
        var dto = new WorkflowExecutionStatusDto
        {
            ExecutionId = "test-exec",
            Status = "Paused",
            CompletedStepIds = ["step-1"],
            StepsAwaitingApproval = ["step-2"]
        };

        dto.ExecutionId.ShouldBe("test-exec");
        dto.CompletedStepIds.Count.ShouldBe(1);
        dto.StepsAwaitingApproval.Count.ShouldBe(1);
    }
}
