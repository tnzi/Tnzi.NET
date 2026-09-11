namespace Tnzi.AI.Sandbox.VirtualPath;

/// <summary>
/// 把一个路径解析到它<b>真正落在磁盘上的位置</b>，逐级跟随符号链接 / junction。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么需要它</b>：沙箱的越界判定是"规范化后的路径必须以工作区目录开头"，而
/// <see cref="Path.GetFullPath(string)"/> 只做<b>字面</b>规范化（消掉 <c>..</c> 与 <c>.</c>），
/// <b>不解析符号链接</b>。于是 agent 只要在工作区里放一个链接：
/// </para>
/// <code>ln -s /etc /mnt/workspace/x</code>
/// <para>
/// 之后 <c>read_file("/mnt/workspace/x/passwd")</c> 与 <c>ls</c> 就都落到宿主的 <c>/etc</c> 上 ——
/// 而每一层校验都认为这条路径乖乖待在工作区里，因为字面上它确实是。
/// </para>
/// <para>
/// <b>必须逐级解析</b>而不能只对末端调用一次 <c>ResolveLinkTarget</c>：上例里末端
/// <c>passwd</c> 本身不是链接，是它的<b>父目录</b>是。只看末端的实现在这个形态上原样通过。
/// </para>
/// <para>
/// 尾部不存在的段（写新文件时的常见情形）原样拼回：它们还不存在，也就还不可能是链接，
/// 而它们的<b>祖先</b>已经被解析过了。
/// </para>
/// </remarks>
public static class RealPathResolver
{
    /// <summary>
    /// 解析路径的真实位置。
    /// </summary>
    /// <param name="path">待解析路径（相对或绝对）。</param>
    /// <returns>逐级跟随链接后的绝对路径。</returns>
    /// <exception cref="SecurityException">
    /// 链接无法解析（成环、层数超限等）。<b>失败关闭</b>：解析不出来就证明不了这条路径在界内，
    /// 而解析不出来的常见原因恰好就是有人在故意绕。
    /// </exception>
    public static string Resolve(string path)
    {
        Check.NotNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);

        if (string.IsNullOrEmpty(root))
        {
            return full;
        }

        var relative = full[root.Length..];
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            current = ResolveOneLevel(current);
        }

        return current;
    }

    /// <summary>
    /// 若这一级本身是链接，解析到最终目标；不是链接或尚不存在则原样返回。
    /// </summary>
    private static string ResolveOneLevel(string path)
    {
        try
        {
            // 目录与文件要分别问：Directory.ResolveLinkTarget 对文件链接返回 null，反之亦然。
            FileSystemInfo? target = null;

            if (Directory.Exists(path))
            {
                target = Directory.ResolveLinkTarget(path, returnFinalTarget: true);
            }
            else if (File.Exists(path))
            {
                target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            }

            return target?.FullName ?? path;
        }
        catch (IOException ex)
        {
            // 成环或链接层数超限都从这里出来 —— 这正是要拦的形态。
            throw new SecurityException($"Unable to resolve path '{path}': {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new SecurityException($"Unable to resolve path '{path}': {ex.Message}");
        }
    }
}
