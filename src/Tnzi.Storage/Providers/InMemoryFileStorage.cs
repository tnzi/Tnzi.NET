namespace Tnzi.Storage.Providers;

/// <summary>
/// In-memory file storage provider for testing purposes.
/// Files are stored in a ConcurrentDictionary and lost when the application stops.
/// </summary>
public class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Provider name
    /// </summary>
    public string ProviderName => "InMemory";

    public async Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
    {
        Check.NotNullOrEmpty(fileName);
        Check.NotNull(stream);

        // Generate unique path with date prefix
        var now = DateTime.UtcNow;
        var path = $"{now:yyyy}/{now:MM}/{now:dd}/{fileName}";

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        _files[path] = ms.ToArray();

        return path;
    }

    public Task<Stream> DownloadAsync(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        if (!_files.TryGetValue(filePath, out var data))
            throw new FileNotFoundException($"File not found: {filePath}");

        return Task.FromResult<Stream>(new MemoryStream(data));
    }

    public Task<bool> DeleteAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult(false);

        return Task.FromResult(_files.TryRemove(filePath, out _));
    }

    public Task<bool> ExistsAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult(false);

        return Task.FromResult(_files.ContainsKey(filePath));
    }

    public Task<string> GetUrlAsync(string filePath, int? expiresIn = null)
    {
        return Task.FromResult(filePath ?? string.Empty);
    }

    public Task<long> GetFileSizeAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !_files.TryGetValue(filePath, out var data))
            return Task.FromResult(0L);

        return Task.FromResult((long)data.Length);
    }

    public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath,
        long? rangeStart = null,
        long? rangeEnd = null)
    {
        Check.NotNullOrEmpty(filePath);

        if (!_files.TryGetValue(filePath, out var data))
            throw new FileNotFoundException($"File not found: {filePath}");

        long totalLength = data.Length;

        if (!rangeStart.HasValue)
        {
            return Task.FromResult<(Stream, long, long, long)>(
                (new MemoryStream(data), 0L, totalLength - 1, totalLength));
        }

        var start = Math.Max(0, rangeStart.Value);
        var end = rangeEnd.HasValue
            ? Math.Min(totalLength - 1, rangeEnd.Value)
            : totalLength - 1;

        if (start > end || start >= totalLength)
            throw new ArgumentException("Invalid range specified.");

        var length = (int)(end - start + 1);
        var rangeData = new byte[length];
        Array.Copy(data, start, rangeData, 0, length);

        return Task.FromResult<(Stream, long, long, long)>(
            (new MemoryStream(rangeData), start, end, totalLength));
    }

    /// <summary>
    /// Get the number of files currently stored (for testing assertions)
    /// </summary>
    public int FileCount => _files.Count;

    /// <summary>
    /// Clear all stored files (for test cleanup)
    /// </summary>
    public void Clear() => _files.Clear();
}
