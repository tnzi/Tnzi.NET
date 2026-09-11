namespace Tnzi.Storage.Workspace.Tests.TestSupport;

/// <summary>
/// 把每一次 <see cref="UploadAsync"/> 收到的**键**记下来，其余全部转给内层 provider。
/// 存在的理由：要断言的是「服务层交给 provider 的键长什么样」，而这个值不落库、
/// 不出现在任何 DTO 上，只有站在 provider 的位置才看得见。
/// </summary>
public sealed class RecordingFileStorage : IFileStorage
{
    private readonly IFileStorage _inner;
    private readonly List<string> _keys = [];

    public RecordingFileStorage(IFileStorage inner)
    {
        _inner = inner;
    }

    /// <summary>按调用顺序记下的全部上传键。</summary>
    public IReadOnlyList<string> UploadedKeys => _keys;

    public string ProviderName => _inner.ProviderName;

    public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
    {
        _keys.Add(fileName);
        return _inner.UploadAsync(fileName, stream, contentType);
    }

    public Task<Stream> DownloadAsync(string filePath) => _inner.DownloadAsync(filePath);

    public Task<bool> DeleteAsync(string filePath) => _inner.DeleteAsync(filePath);

    public Task<bool> ExistsAsync(string filePath) => _inner.ExistsAsync(filePath);

    public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => _inner.GetUrlAsync(filePath, expiresIn);

    public Task<long> GetFileSizeAsync(string filePath) => _inner.GetFileSizeAsync(filePath);

    public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath, long? rangeStart = null, long? rangeEnd = null)
        => _inner.DownloadRangeAsync(filePath, rangeStart, rangeEnd);

    public Task<string?> CopyAsync(string sourcePath, string destFileName)
    {
        _keys.Add(destFileName);
        return _inner.CopyAsync(sourcePath, destFileName);
    }
}
