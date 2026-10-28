using System.Runtime.InteropServices;

namespace Tnzi.AI.Sandbox.Providers.Docker;

/// <summary>
/// 线程目录在 Docker 容器里的挂载方式，以及让容器用户能写进这些挂载源。
/// </summary>
/// <remarks>
/// <para>
/// 线程目录挂在 <c>/workspace</c>（命令的工作目录，与 Local provider 以线程目录为工作目录对应），四个子目录再各自挂在
/// <c>/mnt/{workspace,uploads,outputs,skills}</c>：agent 被告知的就是这些路径，它会把它们写进脚本与文件内容，
/// 而 bash 工具只能改写命令行本身。容器里没有这些路径时，脚本里的 <c>wb.save("/mnt/outputs/r.xlsx")</c>
/// 直接 FileNotFoundError（只读根文件系统也建不出来）。
/// </para>
/// <para>
/// ★ 挂载源在创建容器之前由本进程建好：缺失的挂载源由 Docker 守护进程以 root 身份建出来，非 root 的容器用户随后写不进去。
/// </para>
/// <para>
/// ★ 原生 Linux 的 bind mount 保留宿主的属主。容器以 <c>RunAsUser</c>（默认 1000:1000）运行、<c>CapDrop=ALL</c>，
/// 挂载源却由本进程建出（0755，属主是本进程的 uid），于是只要两者 uid 不同，容器里一个文件都写不了。
/// 本进程是 root 时把可写的挂载源交给容器用户；不是 root 时改不了属主，记一条警告并点名该怎么配，
/// 而不是放宽目录权限（0777 会让宿主上的其他用户也能往 agent 的工作区里放文件）或替运维换掉容器身份。
/// Docker Desktop（Windows / macOS）经文件共享层访问挂载源，不受这条限制。
/// </para>
/// </remarks>
internal static class DockerBindSources
{
    /// <summary>容器里线程目录的挂载点，也是 exec 的工作目录。</summary>
    public const string ThreadMountPoint = "/workspace";

    /// <summary>
    /// 容器用户需要写入的挂载源：三个工作子目录。skills 由本进程复制，容器只读它。
    /// </summary>
    /// <remarks>
    /// ★ 线程根不在其中。根下放的是本进程写的东西（技能接线哨兵）与四个挂载源本身：容器能在根下改名、删除时，
    /// 就能把它们换成链接，本进程随后跟进去，写哨兵覆盖链接指向的宿主文件、chown 链接目标，
    /// 守护进程也按链接目标建 bind mount，把宿主上任意目录可写地挂进下一个容器。
    /// </remarks>
    private static readonly string[] WritableMounts = ["workspace", "uploads", "outputs"];

    /// <summary>Docker bind 的只读模式后缀。</summary>
    private const string ReadOnlyBindMode = "ro";

    /// <summary>
    /// 容器的 Binds：线程目录在 <c>/workspace</c>，每个子目录在 <c>/mnt/{name}</c>；本进程独占写入的子目录（<c>skills</c>）只读。
    /// </summary>
    /// <remarks>
    /// ★ 只读要在两处成立：<c>/mnt/skills</c>，以及 <c>/workspace/skills</c>（它在可写挂载的线程根下面，同一份宿主目录）。
    /// 后者靠在 <c>/workspace/skills</c> 上再叠一层只读挂载：守护进程按目标路径的层级先挂父目录、再挂子目录，
    /// 子挂载盖住父挂载里的同名目录。挂载点本身在容器里也改不了名、删不掉，容器因此没法把 <c>skills</c> 换成别的东西。
    /// 容器能写进技能目录的话，就能在里面放链接；本进程复制技能前删树不跟链接，只剩一个很小的竞态窗口，
    /// 但技能副本归框架所有、对容器只读是文档写明的承诺。
    /// </remarks>
    public static string[] BuildBinds(string threadDirectory)
    {
        Check.NotNullOrWhiteSpace(threadDirectory);

        var binds = new List<string> { $"{threadDirectory}:{ThreadMountPoint}" };
        foreach (var mount in ThreadDataState.MountNames)
        {
            var source = Path.Combine(threadDirectory, mount);
            if (WritableMounts.Contains(mount))
            {
                binds.Add($"{source}:{DockerSandbox.VirtualMountRoot}/{mount}");
                continue;
            }

            binds.Add($"{source}:{DockerSandbox.VirtualMountRoot}/{mount}:{ReadOnlyBindMode}");
            binds.Add($"{source}:{ThreadMountPoint}/{mount}:{ReadOnlyBindMode}");
        }

        return [.. binds];
    }

