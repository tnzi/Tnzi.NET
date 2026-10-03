namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// provider 描述表的合并优先级与 fail-closed 缺省。
/// </summary>
public class CliProviderRegistryTests
{
    private static CliProviderRegistry Registry(CliAgentOptions options)
        => new(new TestOptionsMonitor<CliAgentOptions>(options));

    [Fact]
    public void GetAll_IncludesBuiltInProviders()
    {
        var providers = Registry(new CliAgentOptions()).GetAll();

        providers.ShouldContain(p => p.Key == "claude" && p.Protocol == CliAgentProtocol.StreamJson);
        providers.ShouldContain(p => p.Key == "kimi" && p.Protocol == CliAgentProtocol.Acp);
        providers.ShouldContain(p => p.Key == "codex" && p.Protocol == CliAgentProtocol.VendorAppServer);
    }

    [Fact]
    public void ProviderOverride_DisablesWithoutRemovingFromTheCatalogue()
    {
        // 停用不等于消失：管理端仍要能展示「这个 provider 存在，但本部署关了它」。
        var options = new CliAgentOptions
        {
            Providers = { ["claude"] = new CliProviderOptions { Enabled = false } }
        };

        var registry = Registry(options);

        registry.GetAll().ShouldContain(p => p.Key == "claude");
        registry.GetEnabled().ShouldNotContain(p => p.Key == "claude");
    }

    [Fact]
    public void ProviderOverride_AppliesExecutablePathAndDefaults()
    {
        var options = new CliAgentOptions
        {
            Providers =
            {
                ["claude"] = new CliProviderOptions
                {
                    ExecutablePath = "/opt/claude/bin/claude",
                    DefaultModel = "some-model",
                    ExtraArgs = ["--verbose"]
                }
            }
        };

        var claude = Registry(options).Find("claude");

        claude.ShouldNotBeNull();
        claude!.ExecutablePathOverride.ShouldBe("/opt/claude/bin/claude");
        claude.DefaultModel.ShouldBe("some-model");
        claude.ExtraArgs.ShouldContain("--verbose");
    }

    [Fact]
    public void CustomProvider_AddsANewAcpCliWithoutCodeChanges()
    {
        // 这是描述表设计的核心收益：新增一个说 ACP 的 CLI 只要加一条配置。
        var options = new CliAgentOptions
        {
            CustomProviders =
            [
                new CliCustomProviderOptions
                {
                    Key = "newcli",
                    DisplayName = "New CLI",
                    Protocol = CliAgentProtocol.Acp,
                    DefaultExecutable = "newcli",
                    LaunchArgs = ["acp"],
                    BriefFileName = "AGENTS.md"
                }
            ]
        };

        var provider = Registry(options).Find("newcli");

        provider.ShouldNotBeNull();
        provider!.Protocol.ShouldBe(CliAgentProtocol.Acp);
        provider.LaunchArgs.ShouldContain("acp");
        // 协议契约参数由协议族继承而来，配置方不需要（也就不会漏）声明。
        provider.BlockedArgs.ShouldContainKey("acp");
    }

    [Fact]
    public void CustomProvider_IsAlwaysFailClosedOnResumeRejectionDetection()
    {
        // 框架没验证过自定义 provider 能否区分「resume 被拒」与其他失败。
        // 「分不清」时绝不做 fresh-session 重试 —— 猜错的代价是丢掉整段可恢复的上下文。
        var options = new CliAgentOptions
        {
            CustomProviders =
            [
                new CliCustomProviderOptions
                {
                    Key = "newcli",
                    Protocol = CliAgentProtocol.Acp,
                    DefaultExecutable = "newcli"
                }
            ]
        };

        Registry(options).Find("newcli")!.ResumeRejectionDetectable.ShouldBeFalse();
    }

    [Fact]
    public void CustomProvider_WithoutBriefFileName_RequiresInlineSystemPrompt()
    {
        var options = new CliAgentOptions
        {
            CustomProviders =
            [
                new CliCustomProviderOptions
                {
                    Key = "inline-only",
                    Protocol = CliAgentProtocol.Acp,
                    DefaultExecutable = "inline-only"
                }
            ]
        };

        Registry(options).Find("inline-only")!.RequiresInlineSystemPrompt.ShouldBeTrue();
    }

    [Fact]
    public void CustomProvider_WithMissingExecutable_IsSkippedInsteadOfBreakingTheCatalogue()
    {
        // 一条错配置不该连带弄坏其余 provider 的解析。校验器已在启动期报过错。
        var options = new CliAgentOptions
        {
            CustomProviders = [new CliCustomProviderOptions { Key = "broken" }]
        };

        var registry = Registry(options);
        registry.Find("broken").ShouldBeNull();
        registry.Find("claude").ShouldNotBeNull();
    }

