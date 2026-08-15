using System.Text.RegularExpressions;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// 来源地址只能经 <c>GetClientIp()</c> 取得，不得直接读连接或代理头。
/// </summary>
/// <remarks>
/// <para>
/// <c>AspNetCoreOptions.CollectClientIpAddress</c> 是一个部署级隐私开关：置为 <c>false</c> 后
/// 全框架不再采集来源地址。它的判定<b>只落在 <c>GetClientIp()</c> 这一个入口上</b>，
/// 因此任何绕过该入口、自己去读 <c>Connection.RemoteIpAddress</c> 或 <c>X-Forwarded-For</c> 的代码，
/// 都会让那个开关<b>名不副实</b>——用户关掉了采集，而那条路径照记不误。
/// </para>
/// <para>
/// 这正是引入开关时实际发生过的事：请求日志、审计操作日志与 AI 用量日志三处各自直接读连接，
/// 于是「关掉采集」只覆盖了限流与审计上下文。<b>一个给出虚假保证的隐私开关，比没有这个开关更糟。</b>
/// 逐处修完并不能阻止第四处出现，所以这道门禁比那三处修复本身更重要。
/// </para>
/// <para>
/// 顺带的好处：<c>GetClientIp()</c> 支持反向代理，而直接读连接在代理后面拿到的是代理地址。
/// 绕过它的代码通常也顺带记错了地址。
/// </para>
/// </remarks>
public class ClientIpCollectionConventionTests
{
    /// <summary>
    /// 直接读取来源地址的三种写法。
    /// </summary>
    /// <remarks>
    /// 两个代理头同样在列：只堵 <c>RemoteIpAddress</c> 等于门禁只关了一半，
    /// 而代理头恰好是生产环境实际取到值的那条路径。
    /// </remarks>
    private static readonly Regex DirectAddressRead = new(
        """Connection\??\.RemoteIpAddress|Headers\s*\[\s*"X-Forwarded-For"|Headers\s*\[\s*"X-Real-IP" """.TrimEnd(),
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 唯一允许直接读的文件：<c>GetClientIp()</c> 自己就实现在这里。
    /// </summary>
    private static readonly string[] AllowedFiles = ["HttpContextExtensions.cs"];

    [Fact]
    public void SourceAddress_IsOnlyReadThroughGetClientIp()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in EnumerateFrameworkSources(repoRoot))
        {
            scanned++;

            if (AllowedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            // 完全限定：Moq.Match 与 System.Text.RegularExpressions.Match 在本项目里同时可见。
            foreach (var match in DirectAddressRead.Matches(content).Cast<System.Text.RegularExpressions.Match>())
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {match.Value.Trim()}");
            }
        }

        // 没有这一条，扫描失效之后门禁会一样安静地通过——约定测试烂掉的标准方式。
        Assert.True(scanned > 100, $"the source scan found suspiciously few files ({scanned})");

        Assert.True(offenders.Count == 0,
            "source addresses must be read through GetClientIp() so the CollectClientIpAddress "
            + "privacy switch actually covers them: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheDetector_ActuallyFlagsADirectRead()
    {
        // 上面的扫描如今应当零命中，单靠它分不出「干净」与「检测器坏了」。
        Assert.Matches(DirectAddressRead, """Ip = context.Connection.RemoteIpAddress?.ToString(),""");
        Assert.Matches(DirectAddressRead, """var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();""");
        Assert.Matches(DirectAddressRead, """var fwd = request.Headers["X-Forwarded-For"].FirstOrDefault();""");
        Assert.Matches(DirectAddressRead, """var real = request.Headers["X-Real-IP"].FirstOrDefault();""");

        Assert.DoesNotMatch(DirectAddressRead, """Ip = context.Request.GetClientIp(),""");
        Assert.DoesNotMatch(DirectAddressRead, """var ua = request.Headers["User-Agent"].ToString();""");
    }

    /// <summary>
    /// 逐项目枚举框架 C# 源码。
    /// </summary>
    /// <remarks>
    /// 刻意不对 <c>src/</c> 整体 <c>AllDirectories</c>：<c>src/Tnzi.UI</c> 下有前端 monorepo 的
    /// <c>node_modules</c>，递归进去会把测试宿主拖垮。
    /// </remarks>
    private static IEnumerable<string> EnumerateFrameworkSources(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");

        foreach (var projectDirectory in Directory.EnumerateDirectories(srcRoot))
        {
            if (string.Equals(Path.GetFileName(projectDirectory), "Tnzi.UI", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
