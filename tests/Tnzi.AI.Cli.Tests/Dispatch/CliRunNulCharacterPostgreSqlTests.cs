using Tnzi.AI.Engine;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// CLI 的输出里带 U+0000（读二进制文件、进程的原始输出）：在真实 PostgreSQL 上这一轮照常跑完，
/// 事件、工具入参与输出、思考、回复与错误文本都存得下，NUL 换成了 U+FFFD，其余逐字不变。
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL 的文本列拒收 NUL（SQLSTATE 22021）。此前第一条带 NUL 的事件就让执行器抛出，
/// 运行以「An error occurred while saving the entity changes」失败，回复不进线程。
/// 修复在数据层（<c>TnziDbContext</c> 挂的命令拦截器），这里证明 CLI 运行时写库的每条路径都经过它：
/// 事件行（逐条 <c>SaveChanges</c>）、线程里的回复（<c>SaveChanges</c> + 状态的 <c>ExecuteUpdate</c>）、
/// 运行的终态与错误文本（<c>ExecuteUpdate</c>）。
/// </para>
/// <para>
/// 只在 <see cref="PostgreSqlFactAttribute.ConnectionStringVariable"/> 给出服务器时运行；每个用例一个新库，跑完删掉。
/// </para>
/// </remarks>
public class CliRunNulCharacterPostgreSqlTests : CliChatTurnTestBase
{
    private const string Nul = "\0";
    private const string Replacement = "�";

    private string ConnectionString { get; } = PostgreSqlFactAttribute.ConnectionString is null
        ? string.Empty
        : PostgreSqlFactAttribute.NewDatabase("tnzi_cli_nul");

    protected override bool ConfigureDatabase(DbContextOptionsBuilder options)
    {
        options.UseNpgsql(ConnectionString);
        return true;
    }

    public override void Dispose()
    {
        DbContext.Database.EnsureDeleted();
        base.Dispose();
    }

    [PostgreSqlFact]
    public async Task ATurnWhoseOutputCarriesNul_FinishesNormally_AndEverythingIsStored()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("what does the process print?");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
        [
            new CliAgentEvent { Type = CliAgentEventType.Thinking, Content = $"raw bytes{Nul}here" },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolUse, CallId = "call-1", Tool = "Bash",
                Input = new Dictionary<string, object?> { ["command"] = $"printf 'a{Nul}b'" }
            },
            new CliAgentEvent
            {
                Type = CliAgentEventType.ToolResult, CallId = "call-1", Status = "completed", Output = $"a{Nul}b",
                OutputBlocks = [new CliContentBlock { Type = CliContentBlock.TextType, Text = $"a{Nul}b" }]
            },
            new CliAgentEvent { Type = CliAgentEventType.Log, Level = "debug", Content = $"stderr{Nul}" },
            new CliAgentEvent { Type = CliAgentEventType.Status, Status = $"status{Nul}" },
            new CliAgentEvent { Type = CliAgentEventType.Text, Content = $"It prints a{Nul}b." }
        ]);

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed);
        persisted.Error.ShouldBeNull();
        persisted.Output.ShouldBe($"It prints a{Replacement}b.");

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe($"It prints a{Replacement}b.");
        persisted.AssistantMessageId.ShouldBe(reply.Id);
        var call = JsonSerializer.Deserialize<List<ToolCallDetail>>(reply.ToolCalls!, TnziJsonDefaults.Options)!.ShouldHaveSingleItem();
        call.Input!.Value.GetProperty("command").GetString().ShouldBe($"printf 'a{Nul}b'",
            "JSON escapes the NUL (\\u0000), so the tool input survives exactly");

        var rows = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id).OrderBy(m => m.Sequence).ToListAsync();
        rows.Select(m => m.Type).ShouldBe(
        [
            CliAgentEventType.Thinking, CliAgentEventType.ToolUse, CliAgentEventType.ToolResult,
            CliAgentEventType.Log, CliAgentEventType.Status, CliAgentEventType.Text
        ], "every event is recorded, none replaced by a notice");
        rows[0].Content.ShouldBe($"raw bytes{Replacement}here");
        CliRunMessageConverter.ToEvent(rows[1]).Input!["command"]!.ToString().ShouldBe($"printf 'a{Nul}b'");
        rows[2].Output.ShouldBe($"a{Replacement}b");
        CliRunMessageConverter.ToEvent(rows[2]).OutputBlocks!.ShouldHaveSingleItem().Text.ShouldBe($"a{Nul}b");
        rows[3].Content.ShouldBe($"stderr{Replacement}");
        rows[4].Status.ShouldBe($"status{Replacement}");
        rows[5].Content.ShouldBe($"It prints a{Replacement}b.");
    }

    [PostgreSqlFact]
    public async Task AFailureWhoseErrorTextCarriesNul_IsRecordedAsThatFailure()
    {
        var (threadId, userMessageId) = await SeedThreadWithQuestionAsync("run it");
        var run = await SeedClaimedRunAsync(threadId, userMessageId);
        var adapter = new ScriptedAdapter(
            [new CliAgentEvent { Type = CliAgentEventType.Text, Content = $"partial{Nul}" }],
            status: CliRunStatus.Failed, error: $"exited with{Nul}garbage");

        await CreateExecutor(adapter).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadRunAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.Error.ShouldBe($"exited with{Replacement}garbage", "the person sees the CLI's error, not the database's");

        var reply = await DbContext.Set<AgentThreadMessage>().AsNoTracking()
            .SingleAsync(m => m.ThreadId == threadId && m.Role == MessageRole.Assistant);
        reply.Content.ShouldBe($"partial{Replacement}");
        reply.Status.ShouldBe(ThreadMessageStatuses.Failed);
    }
}
