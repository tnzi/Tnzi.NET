namespace Tnzi.AI.Sandbox.Abstractions;

public interface ISandbox : IAsyncDisposable
{
    string Id { get; }
    Task<CommandResult> ExecuteCommandAsync(string command, CancellationToken ct = default);
    Task<string> ReadFileAsync(string path, CancellationToken ct = default);
    Task WriteFileAsync(string path, string content, bool append = false, CancellationToken ct = default);
    Task UpdateFileAsync(string path, byte[] content, CancellationToken ct = default);
    Task<IReadOnlyList<FileEntry>> ListDirectoryAsync(string path, int maxDepth = 2, CancellationToken ct = default);
}
