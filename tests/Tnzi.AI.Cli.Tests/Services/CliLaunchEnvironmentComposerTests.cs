namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 个人配置隔离档位与兜底令牌。
/// </summary>
public class CliLaunchEnvironmentComposerTests : IDisposable
{
    private const string Token = "sk-ant-oat01-test-token";
    private const string Executable = "/opt/claude";

    private readonly string _root = TestScratch.NewPath("env");
    private readonly Mock<ICliAuthStatusProbe> _probe = new();
    private readonly CliLaunchEnvironmentComposer _composer;

    public CliLaunchEnvironmentComposerTests()
    {
        _composer = new CliLaunchEnvironmentComposer(_probe.Object, NullLogger<CliLaunchEnvironmentComposer>.Instance);
    }

    /// <summary>每线程工作区的形状：运行根以线程命名，工作目录是它下面的 <c>workdir</c>。</summary>
    private string RunRoot(string thread = "thread-1") => Path.Combine(_root, "host", thread);

    private CliWorkspace Workspace(string thread = "thread-1", bool userOwned = false) => new()
    {
        RootDirectory = RunRoot(thread),
        WorkDirectory = Path.Combine(RunRoot(thread), CliWorkspaceLayout.WorkDirectoryName),
        OutputDirectory = Path.Combine(RunRoot(thread), CliWorkspaceLayout.OutputDirectoryName),
        LogDirectory = Path.Combine(RunRoot(thread), CliWorkspaceLayout.LogDirectoryName),
        WorkDirectoryIsUserOwned = userOwned
    };

    public void Dispose()
    {
        TestScratch.Delete(_root);

        GC.SuppressFinalize(this);
    }

    private static CliProviderDescriptor Claude(CliUserConfigIsolation isolation = CliUserConfigIsolation.ExcludeUserSettings, string? configDirectory = null)
        => CliBuiltInProviders.All["claude"] with { UserConfigIsolation = isolation, ConfigDirectory = configDirectory };

    private CliAgentOptions Options(string? token = null) => new()
    {
        Enabled = true,
        WorkspacesRoot = _root,
        Providers = { ["claude"] = new CliProviderOptions { FallbackAuthToken = token } }
    };

    private Task<CliLaunchEnvironment> ComposeAsync(
        CliProviderDescriptor provider, CliAgentOptions options, CliWorkspace? workspace = null, CliLaunchProfile? profile = null)
        => _composer.ComposeAsync(
            provider, Executable, workspace ?? Workspace(), LocalHosts.Create(options), options, profile ?? CliLaunchProfile.Local, CancellationToken.None);

