namespace Tnzi.Redis.Tests;

/// <summary>
/// 连接配置的优先级：<c>Redis:Connection</c> 显式值 &gt; 连接串里的同名参数 &gt; 框架默认。
/// </summary>
public class RedisConnectionOptionsTests
{
    /// <summary>
    /// ★ 回归：按 StackExchange.Redis 惯用写法写在连接串里的参数，此前被 <c>Redis:Connection</c> 的默认值静默改回。
    /// </summary>
    [Fact]
    public void ConnectionStringValues_SurviveWhenRedisConnectionIsNotConfigured()
    {
        var options = RedisCachingModule.BuildConfigurationOptions(
            "redis:6379,syncTimeout=15000,connectTimeout=8000,connectRetry=6,abortConnect=true",
            new ConnectionOptions());

        Assert.Equal(15000, options.SyncTimeout);
        Assert.Equal(8000, options.ConnectTimeout);
        Assert.Equal(6, options.ConnectRetry);
        Assert.True(options.AbortOnConnectFail);
    }

    [Fact]
    public void ExplicitRedisConnectionValues_WinOverTheConnectionString()
    {
        var options = RedisCachingModule.BuildConfigurationOptions(
            "redis:6379,syncTimeout=15000,abortConnect=true",
            new ConnectionOptions { SyncTimeout = 2000, AbortOnConnectFail = false, ConnectRetry = 1, ConnectTimeout = 1000 });

        Assert.Equal(2000, options.SyncTimeout);
        Assert.False(options.AbortOnConnectFail);
        Assert.Equal(1, options.ConnectRetry);
        Assert.Equal(1000, options.ConnectTimeout);
    }

    /// <summary>两处都没写：框架默认（启动时连不上不让进程失败），其余三项是客户端库自己的默认。</summary>
    [Fact]
    public void NeitherConfigured_FallsBackToTheFrameworkDefaults()
    {
        var options = RedisCachingModule.BuildConfigurationOptions("redis:6379", new ConnectionOptions());

        Assert.False(options.AbortOnConnectFail);
        Assert.Equal(3, options.ConnectRetry);
        Assert.Equal(5000, options.ConnectTimeout);
        Assert.Equal(5000, options.SyncTimeout);
    }

    [Fact]
    public void ConnectionStringOptionNames_AreCaseInsensitive()
    {
        var options = RedisCachingModule.BuildConfigurationOptions("redis:6379, AbortConnect = true", new ConnectionOptions());

        Assert.True(options.AbortOnConnectFail);
    }

    [Fact]
    public void RedisConnection_DefaultsToUnset()
    {
        var connection = new RedisOptions().Connection;

        Assert.Null(connection.AbortOnConnectFail);
        Assert.Null(connection.ConnectRetry);
        Assert.Null(connection.ConnectTimeout);
        Assert.Null(connection.SyncTimeout);
    }
}
