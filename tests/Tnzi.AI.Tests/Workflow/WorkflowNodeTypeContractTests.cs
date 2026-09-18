namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// The node kind is resolved from <c>Configuration["nodeType"]</c>. The visual editor wrote
/// <c>__nodeType</c> from 2026-05-18 to 2026-09-12, a key no backend commit ever read, so every
/// router / parallel / debate / ... step built in the editor ran as a plain agent call with a
/// <c>Completed</c> status. The editor now writes <c>nodeType</c>; this fixture covers the
/// backend half: legacy rows still run as their real kind without a re-save, and a
/// <c>nodeType</c> that matches no registered node fails at validate time, not at run time.
/// </summary>
public class WorkflowNodeTypeContractTests : WorkflowServiceSqliteTestBase
{
    private readonly List<string> _executedNodeTypes = [];

    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddSingleton(_executedNodeTypes);
        services.AddScoped<IWorkflowNode, RecordingNode>(sp => new RecordingNode(WorkflowNodeTypes.Agent, sp.GetRequiredService<List<string>>()));
        services.AddScoped<IWorkflowNode, RecordingNode>(sp => new RecordingNode("marker", sp.GetRequiredService<List<string>>()));
    }

    [Fact]
    public async Task RunAsync_LegacyEditorKey_ResolvesTheRealNodeType()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "__nodeType": "marker", "__x": "1", "__y": "2" } }
            ]
            """);

        var run = await CreateService().RunAsync(definition.Id, "start");

        run.Succeeded.ShouldBeTrue(run.Message);
        _executedNodeTypes.ShouldBe(["marker"], "a definition saved by the old editor must run as the kind the operator chose, not as an agent");
    }

    [Fact]
    public async Task RunAsync_ExplicitNodeTypeWins_OverLegacyKey()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "agent", "__nodeType": "marker" } }
            ]
            """);

        var run = await CreateService().RunAsync(definition.Id, "start");

        run.Succeeded.ShouldBeTrue(run.Message);
        _executedNodeTypes.ShouldBe([WorkflowNodeTypes.Agent]);
    }

    [Fact]
    public async Task ValidateAsync_UnknownNodeType_IsAnError()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "no-such-node" } },
              { "stepId": "b", "dependsOn": ["a"], "configuration": { "nodeType": "marker" } }
            ]
            """);

        var result = await CreateService().ValidateAsync(definition.Id);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.IsValid.ShouldBeFalse();
        result.Data.Errors.ShouldContain(e => e.Contains("'a'") && e.Contains("no-such-node"),
            "the error must name the step and the offending type");
        result.Data.Errors.ShouldNotContain(e => e.Contains("'b'"));
    }

    [Fact]
    public async Task ValidateAsync_UnknownLegacyNodeType_IsAnErrorToo()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "__nodeType": "no-such-node" } }
            ]
            """);

        var result = await CreateService().ValidateAsync(definition.Id);

        result.Data!.IsValid.ShouldBeFalse();
        result.Data.Errors.ShouldContain(e => e.Contains("no-such-node"));
    }

    [Fact]
    public async Task ValidateAsync_RegisteredOrAbsentNodeType_IsValid()
    {
        var definition = await InsertDefinitionAsync("""
            [
              { "stepId": "a", "configuration": { "nodeType": "MARKER" } },
              { "stepId": "b", "dependsOn": ["a"] }
            ]
            """);

        var result = await CreateService().ValidateAsync(definition.Id);

        result.Data!.IsValid.ShouldBeTrue(string.Join("; ", result.Data.Errors));
    }

    private sealed class RecordingNode : IWorkflowNode
    {
        private readonly List<string> _executed;
        public RecordingNode(string nodeType, List<string> executed)
        {
            NodeType = nodeType;
            _executed = executed;
        }

        public string NodeType { get; }

        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
        {
            lock (_executed) _executed.Add(NodeType);
            return Task.FromResult(new WorkflowNodeResult { Output = $"{NodeType}-out", IsSuccess = true });
        }
    }
}