    private void ProbeAnswers(bool? signedIn)
        => _probe.Setup(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(signedIn);

    [Fact]
    public async Task DefaultIsolation_ExcludesUserSettingSources()
    {
        var result = await ComposeAsync(Claude(), Options());

        result.Args.ShouldBe(["--setting-sources", "project,local"]);
        result.Environment.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inherit_AddsNothing()
    {
        var result = await ComposeAsync(Claude(CliUserConfigIsolation.Inherit), Options());

        result.Args.ShouldBeEmpty();
        result.Environment.ShouldBeEmpty();
    }

    /// <summary>默认档对不支持它的 provider 只是不加参数，不能因此让运行失败。</summary>
    [Fact]
    public async Task DefaultIsolation_OnAProviderWithoutSupport_IsANoOp()
    {
        var result = await ComposeAsync(CliBuiltInProviders.All["kimi"], Options());

        result.Args.ShouldBeEmpty();
        result.Environment.ShouldBeEmpty();
    }

    [Fact]
    public async Task IsolatedConfigDirectory_DefaultsUnderTheWorkspacesRootAndCreatesIt()
    {
        var result = await ComposeAsync(Claude(CliUserConfigIsolation.IsolatedConfigDirectory), Options());

        var expected = Path.Combine(_root, CliWorkspaceLayout.ConfigDirectoryName, "claude");
        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(expected);
        Directory.Exists(expected).ShouldBeTrue();
        result.Args.ShouldBeEmpty();
    }

    [Fact]
    public async Task IsolatedConfigDirectory_HonoursAnExplicitDirectory()
    {
        var explicitDirectory = Path.Combine(_root, "custom-home");

        var result = await ComposeAsync(Claude(CliUserConfigIsolation.IsolatedConfigDirectory, explicitDirectory), Options());

        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(explicitDirectory);
    }

    /// <summary>部署方选了隔离而 provider 做不到：宁可失败，也不以未隔离的状态跑起来。</summary>
    [Fact]
    public async Task IsolatedConfigDirectory_OnAProviderWithoutSupport_Throws()
    {
        var kimi = CliBuiltInProviders.All["kimi"] with { UserConfigIsolation = CliUserConfigIsolation.IsolatedConfigDirectory };

        await Should.ThrowAsync<CliProviderConfigurationException>(() => ComposeAsync(kimi, Options()));
    }

    [Fact]
    public async Task NoFallbackToken_NeverProbes()
    {
        await ComposeAsync(Claude(), Options());

        _probe.Verify(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// 已登录时不注入：环境里的令牌优先级高于本机登录，注入了就顶掉了它。
    /// </summary>
    [Fact]
    public async Task FallbackToken_SignedIn_IsNotInjected()
    {
        ProbeAnswers(true);

        var result = await ComposeAsync(Claude(), Options(Token));

        result.Environment.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN").ShouldBeFalse();
        result.UsesFallbackToken.ShouldBeFalse();
    }

    [Fact]
    public async Task FallbackToken_SignedOut_IsInjected()
    {
        ProbeAnswers(false);

        var result = await ComposeAsync(Claude(), Options(Token));

        result.Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe(Token);
        result.UsesFallbackToken.ShouldBeTrue();
    }

    /// <summary>查不出来按「没登录」处理：配置了兜底，就是宁可用它也不要在认证上失败。</summary>
    [Fact]
    public async Task FallbackToken_StatusUnknown_IsInjected()
    {
        ProbeAnswers(null);

        var result = await ComposeAsync(Claude(), Options(Token));

        result.Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe(Token);
    }

    /// <summary>
    /// 探针必须在运行将要使用的同一份环境里查：隔离配置目录下，宿主账号自己的登录态与运行无关。
    /// </summary>
    [Fact]
    public async Task Probe_SeesTheSameConfigDirectoryAndWhitelistAsTheRun()
    {
        CliProcessSpec? probed = null;
        _probe
            .Setup(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ICliProcessHost, CliProcessSpec, CancellationToken>((_, spec, _) => probed = spec)
            .ReturnsAsync(false);
        var options = Options(Token);
        options.EnvironmentWhitelist.Add("ANTHROPIC_API_KEY");

        await ComposeAsync(Claude(CliUserConfigIsolation.IsolatedConfigDirectory), options);

        probed.ShouldNotBeNull();
        probed.ExecutablePath.ShouldBe(Executable);
        probed.Arguments.ShouldBe(["auth", "status", "--json"]);
        probed.WorkingDirectory.ShouldBe(Workspace().WorkDirectory);
        probed.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(Path.Combine(_root, CliWorkspaceLayout.ConfigDirectoryName, "claude"));
        probed.Environment.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN").ShouldBeFalse();
        probed.EnvironmentWhitelist.ShouldContain("ANTHROPIC_API_KEY");
    }

    /// <summary>自定义 provider 替换内置项时，令牌取自它自己的配置。</summary>
    [Fact]
    public async Task FallbackToken_ComesFromTheCustomProviderThatReplacesTheBuiltIn()
    {
        ProbeAnswers(false);
        var options = new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = _root,
            Providers = { ["claude"] = new CliProviderOptions { FallbackAuthToken = "stale" } },
            CustomProviders = [new CliCustomProviderOptions { Key = "claude", FallbackAuthToken = Token }]
        };

        var result = await ComposeAsync(Claude(), options);

        result.Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe(Token);
    }

    [Fact]
    public async Task FallbackToken_OnAProviderWithoutATokenVariable_Throws()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = _root,
            Providers = { ["kimi"] = new CliProviderOptions { FallbackAuthToken = Token } }
        };

        await Should.ThrowAsync<CliProviderConfigurationException>(
            () => _composer.ComposeAsync(
                CliBuiltInProviders.All["kimi"], Executable, Workspace(), LocalHosts.Create(options), options, CliLaunchProfile.Local, CancellationToken.None));
    }

    /// <summary>
    /// 每工作区配置目录在运行根下、工作目录之外：工作区被删时它跟着删，CLI 写下的会话记录也就跟着没了。
    /// </summary>
    [Fact]
    public async Task PerWorkspaceConfigDirectory_LivesInTheWorkspaceRunRoot_OutsideTheWorkDirectory()
    {
        var result = await ComposeAsync(Claude(CliUserConfigIsolation.PerWorkspaceConfigDirectory), Options());

        var expected = Path.Combine(RunRoot(), CliWorkspaceLayout.ConfigDirectoryName, "claude");
        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(expected);
        Directory.Exists(expected).ShouldBeTrue();
        expected.ShouldNotStartWith(Workspace().WorkDirectory);
        result.Args.ShouldBeEmpty();
    }

    [Fact]
    public async Task PerWorkspaceConfigDirectory_EachThreadGetsItsOwn_TheSameThreadKeepsItsOwn()
    {
        var provider = Claude(CliUserConfigIsolation.PerWorkspaceConfigDirectory);

        var first = await ComposeAsync(provider, Options(), Workspace("thread-a"));
        var nextTurn = await ComposeAsync(provider, Options(), Workspace("thread-a"));
        var otherThread = await ComposeAsync(provider, Options(), Workspace("thread-b"));

        nextTurn.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(first.Environment["CLAUDE_CONFIG_DIR"], "the next turn resumes from the same transcript");
        otherThread.Environment["CLAUDE_CONFIG_DIR"].ShouldNotBe(first.Environment["CLAUDE_CONFIG_DIR"]);
    }

    /// <summary>
    /// 用户提供的工作目录按运行分运行根：每一轮一个新目录、续接不上，清理也不删它。两个承诺都兑现不了，宁可启动失败。
    /// </summary>
    [Fact]
    public async Task PerWorkspaceConfigDirectory_WithAUserProvidedWorkDirectory_Throws()
    {
        await Should.ThrowAsync<CliProviderConfigurationException>(() => ComposeAsync(
            Claude(CliUserConfigIsolation.PerWorkspaceConfigDirectory), Options(), Workspace(userOwned: true)));
    }

    [Fact]
    public async Task PerWorkspaceConfigDirectory_OnAProviderWithoutSupport_Throws()
    {
        var kimi = CliBuiltInProviders.All["kimi"] with { UserConfigIsolation = CliUserConfigIsolation.PerWorkspaceConfigDirectory };

        await Should.ThrowAsync<CliProviderConfigurationException>(() => ComposeAsync(kimi, Options()));
    }

    /// <summary>
    /// 目录里没有存储的登录：兜底令牌的探针在这个目录里查，答「没登录」，令牌照常注入。
    /// </summary>
    [Fact]
    public async Task PerWorkspaceConfigDirectory_ProbesInsideIt_AndInjectsTheFallbackToken()
    {
        CliProcessSpec? probed = null;
        _probe
            .Setup(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ICliProcessHost, CliProcessSpec, CancellationToken>((_, spec, _) => probed = spec)
            .ReturnsAsync(false);

        var result = await ComposeAsync(Claude(CliUserConfigIsolation.PerWorkspaceConfigDirectory), Options(Token));

        probed.ShouldNotBeNull();
        probed.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(result.Environment["CLAUDE_CONFIG_DIR"]);
        result.Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe(Token);
    }

    /// <summary>
    /// 远程目标：配置目录按目标的路径语法建在目标的文件系统上，登录态也在目标上查，本机磁盘一个字节都不写。
    /// </summary>
    [Fact]
    public async Task OnARemoteTarget_TheConfigDirectoryAndTheProbeStayOnThatTarget()
    {
        ICliProcessHost? probedOn = null;
        _probe
            .Setup(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ICliProcessHost, CliProcessSpec, CancellationToken>((host, _, _) => probedOn = host)
            .ReturnsAsync(false);
        var remote = new TestCliHost((_, _) => throw new InvalidOperationException("not started"), workspacesRoot: "/home/agent/workspaces");
        var fileSystem = (InMemoryHostFileSystem)remote.FileSystem;

        var result = await _composer.ComposeAsync(
            Claude(CliUserConfigIsolation.IsolatedConfigDirectory), "/usr/local/bin/claude", new CliWorkspace
            {
                RootDirectory = "/home/agent/workspaces/host/thread-1",
                WorkDirectory = "/home/agent/workspaces/host/thread-1/workdir",
                OutputDirectory = "/home/agent/workspaces/host/thread-1/output",
                LogDirectory = "/home/agent/workspaces/host/thread-1/logs"
            }, remote, Options(Token), CliLaunchProfile.Default, CancellationToken.None);

        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe("/home/agent/workspaces/.cli-config/claude");
        (await fileSystem.DirectoryExistsAsync("/home/agent/workspaces/.cli-config/claude", CancellationToken.None)).ShouldBeTrue();
        Directory.Exists(_root).ShouldBeFalse("nothing is written to the API host's disk");
        probedOn.ShouldBeSameAs(remote);
    }

    // ── 启动档案 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 档案点名的设置来源取代隔离档位的那一组，并被接管：部署级参数里同名的开关排在后面也盖不掉它。
    /// </summary>
    /// <remarks>
    /// 工作目录是用户仓库时只读 <c>user</c> 来源：仓库提交的 <c>.claude/settings.json</c> 能把平台的令牌引到别处（实测 claude 2.1）。
    /// </remarks>
    [Fact]
    public async Task Profile_SettingSources_ReplaceTheIsolationArgs_AndAreOwned()
    {
        var result = await ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile { SettingSources = ["user"] });

        result.Args.ShouldBe(["--setting-sources", "user"]);
        result.OwnedArgs.ShouldContainKeyAndValue("--setting-sources", BlockedArgMode.WithValue);
    }

    /// <summary>附加设置以 JSON 文本交给 CLI，会话记录保留天数并进同一个对象（claude 的 <c>cleanupPeriodDays</c>）。</summary>
    [Fact]
    public async Task Profile_SettingsAndRetention_GoInOneSettingsObject()
    {
        var result = await ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile
        {
            SettingSources = ["user"],
            SettingsJson = """{"permissions":{"deny":["Bash(touch:*)"]}}""",
            SessionRetentionDays = 30
        });

        var index = result.Args.ToList().IndexOf("--settings");
        index.ShouldBeGreaterThanOrEqualTo(0);
        var settings = JsonNode.Parse(result.Args[index + 1])!.AsObject();
        settings["permissions"]!["deny"]![0]!.GetValue<string>().ShouldBe("Bash(touch:*)");
        settings["cleanupPeriodDays"]!.GetValue<int>().ShouldBe(30);
        result.OwnedArgs.ShouldContainKey("--settings");
    }

    [Theory]
    [InlineData("[1, 2]")]
    [InlineData("{ not json")]
    public async Task Profile_SettingsThatAreNotAJsonObject_FailTheLaunch(string settings)
    {
        await Should.ThrowAsync<CliProviderConfigurationException>(
            () => ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile { SettingsJson = settings }));
    }

    /// <summary>内置工具按次限定：一个名单、一个都不开（空串）、不限定（不带参数），三者不同。</summary>
    [Fact]
    public async Task Profile_BuiltInTools_AreListed_OrDisabledWithAnEmptyValue()
    {
        var some = await ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile { BuiltInTools = ["WebSearch", "WebFetch"] });
        var none = await ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile { BuiltInTools = [] });
        var all = await ComposeAsync(Claude(), Options(), profile: new CliLaunchProfile());

