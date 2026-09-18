using Microsoft.Extensions.Configuration;
using Tnzi.Settings;

namespace Tnzi.Tests.Settings;

/// <summary>
/// 站点名只有一个主键：<c>System:SiteName</c>。
/// </summary>
/// <remarks>
/// 前端 origin 收口那天漏掉了同形的这一对：Identity 的两个事件仍从 <c>App:SiteName</c> 取站点名，
/// 而参考消费方只配 <c>System</c> 节。解析顺序与 <see cref="FrontendUrlResolver"/> 逐字相同。
/// </remarks>
public class SiteNameResolverTests
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

        var name = SiteNameResolver.Resolve(Config(("System:SiteName", " Acme ")), logger);

        Assert.Equal("Acme", name);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void LegacyKey_StillWorks_ButWarnsNamingBothKeys()
    {
        var logger = new RecordingLogger();

        var name = SiteNameResolver.Resolve(Config(("App:SiteName", "Legacy")), logger);

        Assert.Equal("Legacy", name);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("App:SiteName", entry.Message);
        Assert.Contains("System:SiteName", entry.Message);
    }

    [Fact]
    public void PrimaryKey_WinsOverLegacyKey()
    {
        var name = SiteNameResolver.Resolve(Config(("System:SiteName", "Primary"), ("App:SiteName", "Legacy")));

        Assert.Equal("Primary", name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankPrimaryKey_FallsThroughToLegacyKey(string? primary)
    {
        var name = SiteNameResolver.Resolve(Config(("System:SiteName", primary), ("App:SiteName", "Legacy")));

        Assert.Equal("Legacy", name);
    }

    [Fact]
    public void NeitherKey_ReturnsNull_AndNoConfiguration_ReturnsNull()
    {
        Assert.Null(SiteNameResolver.Resolve(Config()));
        Assert.Null(SiteNameResolver.Resolve(null));
    }
}
