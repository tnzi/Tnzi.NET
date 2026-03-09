namespace Tnzi.AI.Tests;

/// <summary>
/// WorkflowBuilder fluent API 单元测试
/// </summary>
public class WorkflowBuilderTests
{
    [Fact]
    public void Build_SingleStep_ReturnsOneStep()
    {
        var agentId = Guid.NewGuid();
        var steps = WorkflowBuilder.Create()
            .AddStep("step1", agentId: agentId)
            .Build();

        steps.Count.ShouldBe(1);
        steps[0].StepId.ShouldBe("step1");
        steps[0].AgentId.ShouldBe(agentId);
        steps[0].Order.ShouldBe(0);
    }

    [Fact]
    public void Build_ChainedSteps_PreservesOrder()
    {
        var steps = WorkflowBuilder.Create()
            .AddStep("extract", agentId: Guid.NewGuid())
            .AddStep("summarize", agentId: Guid.NewGuid())
                .DependsOn("extract")
            .AddStep("review", agentId: Guid.NewGuid())
                .DependsOn("summarize")
            .Build();

        steps.Count.ShouldBe(3);
        steps[0].StepId.ShouldBe("extract");
        steps[1].StepId.ShouldBe("summarize");
        steps[1].DependsOn.ShouldNotBeNull();
        steps[1].DependsOn!.ShouldContain("extract");
        steps[2].StepId.ShouldBe("review");
        steps[2].DependsOn.ShouldNotBeNull();
        steps[2].DependsOn!.ShouldContain("summarize");
    }

    [Fact]
    public void Build_WithRetry_SetsRetryProperties()
    {
        var steps = WorkflowBuilder.Create()
            .AddStep("step1", agentId: Guid.NewGuid())
                .WithRetry(3, delaySeconds: 5)
            .Build();

        steps[0].MaxRetries.ShouldBe(3);
        steps[0].RetryDelaySeconds.ShouldBe(5);
    }

    [Fact]
    public void Build_WithTimeout_SetsTimeoutProperty()
    {
        var steps = WorkflowBuilder.Create()
            .AddStep("step1", agentId: Guid.NewGuid())
                .WithTimeout(120)
            .Build();

        steps[0].TimeoutSeconds.ShouldBe(120);
    }

    [Fact]
    public void Build_RequiresApproval_SetsFlag()
    {
        var steps = WorkflowBuilder.Create()
            .AddStep("step1", agentId: Guid.NewGuid())
                .RequiresApproval()
            .Build();

        steps[0].RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Build_WithAllOptions_SetsAllProperties()
    {
        var agentId = Guid.NewGuid();
        var steps = WorkflowBuilder.Create()
            .AddStep("step1", agentId: agentId)
                .WithInstructions("Do something with {{input}}")
                .WithProvider("openai")
                .WithModel("gpt-4o")
                .WithCondition("{{extract}} contains 'error'")
                .WithRetry(2, 3)
                .WithTimeout(60)
                .RequiresApproval()
            .Build();

        var step = steps[0];
        step.StepId.ShouldBe("step1");
        step.AgentId.ShouldBe(agentId);
        step.Instructions.ShouldBe("Do something with {{input}}");
        step.Provider.ShouldBe("openai");
        step.Model.ShouldBe("gpt-4o");
        step.Condition.ShouldBe("{{extract}} contains 'error'");
        step.MaxRetries.ShouldBe(2);
        step.RetryDelaySeconds.ShouldBe(3);
        step.TimeoutSeconds.ShouldBe(60);
        step.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Build_EmptyWorkflow_Throws()
    {
        Should.Throw<InvalidOperationException>(() => WorkflowBuilder.Create().Build());
    }

    [Fact]
    public void Build_DuplicateStepId_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowBuilder.Create()
                .AddStep("step1", agentId: Guid.NewGuid())
                .AddStep("step1", agentId: Guid.NewGuid())
                .Build());
    }

    [Fact]
    public void Build_UnknownDependency_Throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            WorkflowBuilder.Create()
                .AddStep("step1", agentId: Guid.NewGuid())
                    .DependsOn("nonexistent")
                .Build());
    }

    [Fact]
    public void Build_MultipleDependencies_AllPreserved()
    {
        var steps = WorkflowBuilder.Create()
            .AddStep("a", agentId: Guid.NewGuid())
            .AddStep("b", agentId: Guid.NewGuid())
            .AddStep("c", agentId: Guid.NewGuid())
                .DependsOn("a", "b")
            .Build();

        steps[2].DependsOn.ShouldBe(new[] { "a", "b" });
    }
}
