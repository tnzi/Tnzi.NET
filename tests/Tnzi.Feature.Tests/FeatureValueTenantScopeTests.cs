using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using MockQueryable;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.EventBus;
using Tnzi.Feature.Dtos;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Metadata;
using Tnzi.Feature.Services;
using Tnzi.Mapster;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;

namespace Tnzi.Feature.Tests;

/// <summary>
/// The feature value admin surface is scoped by the <b>caller's</b> tenant, not by the
/// <c>providerKey</c> the client sends.
///
/// Without this, a tenant administrator holding <c>feature.update</c> could name any other
/// tenant's id as the provider key and read, overwrite or delete that tenant's feature values;
/// <c>FeatureValue</c> does not implement <c>IMultiTenant</c> (the tenant lives inside
/// <c>ProviderKey</c>), so the global tenant filter never sees it. Same shape, and same rule,
/// as the setting table: tenant ownership is decided by identity, and a caller asking for
/// another tenant is refused rather than silently redirected to its own.
/// </summary>
public class FeatureValueTenantScopeTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly Mock<IRepository<FeatureDefinition, Guid>> _definitionRepositoryMock = new();
    private readonly Mock<IRepository<FeatureValue, Guid>> _valueRepositoryMock = new();
    private readonly Mock<IFeatureManager> _featureManagerMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly List<FeatureValue> _insertedValues = [];
    private readonly List<FeatureValue> _deletedValues = [];

    public FeatureValueTenantScopeTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventBus))).Returns(new Mock<IEventBus>().Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(new LoggerFactory());
        // No ICurrentUser: the caller's tenant comes from ICurrentTenant alone in these tests.
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns((object?)null);

        _featureManagerMock.Setup(m => m.GetAllAsync()).ReturnsAsync(new List<FeatureDefinitionRecord>());

        _valueRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Callback<FeatureValue, CancellationToken>((v, _) => _insertedValues.Add(v))
            .Returns(Task.CompletedTask);
        _valueRepositoryMock
            .Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<FeatureValue>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<FeatureValue>, CancellationToken>((v, _) => _insertedValues.AddRange(v))
            .Returns(Task.CompletedTask);
        _valueRepositoryMock
            .Setup(r => r.DeleteAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Callback<FeatureValue, CancellationToken>((v, _) => _deletedValues.Add(v))
            .Returns(Task.CompletedTask);
    }

    /// <summary><paramref name="tenantId"/> null = host caller (no tenant bound).</summary>
    private FeatureService CreateService(Guid? tenantId, params IFeatureValueProvider[] providers)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(tenantId);

        return new FeatureService(
            _serviceProviderMock.Object,
            _definitionRepositoryMock.Object,
            _valueRepositoryMock.Object,
            _featureManagerMock.Object,
            providers.Length == 0 ? DefaultProviders() : providers,
            currentTenant: tenant.Object);
    }

    private static IFeatureValueProvider[] DefaultProviders() =>
    [
        new StubFeatureValueProvider("Tenant", 200, requiresKey: true),
        new StubFeatureValueProvider("Global", 100, requiresKey: false),
    ];

    private void SetupDefinitions(params FeatureDefinition[] definitions)
    {
        var list = definitions.ToList();
        var mockQueryable = list.BuildMock();
        _definitionRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((object[] keys) => list.FirstOrDefault(d => d.Id == (Guid)keys[0]));
    }

    private void SetupValues(params FeatureValue[] values)
    {
        var mockQueryable = values.ToList().BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>().Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private static FeatureDefinition BooleanDefinition() =>
        new() { Id = Guid.NewGuid(), Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean, DefaultValue = "false", IsEnabled = true };

    private static FeatureValue TenantRow(FeatureDefinition definition, Guid tenantId) => new()
    {
        Id = Guid.NewGuid(),
        FeatureDefinitionId = definition.Id,
        FeatureDefinition = definition,
        ProviderName = "Tenant",
        ProviderKey = tenantId.ToString(),
        Value = "true"
    };

    private static SetFeatureValueRequest SetRequest(FeatureDefinition definition, Guid tenantKey) => new()
    {
        FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = tenantKey.ToString(), Value = "true"
    };

    // ==================== Writes ====================

    [Fact]
    public async Task SetValue_TenantCaller_OtherTenantKey_Returns403_WithoutWriting()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA);

        var result = await service.SetValueAsync(SetRequest(definition, TenantB));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueScopeForbidden);
        _insertedValues.ShouldBeEmpty();
        _valueRepositoryMock.Verify(r => r.UpdateAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetValue_TenantCaller_OwnTenantKey_Succeeds()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA);

        var result = await service.SetValueAsync(SetRequest(definition, TenantA));

        result.Succeeded.ShouldBeTrue(result.Message);
        _insertedValues.ShouldHaveSingleItem().ProviderKey.ShouldBe(TenantA.ToString());
    }

    [Fact]
    public async Task SetValue_TenantCaller_OwnTenantKey_ComparedCaseInsensitively()
    {
        // ProviderKey is what the client typed; the tenant provider reads back Guid.ToString()
        // (lower-case), so an upper-case key from the client must still count as the caller's own.
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA);

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = TenantA.ToString().ToUpperInvariant(), Value = "true"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task SetValue_HostCaller_AnyTenantKey_Succeeds()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(null);

        (await service.SetValueAsync(SetRequest(definition, TenantB))).Succeeded.ShouldBeTrue();

        _insertedValues.ShouldHaveSingleItem().ProviderKey.ShouldBe(TenantB.ToString());
    }

    [Fact]
    public async Task SetValue_TenantCaller_GlobalScope_StillAllowed()
    {
        // Same line as the setting table: Global rows are not owned by any tenant and stay
        // writable from inside a tenant. Tightening that is a separate decision, for both tables.
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA);

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Global", Value = "true"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task BatchSetValues_TenantCaller_OtherTenantKey_Returns403_BeforeAnyWrite()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA);

        var result = await service.BatchSetValuesAsync(new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = TenantB.ToString(),
            Values = [new BatchFeatureValueItem { FeatureDefinitionId = definition.Id, Value = "true" }]
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueScopeForbidden);
        _insertedValues.ShouldBeEmpty();
    }

    // ==================== Reads ====================

    [Fact]
    public async Task GetValues_TenantCaller_OtherTenantKey_Returns403()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues(TenantRow(definition, TenantB));
        var service = CreateService(TenantA);

        var result = await service.GetValuesAsync("Tenant", TenantB.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueScopeForbidden);
    }

    [Fact]
    public async Task GetValues_HostCaller_OtherTenantKey_Succeeds()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues(TenantRow(definition, TenantB));
        var service = CreateService(null);

        var result = await service.GetValuesAsync("Tenant", TenantB.ToString());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ShouldHaveSingleItem().ProviderKey.ShouldBe(TenantB.ToString());
    }

    [Fact]
    public async Task GetAllValues_TenantCaller_OtherTenantKey_Returns403()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues(TenantRow(definition, TenantB));
        var service = CreateService(TenantA);

        var result = await service.GetAllValuesAsync("Tenant", TenantB.ToString());

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueScopeForbidden);
    }

    // ==================== Delete ====================

    [Fact]
    public async Task DeleteValue_TenantCaller_OtherTenantRow_Returns404_NotRevealingExistence()
    {
        var definition = BooleanDefinition();
        var foreign = TenantRow(definition, TenantB);
        var own = TenantRow(definition, TenantA);
        SetupDefinitions(definition);
        SetupValues(foreign, own);
        var service = CreateService(TenantA);

        var refused = await service.DeleteValueAsync(foreign.Id);

        refused.Succeeded.ShouldBeFalse();
        refused.Code.ShouldBe(404);
        refused.ErrorCode.ShouldBe(ErrorCodes.FeatureValueNotFound);
        _deletedValues.ShouldBeEmpty();

        (await service.DeleteValueAsync(own.Id)).Succeeded.ShouldBeTrue();
        _deletedValues.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
    }

    [Fact]
    public async Task DeleteValue_HostCaller_AnyTenantRow_Succeeds()
    {
        var definition = BooleanDefinition();
        var foreign = TenantRow(definition, TenantB);
        SetupDefinitions(definition);
        SetupValues(foreign);
        var service = CreateService(null);

        (await service.DeleteValueAsync(foreign.Id)).Succeeded.ShouldBeTrue();

        _deletedValues.ShouldHaveSingleItem().Id.ShouldBe(foreign.Id);
    }

    [Fact]
    public async Task DeleteValue_TenantCaller_LegacyRowOfUnregisteredKeyedProvider_Returns404()
    {
        // A row whose provider is no longer registered has nobody to vouch for its key: a
        // tenant-bound caller only gets to touch it when the key is its own tenant id.
        var definition = BooleanDefinition();
        var legacy = new FeatureValue
        {
            Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, FeatureDefinition = definition,
            ProviderName = "Edition", ProviderKey = "pro", Value = "true"
        };
        SetupDefinitions(definition);
        SetupValues(legacy);
        var service = CreateService(TenantA);

        (await service.DeleteValueAsync(legacy.Id)).Code.ShouldBe(404);
        _deletedValues.ShouldBeEmpty();
    }

    // ==================== Provider hook ====================

    private sealed class DepartmentFeatureValueProvider : IFeatureValueProvider
    {
        public string Name => "Department";
        public int Priority => 150;
        public Task<string?> GetOrNullAsync(string featureName) => Task.FromResult<string?>(null);

        // Keys are department ids inside the caller's tenant, so a tenant caller may name any of them.
        public bool IsKeyAccessibleTo(string callerTenantId, string? providerKey) => providerKey?.StartsWith("dept-", StringComparison.Ordinal) == true;
    }

    [Fact]
    public async Task KeyedProvider_DefaultRule_TreatsTheKeyAsATenantId()
    {
        // An "Edition" style provider keyed by something that is not a tenant id is host-only
        // for a tenant-bound caller unless it says otherwise via IsKeyAccessibleTo.
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA, new StubFeatureValueProvider("Edition", 150, requiresKey: true));

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Edition", ProviderKey = "pro", Value = "true"
        });

        result.Code.ShouldBe(403);
        _insertedValues.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeyedProvider_CanWidenTheRule_ViaIsKeyAccessibleTo()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(TenantA, new DepartmentFeatureValueProvider());

        var allowed = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Department", ProviderKey = "dept-7", Value = "true"
        });
        allowed.Succeeded.ShouldBeTrue(allowed.Message);

        var refused = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Department", ProviderKey = "other", Value = "true"
        });
        refused.Code.ShouldBe(403);
    }
}
