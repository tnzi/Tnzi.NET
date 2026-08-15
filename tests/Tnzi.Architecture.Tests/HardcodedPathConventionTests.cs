using System.Text.RegularExpressions;

namespace Tnzi.Architecture.Tests;

/// <summary>
/// 源码里不得把本机绝对路径写死在文件系统调用中。
/// </summary>
/// <remarks>
/// <para>
/// 这条门禁是被一次真实事故换来的。<c>tests/Tnzi.AI.Tests/Channels/</c> 下有 15 处
/// <c>File.ReadAllText("D:/dev/Repo/src/...")</c>：它们在作者本机通过，在 Linux runner 上
/// 那个绝对路径被当成相对路径拼在 <c>bin/Debug/net10.0/</c> 后面，必然 DirectoryNotFoundException。
/// </para>
/// <para>
/// <b>它藏了很久的原因值得记下来</b>：<c>backend-quality.yml</c> 的 full-suite job 一直挂在
/// 更早的 restore 阶段（某消费应用的 NuGet.config 里的 Windows 本地 NuGet 源
/// 在 runner 上不存在），这批测试从未真正在 CI 上跑过。CI 是红的，但红的原因一直是别的，
/// 于是没有人往下看。<b>一条长期红着的流水线，和没有流水线是同一回事。</b>
/// </para>
/// <para>
/// <b>为什么只管文件系统调用，不管所有盘符字面量</b>：
/// <c>ToolPermissionTests</c> 用 <c>"D:\\My\\Tnzi.NET\\src"</c> 当权限规则的 PathPrefix，
/// 测的正是 Windows 盘符路径与反斜杠的归一化 —— 那里的盘符字面量是被测语义本身，
/// 改成中性路径会削弱测试。把规则收窄到"真的会去访问文件系统"的调用上，
/// 这条门禁就不需要任何豁免清单，而不需要豁免的门禁才不会随时间腐化。
/// </para>
/// <para>
/// 需要读仓库内文件时用 <see cref="Tnzi.TestBase.RepoRoot.ReadText"/>：
/// 它经编译期注入的程序集元数据定位仓库根，与运行时的输出目录位置无关。
/// </para>
/// </remarks>
public class HardcodedPathConventionTests
{
    /// <summary>
    /// <c>File.X("C:/...")</c> / <c>Directory.X(@"D:\...")</c> 形态。
    /// 只匹配紧跟在调用括号后的字符串字面量 —— 经变量间接传入的抓不到，
    /// 但那不是这条门禁的目标：目标是最常见、也最容易在 review 时被眼睛滑过去的直写形态。
    /// </summary>
    private static readonly Regex DriveLetterInFileCall = new(
        @"\b(?:File|Directory)\.[A-Za-z]+\(\s*@?""[A-Za-z]:[/\\]",
        RegexOptions.Compiled);

    [Fact]
    public void No_Source_File_Hardcodes_A_Local_Absolute_Path_In_File_System_Calls()
    {
        var repoRoot = RepoRoot.Locate();
        var violations = new List<string>();

        foreach (var scanDir in new[] { "src", "tests" })
        {
            var root = Path.Combine(repoRoot, scanDir);
            if (!Directory.Exists(root)) continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                // 构建产物不是源码
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (!DriveLetterInFileCall.IsMatch(text)) continue;

                var rel = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!DriveLetterInFileCall.IsMatch(lines[i])) continue;

                    // 跳过注释行。这条规则第一次跑就抓到了本文件自己的 XML 文档注释 ——
                    // 那些示例存在的意义正是"让人看见这个坏形态长什么样",不该被自己拦下。
                    // 判据取整行起始:覆盖 `//`、`///` 与块注释续行的 `*`。
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                        trimmed.StartsWith('*'))
                    {
                        continue;
                    }

                    violations.Add($"{rel}:{i + 1}  {lines[i].Trim()}");
                }
            }
        }

        violations.ShouldBeEmpty(
            "源码在文件系统调用里写死了本机绝对路径。这类代码在作者本机通过、在 CI（Linux）必然失败，"
            + "而且失败信息是 DirectoryNotFoundException，看不出根因。"
            + $"读仓库内文件请用 RepoRoot.ReadText(\"src/...\")。{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations));
    }
}