    [Fact]
    public void Find_IsCaseInsensitive()
        => Registry(new CliAgentOptions()).Find("CLAUDE").ShouldNotBeNull();

    [Fact]
    public void ProviderOverride_CarriesIsolationSettings()
    {
        var options = new CliAgentOptions
        {
            Providers =
            {
                ["claude"] = new CliProviderOptions
                {
                    UserConfigIsolation = CliUserConfigIsolation.Inherit,
                    ConfigDirectory = "/srv/claude-home"
                }
            }
        };

        var claude = Registry(options).Find("claude")!;

        claude.UserConfigIsolation.ShouldBe(CliUserConfigIsolation.Inherit);
        claude.ConfigDirectory.ShouldBe("/srv/claude-home");
        claude.ConfigDirectoryEnvironmentVariable.ShouldBe("CLAUDE_CONFIG_DIR");
    }

    [Fact]
    public void BuiltInProvider_DefaultsToExcludingUserSettings()
        => Registry(new CliAgentOptions()).Find("claude")!.UserConfigIsolation.ShouldBe(CliUserConfigIsolation.ExcludeUserSettings);
}

/// <summary>
/// 配置校验：关闭时不阻塞启动，开启时守住会静默出错的组合。
/// </summary>
public class CliAgentOptionsValidatorTests
{
    private static List<string> Validate(CliAgentOptions options)
    {
        var errors = new List<string>();
        var validator = new CliAgentOptionsValidator();
        var method = typeof(CliAgentOptionsValidator)
            .GetMethod("ValidateOptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(validator, [options, errors]);
        return errors;
    }

    [Fact]
    public void DefaultWorkspacesRoot_DoesNotRequireTheDataDirectoryToExistYet()
    {
        // 默认的 SpecialFolderOption.None 在 Unix 上对尚不存在的目录返回空串，夹具只在 DoNotVerify 下回答路径。
        var notYetCreated = Path.Combine(Path.GetTempPath(), $"no-such-dir-{Guid.NewGuid():N}");

        var root = CliWorkspaceLayout.ResolveDefaultWorkspacesRoot((_, option) =>
            option == Environment.SpecialFolderOption.DoNotVerify ? notYetCreated : string.Empty);

        root.ShouldBe(Path.Combine(notYetCreated, "Tnzi", "agent-workspaces"));
    }

    [Fact]
    public void DefaultWorkspacesRoot_UnresolvableHost_IsEmpty_NotARelativePath()
    {
        // 拼出 "Tnzi/agent-workspaces" 这样的相对路径会让工作区静默落进进程当前目录。
        CliWorkspaceLayout.ResolveDefaultWorkspacesRoot((_, _) => string.Empty).ShouldBe(string.Empty);
    }

    [Fact]
    public void Enabled_WithoutWorkspacesRoot_OnAHostWithoutADataDirectory_Fails()
    {
        var errors = new List<string>();
        CliAgentOptionsValidator.ValidateWorkspacesRoot(new CliAgentOptions { Enabled = true }, defaultWorkspacesRoot: string.Empty, errors);

        errors.ShouldHaveSingleItem().ShouldContain("AI:Cli:WorkspacesRoot");
    }

    [Fact]
    public void Enabled_WithExplicitWorkspacesRoot_OnAHostWithoutADataDirectory_Passes()
    {
        var errors = new List<string>();
        var options = new CliAgentOptions { Enabled = true, WorkspacesRoot = Path.Combine(Path.GetTempPath(), "ws") };
        CliAgentOptionsValidator.ValidateWorkspacesRoot(options, defaultWorkspacesRoot: string.Empty, errors);

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Disabled_SkipsEveryCheck()
    {
        // 一个被关掉的可选模块不该有能力阻塞应用启动。
        var options = new CliAgentOptions
        {
            Enabled = false,
            MaxConcurrentRuns = 0,
            LeaseDuration = TimeSpan.Zero
        };

        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void Enabled_RejectsLeaseShorterThanTwiceThePollInterval()
    {
        // 续期赶不上过期，运行中的任务会被自己的回收器抢走并重跑一遍。
        var options = new CliAgentOptions
        {
            Enabled = true,
            PollInterval = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromSeconds(30)
        };

        Validate(options).ShouldContain(e => e.Contains("LeaseDuration"));
    }

    [Fact]
    public void Enabled_WriteBackWithoutAllowedTools_Fails()
    {
        // 回写面是一个安全决定：启用回写而不写 AllowedTools 必须在启动期被拒，
        // 而不是运行期让每一枚凭据静默拿到整个 MCP 面（或静默一个都调不了）。
        var options = new CliAgentOptions
        {
            Enabled = true,
            WriteBack = new CliWriteBackOptions { Enabled = true, McpEndpoint = "https://api.example.com/mcp" }
        };

        Validate(options).ShouldContain(e => e.Contains("AI:Cli:WriteBack:AllowedTools"));
    }

    [Fact]
    public void Enabled_WriteBackWithOnlyBlankAllowedTools_Fails()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            WriteBack = new CliWriteBackOptions { Enabled = true, AllowedTools = [" ", ""] }
        };

        Validate(options).ShouldContain(e => e.Contains("AI:Cli:WriteBack:AllowedTools"));
    }

    [Fact]
    public void Enabled_WriteBackWithWildcard_Passes()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            WriteBack = new CliWriteBackOptions { Enabled = true, AllowedTools = ["*"] }
        };

