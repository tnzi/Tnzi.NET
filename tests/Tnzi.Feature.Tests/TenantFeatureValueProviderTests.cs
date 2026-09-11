using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Services;
using Tnzi.MultiTenancy;
// `Options` alone resolves to the Tnzi.Feature.Options namespace from inside Tnzi.Feature.Tests.
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Feature.Tests;

public class TenantFeatureValueProviderTests
{
    private readonly Mock<IRepository<FeatureValue, Guid>> _repositoryMock = new();

    [Fact]
    public void Inactive_when_multi_tenancy_is_disabled_with_a_reason()
    {
        IFeatureValueProvider provider = new TenantFeatureValueProvider(
            _repositoryMock.Object,
            currentTenant: null,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = false }));

        provider.IsActive.ShouldBeFalse();
        provider.InactiveReason.ShouldNotBeNullOrWhiteSpace();
        provider.InactiveReason!.ShouldContain("multi-tenancy is disabled");
        provider.RequiresKey.ShouldBeTrue();
    }

    [Fact]
    public void Inactive_when_multi_tenancy_options_are_absent()
    {
        // No options registered at all reads as "disabled" - the same default the
        // framework binds (MultiTenancyOptions.Enabled = false).
        IFeatureValueProvider provider = new TenantFeatureValueProvider(_repositoryMock.Object);

        provider.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Active_when_multi_tenancy_is_enabled()
    {
        IFeatureValueProvider provider = new TenantFeatureValueProvider(
            _repositoryMock.Object,
            currentTenant: null,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        provider.IsActive.ShouldBeTrue();
        provider.InactiveReason.ShouldBeNull();
    }

    /// <summary>★热路径：有缓存时同一租户的第二次查询不再碰仓储，且缓存按租户分键。</summary>
    [Fact]
    public async Task GetOrNullAsync_WithCache_QueriesTheRepositoryOncePerTenant()
    {
        var tenantId = Guid.NewGuid();
        var rows = new List<FeatureValue>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FeatureDefinitionId = Guid.NewGuid(),
                FeatureDefinition = new FeatureDefinition { Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean },
                ProviderName = "Tenant",
                ProviderKey = tenantId.ToString(),
                Value = "true"
            }
        };
        var mockQueryable = MockQueryable.MockQueryableExtensions.BuildMock(rows);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var currentTenant = new Mock<ICurrentTenant>();
        currentTenant.Setup(t => t.IsAvailable).Returns(true);
        currentTenant.Setup(t => t.Id).Returns(tenantId);

        var provider = new TenantFeatureValueProvider(
            _repositoryMock.Object,
            currentTenant.Object,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }),
            FeatureValueCacheTests.CreateInMemoryCache());

        (await provider.GetOrNullAsync("Feature.Toggle")).ShouldBe("true");
        (await provider.GetOrNullAsync("Feature.Toggle")).ShouldBe("true");

        _repositoryMock.As<IQueryable<FeatureValue>>().Verify(q => q.Provider, Times.Once);
    }

    [Fact]
    public async Task GetOrNullAsync_WithoutCurrentTenant_ReturnsNull_WithoutQuerying()
    {
        var provider = new TenantFeatureValueProvider(
            _repositoryMock.Object,
            currentTenant: null,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        var value = await provider.GetOrNullAsync("Feature.Toggle");

        value.ShouldBeNull();
        // The IQueryable surface was never touched: no tenant means nothing to look up.
        _repositoryMock.As<IQueryable<FeatureValue>>().Verify(q => q.Provider, Times.Never);
    }
}
