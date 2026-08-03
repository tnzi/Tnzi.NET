
namespace Tnzi.Storage.Providers;

/// <summary>
/// AWS S3存储实现
/// </summary>
public class S3Storage : IFileStorage, IDisposable
{
    private readonly S3StorageOptions _options;
    private readonly string? _baseUrl;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<S3Storage>? _logger;
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
    /// 初始化 <see cref="S3Storage"/> 类型的新实例
    /// </summary>
    /// <param name="options">S3存储配置选项</param>
    /// <param name="configuration">配置</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="optionsMonitor">选项监视器（可选），用于运行时热读取 UrlPrefix</param>
    public S3Storage(S3StorageOptions options, IConfiguration? configuration = null, ILogger<S3Storage>? logger = null, IOptionsMonitor<StorageOptions>? optionsMonitor = null)
    {
        _options = Check.NotNull(options);
        _logger = logger;
        _optionsMonitor = optionsMonitor;

        Check.NotNullOrEmpty(_options.AccessKeyId);
        Check.NotNullOrEmpty(_options.SecretAccessKey);
        Check.NotNullOrEmpty(_options.BucketName);

        _baseUrl = configuration?["Storage:UrlPrefix"] ?? $"https://{_options.BucketName}.s3.{_options.Region}.amazonaws.com";

        // 创建 S3 客户端
        // 注意：AWS SDK 中 RegionEndpoint 与 ServiceURL 互斥，设置自定义端点时不能再设 RegionEndpoint。
        var config = new AmazonS3Config();

        // UseHttp 控制 SDK（含预签名 URL 签名器）生成的 scheme。
        config.UseHttp = !_options.UseHttps;

        if (!string.IsNullOrEmpty(_options.ServiceUrl))
        {
            // 自定义端点（如 MinIO / 自托管 S3 兼容服务）：
            // 依据 UseHttps 规整其 scheme，并使用路径样式以避免把桶名提升为子域名。
            config.ServiceURL = NormalizeServiceUrlScheme(_options.ServiceUrl, _options.UseHttps);
            config.ForcePathStyle = true;
        }
        else
        {
            // 默认 AWS 端点：由 RegionEndpoint 决定。
            config.RegionEndpoint = string.IsNullOrEmpty(_options.Region)
                ? RegionEndpoint.USEast1
                : RegionEndpoint.GetBySystemName(_options.Region);
        }

        _s3Client = new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, config);
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "S3";

    /// <summary>
    /// 上传文件
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="stream">文件流</param>
    /// <param name="contentType">内容类型</param>
    /// <returns>文件路径或URL</returns>
    public async Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
    {
        Check.NotNullOrEmpty(fileName);
        Check.NotNull(stream);

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = fileName,
            InputStream = stream,
            ContentType = contentType ?? "application/octet-stream",
            // AWS SDK 默认读完即关掉 InputStream，而流的生命周期归调用方
            // （见 IFileStorage.UploadAsync 的所有权约定）：上传之后调用方还要读它的
            // Length 写文件记录，被 SDK 关掉就是 ObjectDisposedException。
            AutoCloseStream = false
        };

        await _s3Client.PutObjectAsync(request);

