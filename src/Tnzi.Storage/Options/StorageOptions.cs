namespace Tnzi.Storage.Options;

/// <summary>
/// 存储模块配置选项
/// 配置路径：Storage
/// </summary>
[ConfigSection("Storage")]
[RuntimeSettingGroup(Key = "storage-upload", Module = "Storage", DisplayName = "Upload Limits",
    I18nKey = "admin.modules.system.settings.groups.storageUpload",
    Icon = "mdi:cloud-upload-outline", Order = 300)]
public class StorageOptions
{
    /// <summary>
    /// 获取或设置 存储提供者（Local, S3, R2, Azure）
    /// </summary>
    public string Provider { get; set; } = "Local";

    /// <summary>
    /// 获取或设置 文件存储根路径（Local存储时使用）
    /// </summary>
    public string StoragePath { get; set; } = "AppData/Files";

    /// <summary>
    /// 获取或设置 是否启用MD5验证
    /// </summary>
    public bool EnableMd5Validation { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否启用文件引用
    /// </summary>
    public bool EnableFileReference { get; set; } = true;

    /// <summary>
    /// 获取或设置 最大文件大小（字节）
    /// </summary>
    [RuntimeSetting(Label = "Max File Size (bytes)", I18n = "admin.modules.system.settings.fields.maxFileSize",
        Type = SettingFieldType.Int, Min = 1)]
    public long MaxFileSize { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// 获取或设置 允许的文件扩展名
    /// </summary>
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// 获取或设置 是否自动生成缩略图
    /// </summary>
    public bool AutoGenerateThumbnail { get; set; } = true;

    /// <summary>
    /// 获取或设置 缩略图尺寸（宽度x高度）
    /// </summary>
    public ThumbnailSizeOptions ThumbnailSize { get; set; } = new();

    /// <summary>
    /// 获取或设置 图片压缩质量（1-100）
    /// </summary>
    [RuntimeSetting(Label = "Image Compression Quality", I18n = "admin.modules.system.settings.fields.imageCompressionQuality",
        Type = SettingFieldType.Int, Min = 1, Max = 100)]
    public int ImageCompressionQuality { get; set; } = 85;

    /// <summary>
    /// 获取或设置 文件访问URL前缀
    /// </summary>
    public string? UrlPrefix { get; set; }

    /// <summary>
    /// 获取或设置 S3存储配置（当Provider为S3时使用）
    /// </summary>
    public S3StorageOptions? S3 { get; set; }

    /// <summary>
    /// 获取或设置 Azure Blob存储配置（当Provider为Azure时使用）
    /// </summary>
    public AzureBlobStorageOptions? Azure { get; set; }

    /// <summary>
    /// 获取或设置 Cloudflare R2 存储配置（当Provider为R2时使用）
    /// </summary>
    public R2StorageOptions? R2 { get; set; }

    /// <summary>
    /// 获取或设置 清理机制配置
    /// </summary>
    public CleanupOptions Cleanup { get; set; } = new();
}

/// <summary>
/// S3存储配置选项
/// </summary>
public class S3StorageOptions
{
    /// <summary>
    /// 获取或设置 访问密钥ID
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 访问密钥
    /// </summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 服务端点URL
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 存储桶名称
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 区域
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 是否使用HTTPS
    /// </summary>
    public bool UseHttps { get; set; } = true;
}

/// <summary>
/// Azure Blob存储配置选项
/// </summary>
public class AzureBlobStorageOptions
{
    /// <summary>
    /// 获取或设置 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 容器名称
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;
}

/// <summary>
/// Cloudflare R2 存储配置选项（兼容 S3 API）
/// </summary>
public class R2StorageOptions
{
    /// <summary>
    /// 获取或设置 访问密钥ID
    /// </summary>
    public string AccessKeyId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 访问密钥
    /// </summary>
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 存储桶名称
    /// </summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 账户ID（用于构建URL，格式：https://{AccountId}.r2.cloudflarestorage.com）
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 自定义端点URL（可选，默认使用 https://{AccountId}.r2.cloudflarestorage.com）
    /// </summary>
    public string? CustomEndpoint { get; set; }
}

/// <summary>
/// 文件清理配置选项
/// </summary>
public class CleanupOptions
{
    /// <summary>
    /// 是否启用自动清理任务
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 清理任务执行间隔（分钟），默认60分钟
    /// </summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 临时文件保留时间（小时），超过此时间的临时文件将被清理，默认24小时
    /// </summary>
    public int TemporaryFileRetentionHours { get; set; } = 24;

    /// <summary>
    /// 是否启用僵尸文件清理（ReferenceCount=0 的文件）
    /// </summary>
    public bool EnableOrphanFileCleanup { get; set; } = true;

    /// <summary>
    /// 僵尸文件保留时间（小时），ReferenceCount=0 超过此时间后删除，默认72小时
    /// </summary>
    public int OrphanFileRetentionHours { get; set; } = 72;

    /// <summary>
    /// 是否启用孤立引用清理（引用的实体已不存在）
    /// 注意：此功能需要查询实体表验证，可能影响性能，默认关闭
    /// </summary>
    public bool EnableOrphanReferenceCleanup { get; set; } = false;

    /// <summary>
    /// 单次清理的最大文件数量，防止长时间阻塞，默认100
    /// </summary>
    public int MaxFilesPerRun { get; set; } = 100;

    /// <summary>
    /// 清理任务执行时间（Cron 表达式），如果设置则优先于 IntervalMinutes
    /// 例如: "0 3 * * *" 表示每天凌晨3点执行
    /// </summary>
    public string? CronExpression { get; set; }
}

/// <summary>
/// 缩略图尺寸配置
/// </summary>
public class ThumbnailSizeOptions
{
    /// <summary>
    /// 宽度（像素）
    /// </summary>
    public int Width { get; set; } = 200;

    /// <summary>
    /// 高度（像素）
    /// </summary>
    public int Height { get; set; } = 200;
}

/// <summary>
/// Storage配置验证器
/// </summary>
public class StorageOptionsValidator : OptionsValidatorBase<StorageOptions>
{
    protected override void ValidateOptions(StorageOptions options, List<string> errors)
    {
        if (options.MaxFileSize <= 0)
            errors.Add("MaxFileSize must be greater than 0.");

        if (options.ImageCompressionQuality < 1 || options.ImageCompressionQuality > 100)
            errors.Add("ImageCompressionQuality must be between 1 and 100.");

        if (options.ThumbnailSize.Width <= 0 || options.ThumbnailSize.Height <= 0)
            errors.Add("ThumbnailSize width and height must be greater than 0.");

        // 验证S3配置（如果Provider为S3）
        if (options.Provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
        {
            if (options.S3 == null)
                errors.Add("S3 options are required when Provider is S3.");
            else
            {
                if (string.IsNullOrEmpty(options.S3.AccessKeyId))
                    errors.Add("S3.AccessKeyId is required.");

                if (string.IsNullOrEmpty(options.S3.SecretAccessKey))
                    errors.Add("S3.SecretAccessKey is required.");

                if (string.IsNullOrEmpty(options.S3.BucketName))
                    errors.Add("S3.BucketName is required.");

                if (string.IsNullOrEmpty(options.S3.Region))
                    errors.Add("S3.Region is required.");
            }
        }

        // 验证Azure配置（如果Provider为Azure）
        if (options.Provider.Equals("Azure", StringComparison.OrdinalIgnoreCase))
        {
            if (options.Azure == null)
                errors.Add("Azure options are required when Provider is Azure.");
            else
            {
                if (string.IsNullOrEmpty(options.Azure.ConnectionString))
                    errors.Add("Azure.ConnectionString is required.");

                if (string.IsNullOrEmpty(options.Azure.ContainerName))
                    errors.Add("Azure.ContainerName is required.");
            }
        }

        // 验证R2配置（如果Provider为R2）
        if (options.Provider.Equals("R2", StringComparison.OrdinalIgnoreCase))
        {
            if (options.R2 == null)
                errors.Add("R2 options are required when Provider is R2.");
            else
            {
                if (string.IsNullOrEmpty(options.R2.AccessKeyId))
                    errors.Add("R2.AccessKeyId is required.");

                if (string.IsNullOrEmpty(options.R2.SecretAccessKey))
                    errors.Add("R2.SecretAccessKey is required.");

                if (string.IsNullOrEmpty(options.R2.BucketName))
                    errors.Add("R2.BucketName is required.");

                if (string.IsNullOrEmpty(options.R2.CustomEndpoint) && string.IsNullOrEmpty(options.R2.AccountId))
                    errors.Add("R2.AccountId is required when CustomEndpoint is not set.");
            }
        }
    }
}
