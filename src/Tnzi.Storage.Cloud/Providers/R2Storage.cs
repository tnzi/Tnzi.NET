
namespace Tnzi.Storage.Cloud.Providers;

/// <summary>
/// Cloudflare R2 存储实现（兼容 S3 API）
/// 需要安装 AWSSDK.S3
/// </summary>
public class R2Storage : IFileStorage, IDisposable
{
    /// <summary>R2 的 SigV4 签名区域。R2 只有一个逻辑区域，固定写作 "auto"。</summary>
    private const string R2SigningRegion = "auto";

    private readonly R2StorageOptions _options;
    private readonly string? _baseUrl;
    private readonly IAmazonS3 _s3Client;
    private readonly S3ObjectStore _objects;
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
    /// 初始化 <see cref="R2Storage"/> 类型的新实例
    /// </summary>
    /// <param name="options">R2存储配置选项</param>
    /// <param name="configuration">配置</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="optionsMonitor">选项监视器（可选），用于运行时热读取 UrlPrefix</param>
    public R2Storage(R2StorageOptions options, IConfiguration? configuration = null, ILogger<R2Storage>? logger = null, IOptionsMonitor<StorageOptions>? optionsMonitor = null)
    {
        _options = Check.NotNull(options);
        _optionsMonitor = optionsMonitor;

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
        // R2 需要路径样式（ForcePathStyle = true）和自定义端点。
        // ★ 不能再设 RegionEndpoint：AWS SDK 里它与 ServiceURL 互斥，后设的那个会把先设的清空，
        // 客户端于是连到 s3.us-east-1.amazonaws.com，拿 R2 的密钥签名，每个请求都被拒。
        // 签名区域改由 AuthenticationRegion 给出：R2 的区域是 "auto"。
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint ?? "https://r2.cloudflarestorage.com",
            ForcePathStyle = true, // R2 必须使用路径样式
            AuthenticationRegion = R2SigningRegion
        };

        _s3Client = new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, config);
        _objects = new S3ObjectStore(_s3Client, _options.BucketName, ProviderName, presignProtocol: null, logger);
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "R2";

    /// <inheritdoc />
    public Task<string> UploadAsync(string fileName, Stream stream, string? contentType = null)
        => _objects.UploadAsync(fileName, stream, contentType);

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">对象不存在。</exception>
    public Task<Stream> DownloadAsync(string filePath) => _objects.DownloadAsync(filePath);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string filePath) => _objects.DeleteAsync(filePath);

    /// <inheritdoc />
    /// <remarks>只有 404 答 <c>false</c>；权限、网络等错误照常抛出。</remarks>
    public Task<bool> ExistsAsync(string filePath) => _objects.ExistsAsync(filePath);

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
            return Task.FromResult(_objects.GetPresignedUrl(filePath, expiresIn.Value, HttpVerb.GET));

        // 如果没有过期时间，返回基本URL
        var baseUrl = EffectiveBaseUrl;
        if (!string.IsNullOrEmpty(baseUrl))
        {
            var url = baseUrl.TrimEnd('/') + "/" + filePath.TrimStart('/');
            return Task.FromResult(url);
        }

        return Task.FromResult(filePath);
    }

    /// <inheritdoc />
    public Task<long> GetFileSizeAsync(string filePath) => _objects.GetFileSizeAsync(filePath);

    /// <inheritdoc />
    /// <exception cref="FileNotFoundException">对象不存在。</exception>
    public Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(
        string filePath,
        long? rangeStart = null,
        long? rangeEnd = null)
        => _objects.DownloadRangeAsync(filePath, rangeStart, rangeEnd);

    /// <summary>
    /// Generate a presigned URL for direct upload or temporary public download access.
    /// R2 is S3-compatible, so this uses the AWS SDK presigned URL generation.
    /// A GET URL for a type outside the inline whitelist carries a response-content-disposition of attachment.
    /// </summary>
    /// <param name="filePath">R2 object key</param>
    /// <param name="expiresInSeconds">URL expiration in seconds</param>
    /// <param name="httpMethod">HTTP method: GET for download, PUT for upload</param>
    /// <returns>Presigned URL, or null if the path is empty</returns>
    public Task<string?> GetPresignedUrlAsync(string filePath, int expiresInSeconds = 3600, string httpMethod = "GET")
    {
        if (string.IsNullOrEmpty(filePath))
            return Task.FromResult<string?>(null);

        var verb = httpMethod.Equals("PUT", StringComparison.OrdinalIgnoreCase) ? HttpVerb.PUT : HttpVerb.GET;
        return Task.FromResult<string?>(_objects.GetPresignedUrl(filePath, expiresInSeconds, verb));
    }

    /// <summary>
    /// Server-side copy an R2 (S3-compatible) object to a new key within the same bucket.
    /// </summary>
    /// <returns>The destination key on success, or null if the path is empty or the copy fails</returns>
    public Task<string?> CopyAsync(string sourcePath, string destFileName) => _objects.CopyAsync(sourcePath, destFileName);

    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}