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
    /// 获取或设置 文件访问令牌的签名密钥。
    ///
    /// 浏览器发起的资源请求（<c>&lt;img src&gt;</c> / <c>&lt;a download&gt;</c> / <c>&lt;iframe&gt;</c>）
    /// 带不了 Authorization 头,所以私密文件靠**短时签名令牌**渲染:调用方先经
    /// <c>GET /files/{id}/access-token</c> 换一个只对这一个文件、这一小段时间有效的令牌,
    /// 再把它拼进 URL。签发时会走完整的读权限判定,消费时只验签名与过期。
    ///
    /// 留空时依次回退:<c>Identity:Jwt:SecretKey</c> → 进程内随机密钥(并记 Warning)。
    /// **多实例部署必须显式配置**,否则各实例签发的令牌互不认账。
    /// 刻意不是 <c>[RuntimeSetting]</c>:密钥属于部署机密,不该经管理端下发或回显。
    /// </summary>
    public string? UrlSigningKey { get; set; }

    /// <summary>
    /// 获取或设置 文件访问令牌的有效期(秒),默认 600(10 分钟)。
    ///
    /// 这既是**默认值也是上限**:调用方可以要更短的,不能要更长的 —— 否则
    /// `?expiresInSeconds=999999999` 就能把几分钟的凭据变成几十年的。
    ///
    /// 取值是在"够用"和"泄漏窗口"之间取舍:令牌一旦拼进 URL 就会进浏览器历史、
    /// referrer 与访问日志,而且签发之后即便用户失去权限,令牌仍有效到过期。
    /// 够长到一页图片加载完、够短到分享出去的链接很快失效。
    /// </summary>
    [RuntimeSetting(Label = "Signed URL Lifetime (seconds)", I18n = "admin.modules.system.settings.fields.storageSignedUrlTtlSeconds",
        Type = SettingFieldType.Int, Min = 30, Max = 86400, Subsection = "Files",
        Description = "How long a file access token stays valid, and the ceiling a caller may request. Tokens are appended to file URLs so private files can render in an <img> tag, which cannot send an Authorization header. Shorter is safer.")]
    public int SignedUrlTtlSeconds { get; set; } = 600;

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

    /// <summary>
    /// 获取或设置 对外分享链接配置
    /// </summary>
    public ShareOptions Share { get; set; } = new();
}

/// <summary>
/// 对外分享链接（<c>FileShare</c>）策略。
///
/// 分享链接是这套系统里**唯一**一条不经身份认证就能取到数据的路，所以策略集中在这里，
/// 由管理员统一定，而不是让每个创建链接的人自己拿主意。取值取向对齐业界主流做法
/// （Nextcloud 的 enforce-password / enforce-expiration，Dropbox 与 Google Drive 的
/// 可选口令 + 可选有效期）：**默认可用、口令可选，但管理员能一键收紧**。
/// </summary>
[ConfigSection("Storage:Share")]
[RuntimeSettingGroup(Key = "storage-share", Module = "Storage", DisplayName = "Share Links",
    I18nKey = "admin.modules.system.settings.groups.storageShare",
    Icon = "mdi:link-variant", Order = 320)]
public class ShareOptions
{
    /// <summary>
    /// 获取或设置 分享链接能否被**未登录**访客打开，默认 true。
    ///
    /// 这正是「对外分享」的全部意义：收件人是客户 / 审计师 / 供应商，他们没有账号。
    /// 关掉之后链接只对已登录用户有效，等于退化成"内部传阅链接"。
    ///
    /// 之所以默认开着而不是关着：创建一条分享链接本身已经要求对该文件有**变更**权限
    /// （`storage.file.update`），也就是说链接是某个有权的人**特意**造出来的；
    /// 再加一道默认关闭的开关，只会让这个功能默认是坏的。
    /// </summary>
    [RuntimeSetting(Label = "Allow Anonymous Share Links", I18n = "admin.modules.system.settings.fields.storageShareAllowAnonymous",
        Type = SettingFieldType.Boolean,
        Description = "Let people without an account open a share link. Turn this off to make share links usable only by signed-in users.")]
    public bool AllowAnonymous { get; set; } = true;

