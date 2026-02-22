
namespace Tnzi.Storage.Providers;

/// <summary>
/// Cloudflare R2 存储实现（兼容 S3 API）
/// 需要安装 AWSSDK.S3
/// </summary>
public class R2Storage : IFileStorage, IDisposable
{
    private readonly R2StorageOptions _options;
    private readonly string? _baseUrl;
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<R2Storage>? _logger;

    /// <summary>
    /// 初始化 <see cref="R2Storage"/> 类型的新实例
    /// </summary>
    /// <param name="options">R2存储配置选项</param>
    /// <param name="configuration">配置</param>
    /// <param name="logger">日志记录器</param>
    public R2Storage(R2StorageOptions options, IConfiguration? configuration = null, ILogger<R2Storage>? logger = null)
    {
        _options = Check.NotNull(options);
        _logger = logger;

        Check.NotNullOrEmpty(_options.AccessKeyId);
        Check.NotNullOrEmpty(_options.SecretAccessKey);
        Check.NotNullOrEmpty(_options.BucketName);

        // R2 的端点URL格式：https://{AccountId}.r2.cloudflarestorage.com
        var endpoint = _options.CustomEndpoint;
        if (string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(_options.AccountId))
        {
            endpoint = $"https://{_options.AccountId}.r2.cloudflarestorage.com";
        }

        // 构建基础URL（用于直接访问）
        _baseUrl = configuration?["Storage:UrlPrefix"];
        if (string.IsNullOrEmpty(_baseUrl) && !string.IsNullOrEmpty(endpoint))
        {
            _baseUrl = $"{endpoint.TrimEnd('/')}/{_options.BucketName}";
        }

        // 创建 S3 兼容客户端（用于 R2）
        // R2 需要路径样式（ForcePathStyle = true）和自定义端点
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint ?? "https://r2.cloudflarestorage.com",
            ForcePathStyle = true, // R2 必须使用路径样式
            RegionEndpoint = RegionEndpoint.USEast1 // R2 不区分区域，但需要设置一个默认值
        };

        _s3Client = new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, config);
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "R2";

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
            ContentType = contentType ?? "application/octet-stream"
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
            _logger?.LogWarning(ex, "Failed to delete file from R2: {FilePath}", filePath);
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
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to check file existence in R2: {FilePath}", filePath);
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
        if (!string.IsNullOrEmpty(_baseUrl))
        {
            var url = _baseUrl.TrimEnd('/') + "/" + filePath.TrimStart('/');
            return Task.FromResult(url);
        }

        return Task.FromResult(filePath);
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
            _logger?.LogWarning(ex, "Failed to get file size from R2: {FilePath}", filePath);
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

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}