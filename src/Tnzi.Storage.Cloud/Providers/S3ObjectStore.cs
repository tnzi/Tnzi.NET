namespace Tnzi.Storage.Cloud.Providers;

/// <summary>
/// S3 协议上的对象操作，<see cref="S3Storage"/> 与 <see cref="R2Storage"/> 共用。
/// </summary>
/// <remarks>
/// <para>
/// 两个 provider 的差别只在客户端怎么构造（凭据链、端点、签名区域、预签名 URL 的 scheme）。对象操作本身是同一个协议，
/// 此前在两个类里逐行各抄一份，一处修了另一处就漂开。这里是它们唯一的实现。
/// </para>
/// <para>
/// ★ <b>「不存在」与「出错了」分开报。</b>读一个不存在的对象抛框架的 <see cref="FileNotFoundException"/>（与 Local / InMemory /
/// Azure 同一个类型，上层据此答 404），而不是裸的 <see cref="AmazonS3Exception"/>（500）。<see cref="ExistsAsync"/> 只在 404 时答
/// <c>false</c>：403、超时、网络故障照常抛出。此前它对任何错误都答 <c>false</c>，而删除路径据此判定「对象已不在」并删掉记录，
/// 一次网络故障就让对象失去最后一条指向它的记录。
/// </para>
/// </remarks>
internal sealed class S3ObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly string _providerName;
    private readonly Protocol? _presignProtocol;
    private readonly ILogger? _logger;

    /// <param name="client">已按 provider 配好的客户端；生命周期归调用方。</param>
    /// <param name="bucketName">桶名。</param>
    /// <param name="providerName">写进日志与异常消息的 provider 名（S3 / R2）。</param>
    /// <param name="presignProtocol">预签名 URL 的 scheme；null 表示沿用 SDK 的默认。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public S3ObjectStore(IAmazonS3 client, string bucketName, string providerName, Protocol? presignProtocol, ILogger? logger)
    {
        _client = Check.NotNull(client);
        _bucketName = Check.NotNullOrEmpty(bucketName);
        _providerName = Check.NotNullOrEmpty(providerName);
        _presignProtocol = presignProtocol;
        _logger = logger;
    }

    public async Task<string> UploadAsync(string key, Stream stream, string? contentType)
    {
        Check.NotNullOrEmpty(key);
        Check.NotNull(stream);

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType ?? "application/octet-stream",
            // AWS SDK 默认读完即关掉 InputStream，而流的生命周期归调用方
            // （见 IFileStorage.UploadAsync 的所有权约定）：上传之后调用方还要读它的
            // Length 写文件记录，被 SDK 关掉就是 ObjectDisposedException。
            AutoCloseStream = false
        };

        await _client.PutObjectAsync(request);
        return key;
    }

    public async Task<Stream> DownloadAsync(string key)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            var response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = _bucketName, Key = key });
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw NotFound(key);
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        try
        {
            await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucketName, Key = key });
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to delete file from {Provider}: {FilePath}", _providerName, key);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        try
        {
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucketName, Key = key });
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
        catch (Exception ex)
        {
            // 不知道在不在：交给调用方判断，而不是替它答「不在」。
            _logger?.LogWarning(ex, "Failed to check file existence in {Provider}: {FilePath}", _providerName, key);
            throw;
        }
    }

    public async Task<long> GetFileSizeAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return 0L;

        try
        {
            var response = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucketName, Key = key });
            return response.ContentLength;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to get file size from {Provider}: {FilePath}", _providerName, key);
            return 0L;
        }
    }

    public async Task<(Stream Stream, long Start, long End, long TotalLength)> DownloadRangeAsync(string key, long? rangeStart, long? rangeEnd)
    {
        Check.NotNullOrEmpty(key);

        try
        {
            // 先取元数据拿总长度
            var metadata = await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucketName, Key = key });
            var totalLength = metadata.ContentLength;

            var request = new GetObjectRequest { BucketName = _bucketName, Key = key };
            if (rangeStart.HasValue)
            {
                request.ByteRange = new ByteRange(rangeStart.Value, rangeEnd ?? totalLength - 1);
            }

            var response = await _client.GetObjectAsync(request);

            // 没指定范围就是整个文件
            if (!rangeStart.HasValue)
            {
                return (response.ResponseStream, 0L, totalLength - 1, totalLength);
            }

            return (response.ResponseStream, rangeStart.Value, rangeEnd ?? totalLength - 1, totalLength);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw NotFound(key);
        }
    }

    /// <summary>
    /// 签一条预签名 URL。读 URL 对内联白名单之外的类型带 <c>response-content-disposition=attachment</c>。
    /// </summary>
    /// <remarks>
    /// 预签名的读 URL 由桶直接服务，<c>Content-Type</c> 是上传时按上传者给的文件名定下的（<c>.html → text/html</c>、
    /// <c>.svg → image/svg+xml</c>）。不加处置，浏览器打开这条链接就在桶的源上渲染并执行其中的脚本；桶经同站自定义域名暴露时，
    /// 脚本还拿得到同站上下文。<c>files/{id}/preview</c> 对同一类型早就按附件发出，这里是同一条判据
    /// （<see cref="FileTypeHelper.IsInlineRenderable"/>，按键的扩展名推出类型，键由服务端生成并保留原扩展名）。
    /// 附件处置不影响 <c>&lt;img&gt;</c> / <c>&lt;video&gt;</c> 里的子资源加载。
    /// </remarks>
    public string GetPresignedUrl(string key, int expiresInSeconds, HttpVerb verb)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Verb = verb,
            Expires = DateTime.UtcNow.AddSeconds(expiresInSeconds)
        };

        if (_presignProtocol is { } protocol)
        {
            request.Protocol = protocol;
        }

        if (verb == HttpVerb.GET && !FileTypeHelper.IsInlineRenderable(FileTypeHelper.GetContentType(Path.GetExtension(key))))
        {
            request.ResponseHeaderOverrides.ContentDisposition = "attachment";
        }

        return _client.GetPreSignedURL(request);
    }

    public async Task<string?> CopyAsync(string sourceKey, string destKey)
    {
        if (string.IsNullOrEmpty(sourceKey) || string.IsNullOrEmpty(destKey))
            return null;

        try
        {
            await _client.CopyObjectAsync(new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = sourceKey,
                DestinationBucket = _bucketName,
                DestinationKey = destKey
            });
            return destKey;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to server-side copy {Provider} object: {SourcePath} -> {DestFileName}", _providerName, sourceKey, destKey);
            return null;
        }
    }

    private static bool IsNotFound(AmazonS3Exception ex) => ex.StatusCode == HttpStatusCode.NotFound;

    private FileNotFoundException NotFound(string key)
    {
        _logger?.LogWarning("File '{FilePath}' not found in {Provider}", key, _providerName);
        return new FileNotFoundException($"File '{key}' not found in {_providerName} storage", key);
    }
}
