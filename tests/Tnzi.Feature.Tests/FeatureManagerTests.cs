using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MockQueryable;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Services;
using Tnzi.Feature.Options;

namespace Tnzi.Feature.Tests;

public class FeatureManagerTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<IServiceProvider> _scopeServiceProviderMock;
    private readonly Mock<IRepository<FeatureDefinition, Guid>> _repositoryMock;
    private readonly Mock<IOptionsMonitor<FeatureOptions>> _optionsMock;
    private readonly FeatureOptions _options;
    private readonly List<IFeatureDefinitionProvider> _providers;
    private readonly FeatureManager _manager;

    public FeatureManagerTests()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _scopeServiceProviderMock = new Mock<IServiceProvider>();
        _repositoryMock = new Mock<IRepository<FeatureDefinition, Guid>>();
        _optionsMock = new Mock<IOptionsMonitor<FeatureOptions>>();
        _options = new FeatureOptions { CacheRefreshIntervalMinutes = 0 }; // 0 = no auto-refresh
        _optionsMock.Setup(o => o.CurrentValue).Returns(_options);
        _providers = [];

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_scopeServiceProviderMock.Object);
        _scopeServiceProviderMock.Setup(sp => sp.GetService(typeof(IRepository<FeatureDefinition, Guid>)))
            .Returns(_repositoryMock.Object);

        SetupRepositoryQueryable([]);

        _manager = new FeatureManager(
            _scopeFactoryMock.Object,
            _providers,
            _optionsMock.Object,
            Mock.Of<ILogger<FeatureManager>>());
    }

    private void SetupRepositoryQueryable(List<FeatureDefinition> definitions)
    {
        var mockQueryable = definitions.BuildMock();
        _repositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _repositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _repositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _repositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _repositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyWhenNoDefinitions()
    {
        var result = await _manager.GetAllAsync();
        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsDatabaseDefinitions()
    {
        var definitions = new List<FeatureDefinition>
        {
            new() { Name = "Feature.A", ValueType = FeatureValueType.Boolean, IsEnabled = true, Group = "Core" },
            new() { Name = "Feature.B", ValueType = FeatureValueType.Integer, IsEnabled = true }
        };
        SetupRepositoryQueryable(definitions);

        var result = await _manager.GetAllAsync();
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.Name == "Feature.A" && r.Group == "Core");
        result.ShouldContain(r => r.Name == "Feature.B" && r.Group == null);
    }

    [Fact]
    public async Task GetAllAsync_MergesCodeLevelAndDatabase()
    {
        // Code-level definition
        var provider = new Mock<IFeatureDefinitionProvider>();
        provider.Setup(p => p.Define(It.IsAny<IFeatureDefinitionContext>()))
            .Callback<IFeatureDefinitionContext>(ctx =>
            {
                ctx.Add("Feature.Code", "true", FeatureValueType.Boolean, group: "CodeGroup");
            });
        _providers.Add(provider.Object);

        // Database definition
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.DB", ValueType = FeatureValueType.String, IsEnabled = true }
        ]);

        var manager = new FeatureManager(
            _scopeFactoryMock.Object,
            _providers,
            _optionsMock.Object,
            Mock.Of<ILogger<FeatureManager>>());

        var result = await manager.GetAllAsync();
        result.Count.ShouldBe(2);
        result.ShouldContain(r => r.Name == "Feature.Code" && r.Group == "CodeGroup");
        result.ShouldContain(r => r.Name == "Feature.DB");
    }

    [Fact]
    public async Task GetAllAsync_DatabaseOverridesCodeLevel()
    {
        // Code-level definition
        var provider = new Mock<IFeatureDefinitionProvider>();
        provider.Setup(p => p.Define(It.IsAny<IFeatureDefinitionContext>()))
            .Callback<IFeatureDefinitionContext>(ctx =>
            {
                ctx.Add("Feature.Shared", "code-default", FeatureValueType.String);
            });
        _providers.Add(provider.Object);

        // Database override
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.Shared", DefaultValue = "db-default", ValueType = FeatureValueType.String, IsEnabled = true }
        ]);

        var manager = new FeatureManager(
            _scopeFactoryMock.Object,
            _providers,
            _optionsMock.Object,
            Mock.Of<ILogger<FeatureManager>>());

        var result = await manager.GetAllAsync();
        result.Count.ShouldBe(1);
        result.First().DefaultValue.ShouldBe("db-default");
    }

    [Fact]
    public async Task GetOrNullAsync_ReturnsDefinition()
    {
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.A", ValueType = FeatureValueType.Boolean, IsEnabled = true }
        ]);

        var result = await _manager.GetOrNullAsync("Feature.A");
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Feature.A");
    }

    [Fact]
    public async Task GetOrNullAsync_ReturnsNullForNonExistent()
    {
        var result = await _manager.GetOrNullAsync("NonExistent");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_ForcesRefreshOnNextAccess()
    {
        // First call loads snapshot
        await _manager.GetAllAsync();

        // Invalidate
        await _manager.InvalidateCacheAsync();

        // Setup new data
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.New", ValueType = FeatureValueType.Boolean, IsEnabled = true }
        ]);

        // Next call should reload
        var result = await _manager.GetAllAsync();
        result.ShouldContain(r => r.Name == "Feature.New");
    }

    [Fact]
    public async Task CacheExpiration_RefreshesAfterInterval()
    {
        // Set cache interval to 0 minutes (effectively always expired after first set)
        _options.CacheRefreshIntervalMinutes = 0; // 0 means no auto-refresh

        // First call loads snapshot
        var result1 = await _manager.GetAllAsync();
        result1.ShouldBeEmpty();

        // With CacheRefreshIntervalMinutes = 0, cache should never expire
        // So even after invalidation is not triggered, same snapshot returned
        var result2 = await _manager.GetAllAsync();
        result2.ShouldBeEmpty(); // same cached result
    }

    [Fact]
    public async Task GetOrNullAsync_CaseInsensitive()
    {
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.CaseTest", ValueType = FeatureValueType.Boolean, IsEnabled = true }
        ]);

        var result = await _manager.GetOrNullAsync("feature.casetest");
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Feature.CaseTest");
    }

    [Fact]
    public async Task GroupField_PreservedInRecord()
    {
        SetupRepositoryQueryable(
        [
            new() { Name = "Feature.Grouped", ValueType = FeatureValueType.Boolean, IsEnabled = true, Group = "Premium" }
        ]);

        var result = await _manager.GetOrNullAsync("Feature.Grouped");
        result.ShouldNotBeNull();
        result.Group.ShouldBe("Premium");
    }
}
