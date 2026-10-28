namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 本测试项目的临时目录：一律建在临时文件夹下的同一个父目录里，每个测试一份、用完即删。
/// </summary>
/// <remarks>
/// <para>
/// 每次取用都是新名字（用途标签 + GUID），测试之间从不共用目录：真机冒烟测试在这些目录里启动 agent CLI，
/// 而 CLI 按工作目录记会话与自动记忆，共用一个目录就共用了这两样。
/// </para>
/// <para>
/// ★ 不要往 <see cref="Root"/> 本身写任何东西：CLI 会从工作目录一路往上读每一级父目录的 <c>CLAUDE.md</c>，
/// 放在这里的文件会进入此后每一次冒烟运行的上下文。
/// </para>
/// </remarks>
internal static class TestScratch
{
    private const int MaxDeleteAttempts = 5;
    private static readonly TimeSpan DeleteRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>共同的父目录：<c>%TEMP%/tnzi-ai-cli</c>。</summary>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "tnzi-ai-cli");

    /// <summary>
    /// <see cref="Root"/> 下一个新路径，目录本身不建（交给测试或被测代码去建）。
    /// </summary>
    /// <param name="purpose">用途标签，让残留的目录看得出是哪组测试留下的。</param>
    public static string NewPath(string purpose)
        => Path.Combine(Root, $"{purpose}-{Guid.NewGuid():N}");

    /// <summary>在 <see cref="Root"/> 下建一个新的空目录并返回其路径。</summary>
    /// <param name="purpose">用途标签，见 <see cref="NewPath"/>。</param>
    public static string CreateDirectory(string purpose)
        => Directory.CreateDirectory(NewPath(purpose)).FullName;

    /// <summary>
    /// 连同内容删除一个临时目录。目录已不存在算成功；一直被占用就留下它，不让测试因此变红。
    /// </summary>
    /// <remarks>
    /// Windows 上刚被杀掉的进程还会再占用一小会儿它的工作目录或打开的文件，所以先短暂重试几次再放弃。
    /// </remarks>
    public static void Delete(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MaxDeleteAttempts)
            {
                Thread.Sleep(DeleteRetryDelay * attempt);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 重试之后仍被占用：临时文件夹里留一个目录，不值得让测试失败。
                return;
            }
        }
    }
}
