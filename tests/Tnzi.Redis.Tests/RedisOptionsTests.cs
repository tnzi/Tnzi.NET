namespace Tnzi.Redis.Tests;

public class RedisOptionsTests
{
    /// <summary>
    /// 不配置时启动连不上 Redis 不让进程失败（见 <see cref="RedisConnectionOptionsTests"/> 的优先级说明）。
    /// </summary>
    [Fact]
    public void RedisOptions_ShouldExposeSafeConnectionDefaults()
    {
        var options = new Tnzi.Redis.Options.RedisOptions();

        Assert.NotNull(options.Connection);
        Assert.False(ConnectionOptions.DefaultAbortOnConnectFail);
        Assert.False(RedisCachingModule.BuildConfigurationOptions("localhost:6379", options.Connection).AbortOnConnectFail);
    }
}
