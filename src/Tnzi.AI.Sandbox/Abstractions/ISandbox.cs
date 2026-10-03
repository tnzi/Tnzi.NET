namespace Tnzi.AI.Sandbox.Abstractions;

public interface ISandbox : IAsyncDisposable
{
    string Id { get; }
    Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default);

    /// <summary>
    /// Reads a file's contents, optionally slicing to a line range.
    /// </summary>
    /// <param name="path">Physical path inside the sandbox workspace.</param>
    /// <param name="offset">1-based line to start at (<c>null</c> = from the first line).</param>
    /// <param name="limit">Maximum number of lines to return (<c>null</c> = to end of file).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Implementations enforce the configured maximum file size and the
    /// <c>DeniedPatterns</c> sensitive-file blocklist before returning content;
    /// a denied or oversized read throws <see cref="System.Security.SecurityException"/>.
    /// When <paramref name="offset"/>/<paramref name="limit"/> are supplied the
    /// implementation streams line-by-line rather than materialising the whole file.
    /// </remarks>
    Task<string> ReadFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default);
    Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default);
    Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default);
    Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default);

    /// <summary>
    /// 把一条<b>已经通过宿主侧围栏</b>的物理路径换成沙箱自己寻址的形态。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认恒等：本地 provider 直接在宿主上跑，路径就是路径。Docker provider 把线程目录挂在容器的
    /// <c>/workspace</c> 下，容器里根本不存在宿主路径（Linux 宿主上是 <c>/var/lib/.../{tid}/...</c>，
    /// Windows 宿主上甚至是 <c>D:\...</c>），所以每一条交给沙箱的路径（文件工具的参数、bash 命令里
    /// 翻译出来的 <c>/mnt/*</c> token）都要先经这里换根。
    /// </para>
    /// <para>
    /// 这不是第二道围栏：越界判定发生在<see cref="IVirtualPathTranslator"/>
    /// 里、在解析过符号链接的宿主路径上；这里只负责换根。实现遇到不在自己挂载点下的路径应抛
    /// <see cref="System.Security.SecurityException"/>（失败关闭），而不是猜一个位置。
    /// </para>
    /// </remarks>
    /// <param name="hostPath">宿主侧的物理路径。</param>
    /// <returns>沙箱内寻址用的路径。</returns>
    string MapPath(string hostPath) => hostPath;

    /// <summary>
    /// <see cref="ExecuteCommandAsync"/> 交给哪种 shell 执行，决定 bash 工具怎样给换进命令里的路径加引号。
    /// 默认 POSIX；在 Windows 宿主上直接调 <c>cmd.exe</c> 的实现返回 <see cref="SandboxShellDialect.Cmd"/>。
    /// </summary>
    SandboxShellDialect ShellDialect => SandboxShellDialect.Posix;
}
