namespace Tnzi.AI.Tests.Workflow;

/// <summary>
/// Version snapshot / restore round trip on a real database.
/// </summary>
/// <remarks>
/// <para>
/// ★ Restore used to double-encode <c>Steps</c>: the snapshot serialized the entity's
/// <c>Steps</c> <b>string</b> as a JSON string token, and restore copied
/// <c>GetRawText()</c> (quotes and escapes included) straight back into the entity. The
/// call returned 200 and every subsequent reader of the definition (get, list, run,
/// validate) threw <c>JsonException</c>. Nothing read <c>Steps</c> back inside the
/// restore call itself, so the row was bricked with no symptom at the call site.
/// </para>
/// <para>
/// These tests read the row back through the same service paths a caller would hit
/// afterwards; a mocked repository could not have caught this because the encoding
/// bug only shows up once the JSON is parsed again.
/// </para>
/// </remarks>
public class WorkflowServiceVersionTests : WorkflowServiceSqliteTestBase
{
    // ValidateAsync checks nodeType against the registered IWorkflowNode set, so the two
    // kinds the fixture definition uses must exist. Nothing here executes a node.
    protected override void ConfigureWorkflowNodes(IServiceCollection services)
    {
        services.AddScoped<IWorkflowNode>(_ => new NamedNode(WorkflowNodeTypes.Agent));
        services.AddScoped<IWorkflowNode>(_ => new NamedNode(WorkflowNodeTypes.Review));
    }

    private sealed class NamedNode(string nodeType) : IWorkflowNode
    {
        public string NodeType => nodeType;
        public Task<WorkflowNodeResult> ExecuteAsync(WorkflowNodeContext context, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Version tests never execute a node.");
    }

    private const string OriginalSteps = """
        [
          { "stepId": "draft", "configuration": { "nodeType": "agent" }, "instructions": "write" },
          { "stepId": "review", "dependsOn": ["draft"], "configuration": { "nodeType": "review" } }
        ]
        """;

    [Fact]
    public async Task RestoreVersionAsync_RoundTrip_RestoresOriginalSteps()
    {
        var definition = await InsertDefinitionAsync(OriginalSteps, name: "v1-name", description: "v1-desc");
        var service = CreateService();

        // Update creates version 1 (the pre-update snapshot) and replaces the steps.
        var update = await service.UpdateAsync(definition.Id, new UpdateWorkflowDefinitionDto
        {
            Name = "v2-name",
            Steps = [new WorkflowStepDto { StepId = "only", Configuration = new() { ["nodeType"] = "agent" } }]
        });
        update.Succeeded.ShouldBeTrue(update.Message);

        var restore = await service.RestoreVersionAsync(definition.Id, 1);
        restore.Succeeded.ShouldBeTrue(restore.Message);

        // Every reader of the definition must still work after a restore.
        var restored = await service.GetByIdAsync(definition.Id);
        restored.Succeeded.ShouldBeTrue(restored.Message);
        restored.Data!.Name.ShouldBe("v1-name");
        restored.Data.Description.ShouldBe("v1-desc");
        restored.Data.Steps.Select(s => s.StepId).ShouldBe(["draft", "review"]);
        restored.Data.Steps[0].Instructions.ShouldBe("write");
        restored.Data.Steps[1].DependsOn.ShouldBe(["draft"]);

        var listed = await service.GetListAsync(new WorkflowDefinitionQueryDto());
        listed.Succeeded.ShouldBeTrue(listed.Message);
        listed.Data!.Items.ShouldContain(w => w.Id == definition.Id);

        var validation = await service.ValidateAsync(definition.Id);
        validation.Succeeded.ShouldBeTrue(validation.Message);
        validation.Data!.IsValid.ShouldBeTrue(string.Join("; ", validation.Data.Errors));
    }

