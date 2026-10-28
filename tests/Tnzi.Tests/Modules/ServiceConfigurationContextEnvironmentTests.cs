using Microsoft.Extensions.Configuration;

namespace Tnzi.Tests.Modules;

/// <summary>
/// 模块看到的环境名要与宿主一致。宿主的环境由配置键 <c>environment</c> 决定（命令行 <c>--environment</c>、
/// <c>WebApplicationOptions.EnvironmentName</c>、去掉前缀的 <c>ASPNETCORE_ENVIRONMENT</c> 都落在这个键上）；
/// 此前上下文只读原始的 <c>ASPNETCORE_ENVIRONMENT</c> / <c>DOTNET_ENVIRONMENT</c>，
/// 以命令行参数指定环境时模块一律以为是 Production。
/// </summary>
public class ServiceConfigurationContextEnvironmentTests
{
    private static ServiceConfigurationContext ContextWith(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceConfigurationContext(new ServiceCollection(), configuration);
    }

    [Fact]
    public void TheHostEnvironmentKey_IsRead()
    {
        var context = ContextWith(("environment", "Development"));

        Assert.True(context.IsDevelopment());
        Assert.False(context.IsProduction());
    }

    [Fact]
    public void TheHostEnvironmentKey_WinsOverTheRawEnvironmentVariables()
    {
        // 命令行参数排在环境变量之后，宿主以它为准
        var context = ContextWith(("environment", "Staging"), ("ASPNETCORE_ENVIRONMENT", "Development"));

        Assert.Equal("Staging", context.EnvironmentName);
    }

    [Fact]
    public void TheRawEnvironmentVariables_StillWork()
    {
        Assert.Equal("Development", ContextWith(("ASPNETCORE_ENVIRONMENT", "Development")).EnvironmentName);
        Assert.Equal("Staging", ContextWith(("DOTNET_ENVIRONMENT", "Staging")).EnvironmentName);
    }

    [Fact]
    public void NothingConfigured_MeansProduction_AndAnExplicitNameWins()
    {
        Assert.True(ContextWith().IsProduction());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("environment", "Staging")])
            .Build();
        Assert.Equal("Development", new ServiceConfigurationContext(new ServiceCollection(), configuration, "Development").EnvironmentName);
    }
}
