namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 流的所有权约定（见 <see cref="IFileStorage.UploadAsync"/>）：流归调用方，provider 不得关它。
///
/// 但服务层不能把正确性押在这条约定上：provider 可由消费方经
/// <c>StorageProviderFactory.Register()</c> 自行注册，框架管不到别人写的实现。
/// 所以调用方一律"用完即取值"：需要的东西（长度 / MD5）在把流交出去**之前**就拿到手。
///
/// 这批用例用一个"上传后必关流"的假 provider 把那条约定踩碎，验证保存路径照样成立。
/// </summary>
public class StreamOwnershipTests : StorageIntegrationTestBase
{
    private static StorageOptions PdfOptions(bool enableMd5 = true) => new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".pdf", ".txt"],
        AutoGenerateThumbnail = false,
        EnableMd5Validation = enableMd5
    };

    [Fact]
    public async Task SaveFromBytesAsync_ProviderClosesTheStream_StillSucceedsWithCorrectSize()
    {
        // 服务端生成的文件（这里是 PDF 字节）走的正是这条路：SaveFromBytesAsync 包一个
        // MemoryStream 交给 SaveAsync，provider 读完就把它关掉。
        var storage = new StreamClosingFileStorage();
        var service = CreateStorageService(storage, PdfOptions());
        var content = "%PDF-1.7 generated-server-side"u8.ToArray();

        var result = await service.SaveFromBytesAsync("statement.pdf", content, "application/pdf");

        Assert.True(result.Succeeded);
        Assert.Equal(content.LongLength, result.Data!.Size);
        Assert.True(storage.ClosedTheCallerStream, "假 provider 必须真的关掉了流，否则这条用例没有守住任何东西");
    }

    [Fact]
    public async Task SaveAsync_ProviderClosesTheStream_StillSucceedsWithCorrectSize()
    {
        var storage = new StreamClosingFileStorage();
        var service = CreateStorageService(storage, PdfOptions());
        var content = "%PDF-1.7 stream-overload"u8.ToArray();

        var result = await service.SaveAsync("invoice.pdf", new MemoryStream(content));

        Assert.True(result.Succeeded);
        Assert.Equal(content.LongLength, result.Data!.Size);
    }

    [Fact]
    public async Task SaveAsync_ProviderClosesTheStream_PersistsTheSizeToTheDatabase()
    {
        // Size 是要落库的，不能只是返回值对。
        var storage = new StreamClosingFileStorage();
        var service = CreateStorageService(storage, PdfOptions());
        var content = "%PDF-1.7 persisted"u8.ToArray();

        var result = await service.SaveAsync("report.pdf", new MemoryStream(content));

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal(content.LongLength, DbContext.FileRecords.Single(r => r.Id == result.Data!.Id).Size);
    }

    [Fact]
    public async Task SaveAsync_NonSeekableStream_FallsBackToTheProviderReportedSize()
    {
        // 网络流量不出长度：不该在这里崩，退化成问 provider 要已落盘对象的大小。
        // （MD5 一路要求可 seek，所以这条路径本就只在关闭 MD5 时可达。）
        var storage = new StreamClosingFileStorage();
        var service = CreateStorageService(storage, PdfOptions(enableMd5: false));
        var content = "%PDF-1.7 not-seekable"u8.ToArray();

        var result = await service.SaveAsync("piped.pdf", new NonSeekableStream(content));

        Assert.True(result.Succeeded);
        Assert.Equal(content.LongLength, result.Data!.Size);
    }

    [Fact]
    public async Task LocalStorage_DoesNotDisposeTheCallerStream()
    {
        // 内置 provider 自身必须守住约定，否则上面的防御只是在给自己打补丁。
        using var stream = new MemoryStream("local-contract"u8.ToArray());

        await Storage.UploadAsync("contract.txt", stream, "text/plain");

        Assert.True(stream.CanRead);
        Assert.Equal(14L, stream.Length);
    }

    [Fact]
    public async Task InMemoryStorage_DoesNotDisposeTheCallerStream()
    {
        var storage = new InMemoryFileStorage();
        using var stream = new MemoryStream("inmemory-contract"u8.ToArray());

        await storage.UploadAsync("contract.txt", stream, "text/plain");

        Assert.True(stream.CanRead);
        Assert.Equal(17L, stream.Length);
    }

    /// <summary>
    /// 上传后关掉调用方流的假 provider，复刻 AWS SDK 的
    /// <c>PutObjectRequest.AutoCloseStream</c>（默认 true）在 S3 / R2 上的表现。
    /// </summary>
    private sealed class StreamClosingFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

        public string ProviderName => "StreamClosing";

        /// <summary>是否确实关掉过调用方传进来的流（用来证明这批用例踩到了那条约定）。</summary>
        public bool ClosedTheCallerStream { get; private set; }

        public async Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            var path = $"closing/{fileName}";
            _files[path] = buffer.ToArray();

            stream.Dispose();
            ClosedTheCallerStream = true;
            return path;
        }

        public Task<Stream> DownloadAsync(string filePath)
        {
            if (!_files.TryGetValue(filePath, out var data))
                throw new FileNotFoundException($"File not found: {filePath}");

            return Task.FromResult<Stream>(new MemoryStream(data));
        }

        public Task<bool> DeleteAsync(string filePath) => Task.FromResult(_files.Remove(filePath));

        public Task<bool> ExistsAsync(string filePath) => Task.FromResult(_files.ContainsKey(filePath));

        public Task<string> GetUrlAsync(string filePath, int? expiresIn = null) => Task.FromResult(filePath);

        public Task<long> GetFileSizeAsync(string filePath)
            => Task.FromResult(_files.TryGetValue(filePath, out var data) ? data.LongLength : 0L);

        public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
            string filePath, long? rangeStart = null, long? rangeEnd = null)
        {
            var data = _files.TryGetValue(filePath, out var found) ? found : [];
            return Task.FromResult<(Stream, long, long, long)>(
                (new MemoryStream(data), 0L, data.LongLength - 1, data.LongLength));
        }
    }

    /// <summary>
    /// 只读、不可 seek 的流（网络流的形态）：<c>Length</c> / <c>Position</c> 一律不支持。
    /// </summary>
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;

        public NonSeekableStream(byte[] content) => _inner = new MemoryStream(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }
    }
}