        some.Args.ShouldBe(["--setting-sources", "project,local", "--tools", "WebSearch,WebFetch"]);
        some.OwnedArgs.ShouldContainKey("--tools");
        none.Args.ShouldBe(["--setting-sources", "project,local", "--tools", ""]);
        all.Args.ShouldNotContain("--tools");
        all.OwnedArgs.ShouldNotContainKey("--tools");
    }

    /// <summary>档案要了 provider 做不到的收紧：宁可这一轮失败，也不放宽着跑。</summary>
    [Fact]
    public async Task Profile_AskingForACapabilityTheProviderLacks_FailsTheLaunch()
    {
        var kimi = CliBuiltInProviders.All["kimi"];

        var refused = await Should.ThrowAsync<CliProviderConfigurationException>(
            () => ComposeAsync(kimi, Options(), profile: new CliLaunchProfile { BuiltInTools = ["WebSearch"] }));
        refused.Message.ShouldContain(nameof(CliProviderDescriptor.BuiltInToolsArg));

        await Should.ThrowAsync<CliProviderConfigurationException>(
            () => ComposeAsync(kimi, Options(), profile: new CliLaunchProfile { SettingSources = ["user"] }));
    }

    /// <summary>
    /// 附加目录逐个给出，用户目录模式下的技能目录排在最后；agent 说明以追加系统提示文件交出。两者都只追加、不接管。
    /// </summary>
    [Fact]
    public async Task Profile_AdditionalDirectories_AndTheWorkspaceContextAndSystemPrompt_AreAppended()
    {
        var workspace = Workspace(userOwned: true) with
        {
            ContextDirectory = "/ws/run/.tnzi/context",
            SystemPromptFilePath = "/ws/run/.tnzi/system-prompt.md"
        };

        var result = await ComposeAsync(Claude(), Options(), workspace, new CliLaunchProfile
        {
            AdditionalDirectories = ["/home/u/.tuanzi/projects/demo", "/opt/platform"]
        });

        result.Args.ShouldBe(
        [
            "--setting-sources", "project,local",
            "--add-dir", "/home/u/.tuanzi/projects/demo",
            "--add-dir", "/opt/platform",
            "--add-dir", "/ws/run/.tnzi/context",
            "--append-system-prompt-file", "/ws/run/.tnzi/system-prompt.md"
        ]);
        result.OwnedArgs.ShouldNotContainKey("--add-dir");
    }

    /// <summary>档案给的环境变量照设；与框架自己设置的同名时以框架的为准（档案改不走配置目录与令牌）。</summary>
    [Fact]
    public async Task Profile_Environment_IsSet_ButTheFrameworksOwnVariablesWin()
    {
        ProbeAnswers(false);

        var result = await ComposeAsync(Claude(CliUserConfigIsolation.IsolatedConfigDirectory), Options(Token), profile: new CliLaunchProfile
        {
            Environment = new Dictionary<string, string>
            {
                ["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] = "1",
                ["CLAUDE_CONFIG_DIR"] = "/somewhere/else",
                ["CLAUDE_CODE_OAUTH_TOKEN"] = "not-the-configured-one"
            }
        });

        result.Environment["CLAUDE_CODE_DISABLE_AUTO_MEMORY"].ShouldBe("1");
        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(Path.Combine(_root, CliWorkspaceLayout.ConfigDirectoryName, "claude"));
        result.Environment["CLAUDE_CODE_OAUTH_TOKEN"].ShouldBe(Token);
    }

    /// <summary>
    /// 使用方给的配置目录与项目目录名：用户提供的工作目录也行（这正是这一档的用途），登录态探针在同一份环境里查。
    /// </summary>
    [Fact]
    public async Task ProvidedConfigDirectory_TakesTheDirectoryAndProjectNameFromTheProfile_WithAUserProvidedWorkDirectory()
    {
        CliProcessSpec? probed = null;
        _probe
            .Setup(p => p.IsSignedInAsync(It.IsAny<ICliProcessHost>(), It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Callback<ICliProcessHost, CliProcessSpec, CancellationToken>((_, spec, _) => probed = spec)
            .ReturnsAsync(true);
        var configDirectory = Path.Combine(_root, "user-home", ".claude");

        var result = await ComposeAsync(
            Claude(CliUserConfigIsolation.ProvidedConfigDirectory), Options(Token), Workspace(userOwned: true),
            new CliLaunchProfile { ConfigDirectory = configDirectory, ProjectDirectoryName = "demo" });

        result.Environment["CLAUDE_CONFIG_DIR"].ShouldBe(configDirectory);
        result.Environment["CLAUDE_CODE_PROJECT_DIR_NAME"].ShouldBe("demo");
        Directory.Exists(configDirectory).ShouldBeTrue();
        result.UsesFallbackToken.ShouldBeFalse("the provided directory has its own sign-in");
        probed.ShouldNotBeNull().Environment["CLAUDE_CODE_PROJECT_DIR_NAME"].ShouldBe("demo");
    }

    [Fact]
    public async Task ProvidedConfigDirectory_WithoutADirectoryInTheProfile_FailsTheLaunch()
    {
        var refused = await Should.ThrowAsync<CliProviderConfigurationException>(
            () => ComposeAsync(Claude(CliUserConfigIsolation.ProvidedConfigDirectory), Options(), profile: CliLaunchProfile.Default));

        refused.Message.ShouldContain("launch profile");
    }
}
