namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// Unix 进程组信号：优雅终止整组，而不是只杀 leader。
/// </summary>
/// <remarks>
/// <para>
/// .NET 没有暴露 <c>setpgid</c>，因此本模块在 <c>setsid</c> 可用时经它启动子进程，
/// 让子进程成为自己的会话/进程组 leader。但<b>不假设成功</b>：
/// <c>setsid</c> 在调用方已经是进程组 leader 时会 fork，那样 pid 与 pgid 就对不上了。
/// 于是终止前先 <c>getpgid(pid)</c> <b>核实</b>：只有 pgid == pid（确实成了组 leader）
/// 才用 <c>kill(-pgid, …)</c> 打整组，否则老老实实回落到逐进程终止。
/// </para>
/// <para>
/// 「先探测再决定」比「假设我们已经建了组」重要：假设错了的表现是信号发给了<b>调用方自己</b>
/// 所在的进程组 —— 也就是把 API 进程一起杀掉。
/// </para>
/// </remarks>
internal static class UnixProcessGroup
{
    private const int SIGTERM = 15;
    private const int SIGKILL = 9;

    /// <summary>常见的 <c>setsid</c> 路径。都不存在时回落到直接启动。</summary>
    private static readonly string[] SetsidCandidates = ["/usr/bin/setsid", "/bin/setsid"];

    /// <summary>找到可用的 <c>setsid</c> 绝对路径；没有则返回 null。</summary>
    public static string? FindSetsid()
        => OperatingSystem.IsWindows() ? null : SetsidCandidates.FirstOrDefault(File.Exists);

    /// <summary>
    /// 若 <paramref name="pid"/> 确实是自己所在进程组的 leader，向整组发信号并返回 true；
    /// 否则不发送任何信号并返回 false（由调用方回落）。
    /// </summary>
    public static bool TrySignalGroup(int pid, bool forceful)
    {
        // 平台判定收在类内部（同 WindowsJobObject 的理由）：libc 在 Windows 上不存在，
        // 未经守卫就调用会抛 DllNotFoundException。
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var pgid = getpgid(pid);
        if (pgid <= 0 || pgid != pid)
        {
            return false;
        }

        return kill(-pgid, forceful ? SIGKILL : SIGTERM) == 0;
    }

    /// <summary>向单个进程发 SIGTERM（请求优雅退出）。</summary>
    public static bool TrySignalProcess(int pid)
        => !OperatingSystem.IsWindows() && pid > 0 && kill(pid, SIGTERM) == 0;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport("libc", SetLastError = true)]
    private static extern int getpgid(int pid);
}
