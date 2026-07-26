namespace Tnzi.Storage.Providers;

/// <summary>
/// 本地存储实现
/// </summary>
public class LocalStorage : IFileStorage
{
    private readonly string _basePath;
    private readonly string? _baseUrl;
    private readonly ILogger<LocalStorage>? _logger;
    private readonly IOptionsMonitor<StorageOptions>? _optionsMonitor;

    /// <summary>
    /// 运行时有效的 URL 前缀：优先取 IOptionsMonitor 的当前值（支持配置热更新），
    /// 为空时回退到构造期冻结的 _baseUrl（保持既有行为）。
    /// </summary>
    private string? EffectiveBaseUrl
    {
        get
        {
            var hot = _optionsMonitor?.CurrentValue.UrlPrefix;
            return !string.IsNullOrEmpty(hot) ? hot : _baseUrl;
        }
    }

    /// <summary>
    /// 初始化 <see cref="LocalStorage"/> 类型的新实例
    /// </summary>
    /// <param name="configuration">配置</param>
    /// <param name="environment">Web宿主环境</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="optionsMonitor">选项监视器（可选），用于运行时热读取 UrlPrefix</param>
    public LocalStorage(IConfiguration configuration, IWebHostEnvironment? environment = null, ILogger<LocalStorage>? logger = null, IOptionsMonitor<StorageOptions>? optionsMonitor = null)
    {
        _logger = logger;
        _optionsMonitor = optionsMonitor;
        // 与模块配置节一致：Storage（参见 StorageModule.GetSection("Storage")）
        var storagePath = configuration["Storage:StoragePath"]
                       ?? configuration["Storage:LocalPath"]
                       ?? "AppData/Files";

        // 如果是绝对路径，直接使用
        if (Path.IsPathRooted(storagePath))
        {
            _basePath = storagePath;
        }
        else
        {
            // 相对路径处理：默认使用 ContentRootPath（wwwroot 外的目录）以确保安全
            var contentRootPath = environment?.ContentRootPath ?? Directory.GetCurrentDirectory();
            var normalizedPath = storagePath.Replace('\\', '/').TrimStart('/');

            // 如果路径以 wwwroot 开头，移除前缀，改为 ContentRootPath
            if (normalizedPath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                // 从路径中移除 wwwroot 前缀，使用 ContentRootPath 而非 WebRootPath
                normalizedPath = normalizedPath["wwwroot/".Length..];
            }

            _basePath = Path.Combine(contentRootPath, normalizedPath);
        }

        // URL 前缀用于通过控制器访问（默认通过 API）
        _baseUrl = configuration["Storage:UrlPrefix"];

        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "Local";

    /// <summary>
    /// 上传文件
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="stream">文件流</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>文件路径（相对路径，包含日期目录）</returns>
    public async Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
    {
        Check.NotNullOrEmpty(fileName);
        Check.NotNull(stream);

        // 存储键只接受叶子文件名。调用方传入的名字可能来自客户端（分片上传会话的 FileName、
        // 压缩包名、压缩包内条目名），带目录分隔符或盘符时 Path.Combine 会写到 base 目录之外，
        // 因此统一取叶子名后再拼接。
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(safeFileName))
        {
            throw new FileUploadException("Invalid file name.", fileName);
        }

        // 按日期分目录：yyyy/MM/dd（使用 UTC 时间确保一致性）
        var now = DateTime.UtcNow;
        var datePath = $"{now:yyyy}/{now:MM}/{now:dd}";
        var relativePath = Path.Combine(datePath, safeFileName).Replace('\\', '/');
        var fullPath = ResolvePhysicalPath(relativePath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
        {
            await stream.CopyToAsync(fileStream);
        }

        return relativePath; // 返回相对路径（包含日期目录）
    }

    /// <summary>
    /// 下载文件
    /// </summary>
    /// <param name="filePath">文件路径（相对路径，可能包含日期目录）</param>
    /// <returns>文件流</returns>
    public Task<Stream> DownloadAsync(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        // filePath 是相对路径（包含日期目录），解析时校验其仍落在 base 目录内
        var fullPath = ResolvePhysicalPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        return Task.FromResult<Stream>(new FileStream(fullPath, FileMode.Open, FileAccess.Read));
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否删除成功</returns>
    public Task<bool> DeleteAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !TryResolvePhysicalPath(filePath, out var fullPath))
            return Task.FromResult(false);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(fullPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            // 文件删除失败（可能原因：文件已被删除、权限不足、文件被占用等）
            // 返回 false 表示删除失败，调用方可以根据需要处理
            _logger?.LogWarning(ex, "Failed to delete local file: {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否存在</returns>
    public Task<bool> ExistsAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !TryResolvePhysicalPath(filePath, out var fullPath))
            return Task.FromResult(false);

        return Task.FromResult(File.Exists(fullPath));
    }

    /// <summary>
    /// 获取文件访问URL
    /// 注意：本地存储返回的 URL 仅作为路径标识，实际访问应通过控制器端点
    /// 真正的访问 URL 应在 FileStorageService 中基于文件 ID 生成
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="expiresIn">过期时间（秒），本地存储忽略此参数</param>
    /// <returns>文件路径标识（不用于直接访问）</returns>
    public Task<string> GetUrlAsync(string filePath, int? expiresIn = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult(string.Empty);

        // 返回文件路径作为标识，实际访问 URL 应在 FileStorageService 中基于文件 ID 生成
        // 这里保留原逻辑以兼容其他可能的使用场景
        var baseUrl = EffectiveBaseUrl;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var url = baseUrl.TrimEnd('/') + "/" + filePath.TrimStart('/');
            return Task.FromResult(url);
        }

        return Task.FromResult(filePath);
    }

