using MockQueryable;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Services;

namespace Tnzi.Feature.Tests;

public class GlobalFeatureValueProviderTests
{
    private readonly Mock<IRepository<FeatureValue, Guid>> _repositoryMock = new();

    private GlobalFeatureValueProvider CreateProvider(params FeatureValue[] rows)
    {
        var mockQueryable = rows.ToList().BuildMock();
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
        return new GlobalFeatureValueProvider(_repositoryMock.Object);
    }

    private static FeatureValue Row(string featureName, string provider, string? key, string value) => new()
    {
        Id = Guid.NewGuid(),
        FeatureDefinitionId = Guid.NewGuid(),
        FeatureDefinition = new FeatureDefinition { Name = featureName, ValueType = FeatureValueType.Boolean },
        ProviderName = provider,
        ProviderKey = key,
        Value = value
    };

    [Fact]
    public void Describes_itself_as_keyless_Global_below_Tenant()
    {
        // Interface-typed on purpose: IsActive / InactiveReason are default interface
        // members, which are reachable through the interface only.
        IFeatureValueProvider provider = CreateProvider();

        provider.Name.ShouldBe("Global");
        provider.RequiresKey.ShouldBeFalse();
        provider.IsActive.ShouldBeTrue();
        provider.InactiveReason.ShouldBeNull();
        // Tenant is 200: a tenant override must still win over the deployment-wide value.
        provider.Priority.ShouldBeLessThan(new TenantFeatureValueProvider(_repositoryMock.Object).Priority);
    }

    [Fact]
    public async Task GetOrNullAsync_ReturnsKeylessGlobalRow()
    {
        var provider = CreateProvider(Row("Feature.Toggle", "Global", null, "true"));

        var value = await provider.GetOrNullAsync("Feature.Toggle");

        value.ShouldBe("true");
    }

    [Fact]
    public async Task GetOrNullAsync_IgnoresRowsOfOtherProviders_AndKeyedRows()
    {
        var provider = CreateProvider(
            Row("Feature.Toggle", "Tenant", "t-1", "true"),
            Row("Feature.Toggle", "Global", "stray-key", "true"));

        var value = await provider.GetOrNullAsync("Feature.Toggle");

        value.ShouldBeNull();
    }

    [Fact]
    public async Task GetOrNullAsync_UnknownFeature_ReturnsNull()
    {
        var provider = CreateProvider(Row("Feature.Other", "Global", null, "true"));

        var value = await provider.GetOrNullAsync("Feature.Toggle");

        value.ShouldBeNull();
    }

    /// <summary>★热路径：有缓存时第二次查同一功能不再碰仓储（此前每次检查都是一趟带 join 的查询）。</summary>
    [Fact]
    public async Task GetOrNullAsync_WithCache_QueriesTheRepositoryOnce()
    {
        var provider = CreateProvider(Row("Feature.Toggle", "Global", null, "true"));
        var cached = new GlobalFeatureValueProvider(_repositoryMock.Object, FeatureValueCacheTests.CreateInMemoryCache());
        _ = provider;

        (await cached.GetOrNullAsync("Feature.Toggle")).ShouldBe("true");
        (await cached.GetOrNullAsync("Feature.Toggle")).ShouldBe("true");

        _repositoryMock.As<IQueryable<FeatureValue>>().Verify(q => q.Provider, Times.Once);
    }
}
