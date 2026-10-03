namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 登录状态探针。进程用例一律跑测试自建的脚本，绝不调用本机安装的 CLI。
/// </summary>
public class CliAuthStatusProbeTests : IDisposable
{
    private const string StateVariable = "TNZI_TEST_PROBE_STATE";

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "tnzi-cli-probe-" + Guid.NewGuid().ToString("N"));
    private readonly CliAuthStatusProbe _probe = new(NullLogger<CliAuthStatusProbe>.Instance);

    public CliAuthStatusProbeTests()
    {
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
            // 清理失败不该让测试红。
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("{\"loggedIn\": true, \"authMethod\": \"claude.ai\"}", true)]
    [InlineData("{\"loggedIn\": false, \"authMethod\": \"none\"}", false)]
    [InlineData("{\"authMethod\": \"none\"}", null)]
    [InlineData("{\"loggedIn\": \"yes\"}", null)]
    [InlineData("Not logged in", null)]
    [InlineData("", null)]
    public void ParseSignedIn_ReadsOnlyABooleanLoggedInField(string output, bool? expected)
        => CliAuthStatusProbe.ParseSignedIn(output).ShouldBe(expected);

    /// <summary>
    /// 答案取决于探针进程拿到的环境：显式变量要进去，宿主上没进白名单的变量不能进去。
    /// </summary>
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public async Task IsSignedIn_RunsWithTheSpecEnvironment(string state, bool expected)
    {
        var signedIn = await _probe.IsSignedInAsync(ScriptSpec(new Dictionary<string, string> { [StateVariable] = state }), CancellationToken.None);

        signedIn.ShouldBe(expected);
    }

    [Fact]
    public async Task IsSignedIn_UnreadableOutput_IsUnknown()
    {
        // 变量没给：脚本输出的 loggedIn 值为空，不是合法 JSON。
        var signedIn = await _probe.IsSignedInAsync(ScriptSpec(new Dictionary<string, string>()), CancellationToken.None);

        signedIn.ShouldBeNull();
    }

    [Fact]
    public async Task IsSignedIn_MissingExecutable_IsUnknown()
    {
        var spec = new CliProcessSpec
        {
            ExecutablePath = Path.Combine(_scratch, "does-not-exist"),
            WorkingDirectory = _scratch
        };

        (await _probe.IsSignedInAsync(spec, CancellationToken.None)).ShouldBeNull();
    }

    private CliProcessSpec ScriptSpec(IReadOnlyDictionary<string, string> environment)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(_scratch, "status.cmd");
            File.WriteAllText(script, $"@echo {{\"loggedIn\": %{StateVariable}%}}\r\n");
            return new CliProcessSpec
            {
                ExecutablePath = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
                Arguments = ["/d", "/c", script],
                WorkingDirectory = _scratch,
                Environment = environment
            };
        }

        var shellScript = Path.Combine(_scratch, "status.sh");
        File.WriteAllText(shellScript, $"#!/bin/sh\necho \"{{\\\"loggedIn\\\": ${StateVariable}}}\"\n");
        return new CliProcessSpec
        {
            ExecutablePath = "/bin/sh",
            Arguments = [shellScript],
            WorkingDirectory = _scratch,
            Environment = environment
        };
    }
}
