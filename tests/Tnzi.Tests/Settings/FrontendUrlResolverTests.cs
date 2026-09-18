using Microsoft.Extensions.Configuration;
using Tnzi.Settings;

namespace Tnzi.Tests.Settings;

/// <summary>
/// 前端 origin 只有一个主键：<c>System:FrontendUrl</c>。
/// </summary>
/// <remarks>
/// 此前框架从两个互不相干的键读前端地址：Hosting 的邮件处理器经 <c>ApplicationOptions</c> 读 <c>System:FrontendUrl</c>，
/// Identity 的五处（邀请链接 / OAuth returnUrl 白名单回退 / 回调页 postMessage origin / 重置密码重定向 / 重置事件）
/// 直接读 <c>App:FrontendUrl</c>。参考消费方只配了前者：密码重置信正常，邀请信的接受链接却是相对路径而拒发，
/// 管理端仍报「已发出」。收口为一个解析顺序，所有读者共用。
/// </remarks>
public class FrontendUrlResolverTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public void PrimaryKey_IsUsed_WithoutAnyWarning()
    {
        var logger = new RecordingLogger();

        var url = FrontendUrlResolver.Resolve(Config(("System:FrontendUrl", "https://app.example/")), logger);

        Assert.Equal("https://app.example", url);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void LegacyKey_StillWorks_ButWarnsNamingBothKeys()
    {
        var logger = new RecordingLogger();

        var url = FrontendUrlResolver.Resolve(Config(("App:FrontendUrl", "https://legacy.example")), logger);

        Assert.Equal("https://legacy.example", url);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("App:FrontendUrl", entry.Message);
        Assert.Contains("System:FrontendUrl", entry.Message);
    }

    [Fact]
    public void PrimaryKey_WinsOverLegacyKey()
    {
        var url = FrontendUrlResolver.Resolve(Config(
            ("System:FrontendUrl", "https://primary.example"),
            ("App:FrontendUrl", "https://legacy.example")));

        Assert.Equal("https://primary.example", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPrimaryKey_FallsThroughToLegacyKey(string? primary)
    {
        var url = FrontendUrlResolver.Resolve(Config(
            ("System:FrontendUrl", primary),
            ("App:FrontendUrl", "https://legacy.example")));

        Assert.Equal("https://legacy.example", url);
    }

    [Fact]
    public void NeitherKey_ReturnsNull_AndNoConfiguration_ReturnsNull()
    {
        Assert.Null(FrontendUrlResolver.Resolve(Config()));
        Assert.Null(FrontendUrlResolver.Resolve(null));
    }

    [Fact]
    public void KeysForMessages_NamesThePrimaryKeyFirst()
    {
        // 守卫消息只引用这一句：运维照它加配置就能通，两处各写一遍会漂。
        Assert.StartsWith("System:FrontendUrl", FrontendUrlResolver.KeysForMessages);
        Assert.Contains("App:FrontendUrl", FrontendUrlResolver.KeysForMessages);
    }
}
