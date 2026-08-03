namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// Windows Job Object 包装：把子进程连同它的<b>全部后代</b>装进一个作业对象，
/// 关闭句柄即整树终止。
/// </summary>
/// <remarks>
/// <para>
/// Windows 没有 Unix 的进程组信号语义，<c>Process.Kill(entireProcessTree: true)</c> 依赖
/// 遍历快照找子进程 —— 一个已经把父进程 ID 改掉或刚被重新挂载的后代会漏网。
/// Job Object 是内核层面的归属关系：<c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> 意味着
/// 只要作业句柄关闭，内核保证杀干净每一个成员，无论进程树长成什么样。
/// 实测运行中的 claude 有 6 个后代进程；只杀 leader 会全部泄漏，Job Object 杀得干净。
/// </para>
/// <para>
/// <b>关于 Start 与 Assign 之间的竞态</b>：理论上子进程可能在被加入作业之前就先派生孙进程，
/// 那个孙进程便逃出了作业。本模块用<b>协议本身</b>关掉这个窗口 —— 所有支持的协议都是
/// 「先启动、等我投递提示（或 initialize）之后才干活」，而投递发生在 assign 之后。
/// 换言之子进程在被纳入作业前根本没有理由派生任何东西。这比引入
/// <c>CREATE_SUSPENDED</c> + 手工 P/Invoke <c>CreateProcess</c>（要自己接管三条管道的句柄继承）
/// 简单得多，也更不容易出错。
/// </para>
/// <para>
/// 用经典 <see cref="DllImportAttribute"/> 而不是源生成的 <c>LibraryImport</c>：后者要求整个
/// 程序集打开 <c>AllowUnsafeBlocks</c>，而这是全仓第一个需要 P/Invoke 的项目 ——
/// 为了两个 kernel32 调用给一个「负责执行外部任意代码」的程序集全局放开 unsafe，
/// 不是划算的交换。
/// </para>
/// </remarks>
internal sealed class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private nint _handle;

    private WindowsJobObject(nint handle) => _handle = handle;

    /// <summary>
    /// 创建一个「句柄关闭即杀光成员」的作业对象。创建失败返回 null ——
    /// 调用方回落到逐进程终止，而不是让整个运行起不来。
    /// </summary>
    public static WindowsJobObject? TryCreate()
    {
        // 平台判定收在类内部而不是靠 [SupportedOSPlatform] 逼调用方加守卫：
        // 调用点的「是否 Windows」信息藏在 `_job is not null` 这类条件里，
        // 分析器看不穿，于是属性只会换来一片必须逐个抑制的误报。
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var handle = CreateJobObjectW(nint.Zero, null);
        if (handle == nint.Zero)
        {
            return null;
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                CloseHandle(handle);
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new WindowsJobObject(handle);
    }

    /// <summary>把一个已启动的进程纳入本作业。</summary>
    public bool TryAssign(nint processHandle)
        => OperatingSystem.IsWindows() && _handle != nint.Zero && AssignProcessToJobObject(_handle, processHandle);

    /// <summary>关闭作业句柄 —— 内核随即终止全部仍存活的成员进程。</summary>
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero && OperatingSystem.IsWindows())
        {
            CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
