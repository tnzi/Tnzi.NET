namespace Tnzi.AI.Tests.Workflow;

public class TransformNodeTests
{
    private readonly TransformNode _node;

    public TransformNodeTests()
    {
        _node = new TransformNode(
            Mock.Of<ILogger<TransformNode>>());
    }

    #region Template Transform

    [Fact]
    public async Task ExecuteAsync_TemplateWithVariables_ResolvesFromState()
    {
        var state = new WorkflowState("initial");
        state.SetOutput("step1", "Hello World");

        var context = CreateContext(state, new Dictionary<string, string>
        {
            ["transformType"] = "template",
            ["template"] = "Result: {{step1}}"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Result: Hello World");
        result.Output.Metadata.ShouldContainKeyAndValue("transform_type", "template");
    }

    [Fact]
    public async Task ExecuteAsync_TemplateWithMissingVariable_KeepsPlaceholder()
    {
        var state = new WorkflowState("initial");

        var context = CreateContext(state, new Dictionary<string, string>
        {
            ["transformType"] = "template",
            ["template"] = "Value: {{nonexistent}}"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Value: {{nonexistent}}");
    }

    [Fact]
    public async Task ExecuteAsync_TemplateWithInputVariable_ResolvesInitialInput()
    {
        var state = new WorkflowState("my input text");

        var context = CreateContext(state, new Dictionary<string, string>
        {
            ["transformType"] = "template",
            ["template"] = "Input was: {{input}}"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("Input was: my input text");
    }

    #endregion

    #region JSON Extract

    [Fact]
    public async Task ExecuteAsync_JsonExtract_ExtractsNestedField()
    {
        var jsonInput = """{"result":{"summary":"test summary"}}""";

        var context = CreateContextWithInput(jsonInput, new Dictionary<string, string>
        {
            ["transformType"] = "json_extract",
            ["jsonPath"] = "result.summary"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("test summary");
    }

    [Fact]
    public async Task ExecuteAsync_JsonExtract_MissingPath_ReturnsEmpty()
    {
        var jsonInput = """{"result":{"summary":"test"}}""";

        var context = CreateContextWithInput(jsonInput, new Dictionary<string, string>
        {
            ["transformType"] = "json_extract",
            ["jsonPath"] = "result.nonexistent"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_JsonExtract_NoJsonPath_ReturnsInput()
    {
        var jsonInput = """{"key":"value"}""";

        var context = CreateContextWithInput(jsonInput, new Dictionary<string, string>
        {
            ["transformType"] = "json_extract"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe(jsonInput);
    }

    [Fact]
    public async Task ExecuteAsync_JsonExtract_InvalidJson_ReturnsError()
    {
        var context = CreateContextWithInput("not json", new Dictionary<string, string>
        {
            ["transformType"] = "json_extract",
            ["jsonPath"] = "key"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region Text Split

    [Fact]
    public async Task ExecuteAsync_TextSplit_SplitsAndReturnsJsonArray()
    {
        var context = CreateContextWithInput("line1\nline2\nline3", new Dictionary<string, string>
        {
            ["transformType"] = "text_split"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        var items = JsonSerializer.Deserialize<string[]>(result.Output.Text);
        items.ShouldNotBeNull();
        items.Length.ShouldBe(3);
        items[0].ShouldBe("line1");
    }

    [Fact]
    public async Task ExecuteAsync_TextSplit_CustomDelimiter()
    {
        var context = CreateContextWithInput("a|b|c", new Dictionary<string, string>
        {
            ["transformType"] = "text_split",
            ["delimiter"] = "|"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        var items = JsonSerializer.Deserialize<string[]>(result.Output.Text);
        items.ShouldNotBeNull();
        items.Length.ShouldBe(3);
    }

    #endregion

    #region Merge

    [Fact]
    public async Task ExecuteAsync_Merge_CombinesUpstreamOutputs()
    {
        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["step1"] = new WorkflowStepOutput { Text = "First output" },
            ["step2"] = new WorkflowStepOutput { Text = "Second output" }
        };

        var context = CreateContext(
            new WorkflowState("initial"),
            new Dictionary<string, string> { ["transformType"] = "merge" },
            depOutputs);

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldContain("First output");
        result.Output.Text.ShouldContain("Second output");
    }

    [Fact]
    public async Task ExecuteAsync_Merge_NoDependencies_ReturnsInitialInput()
    {
        var context = CreateContext(
            new WorkflowState("initial input"),
            new Dictionary<string, string> { ["transformType"] = "merge" });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("initial input");
    }

    #endregion

    #region Other Transforms

    [Fact]
    public async Task ExecuteAsync_Uppercase_ConvertsToUpperCase()
    {
        var context = CreateContextWithInput("hello world", new Dictionary<string, string>
        {
            ["transformType"] = "uppercase"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("HELLO WORLD");
    }

    [Fact]
    public async Task ExecuteAsync_Lowercase_ConvertsToLowerCase()
    {
        var context = CreateContextWithInput("HELLO WORLD", new Dictionary<string, string>
        {
            ["transformType"] = "lowercase"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("hello world");
    }

    [Fact]
    public async Task ExecuteAsync_RegexExtract_ExtractsCaptureGroup()
    {
        var context = CreateContextWithInput("The price is $42.50 today", new Dictionary<string, string>
        {
            ["transformType"] = "regex_extract",
            ["pattern"] = @"\$(\d+\.\d+)"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("42.50");
    }

    [Fact]
    public async Task ExecuteAsync_RegexExtract_NoMatch_ReturnsEmpty()
    {
        var context = CreateContextWithInput("no numbers here", new Dictionary<string, string>
        {
            ["transformType"] = "regex_extract",
            ["pattern"] = @"\d+"
        });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultTransformType_IsMerge()
    {
        // 不指定 transformType 时默认为 merge
        var context = CreateContext(
            new WorkflowState("default input"),
            new Dictionary<string, string>());

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("default input");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyInput_NoDependencies_UsesInitialInput()
    {
        var context = CreateContext(
            new WorkflowState("initial"),
            new Dictionary<string, string> { ["transformType"] = "uppercase" });

        var result = await _node.ExecuteAsync(context);

        result.IsSuccess.ShouldBeTrue();
        result.Output.Text.ShouldBe("INITIAL");
    }

    #endregion

    #region Helpers

    private static WorkflowNodeContext CreateContext(
        WorkflowState state,
        Dictionary<string, string> config,
        Dictionary<string, WorkflowStepOutput>? dependencyOutputs = null)
    {
        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "transform-test",
                Configuration = config
            },
            State = state,
            DependencyOutputs = dependencyOutputs ?? new Dictionary<string, WorkflowStepOutput>(),
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }

    private static WorkflowNodeContext CreateContextWithInput(
        string inputText,
        Dictionary<string, string> config)
    {
        var depOutputs = new Dictionary<string, WorkflowStepOutput>
        {
            ["upstream"] = new WorkflowStepOutput { Text = inputText }
        };

        return new WorkflowNodeContext
        {
            Step = new WorkflowStepDto
            {
                StepId = "transform-test",
                Configuration = config
            },
            State = new WorkflowState("initial"),
            DependencyOutputs = depOutputs,
            ServiceProvider = Mock.Of<IServiceProvider>()
        };
    }

    #endregion
}