    /// <summary>
    /// 获取或设置 是否强制每条分享链接都设口令，默认 false。
    ///
    /// 对齐 Nextcloud 的 enforce-password：默认让用户自己选（多数分享并不敏感，
    /// 强制口令会把人逼回"直接用邮件发附件"），但处理合同 / HR 文件的部署可以一键收紧。
    /// </summary>
    [RuntimeSetting(Label = "Require Password on Share Links", I18n = "admin.modules.system.settings.fields.storageShareRequirePassword",
        Type = SettingFieldType.Boolean,
        Description = "Force every new share link to carry a password. Existing links are unaffected.")]
    public bool RequirePassword { get; set; } = false;

    /// <summary>
    /// 获取或设置 未指定有效期时的默认天数，默认 7。设为 0 表示默认永不过期。
    ///
    /// 默认给一个有限期限是刻意的：**永不过期的链接没有人会记得回来撤销**，
    /// 它会一直躺在某封邮件里。7 天是业界常见取值（WeTransfer 等同款）。
    /// </summary>
    [RuntimeSetting(Label = "Default Share Lifetime (days)", I18n = "admin.modules.system.settings.fields.storageShareDefaultExpiryDays",
        Type = SettingFieldType.Int, Min = 0, Max = 3650,
        Description = "Applied when the creator does not pick an expiry. 0 means no expiry by default.")]
    public int DefaultExpiryDays { get; set; } = 7;

    /// <summary>
    /// 获取或设置 有效期上限（天），默认 30。设为 0 表示不限。
    ///
    /// 与 <see cref="DefaultExpiryDays"/> 分开是因为两者回答的是不同问题：一个是
    /// "没人选时给多久"，一个是"最多允许多久"。超出上限的请求被**收窄到上限**而不是拒绝
    /// ——创建分享的人多半只是随手选了个远日期，为此报错只会让他重试一遍。
    /// </summary>
    [RuntimeSetting(Label = "Maximum Share Lifetime (days)", I18n = "admin.modules.system.settings.fields.storageShareMaxExpiryDays",
        Type = SettingFieldType.Int, Min = 0, Max = 3650,
        Description = "Hard ceiling on how far out a share link may expire. Longer requests are clamped down to it. 0 means no ceiling.")]
    public int MaxExpiryDays { get; set; } = 30;

    /// <summary>
    /// 获取或设置 口令连续输错多少次后自动停用该链接，默认 10。设为 0 表示不限。
    ///
    /// 令牌本身是 256 位随机数，猜不到；但**口令**可以在线爆破，所以这条是必需的。
    /// 停用而不是"锁 N 分钟"：分享链接是一次性的对外物件，被人在爆破就说明它已经泄漏了，
    /// 让创建者重新发一条比让它自动解锁更合理。
    /// </summary>
    [RuntimeSetting(Label = "Max Failed Password Attempts", I18n = "admin.modules.system.settings.fields.storageShareMaxFailedAttempts",
        Type = SettingFieldType.Int, Min = 0, Max = 1000,
        Description = "Disable a share link after this many consecutive wrong passwords. The token is unguessable, but its password is not. 0 disables the check.")]
    public int MaxFailedPasswordAttempts { get; set; } = 10;
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

        if (options.SignedUrlTtlSeconds < 30 || options.SignedUrlTtlSeconds > 86400)
            errors.Add("SignedUrlTtlSeconds must be between 30 and 86400.");

        if (options.Share.DefaultExpiryDays < 0)
            errors.Add("Share.DefaultExpiryDays cannot be negative.");

        if (options.Share.MaxExpiryDays < 0)
            errors.Add("Share.MaxExpiryDays cannot be negative.");

        // 默认值超过上限是个自相矛盾的配置：每条新链接一创建就会被收窄，
        // 管理员却以为自己设的是默认值。宁可启动时说清楚。
        if (options.Share.MaxExpiryDays > 0 && options.Share.DefaultExpiryDays > options.Share.MaxExpiryDays)
            errors.Add("Share.DefaultExpiryDays cannot exceed Share.MaxExpiryDays.");

        if (options.Share.MaxFailedPasswordAttempts < 0)
            errors.Add("Share.MaxFailedPasswordAttempts cannot be negative.");

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
