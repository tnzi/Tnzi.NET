using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 设计期工厂会写进程级静态（<c>TableNamePrefixConfiguration.DesignTimeModuleContainer</c>、
/// <c>OutboxMessageConfiguration.OutboxEnabled</c>），这些测试之间与其它测试之间都不能并行。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class DesignTimeStaticsCollection
{
    public const string Name = "DesignTimeStatics";
}

/// <summary>
/// 一份落在临时目录里的 appsettings.json，给 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/>
/// 的 <c>FindConfigurationPath</c> 指路；同一份内容也能以 <see cref="Configuration"/> 喂给运行期注册。
/// </summary>
public sealed class TemporaryAppSettings : IDisposable
{
    public string ConfigurationDirectory { get; }

    public IConfiguration Configuration { get; }

    public TemporaryAppSettings(object settings)
    {
        ConfigurationDirectory = Path.Combine(Path.GetTempPath(), "tnzi-efcore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ConfigurationDirectory);
        File.WriteAllText(Path.Combine(ConfigurationDirectory, "appsettings.json"), JsonSerializer.Serialize(settings));

        Configuration = new ConfigurationBuilder()
            .SetBasePath(ConfigurationDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(ConfigurationDirectory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留下一份无害的 JSON，不值得让测试因此失败。
        }
    }
}

/// <summary>
/// 让每次建模都是缓存未命中：这些用例在同一个进程里对同一个上下文类型反复切换进程级开关，
/// EF 的模型缓存（按上下文类型键）会把第一次的模型喂给后面每一次。
/// 真实的 <c>dotnet ef</c> 是一次性进程，不存在这个问题。
/// </summary>
public sealed class NoModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => new object();
}

/// <summary>把 <see cref="DesignTimeDbContextFactoryBase{TDbContext}"/> 指向一份临时 appsettings.json。</summary>
public class ProbeDesignTimeFactory(string configurationDirectory) : DesignTimeDbContextFactoryBase<MultiTenancyProbeDbContext>
{
    protected override string? FindConfigurationPath() => configurationDirectory;

    protected override MultiTenancyProbeDbContext CreateDbContextInstance(
        DbContextOptions<MultiTenancyProbeDbContext> options, ICurrentUser currentUser)
    {
        var uncached = new DbContextOptionsBuilder<MultiTenancyProbeDbContext>(options)
            .ReplaceService<IModelCacheKeyFactory, NoModelCacheKeyFactory>()
            .Options;
        return base.CreateDbContextInstance(uncached, currentUser);
    }
}
