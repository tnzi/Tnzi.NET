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
    [RuntimeSetting(Label = "Enable MD5 Validation", I18n = "admin.modules.system.settings.fields.storageEnableMd5",
        Type = SettingFieldType.Boolean, Subsection = "Files",
        Description = "Compute MD5 for uploads to enable content-hash de-duplication and integrity checks.")]
    public bool EnableMd5Validation { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否允许未认证请求读取任意文件。
    ///
    /// **默认关闭。** 打开等于让任何人凭文件 id 下载库里的每一个文件,而框架的实体 ID 是
    /// 顺序 GUID(可预测性远高于随机 GUID),所以这不是"猜不到就安全"。
    /// 需要公开的单个资源(头像 / 站点素材)把 `FileRecord.IsPublic` 置 true;
    /// 需要对外分发私密文件走 `FileShare`(token + 可选密码 + 次数上限 + 过期)。
    /// </summary>
    [RuntimeSetting(Label = "Allow Anonymous File Read", I18n = "admin.modules.system.settings.fields.storageAllowAnonymousRead",
        Type = SettingFieldType.Boolean, Subsection = "Files",
        Description = "Let unauthenticated callers download any file by id. Off by default. Prefer marking individual files public, or share them through a share link.")]
    public bool AllowAnonymousRead { get; set; } = false;

    /// <summary>
    /// 获取或设置 是否启用文件引用
    /// </summary>
    [RuntimeSetting(Label = "Enable File Reference Tracking", I18n = "admin.modules.system.settings.fields.storageEnableFileReference",
        Type = SettingFieldType.Boolean, Subsection = "Files",
        Description = "Track [FileField] references so unreferenced files can be cleaned up. Warning: turning this off while files are in use stops reference tracking, so those files may be treated as orphaned by later cleanup.")]
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
    [RuntimeSetting(Label = "Auto-generate Thumbnails", I18n = "admin.modules.system.settings.fields.storageAutoGenerateThumbnail",
        Type = SettingFieldType.Boolean, Subsection = "Files",
        Description = "Generate a square thumbnail for image uploads.")]
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
    [RuntimeSetting(Label = "File URL Prefix", I18n = "admin.modules.system.settings.fields.storageUrlPrefix",
        Type = SettingFieldType.String, Subsection = "Files",
        Description = "Base URL prefix prepended to generated file access URLs (e.g. a CDN origin). Leave empty to use the storage provider default.")]
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
[ConfigSection("Storage:Cleanup")]
[RuntimeSettingGroup(Key = "storage-cleanup", Module = "Storage", DisplayName = "File Cleanup",
    I18nKey = "admin.modules.system.settings.groups.storageCleanup",
    Icon = "mdi:broom", Order = 310)]
public class CleanupOptions
{
    /// <summary>
    /// 是否启用自动清理任务
    /// </summary>
    [RuntimeSetting(Label = "Enable Cleanup Task", I18n = "admin.modules.system.settings.fields.storageCleanupEnabled",
        Type = SettingFieldType.Boolean, Subsection = "Schedule",
        Description = "Enable the background file cleanup task. Note: enabling or disabling this takes effect after an application restart.")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 清理任务执行间隔（分钟），默认60分钟
    /// </summary>
    [RuntimeSetting(Label = "Cleanup Interval (minutes)", I18n = "admin.modules.system.settings.fields.storageCleanupIntervalMinutes",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Schedule",
        Description = "Interval between cleanup runs, in minutes. Ignored when a Cron expression is set.")]
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 临时文件保留时间（小时），超过此时间的临时文件将被清理，默认24小时
    /// </summary>
    [RuntimeSetting(Label = "Temporary File Retention (hours)", I18n = "admin.modules.system.settings.fields.storageCleanupTemporaryFileRetentionHours",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Retention",
        Description = "Temporary files older than this are deleted.")]
    public int TemporaryFileRetentionHours { get; set; } = 24;

    /// <summary>
    /// 是否启用僵尸文件清理（ReferenceCount=0 的文件）
    /// </summary>
    [RuntimeSetting(Label = "Enable Orphan File Cleanup", I18n = "admin.modules.system.settings.fields.storageCleanupEnableOrphanFileCleanup",
        Type = SettingFieldType.Boolean, Subsection = "Orphans",
        Description = "Delete files whose reference count has reached zero.")]
    public bool EnableOrphanFileCleanup { get; set; } = true;

    /// <summary>
    /// 僵尸文件保留时间（小时），ReferenceCount=0 超过此时间后删除，默认72小时
    /// </summary>
    [RuntimeSetting(Label = "Orphan File Retention (hours)", I18n = "admin.modules.system.settings.fields.storageCleanupOrphanFileRetentionHours",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Orphans",
        Description = "Orphaned files (reference count zero) older than this are deleted.")]
    public int OrphanFileRetentionHours { get; set; } = 72;

    /// <summary>
    /// 是否启用孤立引用清理（引用的实体已不存在）
    /// 注意：此功能需要查询实体表验证，可能影响性能，默认关闭
    /// </summary>
    [RuntimeSetting(Label = "Enable Orphan Reference Cleanup", I18n = "admin.modules.system.settings.fields.storageCleanupEnableOrphanReferenceCleanup",
        Type = SettingFieldType.Boolean, Subsection = "Orphans",
        Description = "Remove references whose target entity no longer exists. This queries entity tables to verify existence and may affect performance.")]
    public bool EnableOrphanReferenceCleanup { get; set; } = false;

    /// <summary>
    /// 单次清理的最大文件数量，防止长时间阻塞，默认100
    /// </summary>
    [RuntimeSetting(Label = "Max Files Per Run", I18n = "admin.modules.system.settings.fields.storageCleanupMaxFilesPerRun",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Schedule",
        Description = "Maximum number of files processed in a single cleanup run, to avoid long-running blocking operations.")]
    public int MaxFilesPerRun { get; set; } = 100;

    /// <summary>
    /// 清理任务执行时间（Cron 表达式），如果设置则优先于 IntervalMinutes
    /// 例如: "0 3 * * *" 表示每天凌晨3点执行
    /// </summary>
    [RuntimeSetting(Label = "Cron Expression", I18n = "admin.modules.system.settings.fields.storageCleanupCronExpression",
        Type = SettingFieldType.String, Subsection = "Schedule",
        Description = "Optional cron expression for the cleanup schedule (e.g. '0 3 * * *' for daily at 03:00 UTC). When set, it takes precedence over the interval. Changes take effect after an application restart.")]
    public string? CronExpression { get; set; }
}

/// <summary>
/// 缩略图尺寸配置
/// </summary>
[ConfigSection("Storage:ThumbnailSize")]
[RuntimeSettingGroup(Key = "storage-upload", Module = "Storage", DisplayName = "Upload Limits",
    I18nKey = "admin.modules.system.settings.groups.storageUpload",
    Icon = "mdi:cloud-upload-outline", Order = 300)]
public class ThumbnailSizeOptions
{
    /// <summary>
    /// 宽度（像素）
    /// </summary>
    [RuntimeSetting(Label = "Thumbnail Width (px)", I18n = "admin.modules.system.settings.fields.storageThumbnailWidth",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Thumbnail",
        Description = "Thumbnail width in pixels.")]
    public int Width { get; set; } = 200;

    /// <summary>
    /// 高度（像素）
    /// </summary>
    [RuntimeSetting(Label = "Thumbnail Height (px)", I18n = "admin.modules.system.settings.fields.storageThumbnailHeight",
        Type = SettingFieldType.Int, Min = 1, Subsection = "Thumbnail",
        Description = "Thumbnail height in pixels.")]
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
