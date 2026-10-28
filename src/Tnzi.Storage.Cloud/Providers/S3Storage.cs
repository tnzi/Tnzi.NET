
namespace Tnzi.Storage.Cloud.Providers;

/// <summary>
/// AWS S3存储实现
/// </summary>
public class S3Storage : IFileStorage, IDisposable
{
    private readonly S3StorageOptions _options;
    private readonly string? _baseUrl;
    private readonly IAmazonS3 _s3Client;
    private readonly S3ObjectStore _objects;
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

        if (_options.UsesDefaultCredentialChain)
        {
            // 没配静态密钥：不传凭据，由 AWS SDK 按它自己的默认顺序解析
            // （环境变量 / 共享凭据文件 / ECS 任务角色 / EC2 实例角色，先后次序由 SDK 决定），
            // 与消费方代码里裸写 new AmazonS3Client() 的行为完全一致。
            // 记一条 Information 是因为「这个部署用的是谁的身份」在排查时是第一个要问的问题，
            // 而默认凭据链的答案不在任何配置文件里。
            _logger?.LogInformation(
                "S3 storage: no static credentials configured; resolving credentials from the AWS SDK default chain (environment variables, shared credentials file, ECS/EC2 role).");
            _s3Client = new AmazonS3Client(config);
        }
        else
        {
            // 半填的配置在这里也拦一次：直接 new S3Storage(...) 的调用方绕过了选项验证器。
            Check.NotNullOrWhiteSpace(_options.AccessKeyId);
            Check.NotNullOrWhiteSpace(_options.SecretAccessKey);
            _s3Client = new AmazonS3Client(_options.AccessKeyId, _options.SecretAccessKey, config);
        }

        // 预签名 URL 的 scheme 由请求的 Protocol 决定（默认 HTTPS），与 UseHttps 对齐。
        _objects = new S3ObjectStore(_s3Client, _options.BucketName, ProviderName,
            _options.UseHttps ? Protocol.HTTPS : Protocol.HTTP, logger);
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "S3";

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
        var url = EffectiveBaseUrl?.TrimEnd('/') + "/" + filePath.TrimStart('/');
        return Task.FromResult(url);
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
    /// A GET URL for a type outside the inline whitelist carries a response-content-disposition of attachment.
    /// </summary>
    /// <param name="filePath">S3 object key</param>
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
    /// Server-side copy an S3 object to a new key within the same bucket (no download/upload round-trip).
    /// </summary>
    /// <returns>The destination key on success, or null if the path is empty or the copy fails</returns>
    public Task<string?> CopyAsync(string sourcePath, string destFileName) => _objects.CopyAsync(sourcePath, destFileName);

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