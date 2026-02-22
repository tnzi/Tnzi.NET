namespace Tnzi.OpenTelemetry.Options;

/// <summary>
/// OpenTelemetry 可观测性模块配置选项
/// 配置路径：OpenTelemetry
/// </summary>
public class OpenTelemetryOptions
{
    /// <summary>
    /// 服务名称（用于标识追踪数据来源）
    /// 默认从程序集名称读取
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// 服务版本
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// 服务实例 ID（用于区分多个实例）
    /// </summary>
    public string? ServiceInstanceId { get; set; }

    /// <summary>
    /// 是否启用追踪（Tracing）
    /// 默认值：true
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// 是否启用指标（Metrics）
    /// 默认值：true
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// 是否启用日志导出
    /// 默认值：false（日志通常使用其他方式处理）
    /// </summary>
    public bool EnableLogging { get; set; } = false;

    /// <summary>
    /// OTLP 导出端点
    /// 如果不设置，将使用控制台导出器（仅用于开发）
    /// 示例：http://localhost:4317（gRPC）或 http://localhost:4318（HTTP）
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// 使用 gRPC 协议导出（默认）还是 HTTP/protobuf
    /// 默认值：true（gRPC）
    /// </summary>
    public bool UseGrpc { get; set; } = true;

    /// <summary>
    /// 是否启用 ASP.NET Core 检测（同时控制 Tracing 和 Metrics）
    /// 默认值：true
    /// </summary>
    public bool InstrumentAspNetCore { get; set; } = true;

    /// <summary>
    /// 是否启用 HTTP 客户端检测（同时控制 Tracing 和 Metrics）
    /// 默认值：true
    /// </summary>
    public bool InstrumentHttpClient { get; set; } = true;

    /// <summary>
    /// 是否启用 EF Core 追踪（仅 Tracing，EF Core 无 Metrics 检测）
    /// 默认值：true
    /// </summary>
    public bool InstrumentEntityFramework { get; set; } = true;

    /// <summary>
    /// 采样比率（0.0 到 1.0）
    /// 1.0 = 采样所有请求，0.1 = 采样 10% 的请求
    /// 默认值：1.0（开发环境），生产环境建议 0.1
    /// </summary>
    public double SamplingRatio { get; set; } = 1.0;

    /// <summary>
    /// 是否在控制台输出遥测数据（仅用于开发调试）
    /// 默认值：false
    /// </summary>
    public bool ExportToConsole { get; set; } = false;
}
