using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tnzi.HealthChecks.Tests;

/// <summary>
/// 匿名探针端点的详细输出里能出现什么。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：详细输出默认开着，且带上每个检查项的<b>异常消息</b>与 <c>data</c>。
/// 三个探针端点都是匿名可访问的，于是一次数据库连接失败会把连接串片段发给任何调用方。
/// 文档写着「仅在非生产环境使用」，而源码里<b>没有任何环境判断</b>去兑现这句话 ——
/// 一条只存在于文档里的纪律等于不存在。
/// </remarks>
public class DetailedResponseTests
{
    private const string SecretBearingMessage =
        "Npgsql: failed to connect to Host=db.internal;Username=app;Password=hunter2";

    [Fact]
    public void ByDefault_ExceptionMessagesAndDataAreNotExposed()
    {
        var payload = HealthChecksModule.BuildDetailedPayload(ReportWithAFailure(), exposeErrorDetails: false);

        payload.ShouldNotContain("hunter2");
        payload.ShouldNotContain("db.internal");

        // 逐项状态仍然要有，否则"详细输出"就名不副实了
        payload.ShouldContain("database");
        payload.ShouldContain("Unhealthy");
    }

    [Fact]
    public void WhenErrorDetailsAreExplicitlyExposed_TheyAppear()
    {
        var payload = HealthChecksModule.BuildDetailedPayload(ReportWithAFailure(), exposeErrorDetails: true);

        payload.ShouldContain("hunter2");
        payload.ShouldContain("db.internal");
    }

    private static HealthReport ReportWithAFailure()
    {
        var entry = new HealthReportEntry(
            HealthStatus.Unhealthy,
            "Database connection failed",
            TimeSpan.FromMilliseconds(12),
            new InvalidOperationException(SecretBearingMessage),
            new Dictionary<string, object> { ["ConnectionString"] = "Host=db.internal;Password=hunter2" });

        return new HealthReport(
            new Dictionary<string, HealthReportEntry> { ["database"] = entry },
            TimeSpan.FromMilliseconds(12));
    }
}
