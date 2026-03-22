namespace Tnzi.AI.Tests.Workflow;

public class ConditionalNodeTests
{
    private readonly ConditionalNode _node;

    public ConditionalNodeTests()
    {
        _node = new ConditionalNode(
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<ConditionalNode>>());
    }

    [Fact]
    public async Task ExecuteAsync_ConditionResolvesToSimpleValue_RoutesToLowercaseValue()
    {
        // Arrange
        var context = CreateContext(
            condition: "Accept",
            dependencyOutputs: new Dictionary<string, WorkflowStepOutput>());

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept");
        result.Output.Metadata.ShouldContainKeyAndValue("route", "accept");
    }

    [Fact]
    public async Task ExecuteAsync_ConditionIsTrue_RoutesToTrue()
    {
        var context = CreateContext(condition: "true");

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("true");
    }

    [Fact]
    public async Task ExecuteAsync_ConditionIsFalse_RoutesToFalse()
    {
        var context = CreateContext(condition: "false");

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("false");
    }

    [Fact]
    public async Task ExecuteAsync_ConditionIsNo_RoutesToNo()
    {
        // "no" 是单词，先匹配简单值路径（返回小写）
        var context = CreateContext(condition: "no");

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("no");
    }

    [Fact]
    public async Task ExecuteAsync_NoConditionConfigured_RoutesToDefault()
    {
        var context = CreateContext(condition: null);

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("default");
        result.Output.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyCondition_RoutesToDefault()
    {
        var context = CreateContext(condition: "  ");

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("default");
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamHasVerdict_UsesVerdictAsRoute()
    {
        // Arrange: 上游 ReviewNode 输出了 verdict metadata
        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["review-step"] = new WorkflowStepOutput
            {
                Text = "Looks good",
                Metadata = new Dictionary<string, string> { ["verdict"] = "accept" }
            }
        };
        var context = CreateContext(condition: "{{review-step}}", dependencyOutputs: depOutputs);

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("accept");
    }

    [Fact]
    public async Task ExecuteAsync_UpstreamHasRouteMetadata_UsesRouteAsRoute()
    {
        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["router-step"] = new WorkflowStepOutput
            {
                Text = "some text",
                Metadata = new Dictionary<string, string> { ["route"] = "billing" }
            }
        };
        var context = CreateContext(condition: "check", dependencyOutputs: depOutputs);

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("billing");
    }

    [Fact]
    public async Task ExecuteAsync_ConditionWithTemplateVariable_ResolvesFromState()
    {
        // Arrange: state 中有 step1 的输出
        var state = new WorkflowState("initial input");
        state.SetOutput("step1", "approved");

        var step = new WorkflowStepDto
        {
            StepId = "cond-1",
            Configuration = new Dictionary<string, string> { ["condition"] = "{{step1}}" }
        };

        var context = new WorkflowNodeContext
        {
            Step = step,
            State = state,
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };

        // Act
        var result = await _node.ExecuteAsync(context);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("approved");
    }

    [Fact]
    public async Task ExecuteAsync_MultiWordCondition_RoutesToDefault()
    {
        // 条件包含空格的多词表达式，无法匹配为简单值或布尔
        var context = CreateContext(condition: "some complex expression");

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("default");
    }

    [Fact]
    public async Task ExecuteAsync_ConditionFromStepConditionProperty_WhenNoConfigCondition()
    {
        // 当 config 中没有 condition，从 step.Condition 读取
        var state = new WorkflowState("input");
        var step = new WorkflowStepDto
        {
            StepId = "cond-1",
            Condition = "yes",
            Configuration = new Dictionary<string, string>()
        };

        var context = new WorkflowNodeContext
        {
            Step = step,
            State = state,
            DependencyOutputs = new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.RouteTo.ShouldBe("yes"); // "yes" 是单词，匹配简单值路径（不经过布尔判断）
    }

    private static WorkflowNodeContext CreateContext(
        string? condition,
        Dictionary<string, WorkflowStepOutput>? dependencyOutputs = null)
    {
        var config = new Dictionary<string, string>();
        if (condition != null)
            config["condition"] = condition;

        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "cond-test",
                Configuration = config
            },
            State = new WorkflowState("test input"),
            DependencyOutputs = dependencyOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }
}
