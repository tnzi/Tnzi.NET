using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using MockQueryable;
using MockQueryable.Moq;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.EventBus;
using Tnzi.Feature.Dtos;
using Tnzi.Feature.Entities;
using Tnzi.Feature.Events;
using Tnzi.Feature.Services;
using Tnzi.Mapster;

namespace Tnzi.Feature.Tests;

public class FeatureServiceTests
{
    private readonly Mock<IRepository<FeatureDefinition, Guid>> _definitionRepositoryMock;
    private readonly Mock<IRepository<FeatureValue, Guid>> _valueRepositoryMock;
    private readonly Mock<IFeatureManager> _featureManagerMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly FeatureService _service;

    public FeatureServiceTests()
    {
        // Setup Mapster for MapTo calls
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _definitionRepositoryMock = new Mock<IRepository<FeatureDefinition, Guid>>();
        _valueRepositoryMock = new Mock<IRepository<FeatureValue, Guid>>();
        _featureManagerMock = new Mock<IFeatureManager>();
        _eventBusMock = new Mock<IEventBus>();
        _serviceProviderMock = new Mock<IServiceProvider>();

        // Setup IEventBus in service provider (ApplicationService resolves it lazily)
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(IEventBus)))
            .Returns(_eventBusMock.Object);

        // Setup ILoggerFactory for ApplicationService.Logger lazy property
        var loggerFactory = new LoggerFactory();
        _serviceProviderMock.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory);

        _service = new FeatureService(
            _serviceProviderMock.Object,
            _definitionRepositoryMock.Object,
            _valueRepositoryMock.Object,
            _featureManagerMock.Object);
    }

    private void SetupDefinitionQueryable(List<FeatureDefinition> definitions)
    {
        var mockQueryable = definitions.BuildMock();
        _definitionRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _definitionRepositoryMock.As<IQueryable<FeatureDefinition>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    // ==================== Value Type Validation Tests ====================

    [Fact]
    public async Task SetValueAsync_BooleanFeature_ValidValue_Succeeds()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.Toggle",
            ValueType = FeatureValueType.Boolean,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        // No existing value
        var emptyList = new List<FeatureValue>();
        var mockQueryable = emptyList.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Value = "true"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SetValueAsync_BooleanFeature_InvalidValue_Fails()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.Toggle",
            ValueType = FeatureValueType.Boolean,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Value = "not-a-boolean"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task SetValueAsync_IntegerFeature_InvalidValue_Fails()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.MaxUsers",
            ValueType = FeatureValueType.Integer,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Value = "abc"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task SetValueAsync_IntegerFeature_ValidValue_Succeeds()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.MaxUsers",
            ValueType = FeatureValueType.Integer,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        var emptyList = new List<FeatureValue>();
        var mockQueryable = emptyList.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            Value = "100"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SetValueAsync_StringFeature_AnyValue_Succeeds()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.Theme",
            ValueType = FeatureValueType.String,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        var emptyList = new List<FeatureValue>();
        var mockQueryable = emptyList.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            Value = "any-string-is-valid"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeTrue();
    }

    // ==================== Event Publishing Tests ====================

    [Fact]
    public async Task CreateDefinitionAsync_PublishesEvent()
    {
        SetupDefinitionQueryable([]);

        var request = new CreateFeatureDefinitionRequest
        {
            Name = "Feature.New",
            ValueType = FeatureValueType.Boolean,
            Group = "TestGroup"
        };

        var result = await _service.CreateDefinitionAsync(request);
        result.Succeeded.ShouldBeTrue();

        _eventBusMock.Verify(e => e.PublishAsync(
            It.Is<FeatureDefinitionCreatedEvent>(evt =>
                evt.Name == "Feature.New" && evt.Group == "TestGroup"),
            default), Times.Once);
    }

    [Fact]
    public async Task UpdateDefinitionAsync_PublishesEvent()
    {
        var id = Guid.NewGuid();
        var entity = new FeatureDefinition
        {
            Id = id,
            Name = "Feature.Existing",
            IsEnabled = true,
            ValueType = FeatureValueType.Boolean
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(entity);

        var request = new UpdateFeatureDefinitionRequest
        {
            DisplayName = "Updated Name",
            IsEnabled = false,
            ValueType = FeatureValueType.Boolean
        };

        var result = await _service.UpdateDefinitionAsync(id, request);
        result.Succeeded.ShouldBeTrue();

        _eventBusMock.Verify(e => e.PublishAsync(
            It.Is<FeatureDefinitionUpdatedEvent>(evt =>
                evt.Name == "Feature.Existing"),
            default), Times.Once);
    }

    [Fact]
    public async Task DeleteDefinitionAsync_PublishesEvent()
    {
        var id = Guid.NewGuid();
        var entity = new FeatureDefinition
        {
            Id = id,
            Name = "Feature.ToDelete",
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(entity);

        var result = await _service.DeleteDefinitionAsync(id);
        result.Succeeded.ShouldBeTrue();

        _eventBusMock.Verify(e => e.PublishAsync(
            It.Is<FeatureDefinitionDeletedEvent>(evt =>
                evt.Name == "Feature.ToDelete" && evt.DefinitionId == id),
            default), Times.Once);
    }

    [Fact]
    public async Task SetValueAsync_NewValue_PublishesChangedEvent()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.Toggle",
            ValueType = FeatureValueType.Boolean,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        var emptyList = new List<FeatureValue>();
        var mockQueryable = emptyList.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "t-1",
            Value = "true"
        };

        await _service.SetValueAsync(request);

        _eventBusMock.Verify(e => e.PublishAsync(
            It.Is<FeatureValueChangedEvent>(evt =>
                evt.FeatureName == "Feature.Toggle"
                && evt.Value == "true"
                && evt.PreviousValue == null),
            default), Times.Once);
    }

    [Fact]
    public async Task SetValueAsync_ExistingValue_PublishesChangedEventWithPreviousValue()
    {
        var definitionId = Guid.NewGuid();
        var definition = new FeatureDefinition
        {
            Id = definitionId,
            Name = "Feature.Toggle",
            ValueType = FeatureValueType.Boolean,
            IsEnabled = true
        };

        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync(definition);

        // Existing value
        var existingValue = new FeatureValue
        {
            Id = Guid.NewGuid(),
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "t-1",
            Value = "false"
        };

        var valueList = new List<FeatureValue> { existingValue };
        var mockQueryable = valueList.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = definitionId,
            ProviderName = "Tenant",
            ProviderKey = "t-1",
            Value = "true"
        };

        await _service.SetValueAsync(request);

        _eventBusMock.Verify(e => e.PublishAsync(
            It.Is<FeatureValueChangedEvent>(evt =>
                evt.Value == "true" && evt.PreviousValue == "false"),
            default), Times.Once);
    }

    // ==================== Definition Not Found Tests ====================

    [Fact]
    public async Task SetValueAsync_DefinitionNotFound_Fails()
    {
        _definitionRepositoryMock.Setup(r => r.FindAsync(It.IsAny<object[]>()))
            .ReturnsAsync((FeatureDefinition?)null);

        var request = new SetFeatureValueRequest
        {
            FeatureDefinitionId = Guid.NewGuid(),
            ProviderName = "Tenant",
            Value = "true"
        };

        var result = await _service.SetValueAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task CreateDefinitionAsync_DuplicateName_Fails()
    {
        var existing = new List<FeatureDefinition>
        {
            new() { Name = "Feature.Existing", ValueType = FeatureValueType.Boolean }
        };
        SetupDefinitionQueryable(existing);

        var request = new CreateFeatureDefinitionRequest
        {
            Name = "feature.existing", // case-insensitive duplicate
            ValueType = FeatureValueType.Boolean
        };

        var result = await _service.CreateDefinitionAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
    }

    // ==================== BatchSetValuesAsync Tests ====================

    private void SetupValueQueryable(List<FeatureValue> values)
    {
        var mockQueryable = values.BuildMock();
        _valueRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _valueRepositoryMock.As<IQueryable<FeatureValue>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    [Fact]
    public async Task BatchSetValuesAsync_AllNewValues_CreatesAll()
    {
        // Arrange
        var def1Id = Guid.NewGuid();
        var def2Id = Guid.NewGuid();
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = def1Id, Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean, IsEnabled = true },
            new() { Id = def2Id, Name = "Feature.MaxUsers", ValueType = FeatureValueType.Integer, IsEnabled = true }
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue>()); // No existing values

        var request = new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Values = new List<BatchFeatureValueItem>
            {
                new() { FeatureDefinitionId = def1Id, Value = "true" },
                new() { FeatureDefinitionId = def2Id, Value = "50" }
            }
        };

        // Act
        var result = await _service.BatchSetValuesAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.SucceededCount.ShouldBe(2);
        result.Data.FailedCount.ShouldBe(0);
        result.Data.Errors.ShouldBeEmpty();
        _valueRepositoryMock.Verify(r => r.InsertManyAsync(
            It.Is<IEnumerable<FeatureValue>>(v => v.Count() == 2),
            default), Times.Once);
    }

    [Fact]
    public async Task BatchSetValuesAsync_WithInvalidValue_ReportsError()
    {
        // Arrange
        var defId = Guid.NewGuid();
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = defId, Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean, IsEnabled = true }
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue>());

        var request = new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Values = new List<BatchFeatureValueItem>
            {
                new() { FeatureDefinitionId = defId, Value = "not-a-boolean" }
            }
        };

        // Act
        var result = await _service.BatchSetValuesAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue(); // Overall succeeds with partial results
        result.Data!.SucceededCount.ShouldBe(0);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.Errors.Count.ShouldBe(1);
    }

    [Fact]
    public async Task BatchSetValuesAsync_NonExistentDefinition_ReportsError()
    {
        // Arrange
        SetupDefinitionQueryable(new List<FeatureDefinition>()); // No definitions
        SetupValueQueryable(new List<FeatureValue>());

        var request = new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Values = new List<BatchFeatureValueItem>
            {
                new() { FeatureDefinitionId = Guid.NewGuid(), Value = "true" }
            }
        };

        // Act
        var result = await _service.BatchSetValuesAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.SucceededCount.ShouldBe(0);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.Errors.ShouldContain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task BatchSetValuesAsync_ExistingValues_Updates()
    {
        // Arrange
        var defId = Guid.NewGuid();
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = defId, Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean, IsEnabled = true }
        };

        var existingValue = new FeatureValue
        {
            Id = Guid.NewGuid(),
            FeatureDefinitionId = defId,
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Value = "false"
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue> { existingValue });

        var request = new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Values = new List<BatchFeatureValueItem>
            {
                new() { FeatureDefinitionId = defId, Value = "true" }
            }
        };

        // Act
        var result = await _service.BatchSetValuesAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.SucceededCount.ShouldBe(1);
        result.Data.FailedCount.ShouldBe(0);
        _valueRepositoryMock.Verify(r => r.UpdateManyAsync(
            It.Is<IEnumerable<FeatureValue>>(v => v.Count() == 1),
            default), Times.Once);
    }

    [Fact]
    public async Task BatchSetValuesAsync_MixedResults_ReportsPartialSuccess()
    {
        // Arrange
        var def1Id = Guid.NewGuid();
        var def2Id = Guid.NewGuid();
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = def1Id, Name = "Feature.Toggle", ValueType = FeatureValueType.Boolean, IsEnabled = true }
            // def2Id not in definitions - will fail
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue>());

        var request = new BatchSetFeatureValuesRequest
        {
            ProviderName = "Tenant",
            ProviderKey = "tenant-1",
            Values = new List<BatchFeatureValueItem>
            {
                new() { FeatureDefinitionId = def1Id, Value = "true" },
                new() { FeatureDefinitionId = def2Id, Value = "false" }
            }
        };

        // Act
        var result = await _service.BatchSetValuesAsync(request);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.SucceededCount.ShouldBe(1);
        result.Data.FailedCount.ShouldBe(1);
        result.Data.Errors.Count.ShouldBe(1);
    }

    // ==================== GetAllValuesAsync Tests ====================

    [Fact]
    public async Task GetAllValuesAsync_ReturnsAllDefinitionsWithEffectiveValues()
    {
        // Arrange
        var def1Id = Guid.NewGuid();
        var def2Id = Guid.NewGuid();
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = def1Id, Name = "Feature.Toggle", DisplayName = "Toggle Feature", ValueType = FeatureValueType.Boolean, DefaultValue = "false", IsEnabled = true, Group = "General" },
            new() { Id = def2Id, Name = "Feature.MaxUsers", DisplayName = "Max Users", ValueType = FeatureValueType.Integer, DefaultValue = "10", IsEnabled = true, Group = "Limits" }
        };

        var values = new List<FeatureValue>
        {
            new() { Id = Guid.NewGuid(), FeatureDefinitionId = def1Id, ProviderName = "Tenant", ProviderKey = "tenant-1", Value = "true" }
            // No value for def2 - should use default
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(values);

        // Act
        var result = await _service.GetAllValuesAsync("Tenant", "tenant-1");

        // Assert
        result.Succeeded.ShouldBeTrue();
        var items = result.Data!.ToList();
        items.Count.ShouldBe(2);

        var toggle = items.First(i => i.FeatureName == "Feature.Toggle");
        toggle.EffectiveValue.ShouldBe("true");
        toggle.IsExplicitlySet.ShouldBeTrue();
        toggle.DefaultValue.ShouldBe("false");
        toggle.DisplayName.ShouldBe("Toggle Feature");

        var maxUsers = items.First(i => i.FeatureName == "Feature.MaxUsers");
        maxUsers.EffectiveValue.ShouldBe("10"); // Uses default
        maxUsers.IsExplicitlySet.ShouldBeFalse();
        maxUsers.Group.ShouldBe("Limits");
    }

    [Fact]
    public async Task GetAllValuesAsync_ExcludesDisabledDefinitions()
    {
        // Arrange
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = Guid.NewGuid(), Name = "Feature.Active", ValueType = FeatureValueType.Boolean, IsEnabled = true },
            new() { Id = Guid.NewGuid(), Name = "Feature.Disabled", ValueType = FeatureValueType.Boolean, IsEnabled = false }
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue>());

        // Act
        var result = await _service.GetAllValuesAsync("Tenant", null);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var items = result.Data!.ToList();
        items.Count.ShouldBe(1);
        items.First().FeatureName.ShouldBe("Feature.Active");
    }

    [Fact]
    public async Task GetAllValuesAsync_WithNoValues_AllUseDefaults()
    {
        // Arrange
        var definitions = new List<FeatureDefinition>
        {
            new() { Id = Guid.NewGuid(), Name = "Feature.A", ValueType = FeatureValueType.Boolean, DefaultValue = "true", IsEnabled = true },
            new() { Id = Guid.NewGuid(), Name = "Feature.B", ValueType = FeatureValueType.Integer, DefaultValue = "100", IsEnabled = true }
        };

        SetupDefinitionQueryable(definitions);
        SetupValueQueryable(new List<FeatureValue>());

        // Act
        var result = await _service.GetAllValuesAsync("Tenant", "tenant-1");

        // Assert
        result.Succeeded.ShouldBeTrue();
        var items = result.Data!.ToList();
        items.ShouldAllBe(i => !i.IsExplicitlySet);
        items.First(i => i.FeatureName == "Feature.A").EffectiveValue.ShouldBe("true");
        items.First(i => i.FeatureName == "Feature.B").EffectiveValue.ShouldBe("100");
    }
}
