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
}
