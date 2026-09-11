
namespace Tnzi.OpenTelemetry.Options;

/// <summary>
/// OpenTelemetry 配置选项验证器
/// </summary>
public class OpenTelemetryOptionsValidator : OptionsValidatorBase<OpenTelemetryOptions>
{
    /// <summary>
    /// 推荐的最小采样比率（1% 采样）
    /// </summary>
    private const double MinRecommendedSamplingRatio = 0.01;

    public OpenTelemetryOptionsValidator(ILoggerFactory? loggerFactory = null) : base(loggerFactory)
    {
    }

    /// <summary>
    /// 验证 OpenTelemetry 配置选项
    /// </summary>
    protected override void ValidateOptions(OpenTelemetryOptions options, List<string> errors)
    {
        // 验证采样比率：必须在有效范围内
        if (options.SamplingRatio < 0.0)
        {
            errors.Add("OpenTelemetry.SamplingRatio must be greater than or equal to 0.0.");
        }
        else if (options.SamplingRatio > 1.0)
        {
            errors.Add("OpenTelemetry.SamplingRatio must be less than or equal to 1.0.");
        }

        // 如果设置了 OTLP 端点，验证格式
        if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
        {
            var endpoint = options.OtlpEndpoint.Trim();
            if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("OpenTelemetry.OtlpEndpoint must start with 'http://' or 'https://'.");
            }
            else if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _))
            {
                errors.Add("OpenTelemetry.OtlpEndpoint is not a valid URI.");
            }
        }

        if (options.OtlpTimeoutMilliseconds <= 0)
        {
            AddError(errors, nameof(options.OtlpTimeoutMilliseconds), "OtlpTimeoutMilliseconds must be greater than 0.");
        }

        // 头串的线上格式是 "k1=v1,k2=v2"：值里的逗号、键里的等号与逗号都无法转义，换行会破坏 HTTP 头。
        // 与其让导出器静默切坏（认证失败只会表现为采集器那边什么都没收到），不如在启动期拒绝。
        foreach (var (key, value) in options.OtlpHeaders)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Contains('=') || key.Contains(',') || HasLineBreak(key))
            {
                AddError(errors, nameof(options.OtlpHeaders),
                    $"OtlpHeaders contains an invalid header name '{key}': names must be non-empty and must not contain '=', ',' or line breaks.");
            }

            if (value is null || value.Contains(',') || HasLineBreak(value))
            {
                AddError(errors, nameof(options.OtlpHeaders),
                    $"OtlpHeaders value for '{key}' must not be null and must not contain ',' or line breaks (the OTLP header format cannot escape them).");
            }
        }
    }

    /// <summary>
    /// 收集采样比率相关的警告信息
    /// </summary>
    protected override void CollectWarnings(OpenTelemetryOptions options, List<string> warnings)
    {
        // SamplingRatio == 0 会关闭所有追踪，发出警告
        if (options.SamplingRatio == 0.0)
        {
            AddWarning(warnings, "SamplingRatio",
                $"SamplingRatio is 0.0, which disables all tracing. Recommended minimum value is {MinRecommendedSamplingRatio} (1% sampling).");
        }

        // ★ 端点为空且不导出到控制台 = 一个导出器都没有：全量采样、全部检测开着，每个 span 与指标
        //   被静默丢弃，此前没有任何一行日志说这件事，而开关看起来全是开着的。
        if ((options.EnableTracing || options.EnableMetrics || options.EnableLogging) && !OpenTelemetryModule.HasExportTarget(options))
        {
            AddWarning(warnings, nameof(options.OtlpEndpoint),
                "Telemetry is enabled but has nowhere to go: OtlpEndpoint is not set and ExportToConsole is false. Every span, metric and log record is collected and then discarded. Set OpenTelemetry:OtlpEndpoint, enable ExportToConsole, or disable EnableTracing/EnableMetrics/EnableLogging.");
        }
    }

    private static bool HasLineBreak(string value) => value.Contains('\r') || value.Contains('\n');
}
