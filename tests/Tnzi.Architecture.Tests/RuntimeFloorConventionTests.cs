using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 仓库里任何 MSBuild 文件都不得把「构建机的运行时补丁号」写成部署主机的最低要求。
/// </summary>
/// <remarks>
/// <para>
/// 框架依赖部署的 <c>runtimeconfig.json</c> 里，<c>framework.version</c> 是<b>地板</b>而不是偏好：
/// <c>RollForward=LatestMajor</c> 只向前滚，永不向后。<c>TargetLatestRuntimePatch=true</c> 让 SDK
/// 把构建机上当时装着的最新补丁号（例如 <c>10.0.11</c>）写成这个地板 —— 于是补丁号更低的主机
/// 一律 500.31「Failed to load ASP.NET Core runtime」，而同一台主机上别的 .NET 10 应用照常运行。
/// </para>
/// <para>
/// <b>这不是假想</b>：根 <c>Directory.Build.props</c> 曾写着 <c>true</c>，注释还写着
/// 「使用最新的运行时补丁版本」（与属性的实际语义相反），部署出去的 MCP 服务器因此挂了几周。
/// 同一份源码在两个本地输出目录里要求的版本都不一样（10.0.10 与 10.0.11）——
/// 两次构建之间装了个补丁，产物就变了，谁在哪天发布决定了生产主机要装到哪个补丁。
/// </para>
/// <para>
/// <b>为什么扫全部 MSBuild 文件而不只看根那一份</b>：属性可以在任何一层 props / csproj 里
/// 重新设回 <c>true</c>，<c>RuntimeFrameworkVersion</c> 钉一个非 <c>.0</c> 的补丁号是同一个陷阱的
/// 另一种写法。地板只有一个正确值 —— 主版本的 <c>.0</c>。
/// </para>
/// </remarks>
public class RuntimeFloorConventionTests
{
    private static readonly Regex TargetLatestRuntimePatchTrue = new(
        @"<TargetLatestRuntimePatch(?:\s[^>]*)?>\s*true\s*</TargetLatestRuntimePatch>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary><c>&lt;RuntimeFrameworkVersion&gt;10.0.11&lt;/...&gt;</c> 这类钉死非 .0 补丁号的写法。</summary>
    private static readonly Regex PinnedRuntimePatch = new(
        @"<RuntimeFrameworkVersion(?:\s[^>]*)?>\s*\d+\.\d+\.(?!0\s*<)\d+[^<]*</RuntimeFrameworkVersion>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] MsBuildExtensions = [".csproj", ".props", ".targets"];

    [Fact]
    public void Root_Directory_Build_Props_Explicitly_Keeps_The_Runtime_Floor_At_Patch_Zero()
    {
        var text = RepoRoot.ReadText("Directory.Build.props");

        text.ShouldContain("<TargetLatestRuntimePatch>false</TargetLatestRuntimePatch>",
            customMessage: "根 Directory.Build.props 必须显式写 TargetLatestRuntimePatch=false。"
                + "删掉这一行虽然也落到 SDK 默认值，但那样下一个人看不到它为什么不能是 true。");
        text.ShouldContain("<RollForward>LatestMajor</RollForward>",
            customMessage: "RollForward 是「运行时用主机上最新版本」的正确实现，不能随 TargetLatestRuntimePatch 一起被删。");
    }

    [Fact]
    public void No_MSBuild_File_Pins_The_Build_Machine_Runtime_Patch_As_The_Deployment_Floor()
    {
        var repoRoot = RepoRoot.Locate();
        var scanned = 0;
        var violations = new List<string>();

        foreach (var file in EnumerateMsBuildFiles())
        {
            scanned++;
            var text = File.ReadAllText(file);
            var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            if (TargetLatestRuntimePatchTrue.IsMatch(text))
                violations.Add($"{rel}: TargetLatestRuntimePatch=true 把构建机的补丁号写成了部署地板");

            if (PinnedRuntimePatch.IsMatch(text))
                violations.Add($"{rel}: RuntimeFrameworkVersion 钉死了一个非 .0 的补丁号");
        }

        // 下界守卫：枚举一旦因为扩展名或跳过规则写错而扫不到东西，上面的断言会一起假绿。
        scanned.ShouldBeGreaterThan(50,
            $"只扫到 {scanned} 个 MSBuild 文件 —— 是枚举坏了，不是仓库真的这么小");

        violations.ShouldBeEmpty(
            "runtimeconfig.json 里的 framework.version 是最低要求，不是偏好；RollForward 只向前滚。"
            + "把构建机的补丁号写进去，等于要求每台部署主机都装到与构建机相同的补丁，"
            + $"而症状是 500.31，看不出根因。{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>仓库里全部 MSBuild 文件。</summary>
    /// <remarks>
    /// ★ 走 <see cref="RepoScan"/> 而不是 <c>SearchOption.AllDirectories</c>：本方法的扫描根是
    /// <b>仓库根</b>，底下有 <c>src/Tnzi.UI</c> 的 pnpm 工作区，裸递归会跟着 junction 走进无穷路径，
    /// 测试宿主涨过 3 GB 卡死且一条结果都打不出来。原先那份 <c>SkippedSegments</c> 名单
    /// （<c>bin</c>/<c>obj</c>/<c>node_modules</c>/<c>.git</c>/<c>dist</c>）挡不住它 ——
    /// 它是<b>枚举之后</b>才过滤路径，而走不完的是枚举本身。
    /// </remarks>
    private static IEnumerable<string> EnumerateMsBuildFiles()
        => RepoScan.EnumerateFiles(".", "*")
            .Where(file => MsBuildExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
}
