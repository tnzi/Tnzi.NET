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
}
