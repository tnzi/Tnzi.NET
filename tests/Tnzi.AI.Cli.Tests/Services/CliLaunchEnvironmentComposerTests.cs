namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 个人配置隔离档位与兜底令牌。
/// </summary>
public class CliLaunchEnvironmentComposerTests : IDisposable
{
    private const string Token = "sk-ant-oat01-test-token";
    private const string Executable = "/opt/claude";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tnzi-cli-env-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ICliAuthStatusProbe> _probe = new();
    private readonly CliLaunchEnvironmentComposer _composer;

    public CliLaunchEnvironmentComposerTests()
    {
        _composer = new CliLaunchEnvironmentComposer(_probe.Object, NullLogger<CliLaunchEnvironmentComposer>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // 没建过就不必删。
        }

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

    private Task<CliLaunchEnvironment> ComposeAsync(CliProviderDescriptor provider, CliAgentOptions options)
        => _composer.ComposeAsync(provider, Executable, _root, options, CancellationToken.None);

    private void ProbeAnswers(bool? signedIn)
        => _probe.Setup(p => p.IsSignedInAsync(It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(signedIn);

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

        _probe.Verify(p => p.IsSignedInAsync(It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()), Times.Never);
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
            .Setup(p => p.IsSignedInAsync(It.IsAny<CliProcessSpec>(), It.IsAny<CancellationToken>()))
            .Callback<CliProcessSpec, CancellationToken>((spec, _) => probed = spec)
            .ReturnsAsync(false);
        var options = Options(Token);
        options.EnvironmentWhitelist.Add("ANTHROPIC_API_KEY");

        await ComposeAsync(Claude(CliUserConfigIsolation.IsolatedConfigDirectory), options);

        probed.ShouldNotBeNull();
        probed.ExecutablePath.ShouldBe(Executable);
        probed.Arguments.ShouldBe(["auth", "status", "--json"]);
        probed.WorkingDirectory.ShouldBe(_root);
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
            () => _composer.ComposeAsync(CliBuiltInProviders.All["kimi"], Executable, _root, options, CancellationToken.None));
    }
}
