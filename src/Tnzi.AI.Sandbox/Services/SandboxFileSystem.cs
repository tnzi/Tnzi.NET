namespace Tnzi.AI.Sandbox.Services;

/// <summary>
/// 线程目录里的链接处理：线程目录由 agent 的命令写入，里面可能有 agent 自己建的符号链接或联接点，
/// 指向线程目录之外。凡是要删除或替换线程目录内容的地方，都只动链接本身，绝不跟进链接指向的地方。
/// </summary>
internal static class SandboxFileSystem
{
    /// <summary>
    /// <paramref name="path"/> 本身是不是链接（符号链接或联接点，悬空的也算）。
    /// <see cref="File.GetAttributes(string)"/> 读的是链接条目，不是它的目标。
    /// </summary>
    public static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    /// <summary>
    /// 只删链接条目，不删它指向的目录。Windows 上目录链接由目录 API 删除、文件链接由文件 API 删除；
    /// Unix 上符号链接是一个文件条目，<see cref="Directory.Delete(string)"/> 会拒绝悬空的那种。
    /// </summary>
    public static void RemoveLink(string path)
    {
        if (OperatingSystem.IsWindows() && (File.GetAttributes(path) & FileAttributes.Directory) != 0)
            Directory.Delete(path);
        else
            File.Delete(path);
    }

    /// <summary>
    /// 递归删除一棵目录树，遇到链接只删链接本身。只读文件先去掉只读属性（Windows 上带只读属性的文件删不掉，
    /// agent 克隆下来的 git 仓库里就有）。目录不存在时什么也不做，总线重试同一次删除是安全的。
    /// </summary>
    public static void DeleteTree(string path)
    {
        if (IsLink(path))
        {
            RemoveLink(path);
            return;
        }

        if (!Directory.Exists(path))
            return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                RemoveLink(entry);
            }
            else if ((attributes & FileAttributes.Directory) != 0)
            {
                DeleteTree(entry);
            }
            else
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                File.Delete(entry);
            }
        }

        Directory.Delete(path);
    }
}
