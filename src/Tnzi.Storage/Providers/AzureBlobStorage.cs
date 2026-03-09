
namespace Tnzi.Storage.Providers;

/// <summary>
/// Azure Blob存储实现
/// </summary>
public class AzureBlobStorage : IFileStorage
{
    private readonly AzureBlobStorageOptions _options;
    private readonly ILogger<AzureBlobStorage>? _logger;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly string _baseUrl;

    /// <summary>
    /// 初始化 <see cref="AzureBlobStorage"/> 类型的新实例
    /// </summary>
    /// <param name="options">Azure Blob 存储配置选项</param>
    /// <param name="configuration">配置</param>
    /// <param name="logger">日志记录器</param>
    public AzureBlobStorage(
        AzureBlobStorageOptions options,
        IConfiguration? configuration = null,
        ILogger<AzureBlobStorage>? logger = null)
    {
        _options = Check.NotNull(options);
        _logger = logger;

        Check.NotNullOrEmpty(_options.ConnectionString);
        Check.NotNullOrEmpty(_options.ContainerName);

        // 展开 ${VAR} 占位符后再初始化客户端
        var connectionString = ConnectionStringExpander.Expand(_options.ConnectionString, configuration);

        // 初始化 Azure Blob 客户端
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        // 从连接字符串中提取存储账户名称，构建基础URL
        var accountName = ExtractAccountNameFromConnectionString(connectionString);
        _baseUrl = configuration?["Storage:UrlPrefix"]
            ?? $"https://{accountName}.blob.core.windows.net/{_options.ContainerName}";
    }

    /// <summary>
    /// 获取存储提供者名称
    /// </summary>
    public string ProviderName => "Azure";

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

        try
        {
            // 确保容器存在
            await EnsureContainerExistsAsync();

            // 获取 Blob 客户端
            var blobClient = _containerClient.GetBlobClient(fileName);

            // 设置 HTTP 头
            var blobHttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType ?? GetContentType(fileName)
            };

            // 重置流位置
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            // 上传文件
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = blobHttpHeaders
            };

            await blobClient.UploadAsync(stream, uploadOptions);

            _logger?.LogInformation("File '{FileName}' uploaded to Azure Blob Storage successfully", fileName);

            return fileName;
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to upload file '{FileName}' to Azure Blob Storage", fileName);
            throw new InvalidOperationException($"Failed to upload file '{fileName}' to Azure Blob Storage: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 下载文件
    /// </summary>
    /// <param name="filePath">文件路径或URL</param>
    /// <returns>文件流</returns>
    public async Task<Stream> DownloadAsync(string filePath)
    {
        Check.NotNullOrEmpty(filePath);

        try
        {
            var blobClient = _containerClient.GetBlobClient(filePath);
            var response = await blobClient.DownloadAsync();

            _logger?.LogInformation("File '{FilePath}' downloaded from Azure Blob Storage successfully", filePath);

            return response.Value.Content;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger?.LogWarning("File '{FilePath}' not found in Azure Blob Storage", filePath);
            throw new FileNotFoundException($"File '{filePath}' not found in Azure Blob Storage", filePath);
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to download file '{FilePath}' from Azure Blob Storage", filePath);
            throw new InvalidOperationException($"Failed to download file '{filePath}' from Azure Blob Storage: {ex.Message}", ex);
        }
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
            var blobClient = _containerClient.GetBlobClient(filePath);
            var response = await blobClient.DeleteIfExistsAsync();

            if (response.Value)
            {
                _logger?.LogInformation("File '{FilePath}' deleted from Azure Blob Storage successfully", filePath);
            }
            else
            {
                _logger?.LogWarning("File '{FilePath}' not found in Azure Blob Storage for deletion", filePath);
            }

            return response.Value;
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to delete file '{FilePath}' from Azure Blob Storage", filePath);
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
            var blobClient = _containerClient.GetBlobClient(filePath);
            var response = await blobClient.ExistsAsync();
            return response.Value;
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to check existence of file '{FilePath}' in Azure Blob Storage", filePath);
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

        var blobClient = _containerClient.GetBlobClient(filePath);

        if (expiresIn.HasValue && blobClient.CanGenerateSasUri)
        {
            // 生成SAS URL
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = filePath,
                Resource = "b", // b = blob
                ExpiresOn = DateTimeOffset.UtcNow.AddSeconds(expiresIn.Value)
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return Task.FromResult(sasUri.ToString());
        }

        // 返回基本URL（适用于公开容器或无需SAS的场景）
        var url = _baseUrl.TrimEnd('/') + "/" + filePath.TrimStart('/');
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
            var blobClient = _containerClient.GetBlobClient(filePath);
            var properties = await blobClient.GetPropertiesAsync();
            return properties.Value.ContentLength;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger?.LogWarning("File '{FilePath}' not found in Azure Blob Storage when getting size", filePath);
            return 0L;
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to get file size for '{FilePath}' from Azure Blob Storage", filePath);
            return 0L;
        }
    }

    /// <summary>
    /// 确保容器存在
    /// </summary>
    private async Task EnsureContainerExistsAsync()
    {
        try
        {
            await _containerClient.CreateIfNotExistsAsync();
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to create container '{ContainerName}'", _options.ContainerName);
            throw;
        }
    }

    /// <summary>
    /// 从连接字符串中提取账户名。
    /// </summary>
    private static string ExtractAccountNameFromConnectionString(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return string.Empty;

        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
            {
                return part["AccountName=".Length..];
            }
        }

        return string.Empty;
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

        try
        {
            var blobClient = _containerClient.GetBlobClient(filePath);

            // 获取文件属性以获取总长度
            var properties = await blobClient.GetPropertiesAsync();
            var totalLength = properties.Value.ContentLength;

            // 如果没有指定范围，返回整个文件
            if (!rangeStart.HasValue)
            {
                var fullResponse = await blobClient.DownloadAsync();
                return (fullResponse.Value.Content, 0L, totalLength - 1, totalLength);
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

            // 下载指定范围
            var range = new Azure.HttpRange(start, end - start + 1);
            var response = await blobClient.DownloadAsync(range: range);

            _logger?.LogInformation("File '{FilePath}' downloaded range {Start}-{End} from Azure Blob Storage",
                filePath, start, end);

            return (response.Value.Content, start, end, totalLength);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger?.LogWarning("File '{FilePath}' not found in Azure Blob Storage", filePath);
            throw new FileNotFoundException($"File '{filePath}' not found in Azure Blob Storage", filePath);
        }
        catch (RequestFailedException ex)
        {
            _logger?.LogError(ex, "Failed to download file range for '{FilePath}' from Azure Blob Storage", filePath);
            throw new InvalidOperationException($"Failed to download file range for '{filePath}' from Azure Blob Storage: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据文件名获取内容类型。
    /// </summary>
    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".bmp" => "image/bmp",
            ".ico" => "image/x-icon",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".rar" => "application/x-rar-compressed",
            ".7z" => "application/x-7z-compressed",
            ".tar" => "application/x-tar",
            ".gz" => "application/gzip",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".mp4" => "video/mp4",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream"
        };
    }
}