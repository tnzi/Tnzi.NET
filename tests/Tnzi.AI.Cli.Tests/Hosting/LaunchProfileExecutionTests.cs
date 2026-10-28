namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 一次运行怎样启动 CLI 由执行位置与调用方按次决定（<see cref="CliLaunchProfile"/>），用户提供的工作目录里不出现任何受管文件。
/// </summary>
/// <remarks>
/// 真实的执行器、工作区布置器、启动环境组合器与两个协议适配器，跑在一个「远程」目标上（内存里的 POSIX 文件系统 + 录制的协议帧）。
/// 断言落在进程起来那一刻看到的东西上：命令行、环境变量，以及目标上的文件。
/// </remarks>
public class LaunchProfileExecutionTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111d1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222d2");
    private static readonly Guid ThreadId = Guid.Parse("33333333-3333-3333-3333-3333333333d3");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private const string Executable = "/usr/local/bin/agent-cli";
    private const string Worktree = "/home/u/sessions/s1/demo";

    private readonly string _configuredLocalRoot = TestScratch.NewPath("profile-cfg");

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

    /// <summary>
    /// 远程目标没给档案时：平台的 MCP 与用户自己配的合并（不加 <c>--strict-mcp-config</c>），权限照旧全权；本机宿主仍然只用平台的。
    /// </summary>
    [Fact]
    public async Task ARemoteTarget_ByDefault_MergesTheUsersMcpServers_WhileTheLocalHostStaysStrict()
    {
        var target = await CreateTargetAsync("claude");
        var run = await SeedClaimedRunAsync();

        await ExecuteAsync(target, CliBuiltInProviders.All["claude"], run);

        var start = target.Host.Starts.ShouldHaveSingleItem();
        start.Arguments.ShouldContain("--mcp-config");
        start.Arguments.ShouldNotContain("--strict-mcp-config");
        ValueOf(start.Arguments, "--permission-mode").ShouldBe("bypassPermissions");

        var request = new CliLaunchProfileRequest
        {
            Scope = CliHostScope.HostLevel,
            Provider = CliBuiltInProviders.All["claude"],
            WorkDirectory = "/w"
        };
        var local = await LocalHosts.Create().GetLaunchProfileAsync(request, CancellationToken.None);
        local.StrictMcpConfig.ShouldBeTrue("the local host keeps the managed configuration as the only one");
        local.PermissionMode.ShouldBe(CliPermissionMode.Full);

        // 没实现这个方法的执行目标（2.F1 起就有的远程宿主）拿到的是接口的缺省
        var remote = await ((ICliHostTarget)new MinimalTarget()).GetLaunchProfileAsync(request, CancellationToken.None);
        remote.ShouldBeSameAs(CliLaunchProfile.Default);
        remote.StrictMcpConfig.ShouldBeFalse();
    }

    /// <summary>只实现了必需成员的执行目标：启动档案走接口的缺省实现。</summary>
    private sealed class MinimalTarget : ICliHostTarget
    {
        public string Name => "minimal";

        public ICliHostFileSystem FileSystem { get; } = new InMemoryHostFileSystem();

        public string WorkspacesRoot => "/w";

        public Task<string?> FindExecutableAsync(CliProviderDescriptor provider, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<ICliProcess> StartAsync(CliProcessSpec spec, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// 档案先由执行目标给（内置工具、设置来源），再经 contributor 按这一轮调整（权限档位）；部署级参数里同名的开关这一轮被丢掉。
    /// </summary>
    /// <remarks>
    /// 此前内置工具只能写在 provider 的 <c>ExtraArgs</c> 里，对这家 CLI 的每一次运行都生效，分不出「轻量路径只开网页搜索」与「工作区开全部工具」。
    /// </remarks>
    [Fact]
    public async Task TheTargetAndAContributor_DecideTheRunsPermissionsAndTools()
    {
        var target = await CreateTargetAsync("claude");
        target.Host.LaunchProfile = _ => new CliLaunchProfile { BuiltInTools = ["WebSearch"], SettingSources = ["user"] };
        var contributor = new RecordingContributor(profile => profile with { PermissionMode = CliPermissionMode.ReadOnly });
        var provider = CliBuiltInProviders.All["claude"] with { ExtraArgs = ["--tools", "Bash,Read", "--verbose"] };
        var run = await SeedClaimedRunAsync();

        await ExecuteAsync(target, provider, run, contributor);

        var start = target.Host.Starts.ShouldHaveSingleItem();
        ValueOf(start.Arguments, "--permission-mode").ShouldBe("default");
        ValueOf(start.Arguments, "--tools").ShouldBe("WebSearch");
        start.Arguments.Count(a => a == "--tools").ShouldBe(1, "the deployment-wide --tools does not apply to this run");
        ValueOf(start.Arguments, "--setting-sources").ShouldBe("user");
        start.Arguments.ShouldNotContain("project,local");

        var request = contributor.Requests.ShouldHaveSingleItem();
        request.Scope.RunId.ShouldBe(run.Id);
        request.Scope.ThreadId.ShouldBe(ThreadId);
        request.Provider.Key.ShouldBe("claude");
        request.Purpose.ShouldBe(CliLaunchProfilePurpose.Launch);
        target.Host.ProfileRequests.ShouldHaveSingleItem().WorkDirectory.ShouldBe(request.WorkDirectory);
    }

    /// <summary>
    /// 档案要了 provider 做不到的收紧（ACP CLI 没有限定内置工具的参数）：这一轮以 LaunchFailed 结束、写明原因，不放宽着跑。
    /// </summary>
    [Fact]
    public async Task AProfileTheProviderCannotHonour_FailsTheRunBeforeAnythingStarts()
    {
        var target = await CreateTargetAsync("kimi");
        target.Host.LaunchProfile = _ => new CliLaunchProfile { BuiltInTools = ["WebSearch"] };
        var run = await SeedClaimedRunAsync();

        await ExecuteAsync(target, CliBuiltInProviders.All["kimi"], run);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.LaunchFailed);
        persisted.Error.ShouldNotBeNull().ShouldContain("built-in tool");
        target.Host.Starts.ShouldBeEmpty();
    }

    /// <summary>
    /// 用户提供的工作目录（团队仓库的 worktree）：进程起来的那一刻与运行结束之后，worktree 里都只有用户自己的文件。
    /// agent 说明经 <c>--append-system-prompt-file</c>、MCP 配置（带回写凭据）经 <c>--mcp-config</c> 交出，两个文件都在运行根里，结束即删。
    /// </summary>
    [Fact]
    public async Task AUserProvidedWorktree_GetsNoManagedFiles_AndTheBriefArrivesAsAnAppendedSystemPrompt()
    {
        var target = await CreateTargetAsync("claude", worktree: true);
        var run = await SeedClaimedRunAsync(userWorkDirectory: Worktree);
        var before = WorktreeFiles(target);

        await ExecuteAsync(target, CliBuiltInProviders.All["claude"], run);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        var start = target.Host.Starts.ShouldHaveSingleItem();
        start.WorkingDirectory.ShouldBe(Worktree);
        target.WorktreeAtStart.ShouldBe(before, "nothing of the framework's is in the worktree while the CLI runs");
        WorktreeFiles(target).ShouldBe(before);

        var systemPrompt = ValueOf(start.Arguments, "--append-system-prompt-file").ShouldNotBeNull();
        var mcpConfig = ValueOf(start.Arguments, "--mcp-config").ShouldNotBeNull();
        systemPrompt.ShouldStartWith(target.Host.WorkspacesRoot);
        mcpConfig.ShouldStartWith(target.Host.WorkspacesRoot);
        target.FilesAtStart[systemPrompt].ShouldContain("Answer briefly.");
        target.FilesAtStart[mcpConfig].ShouldContain("Bearer ", Case.Sensitive, "the credential is in this file, outside the worktree");

        var files = ((InMemoryHostFileSystem)target.Host.FileSystem).Files;
        files.ShouldNotContain(systemPrompt);
        files.ShouldNotContain(mcpConfig);
    }

    /// <summary>
    /// ACP 没有会话级的系统提示参数：用户目录模式下说明内联进新会话的第一条提示，续接的会话里不再重复。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AUserProvidedWorktree_OnAnAcpProvider_PutsTheBriefIntoTheFirstPromptOfANewSessionOnly(bool resuming)
    {
        var target = await CreateTargetAsync("kimi", worktree: true);
        var run = await SeedClaimedRunAsync(userWorkDirectory: Worktree);
        if (resuming)
        {
            await SeedFinishedTurnAsync("acp-earlier");
        }

        var before = WorktreeFiles(target);

        await ExecuteAsync(target, CliBuiltInProviders.All["kimi"], run);

        var prompt = target.Written
            .Select(w => JsonDocument.Parse(w).RootElement)
            .Single(e => e.TryGetProperty("method", out var m) && m.GetString() == "session/prompt")
            .GetProperty("params").GetProperty("prompt")[0].GetProperty("text").GetString()
            .ShouldNotBeNull();
        if (resuming)
        {
            prompt.ShouldBe("do the thing");
        }
        else
        {
            prompt.ShouldContain("Answer briefly.");
            prompt.ShouldEndWith("do the thing");
        }

        WorktreeFiles(target).ShouldBe(before);
        target.WorktreeAtStart.ShouldBe(before, "no AGENTS.md is written into the worktree");
    }

    /// <summary>
    /// 配置目录与项目目录名由使用方按运行给出（每个用户一个 <c>~/.claude</c>、会话记录按项目固定），与用户提供的工作目录一起用。
    /// </summary>
    [Fact]
    public async Task ProvidedConfigDirectory_UsesTheProfilesDirectoryAndProjectName_WithAUserProvidedWorktree()
    {
        var target = await CreateTargetAsync("claude", worktree: true);
        target.Host.LaunchProfile = request => new CliLaunchProfile
        {
            ConfigDirectory = "/home/u/.claude",
            ProjectDirectoryName = "demo",
            SessionRetentionDays = 30,
            Environment = new Dictionary<string, string> { ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1" }
        };
        var provider = CliBuiltInProviders.All["claude"] with { UserConfigIsolation = CliUserConfigIsolation.ProvidedConfigDirectory };
        var run = await SeedClaimedRunAsync(userWorkDirectory: Worktree);

        await ExecuteAsync(target, provider, run);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        var start = target.Host.Starts.ShouldHaveSingleItem();
        start.Environment["CLAUDE_CONFIG_DIR"].ShouldBe("/home/u/.claude");
        start.Environment["CLAUDE_CODE_PROJECT_DIR_NAME"].ShouldBe("demo");
        start.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"].ShouldBe("1");
        JsonNode.Parse(ValueOf(start.Arguments, "--settings")!)!["cleanupPeriodDays"]!.GetValue<int>().ShouldBe(30);
        start.Arguments.ShouldNotContain("--setting-sources", "this tier does not exclude the provided directory's own settings");
    }

    // ── fixtures ─────────────────────────────────────────────────────────────

    private async Task<Target> CreateTargetAsync(string providerKey, bool worktree = false)
    {
        Target? created = null;
        var host = new TestCliHost(
            (spec, _) => Task.FromResult<ICliProcess>(created!.Start(spec)),
            executablePath: Executable);

        var fileSystem = host.FileSystem;
        await fileSystem.CreateDirectoryAsync("/usr/local/bin", CancellationToken.None);
        await fileSystem.WriteAllBytesAsync(Executable, [0x7F, 0x45, 0x4C, 0x46], CancellationToken.None);

        if (worktree)
        {
            // 一个 worktree：用户自己的文件、.git 指针、团队的记忆文件
            await fileSystem.CreateDirectoryAsync(Worktree + "/src", CancellationToken.None);
            await fileSystem.WriteAllTextAsync(Worktree + "/.git", "gitdir: /home/u/projects/demo/.git/worktrees/s1", CancellationToken.None);
            await fileSystem.WriteAllTextAsync(Worktree + "/CLAUDE.md", "# Team notes\n", CancellationToken.None);
            await fileSystem.WriteAllTextAsync(Worktree + "/AGENTS.md", "# Team notes\n", CancellationToken.None);
            await fileSystem.WriteAllTextAsync(Worktree + "/src/app.py", "print('hi')\n", CancellationToken.None);
        }

        created = new Target(host, providerKey);
        return created;
    }

    private static Dictionary<string, string> WorktreeFiles(Target target)
    {
        var fileSystem = (InMemoryHostFileSystem)target.Host.FileSystem;
        return fileSystem.Files
            .Where(f => f.StartsWith(Worktree + "/", StringComparison.Ordinal))
            .ToDictionary(f => f, f => fileSystem.ReadText(f)!);
    }

    /// <summary>一个执行目标，以及它在起进程那一刻看到的东西。</summary>
    private sealed class Target(TestCliHost host, string providerKey)
    {
        private FakeCliAgentTransport? _transport;

        public TestCliHost Host { get; } = host;

        public Dictionary<string, string> WorktreeAtStart { get; private set; } = [];

        public Dictionary<string, string> FilesAtStart { get; private set; } = [];

        public IReadOnlyList<string> Written => _transport?.Written ?? [];

        public ICliProcess Start(CliProcessSpec spec)
        {
            WorktreeAtStart = WorktreeFiles(this);
            var fileSystem = (InMemoryHostFileSystem)Host.FileSystem;
            FilesAtStart = fileSystem.Files.ToDictionary(f => f, f => fileSystem.ReadText(f)!);

            _transport = providerKey == "claude"
                ? new FakeCliAgentTransport(
                [
                    """{"type":"system","subtype":"init","session_id":"sess-remote"}""",
                    """{"type":"result","subtype":"success","session_id":"sess-remote","result":"Done.","num_turns":1}"""
                ])
                : new FakeCliAgentTransport([], onWrite: AcpServer);
            return new ExitedProcess(_transport);
        }

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
                "session/resume" => [Ok(id, """{"sessionId":"acp-earlier"}""")],
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

    private sealed class RecordingContributor(Func<CliLaunchProfile, CliLaunchProfile> adjust) : ICliLaunchProfileContributor
    {
        public List<CliLaunchProfileRequest> Requests { get; } = [];

        public Task<CliLaunchProfile> ContributeAsync(CliLaunchProfileRequest request, CliLaunchProfile profile, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(adjust(profile));
        }
    }

    private async Task<CliRun> SeedClaimedRunAsync(string? userWorkDirectory = null)
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = true,
            WorkDirectoryMode = userWorkDirectory is null ? CliWorkDirectoryMode.PerThread : CliWorkDirectoryMode.UserProvided,
            UserWorkDirectory = userWorkDirectory
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "agent @ TEST-HOST",
            ExecutablePath = Executable,
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

    /// <summary>同一线程上已经结束的一轮，留下了一个会话：这一轮续接它。</summary>
    private async Task SeedFinishedTurnAsync(string sessionId)
    {
        DbContext.Set<CliRun>().Add(new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = ThreadId,
            Status = CliRunStatus.Completed,
            Prompt = "earlier",
            ProviderSessionId = sessionId,
            CompletedAt = DateTime.UtcNow.AddMinutes(-1),
            CreatorId = TestHelper.DefaultTestUserId
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private static string? ValueOf(IReadOnlyList<string> arguments, string flag)
    {
        var index = arguments.ToList().IndexOf(flag);
        return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
    }

    private async Task ExecuteAsync(Target target, CliProviderDescriptor provider, CliRun run, ICliLaunchProfileContributor? contributor = null)
    {
        var runs = ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>();

        var registry = new Mock<ICliProviderRegistry>();
        registry.Setup(r => r.Find(It.IsAny<string>())).Returns(provider);

        var agents = new Mock<IRepository<Agent, Guid>>();
        agents.Setup(r => r.GetAsync(AgentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = AgentId, Name = "profile-agent", Instructions = "Answer briefly." });

        var options = new TestOptionsMonitor<CliAgentOptions>(new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = _configuredLocalRoot,
            WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://platform.example.com/mcp", AllowedTools = ["*"] }
        });

        var executor = new CliRunExecutor(
            runs,
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            agents.Object,
            registry.Object,
            new CliProtocolAdapterFactory(ServiceProvider),
            target.Host,
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
            ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            profileContributors: contributor is null ? null : [contributor]);

        await executor.ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);
    }
}