        return fileName;
    }

    /// <summary>
    /// 下载文件
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>文件流</returns>
    public async Task<Stream> DownloadAsync(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        var request = new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = filePath
        };

        var response = await _s3Client.GetObjectAsync(request);
        return response.ResponseStream;
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>是否删除成功</returns>
    public async Task<bool> DeleteAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = filePath
            };
            await _s3Client.DeleteObjectAsync(request);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete file from S3: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>是否存在</returns>
    public async Task<bool> ExistsAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = filePath
            };
            await _s3Client.GetObjectMetadataAsync(request);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to check file existence in S3: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// 获取文件访问URL
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <param name="expiresIn">过期时间（秒），null表示永久有效</param>
    /// <returns>文件访问URL</returns>
    public Task<string> GetUrlAsync(string filePath, int? expiresIn = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult(string.Empty);

        if (expiresIn.HasValue && expiresIn.Value > 0)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = filePath,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.AddSeconds(expiresIn.Value)
            };
            return Task.FromResult(_s3Client.GetPreSignedURL(request));
        }

        // 如果没有过期时间，返回基本URL
        var url = EffectiveBaseUrl?.TrimEnd('/') + "/" + filePath.TrimStart('/');
        return Task.FromResult(url);
    }

    /// <summary>
    /// 获取文件大小
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>文件大小（字节）</returns>
    public async Task<long> GetFileSizeAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return 0L;

        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _options.BucketName,
                Key = filePath
            };
            var response = await _s3Client.GetObjectMetadataAsync(request);
            return response.ContentLength;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get file size from S3: {FilePath}", filePath);
            return 0L;
        }
    }

    /// <summary>
    /// 下载文件（支持 Range 请求，用于断点续传）。
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <param name="rangeStart">Range 起始位置（字节）</param>
    /// <param name="rangeEnd">Range 结束位置（字节）</param>
    /// <returns>文件流和范围信息</returns>
    public async Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath,
        long? rangeStart = null,
        long? rangeEnd = null)
    {
        Check.NotNullOrEmpty(filePath);

        // 先获取文件元数据以获取总长度
        var metadataRequest = new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = filePath
        };
        var metadataResponse = await _s3Client.GetObjectMetadataAsync(metadataRequest);
        var totalLength = metadataResponse.ContentLength;

        var request = new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = filePath
        };

        // 如果指定了范围，添加 Range 头
        if (rangeStart.HasValue)
        {
            var rangeEndForRequest = rangeEnd.HasValue ? rangeEnd.Value : totalLength - 1;
            request.ByteRange = new ByteRange(rangeStart.Value, rangeEndForRequest);
        }

        var response = await _s3Client.GetObjectAsync(request);

        // 如果没有指定范围，返回整个文件
        if (!rangeStart.HasValue)
        {
            return (response.ResponseStream, 0L, totalLength - 1, totalLength);
        }

        // 计算实际范围
        var start = rangeStart.Value;
        var actualEnd = rangeEnd.HasValue ? rangeEnd.Value : totalLength - 1;

        return (response.ResponseStream, start, actualEnd, totalLength);
    }

    /// <summary>
    /// Generate a presigned URL for direct upload or temporary public download access.
    /// </summary>
    /// <param name="filePath">S3 object key</param>
    /// <param name="expiresInSeconds">URL expiration in seconds</param>
    /// <param name="httpMethod">HTTP method: GET for download, PUT for upload</param>
    /// <returns>Presigned URL</returns>
    public Task<string?> GetPresignedUrlAsync(string filePath, int expiresInSeconds = 3600, string httpMethod = "GET")
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult<string?>(null);

        var verb = httpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase)
            ? HttpVerb.PUT
            : HttpVerb.GET;

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = filePath,
            Verb = verb,
            Expires = DateTime.UtcNow.AddSeconds(expiresInSeconds),
            // 预签名 URL 的 scheme 由请求的 Protocol 决定（默认 HTTPS），与 UseHttps 对齐。
            Protocol = _options.UseHttps ? Protocol.HTTPS : Protocol.HTTP
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult<string?>(url);
    }

    /// <summary>
    /// Server-side copy an S3 object to a new key within the same bucket (no download/upload round-trip).
    /// </summary>
    /// <param name="sourcePath">Source object key</param>
    /// <param name="destFileName">Destination object key</param>
    /// <returns>The destination key on success, or null if the path is empty or the copy fails</returns>
    public async Task<string?> CopyAsync(string sourcePath, string destFileName)
    {
        if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destFileName))
            return null;

        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = _options.BucketName,
                SourceKey = sourcePath,
                DestinationBucket = _options.BucketName,
                DestinationKey = destFileName
            };
            await _s3Client.CopyObjectAsync(request);
            return destFileName;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to server-side copy S3 object: {SourcePath} -> {DestFileName}", sourcePath, destFileName);
            return null;
        }
    }

    /// <summary>
    /// 依据 UseHttps 规整自定义服务端点的 scheme。
    /// 若端点已含 scheme 则替换为期望的 http/https；若无 scheme 则补全。
    /// </summary>
    private static string NormalizeServiceUrlScheme(string serviceUrl, bool useHttps)
    {
        var desiredScheme = useHttps ? "https" : "http";

        if (serviceUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return useHttps ? serviceUrl : $"http://{serviceUrl["https://".Length..]}";
        }

        if (serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return useHttps ? $"https://{serviceUrl["http://".Length..]}" : serviceUrl;
        }

        // 无 scheme：按期望补全
        return $"{desiredScheme}://{serviceUrl}";
    }

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}