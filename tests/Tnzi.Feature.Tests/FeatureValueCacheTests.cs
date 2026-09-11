using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Caching;
using Tnzi.Feature.Events;
using Tnzi.Feature.Options;
using Tnzi.Feature.Services;

namespace Tnzi.Feature.Tests;

/// <summary>
/// <see cref="FeatureValueCache"/>：热路径上的功能值缓存与它的精确失效。
/// </summary>
/// <remarks>
/// 缺陷形态：此前每次 <c>IsEnabledAsync</c> 都让两个内置 provider 各查一次库（带 join），
/// 没有任何值缓存 —— 一个 <c>[RequireFeature]</c> 端点每请求两趟读。
/// </remarks>
public class FeatureValueCacheTests
{
    private readonly Dictionary<string, FeatureValueCacheEntry> _store = new();
    private readonly Mock<ICache> _cache = new();
    private readonly FeatureOptions _options = new() { ValueCacheSeconds = 60 };
    private int _loads;

    public FeatureValueCacheTests()
    {
        _cache
            .Setup(c => c.GetAsync<FeatureValueCacheEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) => _store.TryGetValue(key, out var e) ? e : null);
        _cache
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<FeatureValueCacheEntry>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback((string key, FeatureValueCacheEntry entry, TimeSpan? _, CancellationToken _) => _store[key] = entry)
            .Returns(Task.CompletedTask);
        _cache
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string key, CancellationToken _) => _store.Remove(key))
            .Returns(Task.CompletedTask);
    }

    private FeatureValueCache CreateCache()
    {
        var monitor = new Mock<IOptionsMonitor<FeatureOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(() => _options);
        return new FeatureValueCache(_cache.Object, monitor.Object, NullLogger<FeatureValueCache>.Instance);
    }

    /// <summary>供 provider 测试使用：一份真正会记住东西的内存缓存（60 秒 TTL）。</summary>
    internal static FeatureValueCache CreateInMemoryCache()
    {
        var tests = new FeatureValueCacheTests();
        return tests.CreateCache();
    }

    private Task<string?> Load(string? value)
    {
        _loads++;
        return Task.FromResult(value);
    }

    [Fact]
    public async Task Loads_once_and_serves_the_second_call_from_cache()
    {
        var cache = CreateCache();

        var first = await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));
        var second = await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));

        first.ShouldBe("true");
        second.ShouldBe("true");
        _loads.ShouldBe(1);
    }

    /// <summary>★负结果必须缓存：绝大多数检查落在「这个作用域没有值」上，不缓存它等于没有缓存。</summary>
    [Fact]
    public async Task Caches_the_absence_of_a_value_too()
    {
        var cache = CreateCache();

        (await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load(null))).ShouldBeNull();
        (await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load(null))).ShouldBeNull();

        _loads.ShouldBe(1);
    }

    [Fact]
    public async Task Zero_ttl_turns_the_cache_off()
    {
        _options.ValueCacheSeconds = 0;
        var cache = CreateCache();

        await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));
        await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));

        _loads.ShouldBe(2);
        _store.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scopes_do_not_share_entries()
    {
        var cache = CreateCache();

        (await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load("true"))).ShouldBe("true");
        (await cache.GetOrLoadAsync("Tenant", "t-2", "Feature.Toggle", () => Load("false"))).ShouldBe("false");
        (await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load(null))).ShouldBeNull();

        _loads.ShouldBe(3);
    }

    [Fact]
    public async Task Invalidate_removes_exactly_that_scope()
    {
        var cache = CreateCache();
        await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load("true"));
        await cache.GetOrLoadAsync("Tenant", "t-2", "Feature.Toggle", () => Load("true"));

        await cache.InvalidateAsync("Tenant", "t-1", "Feature.Toggle");

        await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load("false"));
        await cache.GetOrLoadAsync("Tenant", "t-2", "Feature.Toggle", () => Load("false"));
        // t-1 重新加载了一次，t-2 仍然命中。
        _loads.ShouldBe(3);
    }

    [Fact]
    public async Task A_failing_cache_degrades_to_the_loader_instead_of_failing_the_check()
    {
        _cache
            .Setup(c => c.GetAsync<FeatureValueCacheEntry>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis is down"));
        var cache = CreateCache();

        var value = await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));

        value.ShouldBe("true");
        _loads.ShouldBe(1);
    }

    /// <summary>事件带的三个字段就是缓存键的三段 —— 写入与删除都精确失效那一条。</summary>
    [Fact]
    public async Task The_handler_invalidates_on_value_changed_and_deleted_events()
    {
        var cache = CreateCache();
        await cache.GetOrLoadAsync("Tenant", "t-1", "Feature.Toggle", () => Load("true"));
        await cache.GetOrLoadAsync("Global", null, "Feature.Toggle", () => Load("true"));
        var handler = new FeatureValueCacheInvalidationHandler(cache);

        await handler.HandleAsync(new FeatureValueChangedEvent { FeatureName = "Feature.Toggle", ProviderName = "Tenant", ProviderKey = "t-1", Value = "false" });
        await handler.HandleAsync(new FeatureValueDeletedEvent { FeatureName = "Feature.Toggle", ProviderName = "Global", ProviderKey = null });

        _store.Keys.ShouldNotContain(FeatureValueCache.BuildKey("Tenant", "t-1", "Feature.Toggle"));
        _store.Keys.ShouldNotContain(FeatureValueCache.BuildKey("Global", null, "Feature.Toggle"));
    }

    [Fact]
    public void Keys_distinguish_a_missing_key_from_an_empty_one_and_carry_all_three_segments()
    {
        FeatureValueCache.BuildKey("Global", null, "Feature.Toggle").ShouldBe("Feature:Value:Global:-:Feature.Toggle");
        FeatureValueCache.BuildKey("Tenant", "t-1", "Feature.Toggle").ShouldBe("Feature:Value:Tenant:t-1:Feature.Toggle");
    }
}
