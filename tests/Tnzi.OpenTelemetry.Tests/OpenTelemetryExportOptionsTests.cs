using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using Tnzi.OpenTelemetry.Options;

namespace Tnzi.OpenTelemetry.Tests;

/// <summary>
/// 导出侧的三条修正：SQL 文本默认不出进程、遥测无处可去时启动期告警、OTLP 头与超时可配置。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>SetDbStatementForText = true</c> 是硬编码：每条 EF 命令的完整 SQL 都以 <c>db.statement</c>
/// 离开进程，<c>FromSqlRaw</c> / 插值 SQL 带着真实数据一起走，而选项里没有任何开关。
/// </para>
/// <para>
/// 端点为空且 <c>ExportToConsole=false</c> 时三处 <c>ConfigureOtlpExporter</c> 一个导出器都不加：
/// 全量采样、全部检测开着、每个 span 与指标被静默丢弃，没有一行日志。
/// </para>
/// <para>
/// <c>ApplyOtlpOptions</c> 只设 Endpoint 与 Protocol：需要 <c>Authorization</c> / <c>x-api-key</c> 头的托管采集器
/// 无法经框架选项接入。
/// </para>
/// </remarks>
public class OpenTelemetryExportOptionsTests
{
    /// <summary>把验证器写出的告警收进列表里。</summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<string> Warnings { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Warnings);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private static (List<string> Errors, List<string> Warnings) Validate(OpenTelemetryOptions options)
    {
        var loggerFactory = new CapturingLoggerFactory();
        var result = new OpenTelemetryOptionsValidator(loggerFactory).Validate(null, options);
        var errors = result.Failed ? result.Failures!.ToList() : [];
        return (errors, loggerFactory.Warnings);
    }

    // ---- SQL 文本 ----

    [Fact]
    public void RecordDbStatementText_DefaultsToFalse()
    {
        new OpenTelemetryOptions().RecordDbStatementText.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntityFrameworkInstrumentation_FollowsTheSwitch(bool record)
    {
        var options = new EntityFrameworkInstrumentationOptions();

        OpenTelemetryModule.ApplyEntityFrameworkOptions(options, new OpenTelemetryOptions { RecordDbStatementText = record });

        options.SetDbStatementForText.ShouldBe(record);
    }

    // ---- 无处可去 ----

    [Fact]
    public void TelemetryEnabledWithNoExporter_ProducesAStartupWarning()
    {
        var (errors, warnings) = Validate(new OpenTelemetryOptions());

        errors.ShouldBeEmpty();
        warnings.ShouldHaveSingleItem().ShouldContain("nowhere");
    }

    [Fact]
    public void AnOtlpEndpoint_OrTheConsoleExporter_SilencesTheWarning()
    {
        Validate(new OpenTelemetryOptions { OtlpEndpoint = "http://localhost:4317" }).Warnings.ShouldBeEmpty();
        Validate(new OpenTelemetryOptions { ExportToConsole = true }).Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void EverythingDisabled_DoesNotWarnAboutExporters()
    {
        var (_, warnings) = Validate(new OpenTelemetryOptions { EnableTracing = false, EnableMetrics = false, EnableLogging = false });

        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void HasExportTarget_AnswersForEachCombination()
    {
        OpenTelemetryModule.HasExportTarget(new OpenTelemetryOptions()).ShouldBeFalse();
        OpenTelemetryModule.HasExportTarget(new OpenTelemetryOptions { OtlpEndpoint = "http://c:4317" }).ShouldBeTrue();
        OpenTelemetryModule.HasExportTarget(new OpenTelemetryOptions { ExportToConsole = true }).ShouldBeTrue();
    }

    // ---- OTLP 头与超时 ----

    [Fact]
    public void OtlpHeadersAndTimeout_DefaultToEmptyAndTenSeconds()
    {
        var options = new OpenTelemetryOptions();

        options.OtlpHeaders.ShouldBeEmpty();
        options.OtlpTimeoutMilliseconds.ShouldBe(10_000);
    }

    [Fact]
    public void ComposeOtlpHeaders_ProducesTheWireFormat()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer abc==",
            ["x-api-key"] = "k1"
        };

        OpenTelemetryModule.ComposeOtlpHeaders(headers).ShouldBe("Authorization=Bearer abc==,x-api-key=k1");
        OpenTelemetryModule.ComposeOtlpHeaders(new Dictionary<string, string>()).ShouldBeNull();
    }

    [Fact]
    public void ApplyOtlpOptions_CopiesEndpointProtocolHeadersAndTimeout()
    {
        var config = new OpenTelemetryOptions
        {
            OtlpEndpoint = "https://collector.example:4318",
            UseGrpc = false,
            OtlpTimeoutMilliseconds = 2500,
            OtlpHeaders = new Dictionary<string, string> { ["x-api-key"] = "k1" }
        };
        var otlp = new OtlpExporterOptions();

        OpenTelemetryModule.ApplyOtlpOptions(otlp, config);

        otlp.Endpoint.ShouldBe(new Uri("https://collector.example:4318"));
        otlp.Protocol.ShouldBe(OtlpExportProtocol.HttpProtobuf);
        otlp.TimeoutMilliseconds.ShouldBe(2500);
        otlp.Headers.ShouldBe("x-api-key=k1");
    }

    [Fact]
    public void ApplyOtlpOptions_WithoutHeaders_LeavesTheExporterDefault()
    {
        var otlp = new OtlpExporterOptions();
        var before = otlp.Headers;

        OpenTelemetryModule.ApplyOtlpOptions(otlp, new OpenTelemetryOptions { OtlpEndpoint = "http://c:4317" });

        otlp.Headers.ShouldBe(before);
        otlp.Protocol.ShouldBe(OtlpExportProtocol.Grpc);
    }

    [Fact]
    public void Validator_RejectsANonPositiveTimeout()
    {
        var (errors, _) = Validate(new OpenTelemetryOptions { OtlpEndpoint = "http://c:4317", OtlpTimeoutMilliseconds = 0 });

        errors.ShouldContain(e => e.Contains("OtlpTimeoutMilliseconds"));
    }

    [Theory]
    [InlineData("Authorization", "Bearer a,b")]
    [InlineData("x-api-key", "line\nbreak")]
    [InlineData("", "value")]
    [InlineData("bad=key", "value")]
    public void Validator_RejectsHeadersTheWireFormatCannotCarry(string key, string value)
    {
        // 头串是 "k1=v1,k2=v2"：值里的逗号、键里的等号都没法转义，与其静默切坏不如启动期拒绝。
        var options = new OpenTelemetryOptions { OtlpEndpoint = "http://c:4317" };
        options.OtlpHeaders[key] = value;

        var (errors, _) = Validate(options);

        errors.ShouldContain(e => e.Contains("OtlpHeaders"));
    }

    [Fact]
    public void Validator_AcceptsAnEqualsSignInsideAHeaderValue()
    {
        // Basic 认证的 base64 尾部就是 '='：解析按第一个 '=' 切分，值里的 '=' 合法。
        var options = new OpenTelemetryOptions { OtlpEndpoint = "http://c:4317" };
        options.OtlpHeaders["Authorization"] = "Basic dXNlcjpwYXNz==";

        Validate(options).Errors.ShouldBeEmpty();
    }
}
