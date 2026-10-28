using System.Xml.Linq;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 框架程序集里要复制到输出目录的内容文件，必须在 NuGet 包里也声明复制到输出目录。
/// </summary>
/// <remarks>
/// <para>
/// <c>CopyToOutputDirectory</c> 只管 <c>ProjectReference</c> 与仓内构建。打进包的内容文件在 nuspec 里是
/// <c>&lt;files buildAction="Content" /&gt;</c>，<c>copyToOutput</c> 缺省为 <b>false</b>，
/// 以 <c>PackageReference</c> 消费框架的应用 bin 里根本没有这些文件，需要 <c>PackageCopyToOutput=true</c>
/// 才会写成 <c>copyToOutput="true"</c>。
/// </para>
/// <para>
/// ★ 这不是假想：<c>Tnzi.Hosting</c> 的四个邮件模板（欢迎、重置密码、双因素验证码、邀请）与
/// <c>Tnzi.Payment.Billing</c> 的发票模板就是这样漏掉的。模板存储只认输出目录下的 <c>Templates/</c>，
/// 找不到模板时通知失败、事件总线重试后进死信，用户收不到验证码、重置链接与邀请。
/// 以 <c>ProjectReference</c> 消费框架的参考应用 bin 里有这些文件，所以从来没有暴露过：
/// <b>只有走 NuGet 的消费方受影响</b>，与 <c>HostingTargetsSyncTests</c> 守的是同一类盲区。
/// </para>
/// </remarks>
public class PackagedContentFilesConventionTests
{
    [Fact]
    public void ContentCopiedToOutput_IsAlsoCopiedToOutputForPackageConsumers()
    {
        var projects = RepoScan.EnumerateFiles("src", "*.csproj").ToList();
        Assert.NotEmpty(projects);

        var copiedItems = 0;
        var violations = new List<string>();
        foreach (var project in projects)
        {
            foreach (var content in XDocument.Load(project).Descendants().Where(e => e.Name.LocalName == "Content"))
            {
                var copy = Value(content, "CopyToOutputDirectory");
                if (string.IsNullOrEmpty(copy) || copy.Equals("Never", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(Value(content, "Pack"), "false", StringComparison.OrdinalIgnoreCase)) continue;

                copiedItems++;
                if (!string.Equals(Value(content, "PackageCopyToOutput"), "true", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Path.GetFileName(project)}: <Content Include=\"{content.Attribute("Include")?.Value}\">");
                }
            }
        }

        // 非空洞守卫：一个都没扫到时这条约定恒真。
        Assert.True(copiedItems > 0, "Expected at least one Content item copied to the output directory under src/.");
        Assert.True(
            violations.Count == 0,
            "These Content items are copied to the output directory for project references but not for package consumers. "
            + "Add PackageCopyToOutput=\"true\":" + Environment.NewLine
            + string.Join(Environment.NewLine, violations.Select(v => "  - " + v)));
    }

    /// <summary>元数据既可以写成特性也可以写成子元素。</summary>
    private static string? Value(XElement item, string name)
        => item.Attribute(name)?.Value
           ?? item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();
}
