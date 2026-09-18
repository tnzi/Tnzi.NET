using Microsoft.Extensions.Configuration;
using Tnzi.Caching;
using Tnzi.Modules;

namespace Tnzi.Template.Tests;

/// <summary>
/// 模板模块的编译缓存必须是<b>自持</b>的：它的 <c>Template:CacheSizeLimit</c> 只能作用于本模块，
/// 不能碰全进程共享的 <c>IMemoryCache</c>。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：模块此前在 <c>ConfigureServicesAsync</c> 里无条件
/// <c>AddMemoryCache(o =&gt; o.SizeLimit = CacheSizeLimit)</c>。<c>AddMemoryCache(setup)</c> 只是
/// <c>services.Configure(setup)</c>，它与核心 <c>CachingModule</c> 的委托按注册顺序作用于同一个
/// <c>MemoryCacheOptions</c>，Template（LoadOrder 30）后跑 ⇒ 共享缓存的 <c>SizeLimit</c> 恒为 1000。
/// <c>MemoryCache</c> 在设了 <c>SizeLimit</c> 后拒绝任何不带 <c>Size</c> 的写入（抛
/// <c>InvalidOperationException</c>），而别的模块写共享缓存时并不都带 <c>Size</c>：行级数据授权的
/// 过滤器缓存写入当场 500，AI.Skills 的语义检索每次付完嵌入费后静默退回关键词结果。
/// </remarks>
public class TemplateModuleCacheRegistrationTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        var context = new ServiceConfigurationContext(services, configuration);

        // 与真实启动同序：核心缓存模块（LoadOrder 0）先于模板模块（LoadOrder 30）
        var caching = new CachingModule();
        var template = new TemplateModule();
        caching.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        template.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        caching.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        template.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Loading_the_template_module_does_not_put_a_size_limit_on_the_shared_memory_cache()
    {
        using var provider = BuildProvider();

        var sharedOptions = provider.GetRequiredService<IOptions<MemoryCacheOptions>>().Value;

        // Caching:MemorySizeLimit 未配置 = 不限制；模板模块不得替核心做这个决定
        Assert.Null(sharedOptions.SizeLimit);
    }

    [Fact]
    public void Another_module_can_still_write_the_shared_cache_without_a_size()
    {
        using var provider = BuildProvider();
        var shared = provider.GetRequiredService<IMemoryCache>();

        // 行级数据授权与 AI.Skills 都是这样写的：TimeSpan 重载，不带 Size
        var exception = Record.Exception(() => shared.Set("someone-else", "value", TimeSpan.FromMinutes(1)));

        Assert.Null(exception);
        Assert.True(shared.TryGetValue("someone-else", out _));
    }

    [Fact]
    public void The_engine_cache_is_a_separate_instance_bounded_by_the_template_size_limit()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["Template:CacheSizeLimit"] = "2" });
        var shared = provider.GetRequiredService<IMemoryCache>();
        var engineCache = provider.GetRequiredKeyedService<IMemoryCache>(RazorTemplateEngine.CacheServiceKey);

        Assert.NotSame(shared, engineCache);

        // 上限只落在引擎自己的实例上：第三个条目被拒，前两个还在
        for (var i = 0; i < 3; i++)
            engineCache.Set($"t{i}", i, new MemoryCacheEntryOptions { Size = 1 });

        Assert.Equal(2, ((MemoryCache)engineCache).Count);
        Assert.False(engineCache.TryGetValue("t2", out _));
    }

    [Fact]
    public void A_non_positive_template_size_limit_means_unbounded()
    {
        using var provider = BuildProvider(new Dictionary<string, string?> { ["Template:CacheSizeLimit"] = "0" });
        var engineCache = provider.GetRequiredKeyedService<IMemoryCache>(RazorTemplateEngine.CacheServiceKey);

        for (var i = 0; i < 5; i++)
            engineCache.Set($"t{i}", i, new MemoryCacheEntryOptions { Size = 1 });

        Assert.Equal(5, ((MemoryCache)engineCache).Count);
    }

    [Fact]
    public async Task The_engine_resolves_against_its_own_cache_not_the_shared_one()
    {
        using var provider = BuildProvider();
        var shared = provider.GetRequiredService<IMemoryCache>();
        var engineCache = provider.GetRequiredKeyedService<IMemoryCache>(RazorTemplateEngine.CacheServiceKey);
        var engine = provider.GetRequiredService<ITemplateEngine>();

        await engine.RenderAsync("Hello @Model.Name", new { Name = "x" });

        Assert.Equal(0, ((MemoryCache)shared).Count);
        Assert.Equal(1, ((MemoryCache)engineCache).Count);
    }
}
