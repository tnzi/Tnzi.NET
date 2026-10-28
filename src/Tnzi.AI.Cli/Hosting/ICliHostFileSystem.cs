namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// 执行目标上的文件系统：工作区布置、记忆文件注入与回滚、技能、MCP 配置、配置目录都经它读写。
/// </summary>
/// <remarks>
/// <para>
/// 路径是<b>目标自己</b>的路径，语法由 <see cref="Paths"/> 决定：API 进程跑在 Windows、CLI 跑在 Linux 容器里时，
/// 工作区路径是 <c>/home/agent/...</c>，拼接、取父目录、比较都得按 POSIX 来，<see cref="Path"/> 会把它们拼错。
/// </para>
/// <para>操作语义（实现方要照此行为，布置与回滚依赖它们）：</para>
/// <list type="bullet">
/// <item><see cref="ReadAllBytesAsync"/>：文件不在抛 <see cref="FileNotFoundException"/>。</item>
/// <item><see cref="WriteAllBytesAsync"/>：整文件覆盖；父目录不在抛 <see cref="DirectoryNotFoundException"/>。</item>
/// <item><see cref="CreateDirectoryAsync"/>：连同缺失的上级目录一起建，已存在不算错。</item>
/// <item><see cref="DeleteFileAsync"/> / <see cref="DeleteDirectoryAsync"/>：不在不算错；非递归删除一个非空目录抛 <see cref="IOException"/>
/// （回滚靠它保留 agent 自己留在受管目录里的文件）。</item>
/// <item>其余 IO 失败抛 <see cref="IOException"/> 或 <see cref="UnauthorizedAccessException"/>：执行器把它们归为「工作区布置失败」。</item>
/// </list>
/// </remarks>
public interface ICliHostFileSystem
{
    /// <summary>这个文件系统的路径语法。</summary>
    CliHostPaths Paths { get; }

    /// <summary>文件是否存在。</summary>
    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>目录是否存在。</summary>
    Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>建目录（含缺失的上级）。</summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken);

    /// <summary>读整个文件的字节。</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>以给定字节整体覆盖写文件。</summary>
    Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken);

    /// <summary>删文件。</summary>
    Task DeleteFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>删目录。</summary>
    Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="ICliHostFileSystem"/> 的文本读写。
/// </summary>
public static class CliHostFileSystemExtensions
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>以 UTF-8（无 BOM）写文本。</summary>
    public static Task WriteAllTextAsync(this ICliHostFileSystem fileSystem, string path, string contents, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);
        return fileSystem.WriteAllBytesAsync(path, Utf8NoBom.GetBytes(contents ?? string.Empty), cancellationToken);
    }

    /// <summary>读文本：按 BOM 认 UTF-8 / UTF-16 / UTF-32，没有 BOM 按 UTF-8。</summary>
    public static async Task<string> ReadAllTextAsync(this ICliHostFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        Check.NotNull(fileSystem);
        var bytes = await fileSystem.ReadAllBytesAsync(path, cancellationToken);
        using var reader = new StreamReader(new MemoryStream(bytes), Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
