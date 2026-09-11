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
    /// OTLP 导出端点。示例：http://localhost:4317（gRPC）或 http://localhost:4318（HTTP）。
    /// </summary>
    /// <remarks>
    /// ★ 不设置时<b>什么都不导出</b>，除非另外打开 <see cref="ExportToConsole"/> ——
    /// 这两个开关是各自独立的，控制台导出器只由后者决定。
    /// 本注释此前写着「不设置将使用控制台导出器」，而 <c>ConfigureOtlpExporter</c> 里没有这条回退：
    /// 端点为空 + <c>ExportToConsole=false</c> 的配置会让遥测<b>静默地哪儿也不去</b>，
    /// 而开关看起来都是开着的。
    /// </remarks>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// 使用 gRPC 协议导出（默认）还是 HTTP/protobuf
    /// 默认值：true（gRPC）
    /// </summary>
    public bool UseGrpc { get; set; } = true;

    /// <summary>
    /// 随每次 OTLP 导出附带的请求头（如 <c>Authorization</c> / <c>x-api-key</c>），托管采集器的认证靠它。
    /// </summary>
    /// <remarks>
    /// 线上格式是 <c>k1=v1,k2=v2</c>，值里的逗号与键里的等号没法转义 —— 验证器在启动期拒绝这类头，
    /// 而不是静默切坏。值里的 <c>=</c> 合法（Basic 认证的 base64 尾部就是它），解析按第一个 <c>=</c> 切分。
    /// </remarks>
    public Dictionary<string, string> OtlpHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 单次 OTLP 导出的超时（毫秒）。默认 10000，与导出器自身默认一致。
    /// </summary>
    public int OtlpTimeoutMilliseconds { get; set; } = 10_000;

    /// <summary>
    /// 是否把每条 EF Core 命令的<b>完整 SQL 文本</b>作为 <c>db.statement</c> 附到 span 上。
    /// 默认值：<c>false</c>。
    /// </summary>
    /// <remarks>
    /// ★ 此前是硬编码开启且没有任何开关：参数化查询的文本只含占位符，但 <c>FromSqlRaw</c> / 插值 SQL
    /// 会把真实数据一起带出进程，落进采集器与它背后的任何存储。排障时按需打开。
    /// </remarks>
    public bool RecordDbStatementText { get; set; } = false;

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

    /// <summary>
    /// Whether to auto-subscribe to framework ActivitySource/Meter instances (Tnzi.*)
    /// This enables tracing and metrics from AI, RAG, Performance, and other framework modules
    /// Default: true
    /// </summary>
    /// <remarks>
    /// Known framework sources: Tnzi.AI, Tnzi.AI.Rag, Tnzi.Framework
    /// </remarks>
    public bool InstrumentFramework { get; set; } = true;

    /// <summary>
    /// Additional ActivitySource names to subscribe for tracing
    /// Use this to include custom application-level ActivitySources
    /// </summary>
    public List<string> AdditionalActivitySources { get; set; } = new();

    /// <summary>
    /// Additional Meter names to subscribe for metrics
    /// Use this to include custom application-level Meters
    /// </summary>
    public List<string> AdditionalMeters { get; set; } = new();
}
