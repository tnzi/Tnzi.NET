namespace Tnzi.Logging.Tests.Options;

/// <summary>
/// <c>Logging:MinimumLevelOverrides</c> 的合并语义。
///
/// 关键在于 <c>Microsoft.AspNetCore</c> → Warning 这一条不是噪音偏好而是安全控制：
/// 它压掉的 <c>Hosting.Diagnostics</c> 两行会把 <c>QueryString</c> 原文写进日志。
/// 端到端的证明在 <c>RequestLogging/RequestLoggingCredentialLeakTests</c>，
/// 这里只钉合并规则本身。
/// </summary>
public class MinimumLevelOverrideTests
{
    [Fact]
    public void Defaults_SuppressAspNetCoreHostingDiagnostics()
    {
        var resolved = LoggingModule.ResolveMinimumLevelOverrides(new LoggingOptions());

        resolved["Microsoft.AspNetCore"].ShouldBe(LogEventLevel.Warning);
    }

    /// <summary>
    /// 消费方补一条无关的覆盖，默认名单必须原样保留 —— 这是 B1 的核心。
    /// </summary>
    [Fact]
    public void ConsumerOverrides_AreMergedOnTopOfTheDefaults()
    {
        var options = new LoggingOptions
        {
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["MyApp.Data"] = LogEventLevel.Debug,
            },
        };

        var resolved = LoggingModule.ResolveMinimumLevelOverrides(options);

        resolved["MyApp.Data"].ShouldBe(LogEventLevel.Debug);
        resolved["Microsoft.AspNetCore"].ShouldBe(LogEventLevel.Warning);
    }

    /// <summary>
    /// 显式调回来仍然可以 —— 那是一次知情的选择，不是顺手删掉。
    /// </summary>
    [Fact]
    public void ConsumerCanLowerTheFrameworkDefaultExplicitly()
    {
        var options = new LoggingOptions
        {
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["Microsoft.AspNetCore"] = LogEventLevel.Information,
            },
        };

        var resolved = LoggingModule.ResolveMinimumLevelOverrides(options);

        resolved["Microsoft.AspNetCore"].ShouldBe(LogEventLevel.Information);
    }

    /// <summary>
    /// 大小写写错的消费方配置**不得**顶掉默认条目。
    ///
    /// Serilog 的来源前缀匹配区分大小写，所以 <c>"microsoft.aspnetcore"</c> 这条
    /// 自己一个来源都匹配不上；如果它还把正确大小写的默认条目挤掉，结果是两条都不生效
    /// —— 一次拼写失误换来一个静默失效的安全控制。
    /// </summary>
    [Fact]
    public void MiscasedConsumerKey_DoesNotDisplaceTheFrameworkDefault()
    {
        var options = new LoggingOptions
        {
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["microsoft.aspnetcore"] = LogEventLevel.Verbose,
            },
        };

        var resolved = LoggingModule.ResolveMinimumLevelOverrides(options);

        resolved["Microsoft.AspNetCore"].ShouldBe(LogEventLevel.Warning);
        resolved["microsoft.aspnetcore"].ShouldBe(LogEventLevel.Verbose);
    }

    [Fact]
    public void BlankSourceKeys_AreIgnored()
    {
        var options = new LoggingOptions
        {
            MinimumLevelOverrides = new Dictionary<string, LogEventLevel>
            {
                ["  "] = LogEventLevel.Verbose,
            },
        };

        var resolved = LoggingModule.ResolveMinimumLevelOverrides(options);

        resolved.Keys.ShouldNotContain("  ");
        resolved["Microsoft.AspNetCore"].ShouldBe(LogEventLevel.Warning);
    }

    /// <summary>
    /// 解析结果不得回带内部的默认名单实例 —— 否则调用方一次写入就污染了整个进程的默认值。
    /// </summary>
    [Fact]
    public void ResolvedMap_IsAFreshCopyEachTime()
    {
        var first = LoggingModule.ResolveMinimumLevelOverrides(new LoggingOptions());
        var second = LoggingModule.ResolveMinimumLevelOverrides(new LoggingOptions());

        first.ShouldNotBeSameAs(second);
    }
}
