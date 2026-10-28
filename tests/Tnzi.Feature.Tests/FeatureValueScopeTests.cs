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

namespace Tnzi.Feature.Tests;

/// <summary>
/// Write-side scope validation and the effective-value chain of <see cref="FeatureService"/>.
///
/// The failure these guard against has no symptom: a value stored under a provider name
/// nothing reads (unregistered, inactive, or keyed the wrong way) is persisted, answered
/// with 200, and then ignored by <see cref="FeatureChecker"/> forever.
/// </summary>
public class FeatureValueScopeTests
{
    private readonly Mock<IRepository<FeatureDefinition, Guid>> _definitionRepositoryMock = new();
    private readonly Mock<IRepository<FeatureValue, Guid>> _valueRepositoryMock = new();
    private readonly Mock<IFeatureManager> _featureManagerMock = new();
    private readonly Mock<IServiceProvider> _serviceProviderMock = new();
    private readonly List<FeatureValue> _insertedValues = [];

    public FeatureValueScopeTests()
    {
        var config = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(config));

        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventBus))).Returns(new Mock<IEventBus>().Object);
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(new LoggerFactory());

        _featureManagerMock.Setup(m => m.GetAllAsync()).ReturnsAsync(new List<FeatureDefinitionRecord>());

        _valueRepositoryMock
            .Setup(r => r.InsertAsync(It.IsAny<FeatureValue>(), It.IsAny<CancellationToken>()))
            .Callback<FeatureValue, CancellationToken>((v, _) => _insertedValues.Add(v))
            .Returns(Task.CompletedTask);
    }

    private FeatureService CreateService(params IFeatureValueProvider[] providers)
    {
        return new FeatureService(
            _serviceProviderMock.Object,
            _definitionRepositoryMock.Object,
            _valueRepositoryMock.Object,
            _featureManagerMock.Object,
            providers);
    }

    private static IFeatureValueProvider[] DefaultProviders(bool tenantActive = true) =>
    [
        new StubFeatureValueProvider("Tenant", 200, requiresKey: true, isActive: tenantActive,
            inactiveReason: "multi-tenancy is disabled"),
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

    private static FeatureDefinition BooleanDefinition(string name = "Feature.Toggle", string? defaultValue = "false") =>
        new() { Id = Guid.NewGuid(), Name = name, ValueType = FeatureValueType.Boolean, DefaultValue = defaultValue, IsEnabled = true };

    // ==================== Provider validation (writes) ====================

    [Fact]
    public async Task SetValueAsync_UnknownProvider_Rejected400_ListingRegisteredProviders()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Edition", ProviderKey = "pro", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderUnknown);
        result.Message!.ShouldContain("Tenant");
        result.Message!.ShouldContain("Global");
        _insertedValues.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetValueAsync_InactiveProvider_Rejected400_WithReason()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders(tenantActive: false));

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "t-1", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderInactive);
        result.Message!.ShouldContain("multi-tenancy is disabled");
        _insertedValues.ShouldBeEmpty();
    }

    [Fact]
    public async Task SetValueAsync_KeyedProviderWithoutKey_Rejected400()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "   ", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderKeyRequired);
    }

    [Fact]
    public async Task SetValueAsync_KeylessProviderWithKey_Rejected400()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = "t-1", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderKeyNotAllowed);
    }

    [Fact]
    public async Task SetValueAsync_NormalizesProviderCasingAndKey()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "tenant", ProviderKey = "  t-1  ", Value = "true"
        });

        result.Succeeded.ShouldBeTrue();
        // What gets persisted is what the provider reads back: canonical name, trimmed key.
        _insertedValues.ShouldHaveSingleItem();
        _insertedValues[0].ProviderName.ShouldBe("Tenant");
        _insertedValues[0].ProviderKey.ShouldBe("t-1");
        result.Data!.ProviderName.ShouldBe("Tenant");
    }

    [Fact]
    public async Task SetValueAsync_BlankKeyOnKeylessProvider_StoredAsNull()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = "", Value = "true"
        });

        result.Succeeded.ShouldBeTrue();
        _insertedValues.ShouldHaveSingleItem();
        _insertedValues[0].ProviderKey.ShouldBeNull();
    }

    [Fact]
    public async Task SetValueAsync_CodeDefinedDefinition_Rejected400_NotOverridable()
    {
        SetupDefinitions();
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = Guid.Empty, ProviderName = "Global", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureDefinitionNotOverridable);
    }

    [Fact]
    public async Task BatchSetValuesAsync_UnknownProvider_RejectedBeforeAnyWrite()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.BatchSetValuesAsync(new BatchSetFeatureValuesRequest
        {
            ProviderName = "Nope",
            Values = [new BatchFeatureValueItem { FeatureDefinitionId = definition.Id, Value = "true" }]
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderUnknown);
        _valueRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<FeatureValue>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchSetValuesAsync_CodeDefinedItem_CountedAsFailure()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.BatchSetValuesAsync(new BatchSetFeatureValuesRequest
        {
            ProviderName = "Global",
            Values =
            [
                new BatchFeatureValueItem { FeatureDefinitionId = definition.Id, Value = "true" },
                new BatchFeatureValueItem { FeatureDefinitionId = Guid.Empty, Value = "true" },
            ]
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.SucceededCount.ShouldBe(1);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.Errors.ShouldHaveSingleItem().ShouldContain("Code-defined");
    }

    // ==================== Reads on an inactive provider stay possible ====================

    [Fact]
    public async Task GetValuesAsync_InactiveProvider_StillReadable_ForCleanup()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues(new FeatureValue
        {
            Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, FeatureDefinition = definition,
            ProviderName = "Tenant", ProviderKey = "t-1", Value = "true"
        });
        var service = CreateService(DefaultProviders(tenantActive: false));

        var result = await service.GetValuesAsync("Tenant", "t-1");

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldHaveSingleItem().Value.ShouldBe("true");
    }

    [Fact]
    public async Task GetValuesAsync_UnknownProvider_Rejected400()
    {
        SetupDefinitions();
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.GetValuesAsync("Edition", "pro");

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderUnknown);
    }

    // ==================== Providers listing ====================

    [Fact]
    public async Task GetValueProvidersAsync_ListsRegisteredProviders_ByPriorityDesc_WithConstraints()
    {
        SetupDefinitions();
        SetupValues();
        var service = CreateService(
            new StubFeatureValueProvider("Global", 100, requiresKey: false),
            new StubFeatureValueProvider("Tenant", 200, requiresKey: true, isActive: false, inactiveReason: "off"));

        var result = await service.GetValueProvidersAsync();

        result.Succeeded.ShouldBeTrue();
        var items = result.Data!.ToList();
        items.Select(p => p.Name).ShouldBe(["Tenant", "Global"]);
        items[0].RequiresKey.ShouldBeTrue();
        items[0].IsActive.ShouldBeFalse();
        items[0].InactiveReason.ShouldBe("off");
        items[1].RequiresKey.ShouldBeFalse();
        items[1].IsActive.ShouldBeTrue();
        items[1].InactiveReason.ShouldBeNull();
    }

    // ==================== Effective value chain ====================

    [Fact]
    public async Task GetAllValuesAsync_TenantScope_InheritsGlobalValue_WhenTenantHasNone()
    {
        var definition = BooleanDefinition(defaultValue: "false");
        SetupDefinitions(definition);
        SetupValues(new FeatureValue
        {
            Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = null, Value = "true"
        });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Tenant", "t-1");

        var item = result.Data!.ShouldHaveSingleItem();
        item.EffectiveValue.ShouldBe("true");
        item.EffectiveSource.ShouldBe(FeatureValueSource.Inherited);
        item.EffectiveProvider.ShouldBe("Global");
        item.IsExplicitlySet.ShouldBeFalse();
        item.Id.ShouldBe(Guid.Empty);
    }

    [Fact]
    public async Task GetAllValuesAsync_TenantScope_ExplicitValue_BeatsGlobal()
    {
        var definition = BooleanDefinition(defaultValue: "false");
        var tenantRowId = Guid.NewGuid();
        SetupDefinitions(definition);
        SetupValues(
            new FeatureValue { Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = null, Value = "true" },
            new FeatureValue { Id = tenantRowId, FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "t-1", Value = "false" });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Tenant", "t-1");

        var item = result.Data!.ShouldHaveSingleItem();
        item.EffectiveValue.ShouldBe("false");
        item.EffectiveSource.ShouldBe(FeatureValueSource.Explicit);
        item.EffectiveProvider.ShouldBe("Tenant");
        item.IsExplicitlySet.ShouldBeTrue();
        item.Id.ShouldBe(tenantRowId);
    }

    [Fact]
    public async Task GetAllValuesAsync_OtherTenantsValue_DoesNotLeakIntoThisScope()
    {
        var definition = BooleanDefinition(defaultValue: "false");
        SetupDefinitions(definition);
        SetupValues(new FeatureValue
        {
            Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "t-OTHER", Value = "true"
        });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Tenant", "t-1");

        var item = result.Data!.ShouldHaveSingleItem();
        item.EffectiveSource.ShouldBe(FeatureValueSource.Default);
        item.EffectiveValue.ShouldBe("false");
        item.EffectiveProvider.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllValuesAsync_GlobalScope_DoesNotInheritFromHigherPriorityTenant()
    {
        var definition = BooleanDefinition(defaultValue: "false");
        SetupDefinitions(definition);
        SetupValues(new FeatureValue
        {
            Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "t-1", Value = "true"
        });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Global", null);

        var item = result.Data!.ShouldHaveSingleItem();
        item.EffectiveSource.ShouldBe(FeatureValueSource.Default);
        item.EffectiveValue.ShouldBe("false");
    }

    [Fact]
    public async Task GetAllValuesAsync_IncludesCodeDefinedDefinitions_AsNotOverridable()
    {
        var dbDefinition = BooleanDefinition("Feature.Db");
        SetupDefinitions(dbDefinition);
        SetupValues();
        _featureManagerMock.Setup(m => m.GetAllAsync()).ReturnsAsync(new List<FeatureDefinitionRecord>
        {
            new("Feature.Db", "Db", null, "false", FeatureValueType.Boolean, null, true, null),        // DB wins
            new("Feature.Code", "Code", null, "42", FeatureValueType.Integer, null, true, "Limits"),
            new("Feature.CodeDisabled", "Off", null, "1", FeatureValueType.Integer, null, false, null), // excluded
        });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Global", null);

        var items = result.Data!.ToList();
        items.Count.ShouldBe(2);
        var db = items.Single(i => i.FeatureName == "Feature.Db");
        db.Source.ShouldBe("Database");
        db.CanOverride.ShouldBeTrue();
        var code = items.Single(i => i.FeatureName == "Feature.Code");
        code.Source.ShouldBe("Code");
        code.CanOverride.ShouldBeFalse();
        code.FeatureDefinitionId.ShouldBe(Guid.Empty);
        code.EffectiveValue.ShouldBe("42");
        code.EffectiveSource.ShouldBe(FeatureValueSource.Default);
        code.Group.ShouldBe("Limits");
    }

    // ==================== Tenant keys are stored the way the runtime reads them ====================

    private TenantFeatureValueProvider RealTenantProvider()
        => new(_valueRepositoryMock.Object, currentTenant: null,
            multiTenancyOptions: Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = true }));

    /// <summary>
    /// 运行时按 <c>ICurrentTenant.Id.ToString()</c>（小写、带连字符）查租户值。宿主管理员手输大写 GUID 时
    /// 此前 200 存进去，在区分大小写的库上运行时永远对不上。
    /// </summary>
    [Theory]
    [InlineData("AAAAAAAA-0000-0000-0000-000000000001")]
    [InlineData("aaaaaaaa000000000000000000000001")]
    [InlineData("  {AAAAAAAA-0000-0000-0000-000000000001}  ")]
    public async Task SetValueAsync_TenantKey_IsStoredInTheRuntimeForm(string key)
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(RealTenantProvider(), new StubFeatureValueProvider("Global", 100, requiresKey: false));

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = key, Value = "true"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        _insertedValues.ShouldHaveSingleItem().ProviderKey.ShouldBe("aaaaaaaa-0000-0000-0000-000000000001");
    }

    [Fact]
    public async Task SetValueAsync_TenantKeyThatIsNotATenantId_Rejected400()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(RealTenantProvider(), new StubFeatureValueProvider("Global", 100, requiresKey: false));

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Tenant", ProviderKey = "acme", Value = "true"
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueProviderKeyInvalid);
        _insertedValues.ShouldBeEmpty();
    }

    // ==================== Lengths are refused with 400, not a 500 from the database ====================

    [Fact]
    public async Task SetValueAsync_ValueLongerThanTheColumn_Rejected400()
    {
        var definition = new FeatureDefinition { Id = Guid.NewGuid(), Name = "Feature.Text", ValueType = FeatureValueType.String, IsEnabled = true };
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.SetValueAsync(new SetFeatureValueRequest
        {
            FeatureDefinitionId = definition.Id, ProviderName = "Global", Value = new string('x', 201)
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureFieldTooLong);
        _insertedValues.ShouldBeEmpty();
    }

    [Fact]
    public async Task BatchSetValuesAsync_OverlongItem_IsCountedAsFailure_OthersStillWritten()
    {
        var text = new FeatureDefinition { Id = Guid.NewGuid(), Name = "Feature.Text", ValueType = FeatureValueType.String, IsEnabled = true };
        var toggle = BooleanDefinition();
        SetupDefinitions(text, toggle);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.BatchSetValuesAsync(new BatchSetFeatureValuesRequest
        {
            ProviderName = "Global",
            Values =
            [
                new BatchFeatureValueItem { FeatureDefinitionId = text.Id, Value = new string('x', 201) },
                new BatchFeatureValueItem { FeatureDefinitionId = toggle.Id, Value = "true" },
            ]
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.SucceededCount.ShouldBe(1);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.Errors.ShouldHaveSingleItem().ShouldContain("at most 200");
        _valueRepositoryMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<FeatureValue>>(v => v.Single().FeatureDefinitionId == toggle.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ==================== Duplicate definitions in one batch ====================

    /// <summary>
    /// 同一个定义在一批里出现两次、库里还没有行时，两条都进待插入列表：撞唯一索引整批 500，而无键作用域在
    /// NULL 互不相等的库上连索引都挡不住，直接存成两行，之后值视图每次加载都 500。
    /// </summary>
    [Fact]
    public async Task BatchSetValuesAsync_SameDefinitionTwice_Rejected400_BeforeAnyWrite()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues();
        var service = CreateService(DefaultProviders());

        var result = await service.BatchSetValuesAsync(new BatchSetFeatureValuesRequest
        {
            ProviderName = "Global",
            Values =
            [
                new BatchFeatureValueItem { FeatureDefinitionId = definition.Id, Value = "true" },
                new BatchFeatureValueItem { FeatureDefinitionId = definition.Id, Value = "false" },
            ]
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.FeatureValueBatchDuplicate);
        _valueRepositoryMock.Verify(r => r.InsertManyAsync(It.IsAny<IEnumerable<FeatureValue>>(), It.IsAny<CancellationToken>()), Times.Never);
        _valueRepositoryMock.Verify(r => r.UpdateManyAsync(It.IsAny<IEnumerable<FeatureValue>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAllValuesAsync_DuplicateGlobalRows_StillLoad()
    {
        var definition = BooleanDefinition();
        SetupDefinitions(definition);
        SetupValues(
            new FeatureValue { Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = null, Value = "true" },
            new FeatureValue { Id = Guid.NewGuid(), FeatureDefinitionId = definition.Id, ProviderName = "Global", ProviderKey = null, Value = "false" });
        var service = CreateService(DefaultProviders());

        var result = await service.GetAllValuesAsync("Global", null);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ShouldHaveSingleItem().IsExplicitlySet.ShouldBeTrue();
    }
}
