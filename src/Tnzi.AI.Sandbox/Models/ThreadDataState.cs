namespace Tnzi.AI.Sandbox.Models;

/// <summary>
/// 一个线程的数据目录布局。只是路径，不保证目录存在：目录与技能副本由
/// <see cref="IThreadDataProvisioner"/> 在沙箱第一次被用到时才布置。
/// </summary>
public record ThreadDataState(string ThreadDirectory, string WorkspacePath, string UploadsPath, string OutputsPath, string SkillsPath)
{
    /// <summary>
    /// 按线程目录推出四个子目录的路径（与 <c>VirtualPathTranslator</c> 的 <c>/mnt/*</c> 子路径同名）。
    /// </summary>
    public static ThreadDataState FromThreadDirectory(string threadDirectory)
    {
        Check.NotNullOrWhiteSpace(threadDirectory);
        return new ThreadDataState(
            ThreadDirectory: threadDirectory,
            WorkspacePath: Path.Combine(threadDirectory, "workspace"),
            UploadsPath: Path.Combine(threadDirectory, "uploads"),
            OutputsPath: Path.Combine(threadDirectory, "outputs"),
            SkillsPath: Path.Combine(threadDirectory, "skills"));
    }
}
