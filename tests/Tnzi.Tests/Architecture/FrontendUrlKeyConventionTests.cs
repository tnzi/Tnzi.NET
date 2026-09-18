using System.Text.RegularExpressions;
using Tnzi.Settings;

namespace Tnzi.Tests.Architecture;

/// <summary>
/// 前端 origin 只能经 <see cref="FrontendUrlResolver"/> 取得，不得直接读配置键。
/// </summary>
/// <remarks>
/// <para>
/// 2026-09-12 之前框架从两个互不相干的键读前端地址：Hosting 的邮件处理器读 <c>System:FrontendUrl</c>，
/// Identity 的五处读 <c>App:FrontendUrl</c>。消费方只配一个键就以为全配了 —— 密码重置信正常，
/// 邀请信的接受链接却是相对路径、拒发，而管理端仍报「已发出」。逐处修完并不能阻止第六处出现，
/// 所以这道门禁比那五处修复本身更重要：任何 <c>"App:FrontendUrl"</c> / <c>"System:FrontendUrl"</c>
/// 字面量只允许出现在解析器自己那个文件里。
/// </para>
/// <para>
/// 站点名是同一个形状的第二对（<c>System:SiteName</c> / <c>App:SiteName</c>，解析器 <see cref="SiteNameResolver"/>）：
/// origin 收口那天正是这一对被漏掉，评审才补上，所以门禁一起守。
/// </para>
/// <para>
/// <c>Identity:Invitation:AcceptUrlTemplate</c> 不在此列：它是一条完整的链接模板（移动端深链也走它），
/// 不是 origin，是解析顺序里排在 origin 之前的显式覆盖。
/// </para>
/// </remarks>
public class FrontendUrlKeyConventionTests
{
    /// <summary>
    /// 任何以 <c>:FrontendUrl"</c> 或 <c>:SiteName"</c> 结尾的字符串字面量：不限前缀，<c>$"{Section}:FrontendUrl"</c> 这类拼出来的键也算。
    /// </summary>
    private static readonly Regex FrontendUrlKeyLiteral = new(
        """
        :(?:FrontendUrl|SiteName)"
        """.Trim(),
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>唯一允许写这些字面量的文件：两个解析器自己。</summary>
    private static readonly string[] AllowedFiles = ["FrontendUrlResolver.cs", "SiteNameResolver.cs"];

    [Fact]
    public void FrontendUrl_IsOnlyReadThroughTheResolver()
    {
        var repoRoot = RepoRoot.Locate();

        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in RepoScan.EnumerateFiles("src", "*.cs"))
        {
            scanned++;

            if (AllowedFiles.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = StripComments(File.ReadAllText(file));
            foreach (var match in FrontendUrlKeyLiteral.Matches(content).Cast<System.Text.RegularExpressions.Match>())
            {
                offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {match.Value}");
            }
        }

        Assert.True(scanned > 100, $"the source scan found suspiciously few files ({scanned})");

        Assert.True(offenders.Count == 0,
            "the frontend origin must be read through FrontendUrlResolver.Resolve(configuration) so every "
            + "link-building path agrees on one configuration key (System:FrontendUrl, legacy App:FrontendUrl), and the "
            + "site name through SiteNameResolver.Resolve (System:SiteName, legacy App:SiteName): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheDetector_ActuallyFlagsADirectRead()
    {
        Assert.Matches(FrontendUrlKeyLiteral, """var frontendUrl = _configuration?["App:FrontendUrl"];""");
        Assert.Matches(FrontendUrlKeyLiteral, """Configuration["System:FrontendUrl"]""");
        Assert.Matches(FrontendUrlKeyLiteral, """Configuration[$"{Section}:FrontendUrl"]""");
        Assert.Matches(FrontendUrlKeyLiteral, """SiteName = _configuration?["App:SiteName"]""");
        Assert.DoesNotMatch(FrontendUrlKeyLiteral, """var apiBase = Configuration["System:ApiBaseUrl"];""");
        Assert.DoesNotMatch(FrontendUrlKeyLiteral, StripComments("""// 此前读 "App:FrontendUrl"，现在经解析器"""));
    }

    [Fact]
    public void StripComments_DoesNotCutInsideStringLiterals()
    {
        // 字符串里的 "https://" 不是注释：从第一个 // 起整行剥掉会让它后面的直接读取隐身
        var line = """var url = "https://" + Configuration["App:FrontendUrl"];""";
        Assert.Matches(FrontendUrlKeyLiteral, StripComments(line));

        var verbatim = """var url = @"https://x" + Configuration["App:FrontendUrl"]; // 读 "App:FrontendUrl" 的旧注释""";
        Assert.Single(FrontendUrlKeyLiteral.Matches(StripComments(verbatim)));

        var escaped = """var s = "a\"//b" + Configuration["System:FrontendUrl"];""";
        Assert.Matches(FrontendUrlKeyLiteral, StripComments(escaped));

        var charLiteral = """var c = '"'; var u = Configuration["System:FrontendUrl"]; // "App:FrontendUrl" 的旧注释""";
        Assert.Single(FrontendUrlKeyLiteral.Matches(StripComments(charLiteral)));

        Assert.DoesNotMatch(FrontendUrlKeyLiteral, StripComments("""var x = 1; // 此前读 "App:FrontendUrl" 的旧注释"""));
        Assert.DoesNotMatch(FrontendUrlKeyLiteral, StripComments("""/* 此前读 "App:FrontendUrl" */ var x = 1;"""));
    }

    /// <summary>
    /// 去掉 <c>//</c> 行注释与 <c>/* */</c> 块注释，但只在字符串 / 字符字面量之外：
    /// <c>"https://"</c> 里的 <c>//</c> 不是注释。认普通字符串的 <c>\"</c> 转义与 <c>@""</c> 逐字串的 <c>""</c>。
    /// 不追求做成完整的 C# 词法器（原始字符串、插值里的嵌套引号不处理），够挡住普通的一行形态就行。
    /// </summary>
    private static string StripComments(string content)
    {
        var result = new StringBuilder(content.Length);
        var i = 0;
        while (i < content.Length)
        {
            var c = content[i];
            var next = i + 1 < content.Length ? content[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < content.Length && content[i] != '\n')
                    i++;
                continue;
            }

            if (c == '/' && next == '*')
            {
                var end = content.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? content.Length : end + 2;
                continue;
            }

            if (c == '"' || (c == '@' && next == '"'))
            {
                var verbatim = c == '@';
                var start = i;
                i += verbatim ? 2 : 1;
                while (i < content.Length)
                {
                    if (verbatim && content[i] == '"' && i + 1 < content.Length && content[i + 1] == '"')
                    {
                        i += 2;
                        continue;
                    }

                    if (!verbatim && content[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (content[i] == '"')
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                i = Math.Min(i, content.Length);
                result.Append(content, start, i - start);
                continue;
            }

            if (c == '\'')
            {
                var start = i;
                i++;
                while (i < content.Length && content[i] != '\'')
                    i += content[i] == '\\' ? 2 : 1;
                i = Math.Min(i + 1, content.Length);
                result.Append(content, start, i - start);
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }
}