    [Fact]
    public async Task RestoreVersionAsync_LegacyStringEncodedSnapshot_IsDecoded()
    {
        // A snapshot persisted by the old code: "steps" is a JSON *string* holding the
        // array, not the array itself. Restore must decode it rather than copy the
        // quoted literal into the definition.
        var definition = await InsertDefinitionAsync("""[{ "stepId": "current" }]""");
        var legacySnapshot = JsonSerializer.Serialize(new
        {
            name = "legacy-name",
            description = "legacy-desc",
            steps = OriginalSteps,
            executionMode = "Dag",
            isEnabled = true,
            configuration = (string?)null
        });
        DbContext.Set<WorkflowDefinitionVersion>().Add(new WorkflowDefinitionVersion
        {
            WorkflowDefinitionId = definition.Id,
            VersionNumber = 7,
            Definition = legacySnapshot
        });
        await DbContext.SaveChangesAsync();

        var restore = await CreateService().RestoreVersionAsync(definition.Id, 7);
        restore.Succeeded.ShouldBeTrue(restore.Message);

        var restored = await CreateService().GetByIdAsync(definition.Id);
        restored.Succeeded.ShouldBeTrue(restored.Message);
        restored.Data!.Name.ShouldBe("legacy-name");
        restored.Data.Steps.Select(s => s.StepId).ShouldBe(["draft", "review"]);
    }

    [Fact]
    public async Task RestoreVersionAsync_NullDescriptionAndConfiguration_AreRestoredAsNull()
    {
        var definition = await InsertDefinitionAsync(OriginalSteps, description: null, configuration: null);
        var service = CreateService();

        var update = await service.UpdateAsync(definition.Id, new UpdateWorkflowDefinitionDto { Description = "later" });
        update.Succeeded.ShouldBeTrue(update.Message);

        var restore = await service.RestoreVersionAsync(definition.Id, 1);
        restore.Succeeded.ShouldBeTrue(restore.Message);

        var row = await DbContext.Set<WorkflowDefinition>().AsNoTracking().FirstAsync(d => d.Id == definition.Id);
        row.Description.ShouldBeNull();
        row.Configuration.ShouldBeNull();
    }

    [Fact]
    public async Task RestoreVersionAsync_ConfigurationRoundTrip_KeepsRawJsonObject()
    {
        const string configuration = """{"conditionalEdges":[],"loops":{}}""";
        var definition = await InsertDefinitionAsync(OriginalSteps, configuration: configuration);
        var service = CreateService();

        var update = await service.UpdateAsync(definition.Id, new UpdateWorkflowDefinitionDto { Name = "changed" });
        update.Succeeded.ShouldBeTrue(update.Message);

        var restore = await service.RestoreVersionAsync(definition.Id, 1);
        restore.Succeeded.ShouldBeTrue(restore.Message);

        var row = await DbContext.Set<WorkflowDefinition>().AsNoTracking().FirstAsync(d => d.Id == definition.Id);
        row.Configuration.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(row.Configuration!);
        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Object);
        doc.RootElement.TryGetProperty("conditionalEdges", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task RestoreVersionAsync_UnparseableSnapshotSteps_IsRefused_AndRowIsUntouched()
    {
        var definition = await InsertDefinitionAsync(OriginalSteps, name: "keep-me");
        DbContext.Set<WorkflowDefinitionVersion>().Add(new WorkflowDefinitionVersion
        {
            WorkflowDefinitionId = definition.Id,
            VersionNumber = 3,
            Definition = """{"name":"broken","steps":"not-json-at-all"}"""
        });
        await DbContext.SaveChangesAsync();

        var restore = await CreateService().RestoreVersionAsync(definition.Id, 3);

        restore.Succeeded.ShouldBeFalse();
        restore.Code.ShouldBe(500);

        var row = await DbContext.Set<WorkflowDefinition>().AsNoTracking().FirstAsync(d => d.Id == definition.Id);
        row.Name.ShouldBe("keep-me");
        row.Steps.ShouldBe(OriginalSteps);
    }

    [Fact]
    public async Task CreateVersionSnapshot_StoresStepsAsJsonArray_NotAsString()
    {
        var definition = await InsertDefinitionAsync(OriginalSteps);
        var service = CreateService();

        var update = await service.UpdateAsync(definition.Id, new UpdateWorkflowDefinitionDto { Name = "changed" });
        update.Succeeded.ShouldBeTrue(update.Message);

        var version = await service.GetVersionAsync(definition.Id, 1);
        version.Succeeded.ShouldBeTrue(version.Message);
        using var doc = JsonDocument.Parse(version.Data!.Definition!);
        doc.RootElement.GetProperty("steps").ValueKind.ShouldBe(JsonValueKind.Array);
    }
}
