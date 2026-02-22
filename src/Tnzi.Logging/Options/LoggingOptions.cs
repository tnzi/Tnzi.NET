namespace Tnzi.Logging.Options;

/// <summary>
/// 日志模块配置选项
/// 配置路径：Logging
/// </summary>
public class LoggingOptions
{
    /// <summary>
    /// 日志文件存储的基础路径（默认: Logs）
    /// </summary>
    public string BasePath { get; set; } = "Logs";

    /// <summary>
    /// 最低日志级别（默认: Information）
    /// </summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>
    /// 是否启用控制台输出（默认: true）
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// 是否启用文件输出（默认: true）
    /// </summary>
    public bool EnableFile { get; set; } = true;

    /// <summary>
    /// 文件刷新到磁盘的间隔（默认: 1秒）
    /// </summary>
    public TimeSpan FlushToDiskInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 日志文件输出选项
    /// </summary>
    public FileOutputOptions FileOutput { get; set; } = new();

    /// <summary>
    /// 请求日志选项
    /// </summary>
    public RequestLoggingOptions RequestLogging { get; set; } = new();
}

/// <summary>
/// 文件输出选项
/// </summary>
public class FileOutputOptions
{
    /// <summary>
    /// Information 级别日志配置
    /// </summary>
    public LevelFileOptions Information { get; set; } = new()
    {
        Enabled = true,
        RetainedFileCountLimit = 30
    };

    /// <summary>
    /// Warning 级别日志配置
    /// </summary>
    public LevelFileOptions Warning { get; set; } = new()
    {
        Enabled = true,
        RetainedFileCountLimit = 30
    };

    /// <summary>
    /// Error 级别日志配置
    /// </summary>
    public LevelFileOptions Error { get; set; } = new()
    {
        Enabled = true,
        RetainedFileCountLimit = 60
    };

    /// <summary>
    /// Fatal 级别日志配置
    /// </summary>
    public LevelFileOptions Fatal { get; set; } = new()
    {
        Enabled = true,
        RetainedFileCountLimit = 90
    };

    /// <summary>
    /// Debug 级别日志配置
    /// </summary>
    public LevelFileOptions Debug { get; set; } = new()
    {
        Enabled = false, // 默认不启用，由环境或配置决定
        RetainedFileCountLimit = 7
    };
}

/// <summary>
/// 单个日志级别的文件输出选项
/// </summary>
public class LevelFileOptions
{
    /// <summary>
    /// 是否启用此级别的文件输出
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 保留的文件数量限制
    /// </summary>
    public int RetainedFileCountLimit { get; set; } = 30;
}

/// <summary>
/// 请求日志选项
/// </summary>
public class RequestLoggingOptions
{
    /// <summary>
    /// 是否启用请求日志（默认: true）
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 日志消息模板
    /// </summary>
    public string? MessageTemplate { get; set; }

    /// <summary>
    /// 请求日志级别（默认: Information）
    /// </summary>
    public LogEventLevel Level { get; set; } = LogEventLevel.Information;

    /// <summary>
    /// 慢请求阈值（秒），超过此值的请求将以 Warning 级别记录
    /// 默认：1.0 秒
    /// </summary>
    public double SlowRequestThresholdSeconds { get; set; } = 1.0;
}