    /// <summary>建出线程目录与四个子目录（已存在的真目录不动；是链接的换成真目录）。</summary>
    /// <remarks>
    /// ★ 挂载源必须是真目录：守护进程在宿主上解析 bind 源，链接会把它的目标挂进容器。线程目录由 agent 的命令写入
    ///（Local provider 直接在宿主上跑，Docker 下容器用户与本进程同一 uid 时同样能在线程根下建链接），
    /// 一条指向应用目录或别的线程目录的 <c>workspace</c> 链接，下一次建容器就把那里可写地交给了 agent。
    /// 只删链接本身，不碰它指向的地方（<see cref="SandboxFileSystem.RemoveLink"/>）。
    /// </remarks>
    public static void EnsureExist(string threadDirectory)
    {
        Check.NotNullOrWhiteSpace(threadDirectory);

        Directory.CreateDirectory(threadDirectory);
        foreach (var mount in ThreadDataState.MountNames)
        {
            var source = Path.Combine(threadDirectory, mount);
            if (SandboxFileSystem.IsLink(source))
            {
                SandboxFileSystem.RemoveLink(source);
            }
            else if (File.Exists(source))
            {
                // 同名的普通文件：建不出目录，挂载源也就不存在（守护进程会以 root 身份另建一个）。不替 agent 删它的文件，明说。
                throw new IOException($"Docker sandbox mount source {source} is a file, not a directory.");
            }

            Directory.CreateDirectory(source);
        }
    }

    /// <summary>容器用户需要写入的那几个宿主目录（不含线程根，理由见 <see cref="WritableMounts"/>）。</summary>
    public static IEnumerable<string> WritableSources(string threadDirectory)
        => WritableMounts.Select(mount => Path.Combine(threadDirectory, mount));

    /// <summary>
    /// 解析纯数字的 <c>RunAsUser</c>（<c>uid</c> 或 <c>uid:gid</c>）。用户名形式在宿主上解析不了，返回 null。
    /// </summary>
    public static ContainerIdentity? ParseNumericIdentity(string? runAsUser)
    {
        if (string.IsNullOrWhiteSpace(runAsUser))
            return null;

        var parts = runAsUser.Trim().Split(':');
        if (parts.Length > 2 || !uint.TryParse(parts[0], out var uid))
            return null;

        if (parts.Length == 1)
            return new ContainerIdentity(uid, null);

        return uint.TryParse(parts[1], out var gid) ? new ContainerIdentity(uid, gid) : null;
    }

    /// <summary>
    /// 本进程（<paramref name="processUid"/>）与容器用户（<paramref name="container"/>）之间，挂载源需要什么处理。
    /// </summary>
    /// <param name="isLinux">宿主是否原生 Linux（只有它的 bind mount 原样保留属主）。</param>
    /// <param name="processUid">本进程的有效 uid；取不到为 null。</param>
    /// <param name="container">容器身份；用户名形式解析不了为 null。</param>
    public static BindOwnershipAction DecideOwnership(bool isLinux, uint? processUid, ContainerIdentity? container)
    {
        if (!isLinux || processUid is null || container is null || container.Value.Uid == processUid)
            return BindOwnershipAction.None;

        return processUid == 0 ? BindOwnershipAction.HandOverToContainerUser : BindOwnershipAction.WarnNotWritable;
    }

    /// <summary>本进程的有效 uid；非 Linux 返回 null。</summary>
    public static uint? CurrentProcessUid => OperatingSystem.IsLinux() ? geteuid() : null;

    /// <summary>把 <paramref name="path"/> 的属主改成容器身份（gid 缺省时不改组）。链接一律拒绝（chown 会跟进链接）。</summary>
    public static void HandOver(string path, ContainerIdentity container)
    {
        if (SandboxFileSystem.IsLink(path))
            throw new IOException($"{path} is a link; it is not handed to the container user.");

        if (!OperatingSystem.IsLinux())
            return;

        // gid_t 的 -1 表示「组不变」。
        if (chown(path, container.Uid, container.Gid ?? uint.MaxValue) != 0)
        {
            throw new IOException($"chown {container.Uid}:{container.Gid?.ToString() ?? "-"} failed for {path} (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();

    [DllImport("libc", SetLastError = true)]
    private static extern int chown([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint owner, uint group);
}

/// <summary>容器进程的数字身份。</summary>
internal readonly record struct ContainerIdentity(uint Uid, uint? Gid)
{
    public override string ToString() => Gid is { } gid ? $"{Uid}:{gid}" : $"{Uid}";
}

/// <summary>挂载源属主的处理方式。</summary>
internal enum BindOwnershipAction
{
    /// <summary>不需要（同一 uid、非原生 Linux、或身份不是数字）。</summary>
    None,

    /// <summary>本进程是 root：把可写挂载源的属主改成容器用户。</summary>
    HandOverToContainerUser,

    /// <summary>本进程不是 root 且 uid 与容器用户不同：改不了属主，容器写不进挂载源。</summary>
    WarnNotWritable
}