        Validate(options).ShouldNotContain(e => e.Contains("AllowedTools"));
    }

    [Fact]
    public void Enabled_WriteBackDisabled_DoesNotRequireAllowedTools()
    {
        var options = new CliAgentOptions { Enabled = true, WriteBack = new CliWriteBackOptions { Enabled = false } };

        Validate(options).ShouldNotContain(e => e.Contains("AllowedTools"));
    }

    [Fact]
    public void Enabled_RejectsArtifactPatternWithPathSeparator()
    {
        // 一条 "../.." 就能把回收器变成删库工具。
        var options = new CliAgentOptions
        {
            Enabled = true,
            Gc = { ArtifactPatterns = ["../.."] }
        };

        Validate(options).ShouldContain(e => e.Contains("ArtifactPatterns"));
    }

    [Fact]
    public void Enabled_RejectsCustomProviderOnAnUnimplementedProtocol()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            CustomProviders =
            [
                new CliCustomProviderOptions
                {
                    Key = "vendor",
                    DefaultExecutable = "vendor",
                    Protocol = CliAgentProtocol.VendorAppServer
                }
            ]
        };

        Validate(options).ShouldContain(e => e.Contains("VendorAppServer"));
    }

    [Fact]
    public void Enabled_RejectsIsolatedConfigDirectoryOnAProviderThatCannotRelocateIt()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            Providers = { ["kimi"] = new CliProviderOptions { UserConfigIsolation = CliUserConfigIsolation.IsolatedConfigDirectory } }
        };

        Validate(options).ShouldContain(e => e.Contains("'kimi'") && e.Contains("IsolatedConfigDirectory"));
    }

    [Fact]
    public void Enabled_RejectsFallbackTokenOnAProviderWithoutATokenVariable()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            Providers = { ["kimi"] = new CliProviderOptions { FallbackAuthToken = "token" } }
        };

        Validate(options).ShouldContain(e => e.Contains("'kimi'") && e.Contains("FallbackAuthToken"));
    }

    [Fact]
    public void Enabled_RejectsARelativeConfigDirectory()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            Providers = { ["claude"] = new CliProviderOptions { ConfigDirectory = "relative/home" } }
        };

        Validate(options).ShouldContain(e => e.Contains("ConfigDirectory must be an absolute path"));
    }

    [Fact]
    public void Enabled_AcceptsClaudeWithIsolationAndFallbackToken()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            Providers =
            {
                ["claude"] = new CliProviderOptions
                {
                    UserConfigIsolation = CliUserConfigIsolation.IsolatedConfigDirectory,
                    FallbackAuthToken = "token"
                }
            }
        };

        Validate(options).ShouldBeEmpty();
    }

    /// <summary>自定义 provider 声明了能力就能用；没声明就与内置的 ACP 项一样被拒。</summary>
    [Fact]
    public void Enabled_JudgesCustomProvidersByWhatTheyDeclare()
    {
        var declared = new CliCustomProviderOptions
        {
            Key = "forked-claude",
            DefaultExecutable = "forked",
            Protocol = CliAgentProtocol.StreamJson,
            ConfigDirectoryEnvironmentVariable = "FORKED_CONFIG_DIR",
            AuthTokenEnvironmentVariable = "FORKED_TOKEN",
            UserConfigIsolation = CliUserConfigIsolation.IsolatedConfigDirectory,
            FallbackAuthToken = "token"
        };
        var undeclared = new CliCustomProviderOptions
        {
            Key = "bare",
            DefaultExecutable = "bare",
            UserConfigIsolation = CliUserConfigIsolation.IsolatedConfigDirectory
        };

        Validate(new CliAgentOptions { Enabled = true, CustomProviders = [declared] }).ShouldBeEmpty();
        Validate(new CliAgentOptions { Enabled = true, CustomProviders = [undeclared] })
            .ShouldContain(e => e.Contains("'bare'"));
    }
}
