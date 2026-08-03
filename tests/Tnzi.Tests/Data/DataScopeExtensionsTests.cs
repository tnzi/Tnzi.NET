using System.Linq.Expressions;

namespace Tnzi.Tests.Data;

/// <summary>
/// <see cref="IDataScopeProvider{TEntity}"/> + <see cref="DataScopeExtensions"/> 行为测试：
/// 无 provider 零影响、单 provider 生效、多 provider AND 组合、DI 解析、单行访问校验。
/// </summary>
public class DataScopeExtensionsTests
{
    private sealed class ScopeItem
    {
        public int Value { get; init; }
    }

    private sealed class ValueScopeProvider(Expression<Func<ScopeItem, bool>>? filter) : IDataScopeProvider<ScopeItem>
    {
        public Expression<Func<ScopeItem, bool>>? GetFilter() => filter;
    }

    private static readonly List<ScopeItem> Items =
        [new() { Value = 1 }, new() { Value = 2 }, new() { Value = 3 }, new() { Value = 4 }];

    [Fact]
    public void BuildDataScopeFilter_WithNoProviders_ShouldReturnNull()
    {
        var result = DataScopeExtensions.BuildDataScopeFilter(Array.Empty<IDataScopeProvider<ScopeItem>>());
        Assert.Null(result);
    }

    [Fact]
    public void BuildDataScopeFilter_WithNullFilters_ShouldReturnNull()
    {
        var providers = new IDataScopeProvider<ScopeItem>[] { new ValueScopeProvider(null), new ValueScopeProvider(null) };
        var result = DataScopeExtensions.BuildDataScopeFilter(providers);
        Assert.Null(result);
    }

    [Fact]
    public void BuildDataScopeFilter_WithMultipleProviders_ShouldAndCombine()
    {
        var providers = new IDataScopeProvider<ScopeItem>[]
        {
            new ValueScopeProvider(i => i.Value >= 2),
            new ValueScopeProvider(i => i.Value <= 3),
        };

        var filter = DataScopeExtensions.BuildDataScopeFilter(providers);

        Assert.NotNull(filter);
        var predicate = filter!.Compile();
        Assert.False(predicate(new ScopeItem { Value = 1 }));
        Assert.True(predicate(new ScopeItem { Value = 2 }));
        Assert.True(predicate(new ScopeItem { Value = 3 }));
        Assert.False(predicate(new ScopeItem { Value = 4 }));
    }

    [Fact]
    public void ApplyDataScope_WithNoProviders_ShouldReturnSourceUnchanged()
    {
        var query = Items.AsQueryable();
        var result = query.ApplyDataScope(Array.Empty<IDataScopeProvider<ScopeItem>>());
        Assert.Equal(4, result.Count());
    }

    [Fact]
    public void ApplyDataScope_WithProviders_ShouldFilter()
    {
        var providers = new IDataScopeProvider<ScopeItem>[]
        {
            new ValueScopeProvider(i => i.Value >= 2),
            new ValueScopeProvider(i => i.Value <= 3),
        };

        var result = Items.AsQueryable().ApplyDataScope(providers).ToList();

        Assert.Equal([2, 3], result.Select(i => i.Value));
    }

    [Fact]
    public void ApplyDataScope_FromServiceProvider_ShouldResolveAndApply()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataScopeProvider<ScopeItem>>(new ValueScopeProvider(i => i.Value == 3));
        var provider = services.BuildServiceProvider();

        var result = Items.AsQueryable().ApplyDataScope(provider).ToList();

        Assert.Single(result);
        Assert.Equal(3, result[0].Value);
    }

    [Fact]
    public async Task CanAccessAsync_WithNoProviders_ShouldReturnTrue()
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        Assert.True(await provider.CanAccessAsync(new ScopeItem { Value = 99 }));
    }

    [Fact]
    public async Task CanAccessAsync_ShouldAndAllProviders()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDataScopeProvider<ScopeItem>>(new ValueScopeProvider(i => i.Value >= 2));
        services.AddSingleton<IDataScopeProvider<ScopeItem>>(new ValueScopeProvider(i => i.Value <= 3));
        var provider = services.BuildServiceProvider();

        Assert.False(await provider.CanAccessAsync(new ScopeItem { Value = 1 }));
        Assert.True(await provider.CanAccessAsync(new ScopeItem { Value = 2 }));
        Assert.False(await provider.CanAccessAsync(new ScopeItem { Value = 4 }));
    }
}
