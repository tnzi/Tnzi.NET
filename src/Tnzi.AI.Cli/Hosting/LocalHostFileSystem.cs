namespace Tnzi.AI.Cli.Hosting;

/// <summary>
/// API 进程所在机器的文件系统（<see cref="File"/> / <see cref="Directory"/>）。
/// </summary>
public sealed class LocalHostFileSystem : ICliHostFileSystem
{
    /// <summary>唯一实例（无状态）。</summary>
    public static LocalHostFileSystem Instance { get; } = new();

    private LocalHostFileSystem()
    {
    }

    /// <inheritdoc />
    public CliHostPaths Paths => CliHostPaths.Native;

    /// <inheritdoc />
    public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(path));

    /// <inheritdoc />
    public Task<bool> DirectoryExistsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(Directory.Exists(path));

    /// <inheritdoc />
    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
        => File.ReadAllBytesAsync(path, cancellationToken);

    /// <inheritdoc />
    public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, contents, cancellationToken);

    /// <inheritdoc />
    public Task DeleteFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // File.Delete 对不存在的文件本就不报错，只有父目录也不在时才抛
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // 同上：不在就算完成
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteDirectoryAsync(string path, bool recursive, CancellationToken cancellationToken)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // 检查与删除之间被别人删掉了（回收器、线程删除），与「本来就不在」同一结果
        }

        return Task.CompletedTask;
    }
}
