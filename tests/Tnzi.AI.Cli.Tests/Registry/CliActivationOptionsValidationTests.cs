namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 启动校验：唤起与在线判定的时长、本机按运行指定工作目录的根、版本范围的写法。
/// </summary>
public class CliActivationOptionsValidationTests
{
    [Fact]
    public void TheDefaults_PassValidation()
        => Validate(new CliAgentOptions { Enabled = true, WorkspacesRoot = Path.Combine(Path.GetTempPath(), "tnzi-cli-ws") }).ShouldBeEmpty();

    [Fact]
    public void AnOnlineWindowNotLongerThanTwoPolls_IsRejected()
    {
        var options = new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = Path.Combine(Path.GetTempPath(), "tnzi-cli-ws"),
            Activation = new CliActivationOptions { OnlineTtl = TimeSpan.FromSeconds(6) }
        };

        Validate(options).ShouldContain(e => e.Contains("OnlineTtl", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAssignedWorkDirectoryRoot_MustBeAbsolute_AndMustNotOverlapTheManagedWorkspacesRoot()
    {
        var workspaces = Path.Combine(Path.GetTempPath(), "tnzi-cli-ws");
        var options = new CliAgentOptions
        {
            Enabled = true,
            WorkspacesRoot = workspaces,
            AssignedWorkDirectoryRoots = ["relative", Path.Combine(workspaces, "chat"), Path.Combine(Path.GetTempPath(), "tnzi-chat")]
        };

        var errors = Validate(options);

        errors.Count(e => e.Contains("AssignedWorkDirectoryRoots", StringComparison.Ordinal)).ShouldBe(2);
        errors.ShouldContain(e => e.Contains("absolute", StringComparison.Ordinal));
        errors.ShouldContain(e => e.Contains("overlaps", StringComparison.Ordinal));
    }

    [Fact]
    public void AVersionBoundThatIsNotANumber_IsRejected()
    {
        var options = new CliAgentOptions { Enabled = true, WorkspacesRoot = Path.Combine(Path.GetTempPath(), "tnzi-cli-ws") };
        options.Providers["claude"] = new CliProviderOptions { MinimumVersion = "latest" };

        Validate(options).ShouldContain(e => e.Contains("MinimumVersion", StringComparison.Ordinal));
    }

    private static List<string> Validate(CliAgentOptions options)
    {
        var result = new CliAgentOptionsValidator().Validate(null, options);
        return result.Failed ? [.. result.Failures] : [];
    }
}
