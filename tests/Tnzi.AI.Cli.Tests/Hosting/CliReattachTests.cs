using System.Runtime.CompilerServices;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 服务端重启时，跑在能重新附着的执行宿主上的运行：进程留在宿主上接着跑，重启后接回来、从第一行重放、事件不丢不重；
/// 接不回来时这一轮记为中断，下一轮开新的 CLI 会话并带上中断说明。没有句柄的进程（本机宿主）照旧终止、打回队列。
/// </summary>
/// <remarks>
/// 真实的执行器、队列处理器的认领与租约回收、工作区布置器与两个协议适配器，SQLite 仓储；只有执行宿主是假的：
/// <see cref="SimulatedRemoteProcess"/> 是一个活过服务端连接的进程，按顺序记下自己写出的每一行 stdout 与收到的每一行 stdin，
/// 就像一个在用户容器里缓冲输出的工作区代理。
/// </remarks>
public class CliReattachTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111d1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222d2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333d3");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string RemoteRoot = "/home/agent/workspaces";
    private const string RemoteExecutable = "/usr/local/bin/agent-cli";
    private const string OutOfMemory = "The workspace container was restarted after running out of memory.";

    private readonly string _configuredLocalRoot = TestScratch.NewPath("reattach-cfg");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_configuredLocalRoot);
        base.Dispose();
    }

    // ── 进程仍在：接回来，从序号 N 接着 ────────────────────────────────────────

    [Fact]
    public async Task AfterARestart_TheStillRunningProcessIsReattached_AndItsEventsAreNeitherLostNorDuplicated()
    {
        var remote = new SimulatedRemoteProcess("proc-1", line => line.Contains("\"type\":\"user\"", StringComparison.Ordinal)
            ?
            [
                """{"type":"system","subtype":"init","session_id":"sess-r"}""",
                """{"type":"assistant","session_id":"sess-r","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"Working on it."}]}}""",
                """{"type":"assistant","session_id":"sess-r","message":{"role":"assistant","model":"m","content":[{"type":"tool_use","id":"toolu_1","name":"Bash","input":{"command":"make"}}]}}"""
            ]
            : []);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync("claude");

        // 第一次执行：跑到工具调用，然后服务端停机
        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host, "claude").ExecuteAsync(run.Id, shutdown.Token);
            await WaitUntilAsync(async () => (await MessagesAsync(run.Id)).Any(m => m.Type == CliAgentEventType.ToolUse));
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        var detached = await ReloadAsync(run.Id);
        detached.Status.ShouldBe(CliRunStatus.Running, "the process is still running on the host; the run must not go back to the queue");
        detached.ReattachHandle.ShouldBe("proc-1");
        detached.DetachedAt.ShouldNotBeNull();
        detached.ClaimedByHostId.ShouldBeNull();
        detached.LeaseExpiresAt!.Value.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        remote.Terminated.ShouldBeFalse("a reattachable process is left running at shutdown");

        // 进程正在用的工作区（含它读着的 MCP 配置）原样留着
        var mcpConfig = McpConfigPath(host, detached.WorkDirectory!);
        (await host.FileSystem.FileExistsAsync(mcpConfig, CancellationToken.None)).ShouldBeTrue();

        var before = await MessagesAsync(run.Id);

        // 服务端不在的这段时间，CLI 接着干完了活
        remote.Emit("""{"type":"user","session_id":"sess-r","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"built"}]}}""");
        remote.Emit("""{"type":"assistant","session_id":"sess-r","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"All done."}]}}""");
        remote.Emit("""{"type":"result","subtype":"success","session_id":"sess-r","result":"All done.","num_turns":2}""");

        // 重启：认领它、接回进程
        var processor = CreateProcessor();
        (await processor.TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        (await processor.TryClaimAsync(CancellationToken.None)).ShouldBeNull("a run waiting to be reattached is not a queued run");
        host.OnReattach = request =>
        {
            request.RunId.ShouldBe(run.Id);
            request.Handle.ShouldBe("proc-1");
            return CliReattachResult.Attached(remote.Connect());
        };

        await CreateExecutor(host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.Output.ShouldBe("All done.");
        finished.ProviderSessionId.ShouldBe("sess-r");
        finished.ReattachHandle.ShouldBeNull();
        finished.DetachedAt.ShouldBeNull();

        // 一个进程，起过一次；它只收到过一次提示词（重放重新写出的那一行没有再发过去），结束时被终止
        host.Starts.ShouldHaveSingleItem();
        remote.Stdin.Count.ShouldBe(1);
        remote.Terminated.ShouldBeTrue();

        // 事件：停机前写下的原样留着，之后的接着编号，没有一条写了两遍
        var after = await MessagesAsync(run.Id);
        after.Take(before.Count).Select(m => m.Id).ShouldBe(before.Select(m => m.Id));
        after.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, after.Count));
        after.Count(m => m.Type == CliAgentEventType.ToolUse).ShouldBe(1);
        after.Count(m => m.Type == CliAgentEventType.ToolResult).ShouldBe(1);
        after.Count(m => m.Content == "Working on it.").ShouldBe(1);
        after.Count(m => m.Content == "All done.").ShouldBe(1);
        finished.MessageCount.ShouldBe(after.Count);

        // 收尾在接回来的那一次做：受管文件按清单回滚
        (await host.FileSystem.FileExistsAsync(mcpConfig, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task AnAcpProcessIsReplayedInItsOriginalOrder_SoNoRequestIsSentTwice()
    {
        var phase = 1;
        var remote = new SimulatedRemoteProcess("acp-1", line => AcpAgent(line, () => phase));
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync("kimi");

        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host, "kimi").ExecuteAsync(run.Id, shutdown.Token);
            await WaitUntilAsync(async () => (await MessagesAsync(run.Id)).Any(m => m.Content == "Working"));
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Running);
        var delivered = remote.Stdin.Count;

        // 停机期间 prompt 答完了
        phase = 2;
        var promptId = remote.Stdin.Select(l => JsonDocument.Parse(l).RootElement)
            .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/prompt")
            .GetProperty("id").GetRawText();
        remote.Emit(AcpChunk(" and done."));
        remote.Emit("{\"jsonrpc\":\"2.0\",\"id\":" + promptId + ",\"result\":{\"stopReason\":\"end_turn\"}}");

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());
        await CreateExecutor(host, "kimi").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var finished = await ReloadAsync(run.Id);
        finished.Status.ShouldBe(CliRunStatus.Completed, finished.Error);
        finished.Output.ShouldBe("Working and done.");

        // initialize / session/new / session/prompt 各只到过进程一次
        remote.Stdin.Count.ShouldBe(delivered);
        var messages = await MessagesAsync(run.Id);
        messages.Select(m => m.Sequence).ShouldBe(Enumerable.Range(1, messages.Count));
        messages.Count(m => m.Content == "Working").ShouldBe(1);
        messages.Count(m => m.Content == " and done.").ShouldBe(1);
    }

    /// <summary>
    /// 接回来之后才到的权限请求，按这次运行的启动档案应答：一条只读的运行不能因为服务端重启过就被当成全权放行。
    /// </summary>
    [Theory]
    [InlineData(CliPermissionMode.ReadOnly, "deny")]
    [InlineData(CliPermissionMode.Full, "allow")]
    public async Task APermissionRequestAfterReattachment_IsAnsweredWithTheRunsPermissionMode(CliPermissionMode mode, string expectedOption)
    {
        var phase = 1;
        var remote = new SimulatedRemoteProcess("acp-perm", line => AcpAgent(line, () => phase));
        var host = await CreateHostAsync(remote);
        host.LaunchProfile = _ => new CliLaunchProfile { PermissionMode = mode };
        var run = await SeedClaimedRunAsync("kimi");

        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host, "kimi").ExecuteAsync(run.Id, shutdown.Token);
            await WaitUntilAsync(async () => (await MessagesAsync(run.Id)).Any(m => m.Content == "Working"));
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        // 停机期间 agent 要权限，随后答完 prompt
        phase = 2;
        var delivered = remote.Stdin.Count;
        var promptId = remote.Stdin.Select(l => JsonDocument.Parse(l).RootElement)
            .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/prompt")
            .GetProperty("id").GetRawText();
        remote.Emit("""
            {"jsonrpc":"2.0","id":"perm-r","method":"session/request_permission","params":{"toolCall":{"toolCallId":"t1","kind":"execute"},"options":[{"optionId":"allow","kind":"allow_once"},{"optionId":"deny","kind":"reject_once"}]}}
            """.Trim());
        remote.Emit("{\"jsonrpc\":\"2.0\",\"id\":" + promptId + ",\"result\":{\"stopReason\":\"end_turn\"}}");

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());
        await CreateExecutor(host, "kimi").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var answer = remote.Stdin.Skip(delivered).Single(l => l.Contains("perm-r", StringComparison.Ordinal));
        answer.ShouldContain($"\"optionId\":\"{expectedOption}\"");
    }

    // ── 进程已不在：中断，下一轮开新会话并带上说明 ──────────────────────────────

    [Fact]
    public async Task WhenTheHostReportsTheProcessGone_TheTurnIsInterrupted_AndTheNextTurnForksFromThePreviousOneWithANotice()
    {
        var host = await CreateHostAsync(remote: null);
        var previous = await SeedFinishedRunAsync("sess-prev", "entry-prev", DateTime.UtcNow.AddMinutes(-30));
        var interrupted = await SeedDetachedRunAsync("proc-gone", DateTime.UtcNow.AddMinutes(-1), "Refactor the parser");
        await SeedMessagesAsync(interrupted.Id,
            new CliRunMessage { Type = CliAgentEventType.Text, Content = "Half way there." },
            new CliRunMessage { Type = CliAgentEventType.ToolUse, Tool = "Bash", CallId = "t1", InputJson = """{"command":"sed -i x parser.c"}""" });
        host.OnReattach = _ => CliReattachResult.Gone(OutOfMemory);

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(interrupted.Id);
        await CreateExecutor(host, "claude").ExecuteAsync(interrupted.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var stopped = await ReloadAsync(interrupted.Id);
        stopped.Status.ShouldBe(CliRunStatus.Failed);
        stopped.FailureReason.ShouldBe(CliRunFailureReason.Interrupted);
        stopped.Error.ShouldBe(OutOfMemory);
        stopped.ProviderSessionId.ShouldBeNull("an interrupted session is never resumed");
        stopped.MessageCount.ShouldBe(2);
        (await MessagesAsync(interrupted.Id)).Count.ShouldBe(2, "what the run had written is kept");
        host.Starts.ShouldBeEmpty("an interrupted turn is not run again");

        // 下一轮：从中断之前那一轮截断、分叉出新会话，并在本轮上下文里说明上一轮停在哪里
        FakeCliAgentTransport? transport = null;
        host.StartWith = _ =>
        {
            transport = new FakeCliAgentTransport(
            [
                """{"type":"system","subtype":"init","session_id":"sess-next"}""",
                """{"type":"result","subtype":"success","session_id":"sess-next","result":"Resumed.","num_turns":1}"""
            ]);
            return new ExitedProcess(transport);
        };
        var next = await SeedClaimedRunAsync("claude", prompt: "Please carry on");
        await CreateExecutor(host, "claude").ExecuteAsync(next.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var done = await ReloadAsync(next.Id);
        done.Status.ShouldBe(CliRunStatus.Completed, done.Error);
        done.ResumeAtEntryId.ShouldBe("entry-prev");

        var args = host.Starts.ShouldHaveSingleItem().Arguments;
        args.ShouldContain("--resume");
        args[args.ToList().IndexOf("--resume") + 1].ShouldBe(previous.ProviderSessionId);
        args.ShouldContain("--resume-session-at");
        args.ShouldContain("--fork-session");

        done.PerTurnContext.ShouldNotBeNull();
        done.PerTurnContext.ShouldContain("the previous turn was interrupted");
        done.PerTurnContext.ShouldContain(OutOfMemory);
        done.PerTurnContext.ShouldContain("Refactor the parser");
        done.PerTurnContext.ShouldContain("Half way there.");
        done.PerTurnContext.ShouldContain("`Bash`");
        transport!.Written.ShouldHaveSingleItem().ShouldContain("the previous turn was interrupted");
    }

    [Fact]
    public async Task AnUnreachableHost_IsRetriedLater_AndOnlyInterruptsTheTurnOnceTheReattachTimeoutHasPassed()
    {
        var host = await CreateHostAsync(remote: null);
        host.ReattachUnavailableWith = new CliHostUnavailableException("The workspace agent for this user is not connected.");
        var detachedAt = DateTime.UtcNow.AddMinutes(-1);
        var run = await SeedDetachedRunAsync("proc-later", detachedAt, "Long job");

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor(host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var waiting = await ReloadAsync(run.Id);
        waiting.Status.ShouldBe(CliRunStatus.Running, "within the reattach timeout the run keeps waiting for its process");
        waiting.ReattachHandle.ShouldBe("proc-later");
        waiting.ClaimedByHostId.ShouldBeNull();
        waiting.LeaseExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow, "the next attempt waits for the retry interval");
        waiting.DetachedAt!.Value.ShouldBe(detachedAt, TimeSpan.FromSeconds(1));
        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBeNull("not before the retry interval");

        // 断开已经超过期限
        await DbContext.Set<CliRun>().IgnoreQueryFilters().Where(r => r.Id == run.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.DetachedAt, DateTime.UtcNow.AddMinutes(-11))
                .SetProperty(r => r.LeaseExpiresAt, DateTime.UtcNow.AddSeconds(-1)));

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor(host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var interrupted = await ReloadAsync(run.Id);
        interrupted.Status.ShouldBe(CliRunStatus.Failed);
        interrupted.FailureReason.ShouldBe(CliRunFailureReason.Interrupted);
        interrupted.Error.ShouldBe(
            "The process of this run could not be reattached within 10 minute(s): The workspace agent for this user is not connected.");
        host.Starts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACancelRequestedWhileDetached_StopsTheProcessAsSoonAsItIsReattached()
    {
        var remote = new SimulatedRemoteProcess("proc-cancel", _ => []);
        remote.Emit("""{"type":"system","subtype":"init","session_id":"sess-c"}""");
        var host = await CreateHostAsync(remote);
        var run = await SeedDetachedRunAsync("proc-cancel", DateTime.UtcNow.AddMinutes(-1), "Long job", cancelRequested: true);
        host.OnReattach = _ => CliReattachResult.Attached(remote.Connect());

        // 取消请求不让租约回收把它直接判掉：那样停不掉进程
        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);
        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Running);

        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(run.Id);
        await CreateExecutor(host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Cancelled);
        remote.Terminated.ShouldBeTrue();
    }

    // ── 没有句柄（本机宿主）：照旧 ─────────────────────────────────────────────

    [Fact]
    public async Task AProcessWithoutAReattachHandle_IsStillTerminatedAndReleasedToTheQueueAtShutdown()
    {
        var remote = new SimulatedRemoteProcess(handle: null, line => line.Contains("\"type\":\"user\"", StringComparison.Ordinal)
            ? ["""{"type":"assistant","message":{"role":"assistant","model":"m","content":[{"type":"text","text":"Working on it."}]}}"""]
            : []);
        var host = await CreateHostAsync(remote);
        var run = await SeedClaimedRunAsync("claude");

        using (var shutdown = new CancellationTokenSource())
        {
            var execution = CreateExecutor(host, "claude").ExecuteAsync(run.Id, shutdown.Token);
            await WaitUntilAsync(async () => (await MessagesAsync(run.Id)).Count > 0);
            await shutdown.CancelAsync();
            await execution.WaitAsync(TestTimeout);
        }

        var released = await ReloadAsync(run.Id);
        released.Status.ShouldBe(CliRunStatus.Queued);
        released.ReattachHandle.ShouldBeNull();
        released.DetachedAt.ShouldBeNull();
        remote.Terminated.ShouldBeTrue();
    }

    [Fact]
    public async Task LeaseReclaim_RequeuesRunsWithoutAHandle_AndLeavesReattachableOnesForReattachment()
    {
        var plain = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, Status = CliRunStatus.Running, Prompt = "a",
            StartedAt = DateTime.UtcNow.AddMinutes(-5), LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-5), ClaimedByHostId = "DEAD-HOST"
        };
        var reattachable = new CliRun
        {
            AgentId = AgentId, CliRuntimeId = RuntimeId, Status = CliRunStatus.Running, Prompt = "b",
            StartedAt = DateTime.UtcNow.AddMinutes(-5), LeaseExpiresAt = DateTime.UtcNow.AddSeconds(-5), ClaimedByHostId = "DEAD-HOST",
            ReattachHandle = "proc-x"
        };
        DbContext.Set<CliRun>().AddRange(plain, reattachable);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await CreateProcessor().ReclaimExpiredLeasesAsync(CancellationToken.None);

        (await ReloadAsync(plain.Id)).Status.ShouldBe(CliRunStatus.Queued);
        var kept = await ReloadAsync(reattachable.Id);
        kept.Status.ShouldBe(CliRunStatus.Running);
        kept.ReattachHandle.ShouldBe("proc-x");

        // 认领它时记下断开的时刻（宿主崩溃：租约到期的那一刻）
        (await CreateProcessor().TryClaimReattachAsync(CancellationToken.None)).ShouldBe(reattachable.Id);
        var claimed = await ReloadAsync(reattachable.Id);
        claimed.DetachedAt.ShouldNotBeNull();
        claimed.DetachedAt!.Value.ShouldBe(reattachable.LeaseExpiresAt!.Value, TimeSpan.FromSeconds(1));
        claimed.LeaseExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private static IEnumerable<string> AcpAgent(string line, Func<int> phase)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (!root.TryGetProperty("method", out var method) || !root.TryGetProperty("id", out var idElement))
        {
            return [];
        }

        var id = idElement.GetRawText();
        return method.GetString() switch
        {
            "initialize" => [Ok(id, """{"agentCapabilities":{"mcpCapabilities":{"http":true}}}""")],
            "session/new" => [Ok(id, """{"sessionId":"acp-r"}""")],
            // 第一阶段：开始干活但还没答完，服务端就停机了
            "session/prompt" when phase() == 1 => [AcpChunk("Working")],
            _ => [Ok(id, "{}")]
        };

        static string Ok(string id, string resultJson) => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";
    }

    private static string AcpChunk(string text)
        => """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"acp-r","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"""
           + JsonSerializer.Serialize(text) + "}}}}";

    private async Task<TestCliHost> CreateHostAsync(SimulatedRemoteProcess? remote)
    {
        var host = new TestCliHost(
            (_, _) => Task.FromResult<ICliProcess>(remote!.Connect()),
            executablePath: RemoteExecutable,
            fileSystem: new InMemoryHostFileSystem(),
            workspacesRoot: RemoteRoot);

        var paths = host.FileSystem.Paths;
        await host.FileSystem.CreateDirectoryAsync(paths.GetDirectoryName(RemoteExecutable)!, CancellationToken.None);
        await host.FileSystem.WriteAllBytesAsync(RemoteExecutable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None);
        return host;
    }

    private static string McpConfigPath(TestCliHost host, string workDirectory)
        => host.FileSystem.Paths.Combine(workDirectory, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.McpConfigFileName);

    private async Task SeedSetupAsync(string providerKey)
    {
        if (await DbContext.Set<CliAgentBinding>().AnyAsync())
        {
            return;
        }

        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = true,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = providerKey,
            Name = $"{providerKey} @ TEST-HOST",
            ExecutablePath = RemoteExecutable,
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> SeedClaimedRunAsync(string providerKey, string prompt = "do the thing")
    {
        await SeedSetupAsync(providerKey);
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = prompt
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> SeedFinishedRunAsync(string sessionId, string entryId, DateTime completedAt)
    {
        await SeedSetupAsync("claude");
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Completed,
            Prompt = "earlier turn",
            ProviderSessionId = sessionId,
            ProviderSessionEntryId = entryId,
            StartedAt = completedAt.AddMinutes(-1),
            CompletedAt = completedAt,
            CreatorId = TestHelper.DefaultTestUserId
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    /// <summary>一条停机时留在宿主上的运行：Running、带句柄、租约已到期、没人占着。</summary>
    private async Task<CliRun> SeedDetachedRunAsync(string handle, DateTime detachedAt, string prompt, bool cancelRequested = false)
    {
        await SeedSetupAsync("claude");
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Running,
            Prompt = prompt,
            ProviderSessionId = "sess-prev",
            ReattachHandle = handle,
            DetachedAt = detachedAt,
            StartedAt = detachedAt.AddMinutes(-5),
            DispatchedAt = detachedAt.AddMinutes(-5),
            LeaseExpiresAt = detachedAt,
            WorkDirectory = $"{RemoteRoot}/host/{ThreadId:N}/{CliWorkspaceLayout.WorkDirectoryName}",
            CancelRequested = cancelRequested
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task SeedMessagesAsync(Guid runId, params CliRunMessage[] messages)
    {
        for (var i = 0; i < messages.Length; i++)
        {
            messages[i].RunId = runId;
            messages[i].Sequence = i + 1;
        }

        DbContext.Set<CliRunMessage>().AddRange(messages);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<List<CliRunMessage>> MessagesAsync(Guid runId)
        => await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == runId).OrderBy(m => m.Sequence).ToListAsync();

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(20);
        }
    }

    private TestOptionsMonitor<CliAgentOptions> Options() => new(new CliAgentOptions
    {
        Enabled = true,
        WorkspacesRoot = _configuredLocalRoot,
        LeaseDuration = TimeSpan.FromMinutes(30),
        WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://platform.example.com/mcp", AllowedTools = ["*"] }
    });

    private CliRunQueueProcessor CreateProcessor()
        => new(
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            Options(),
            NullLogger<CliRunQueueProcessor>.Instance);

    private CliRunExecutor CreateExecutor(ICliHost host, string providerKey)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find(providerKey)).Returns(CliBuiltInProviders.All[providerKey]);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "remote-agent", Instructions = "Answer briefly." });

        var options = Options();
        return new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            new CliProtocolAdapterFactory(ServiceProvider),
            host,
            new FileSystemWorkspacePreparer(options, NullLogger<FileSystemWorkspacePreparer>.Instance),
            new CliBriefComposer(),
            new CliMcpConfigComposer(NullLogger<CliMcpConfigComposer>.Instance),
            new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
            new CliLaunchEnvironmentComposer(new CliAuthStatusProbe(NullLogger<CliAuthStatusProbe>.Instance), NullLogger<CliLaunchEnvironmentComposer>.Instance),
            Mock.Of<IAgentGrantService>(),
            Mock.Of<ISkillService>(),
            new CliRunSignalHub(),
            options,
            ServiceProvider.GetRequiredService<ICurrentTenant>(),
            NullLogger<CliRunExecutor>.Instance,
            ServiceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    private sealed class ExitedProcess(ICliAgentTransport transport) : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = transport;

        public int ProcessId => 4242;

        public bool HasExited => true;

        public int? ExitCode => 0;

        public Task TerminateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// 一个活过服务端连接的「远程」进程：按顺序记下写出的每一行 stdout（连同写它之前收到了几行 stdin）与收到的每一行 stdin，
