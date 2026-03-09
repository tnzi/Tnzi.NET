namespace Tnzi.Redis.Tests;

public class RedisOptionsTests
{
    [Fact]
    public void RedisOptions_ShouldExposeSafeConnectionDefaults()
    {
        var options = new Tnzi.Redis.Options.RedisOptions();

        Assert.NotNull(options.Connection);
        Assert.False(options.Connection.AbortOnConnectFail);
        Assert.Equal(3, options.Connection.ConnectRetry);
        Assert.Equal(5000, options.Connection.ConnectTimeout);
        Assert.Equal(5000, options.Connection.SyncTimeout);
    }
}
