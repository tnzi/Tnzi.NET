using MsOptions = Microsoft.Extensions.Options.Options;
using Tnzi.AI.Sandbox.Controllers.Admin;

namespace Tnzi.AI.Tests.Sandbox;

/// <summary>
/// <c>GET /admin/sandbox/status</c> 按<b>当前生效的 provider</b> 投影。
/// </summary>
/// <remarks>
/// ★ 此前 <c>Provider</c> 字段如实答 "docker"，而命令 / 模式黑名单三行恒读
/// <c>opts.Local.*</c> —— 管理端展示的是一份根本没在执行的规则，而外观完全正常。
/// </remarks>
public class SandboxAdminStatusTests
{
    private static SandboxModuleOptions BuildOptions() => new()
    {
        Enabled = true,
        DataRoot = "/data",
        Local = new LocalSandboxOptions
        {
            DeniedCommands = ["local-denied"],
            DeniedPatterns = ["local.pem"],
            EnvironmentBlacklist = ["LOCAL_SECRET"]
        },
        Docker = new DockerSandboxOptions
        {
            DeniedCommands = ["docker-denied"],
            DeniedPatterns = ["docker.pem"]
        }
    };

    private static DefaultSandboxAdminController CreateController(string providerName)
    {
        var provider = new Mock<ISandboxProvider>();
        provider.SetupGet(p => p.Name).Returns(providerName);

        return new DefaultSandboxAdminController(provider.Object, MsOptions.Create(BuildOptions()));
    }

    [Fact]
    public void Status_LocalProvider_ReportsTheLocalRules()
    {
        var dto = CreateController("local").GetStatus().Data!;

        dto.Provider.ShouldBe("local");
        dto.DeniedCommands.ShouldBe(["local-denied"]);
        dto.DeniedPatterns.ShouldBe(["local.pem"]);
        dto.EnvironmentBlacklist.ShouldBe(["LOCAL_SECRET"]);
    }

    [Fact]
    public void Status_DockerProvider_ReportsTheDockerRules()
    {
        var dto = CreateController("docker").GetStatus().Data!;

        dto.Provider.ShouldBe("docker");
        dto.DeniedCommands.ShouldBe(["docker-denied"]);
        dto.DeniedPatterns.ShouldBe(["docker.pem"]);
    }

    [Fact]
    public void Status_DockerProvider_ReportsAnEmptyEnvironmentBlacklist()
    {
        // 环境变量黑名单只有本地 provider 有（容器里根本不继承宿主环境）。
        // 端出 Local 的那份等于宣称一条并不存在的防护。
        CreateController("docker").GetStatus().Data!.EnvironmentBlacklist.ShouldBeEmpty();
    }
}