/// 每次 <see cref="Connect"/> 都是一条新连接；释放连接不终止进程。
/// </summary>
internal sealed class SimulatedRemoteProcess(string? handle, Func<string, IEnumerable<string>> respond)
{
    private readonly object _gate = new();
    private readonly List<CliOutputLine> _output = [];
    private readonly List<string> _stdin = [];
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _exited;
    private volatile bool _terminated;

    public string? Handle => handle;

    public bool Terminated => _terminated;

    public IReadOnlyList<string> Stdin
    {
        get
        {
            lock (_gate)
            {
                return [.. _stdin];
            }
        }
    }

    public Connection Connect() => new(this);

    /// <summary>进程写出一行 stdout。</summary>
    public void Emit(string line)
    {
        lock (_gate)
        {
            _output.Add(new CliOutputLine(line, _stdin.Count));
            Changed();
        }
    }

    private void Receive(string line)
    {
        lock (_gate)
        {
            _stdin.Add(line);
        }

        foreach (var response in respond(line))
        {
            Emit(response);
        }
    }

    private void Exit()
    {
        lock (_gate)
        {
            _exited = true;
            Changed();
        }
    }

    private void Changed()
    {
        var changed = _changed;
        _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        changed.TrySetResult();
    }

    private async IAsyncEnumerable<CliOutputLine> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var next = 0;
        while (true)
        {
            CliOutputLine? line = null;
            Task changed;
            lock (_gate)
            {
                if (next < _output.Count)
                {
                    line = _output[next++];
                }
                else if (_exited)
                {
                    yield break;
                }

                changed = _changed.Task;
            }

            if (line is { } available)
            {
                yield return available;
                continue;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    internal sealed class Connection(SimulatedRemoteProcess process) : ICliReattachedProcess, ICliAgentTransport
    {
        private readonly long _received = process.Stdin.Count;

        public ICliAgentTransport Transport => this;

        public int ProcessId => 7;

        public bool HasExited => process._exited;

        public int? ExitCode => process._exited ? 0 : null;

        public string? ReattachHandle => process.Handle;

        public long StdinLinesReceived => _received;

        public string StderrTail => string.Empty;

        public IAsyncEnumerable<CliOutputLine> ReadOutputAsync(CancellationToken cancellationToken) => process.ReadAsync(cancellationToken);

        public async IAsyncEnumerable<string> ReadLinesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var line in process.ReadAsync(cancellationToken))
            {
                yield return line.Text;
            }
        }

        public Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            process.Receive(line);
            return Task.CompletedTask;
        }

        public Task CloseInputAsync()
        {
            process.Exit();
            return Task.CompletedTask;
        }

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            process._terminated = true;
            process.Exit();
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        // 断开连接，进程照常跑
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
