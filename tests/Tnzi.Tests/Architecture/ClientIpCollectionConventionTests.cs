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
    /// 直接读取连接地址的写法。
    /// </summary>
    private static readonly Regex ConnectionAddressRead = new(
        """Connection\??\.RemoteIpAddress""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 从<b>调用方可控</b>的代理头取地址的写法。
    /// </summary>
    /// <remarks>
    /// ★★★ 这一条<b>没有允许列表</b>，<c>GetClientIp()</c> 自己也不例外。
    /// 转发头是调用方随便写的：每次请求换一个值，限流分区键就每次落进新桶，
    /// 于是「限流开着」而匿名端点一次都拦不住。哪一跳有资格改写地址，
    /// 是一次<b>部署声明</b>（<c>AspNetCore:TrustedProxies</c>）而不是请求自称的事 ——
    /// 声明交给 <c>UseForwardedHeaders</c> 兑现，它按受信代理从右往左消费并写进连接地址。
    /// 代码里任何一处自己解析这两个头，都在那道声明外面重开一个后门。
    /// </remarks>
    private static readonly Regex ForwardedHeaderRead = new(
        """Headers\s*\[\s*"X-Forwarded-For"|Headers\s*\[\s*"X-Real-IP" """.TrimEnd(),
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// 唯一允许直接读连接地址的文件：<c>GetClientIp()</c> 自己就实现在这里。
    /// </summary>
    private static readonly string[] ConnectionReadAllowedFiles = ["HttpContextExtensions.cs"];

    [Fact]
    public void SourceAddress_IsOnlyReadThroughGetClientIp()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in RepoScan.EnumerateFiles("src", "*.cs"))
        {
            scanned++;

            if (ConnectionReadAllowedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = StripComments(File.ReadAllText(file));
            // 完全限定：Moq.Match 与 System.Text.RegularExpressions.Match 在本项目里同时可见。
            foreach (var match in ConnectionAddressRead.Matches(content).Cast<System.Text.RegularExpressions.Match>())
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
    public void ProxyHeaders_AreNeverParsedByHand()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in RepoScan.EnumerateFiles("src", "*.cs"))
        {
            scanned++;

            var content = StripComments(File.ReadAllText(file));
            foreach (var match in ForwardedHeaderRead.Matches(content).Cast<System.Text.RegularExpressions.Match>())
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {match.Value.Trim()}");
            }
        }

        Assert.True(scanned > 100, $"the source scan found suspiciously few files ({scanned})");

        Assert.True(offenders.Count == 0,
            "X-Forwarded-For / X-Real-IP are caller-controlled: trust must be declared through "
            + "AspNetCore:TrustedProxies and applied by UseForwardedHeaders, never parsed by hand. "
            + "Read Connection.RemoteIpAddress through GetClientIp() instead: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheDetector_ActuallyFlagsADirectRead()
    {
        // 上面两条扫描如今应当零命中，单靠它们分不出「干净」与「检测器坏了」。
        Assert.Matches(ConnectionAddressRead, """Ip = context.Connection.RemoteIpAddress?.ToString(),""");
        Assert.Matches(ConnectionAddressRead, """var ip = httpContext?.Connection?.RemoteIpAddress?.ToString();""");
        Assert.Matches(ForwardedHeaderRead, """var fwd = request.Headers["X-Forwarded-For"].FirstOrDefault();""");
        Assert.Matches(ForwardedHeaderRead, """var real = request.Headers["X-Real-IP"].FirstOrDefault();""");

        Assert.DoesNotMatch(ConnectionAddressRead, """Ip = context.Request.GetClientIp(),""");
        Assert.DoesNotMatch(ForwardedHeaderRead, """var ua = request.Headers["User-Agent"].ToString();""");
    }

    [Fact]
    public void TheScannerIgnoresProse()
    {
        // 门禁扫的是代码，不是注释 —— 而这两条规则的**理由**恰恰要在注释里
        // 把被禁的写法原样写出来才说得清。少了这一步，写清楚为什么会让门禁变红，
        // 于是下一个人删掉的是解释而不是违规。
        const string prose = """
            /// 刻意不读 Headers["X-Forwarded-For"]，也不直接读 Connection.RemoteIpAddress。
            var ip = context.Request.GetClientIp();
            """;

        var stripped = StripComments(prose);

        Assert.DoesNotMatch(ConnectionAddressRead, stripped);
        Assert.DoesNotMatch(ForwardedHeaderRead, stripped);

        // 反面：同一段里真的读一次，仍然抓得到。
        Assert.Matches(
            ConnectionAddressRead,
            StripComments(prose + "\nvar real = context.Connection.RemoteIpAddress;"));
    }

    /// <summary>
    /// 去掉行注释（含 <c>///</c> 文档注释）后再匹配。
    /// </summary>
    /// <remarks>
    /// 只切 <c>//</c> 到行尾，不做完整词法分析：一行里 <c>//</c> 之后的东西不会执行，
    /// 所以漏判的唯一形态是「同一行先出现一个含 <c>//</c> 的字符串字面量、之后才读地址」，
    /// 而那种写法不存在于本仓。
    /// </remarks>
    private static string StripComments(string content)
    {
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var commentStart = lines[i].IndexOf("//", StringComparison.Ordinal);
            if (commentStart >= 0)
            {
                lines[i] = lines[i][..commentStart];
            }
        }

        return string.Join('\n', lines);
    }
}
