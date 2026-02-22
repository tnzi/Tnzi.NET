
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
    }
}
