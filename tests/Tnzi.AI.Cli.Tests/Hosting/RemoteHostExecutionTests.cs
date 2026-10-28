namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一次运行从认领到收尾都经执行宿主：在一个「远程」目标（内存里的 POSIX 文件系统 + 测试给出的进程）上跑通，
/// 与在本机文件系统上跑通是同一条路径。
/// </summary>
/// <remarks>
/// <para>
/// 真实的 <see cref="CliRunExecutor"/>、<see cref="FileSystemWorkspacePreparer"/>、<see cref="CliMcpConfigComposer"/>、
/// <see cref="CliLaunchEnvironmentComposer"/>、<see cref="CliProtocolAdapterFactory"/>（stream-json 与 ACP 两个真实适配器）
/// 与 SQLite 仓储；只有进程是录制的协议帧。
/// </para>
/// <para>
/// 远程目标的路径（<c>/home/agent/...</c>）在 Windows 测试机上根本不存在，配置里的本机工作区根又刻意指向一个
/// 不该被创建的目录：任何一处绕过宿主直接用 <see cref="File"/> / <see cref="Directory"/> / <see cref="Path"/> 的代码，
/// 在这里要么失败、要么在本机磁盘上留下东西。
/// </para>
/// </remarks>
public class RemoteHostExecutionTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111c1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222c2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333c3");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string RemoteRoot = "/home/agent/workspaces";
    private const string RemoteExecutable = "/usr/local/bin/agent-cli";

    /// <summary>配置里的工作区根：只有本机宿主会用它。远程场景下它必须一直不存在。</summary>
    private readonly string _configuredLocalRoot = TestScratch.NewPath("remote-cfg");

    /// <summary>本机场景的执行目标根与可执行文件所在目录。</summary>
    private readonly string _localTargetRoot = TestScratch.NewPath("remote-local");

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
        TestScratch.Delete(_localTargetRoot);

        base.Dispose();
    }

    [Theory]
    [InlineData("claude", true)]
    [InlineData("claude", false)]
    [InlineData("kimi", true)]
    [InlineData("kimi", false)]
    public async Task ARun_GoesThroughTheHost_FromWorkspaceToCleanup(string providerKey, bool remote)
    {
        var target = await CreateTargetAsync(providerKey, remote);
        var run = await SeedClaimedRunAsync(providerKey, target.ExecutablePath);

        await CreateExecutor(target.Host, providerKey).ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Completed, persisted.Error);
        persisted.Output.ShouldBe("Done.");

        var paths = target.Host.FileSystem.Paths;
        var runRoot = paths.Combine(target.Host.WorkspacesRoot, "host", ThreadId.ToString("N"));
        var workDirectory = paths.Combine(runRoot, CliWorkspaceLayout.WorkDirectoryName);
        var mcpConfigPath = paths.Combine(workDirectory, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.McpConfigFileName);
        persisted.WorkDirectory.ShouldBe(workDirectory);

        // 目标是按这次运行的身份取的
        var scope = target.Host.Scopes.ShouldHaveSingleItem();
        scope.RunId.ShouldBe(run.Id);
        scope.AgentId.ShouldBe(AgentId);
        scope.ThreadId.ShouldBe(ThreadId);
        scope.UserId.ShouldBe(TestHelper.DefaultTestUserId);
        scope.RuntimeId.ShouldBe(RuntimeId);
        scope.ProviderKey.ShouldBe(providerKey);
        scope.IsHostLevel.ShouldBeFalse();

        // 进程在目标上起：登记过的可执行文件在目标上存在，就用它（不再现场查找）；cwd 是目标上的工作目录
        var start = target.Host.Starts.ShouldHaveSingleItem();
        start.ExecutablePath.ShouldBe(target.ExecutablePath);
        start.WorkingDirectory.ShouldBe(workDirectory);
        start.Purpose.ShouldBe(CliProcessPurpose.AgentSession);

        // 进程起来的那一刻，brief 与 MCP 配置已经写在目标上；回写条目是目标决定的那一条
        target.BriefAtStart.ShouldNotBeNull().ShouldContain("Answer briefly.");
        target.McpConfigAtStart.ShouldNotBeNull();
        var writeBack = JsonNode.Parse(target.McpConfigAtStart!)!["mcpServers"]!["tnzi"]!.AsObject();
        if (remote)
        {
            writeBack["command"]!.GetValue<string>().ShouldBe("/opt/agent/bin/agent");
            writeBack.ContainsKey("url").ShouldBeFalse();
        }
        else
        {
            writeBack["type"]!.GetValue<string>().ShouldBe("http");
            writeBack["url"]!.GetValue<string>().ShouldBe("https://platform.example.com/mcp");
        }

        if (providerKey == "claude")
        {
            // stream-json：配置文件的路径（目标上的）交给 --mcp-config
            start.Arguments.ShouldContain(mcpConfigPath);
        }
        else
        {
            // ACP：内容随启动上下文送进 session/new，cwd 也是目标上的路径
            var sessionNew = target.Written
                .Select(w => JsonDocument.Parse(w).RootElement)
                .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/new");
            sessionNew.GetProperty("params").GetProperty("cwd").GetString().ShouldBe(workDirectory);
            var server = sessionNew.GetProperty("params").GetProperty("mcpServers").EnumerateArray().ShouldHaveSingleItem();
            if (remote)
            {
                server.GetProperty("command").GetString().ShouldBe("/opt/agent/bin/agent");
            }
            else
            {
                server.GetProperty("url").GetString().ShouldBe("https://platform.example.com/mcp");
            }
        }

        // 收尾也经目标：本次写下的受管文件按清单回滚，回收元数据留着
        var fileSystem = target.Host.FileSystem;
        (await fileSystem.FileExistsAsync(mcpConfigPath, CancellationToken.None)).ShouldBeFalse();
        (await fileSystem.FileExistsAsync(paths.Combine(workDirectory, CliBuiltInProviders.All[providerKey].BriefFileName!), CancellationToken.None))
            .ShouldBeFalse();
        (await fileSystem.FileExistsAsync(paths.Combine(runRoot, CliWorkspaceLayout.GcMetadataFileName), CancellationToken.None)).ShouldBeTrue();

        // 配置里的本机工作区根从没被碰过：本机宿主之外，工作区的位置由目标决定
        Directory.Exists(_configuredLocalRoot).ShouldBeFalse();
    }

    [Fact]
    public async Task AnUnavailableTarget_FailsTheRunAsLaunchFailed_WithoutStartingAnything()
    {
        var target = await CreateTargetAsync("claude", remote: true);
        target.Host.UnavailableWith = new CliHostUnavailableException("The workspace agent for this user is not connected.");
        var run = await SeedClaimedRunAsync("claude", target.ExecutablePath);

        await CreateExecutor(target.Host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.LaunchFailed);
        persisted.Error.ShouldBe("The workspace agent for this user is not connected.");
        target.Host.Starts.ShouldBeEmpty();
        ((InMemoryHostFileSystem)target.Host.FileSystem).Files.ShouldBe([RemoteExecutable]);
    }

    [Fact]
    public async Task ARegisteredExecutableMissingOnTheTarget_IsLookedUpOnTheTarget()
    {
        // 登记的路径在目标上不存在：在目标上现场查找（这里查到的是另一个路径），不看 API 进程所在机器的磁盘
        var target = await CreateTargetAsync("claude", remote: true, findExecutable: "/opt/other/agent-cli");
        var run = await SeedClaimedRunAsync("claude", "/registered/elsewhere/agent-cli");

        await CreateExecutor(target.Host, "claude").ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        target.Host.Starts.ShouldHaveSingleItem().ExecutablePath.ShouldBe("/opt/other/agent-cli");
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private async Task<Target> CreateTargetAsync(string providerKey, bool remote, string? findExecutable = null)
    {
        Target? created = null;
        var fileSystem = remote ? new InMemoryHostFileSystem() : (ICliHostFileSystem)LocalHostFileSystem.Instance;
        var root = remote ? RemoteRoot : Path.Combine(_localTargetRoot, "workspaces");
        var executable = remote ? RemoteExecutable : Path.Combine(_localTargetRoot, "bin", "agent-cli");

        var host = new TestCliHost(
            (spec, _) => Task.FromResult<ICliProcess>(created!.Start(spec)),
            executablePath: findExecutable,
            fileSystem: fileSystem,
            workspacesRoot: root,
            writeBackEntry: remote
                ? channel => new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "/opt/agent/bin/agent",
                    ["args"] = new JsonArray("mcp-relay"),
                    ["env"] = new JsonObject { ["RELAY_TOKEN"] = channel.Token }
                }
                : null);

        var paths = fileSystem.Paths;
        await fileSystem.CreateDirectoryAsync(paths.GetDirectoryName(executable)!, CancellationToken.None);
        await fileSystem.WriteAllBytesAsync(executable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None);

        created = new Target(host, executable, providerKey);
        return created;
    }

    /// <summary>一个执行目标，以及它在起进程那一刻看到的东西。</summary>
    private sealed class Target(TestCliHost host, string executablePath, string providerKey)
    {
        private FakeCliAgentTransport? _transport;

        public TestCliHost Host { get; } = host;

        public string ExecutablePath { get; } = executablePath;

        public string? BriefAtStart { get; private set; }

        public string? McpConfigAtStart { get; private set; }

        public IReadOnlyList<string> Written => _transport?.Written ?? [];

        public ICliProcess Start(CliProcessSpec spec)
        {
            var paths = Host.FileSystem.Paths;
            BriefAtStart = ReadOrNull(paths.Combine(spec.WorkingDirectory!, CliBuiltInProviders.All[providerKey].BriefFileName!));
            McpConfigAtStart = ReadOrNull(
                paths.Combine(spec.WorkingDirectory!, CliWorkspaceLayout.MetadataDirectoryName, CliWorkspaceLayout.McpConfigFileName));

            _transport = providerKey == "claude"
                ? new FakeCliAgentTransport(
                [
                    """{"type":"system","subtype":"init","session_id":"sess-remote"}""",
                    """{"type":"result","subtype":"success","session_id":"sess-remote","result":"Done.","num_turns":1}"""
                ])
                : new FakeCliAgentTransport([], onWrite: AcpServer);
            return new ExitedProcess(_transport);
        }

        private string? ReadOrNull(string path)
            => Host.FileSystem.FileExistsAsync(path, CancellationToken.None).GetAwaiter().GetResult()
                ? Host.FileSystem.ReadAllTextAsync(path, CancellationToken.None).GetAwaiter().GetResult()
                : null;

        private static IEnumerable<string> AcpServer(string line)
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
                "session/new" => [Ok(id, """{"sessionId":"acp-remote"}""")],
                "session/prompt" =>
                [
                    """{"jsonrpc":"2.0","method":"session/update","params":{"sessionId":"acp-remote","update":{"sessionUpdate":"agent_message_chunk","content":{"type":"text","text":"Done."}}}}""",
                    Ok(id, """{"stopReason":"end_turn"}""")
                ],
                _ => [Ok(id, "{}")]
            };
        }

        private static string Ok(string id, string resultJson)
            => $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";
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

    private async Task<CliRun> SeedClaimedRunAsync(string providerKey, string registeredExecutable)
    {
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
            ExecutablePath = registeredExecutable,
            Status = CliRuntimeStatus.Online
        });
        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private CliRunExecutor CreateExecutor(ICliHost host, string providerKey)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find(providerKey)).Returns(CliBuiltInProviders.All[providerKey]);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "remote-agent", Instructions = "Answer briefly." });

        var options = new TestOptionsMonitor<CliAgentOptions>(new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = _configuredLocalRoot,
            WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://platform.example.com/mcp", AllowedTools = ["*"] }
        });

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
}