    /// <summary>
    /// 获取文件大小
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>文件大小（字节）</returns>
    public Task<long> GetFileSizeAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !TryResolvePhysicalPath(filePath, out var fullPath))
            return Task.FromResult(0L);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(0L);
        }

        var fileInfo = new FileInfo(fullPath);
        return Task.FromResult(fileInfo.Length);
    }

    /// <summary>
    /// 下载文件（支持 Range 请求，用于断点续传）。
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="rangeStart">Range 起始位置（字节）</param>
    /// <param name="rangeEnd">Range 结束位置（字节）</param>
    /// <returns>文件流和范围信息</returns>
    public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath,
        long? rangeStart = null,
        long? rangeEnd = null)
    {
        Check.NotNullOrEmpty(filePath);

        var fullPath = ResolvePhysicalPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var fileInfo = new FileInfo(fullPath);
        var totalLength = fileInfo.Length;

        // 如果没有指定范围，返回整个文件
        if (!rangeStart.HasValue)
        {
            var fullStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            return Task.FromResult<(Stream, long, long, long)>((fullStream, 0L, totalLength - 1, totalLength));
        }

        // 验证和调整范围
        var start = Math.Max(0, rangeStart.Value);
        var end = rangeEnd.HasValue
            ? Math.Min(totalLength - 1, rangeEnd.Value)
            : totalLength - 1;

        if (start > end || start >= totalLength)
        {
            throw new ArgumentException("Invalid range specified.");
        }

        // 创建范围流
        var rangeFileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        rangeFileStream.Seek(start, SeekOrigin.Begin);

        // 返回范围流（需要包装以限制读取长度）
        var rangeStream = new RangeStream(rangeFileStream, start, end - start + 1);
        return Task.FromResult<(Stream, long, long, long)>((rangeStream, start, end, totalLength));
    }

    /// <summary>
    /// 把存储键解析为 base 目录内的物理路径；越界即抛出。
    /// 键本应由本模块生成（yyyy/MM/dd/文件名），此处是兜底防线：
    /// 含 ".." 或盘符/根路径的键会被 Path.Combine 解析到 base 目录之外，一律拒绝。
    /// </summary>
    private string ResolvePhysicalPath(string relativeKey)
    {
        if (!TryResolvePhysicalPath(relativeKey, out var fullPath))
        {
            throw new StorageException("Invalid file path.", relativeKey);
        }

        return fullPath;
    }

    /// <summary>
    /// 尝试解析存储键；越界或键本身非法时返回 false（供 Delete/Exists 等容错方法使用）。
    /// </summary>
    private bool TryResolvePhysicalPath(string relativeKey, out string fullPath)
    {
        fullPath = string.Empty;

        try
        {
            var root = Path.GetFullPath(_basePath);
            var resolved = Path.GetFullPath(Path.Combine(root, relativeKey));

            var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
                ? root
                : root + Path.DirectorySeparatorChar;

            if (!resolved.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            fullPath = resolved;
            return true;
        }
        catch (ArgumentException)
        {
            // 键含非法字符：视为无效路径，交由调用方按各自语义处理
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }
}

/// <summary>
/// 范围流包装器，限制读取长度
/// </summary>
internal class RangeStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public RangeStream(Stream baseStream, long start, long length)
    {
        _baseStream = Check.NotNull(baseStream);
        _start = start;
        _length = length;
        _position = 0;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = _length - _position;
        if (remaining <= 0)
            return 0;

        var bytesToRead = (int)Math.Min(count, remaining);
        var bytesRead = _baseStream.Read(buffer, offset, bytesToRead);
        _position += bytesRead;
        return bytesRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => _baseStream.Flush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _baseStream?.Dispose();
        }
        base.Dispose(disposing);
    }
}
