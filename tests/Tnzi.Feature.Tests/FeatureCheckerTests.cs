using Moq;
using Tnzi.Feature.Services;
using Tnzi.Feature.Dtos;
using Tnzi.Feature.Entities;

namespace Tnzi.Feature.Tests;

public class FeatureCheckerTests
{
    private readonly Mock<IFeatureManager> _featureManagerMock;
    private readonly List<IFeatureValueProvider> _providers;
    private readonly FeatureChecker _checker;

    public FeatureCheckerTests()
    {
        _featureManagerMock = new Mock<IFeatureManager>();
        _providers = [];
        _checker = CreateChecker();
    }

    private FeatureChecker CreateChecker()
    {
        return new FeatureChecker(_featureManagerMock.Object, _providers);
    }

    private void SetupFeature(string name, string? defaultValue = "true", FeatureValueType valueType = FeatureValueType.Boolean, bool isEnabled = true)
    {
        _featureManagerMock.Setup(m => m.GetOrNullAsync(name))
            .ReturnsAsync(new FeatureDefinitionRecord(name, name, null, defaultValue, valueType, null, isEnabled, null));
    }

    [Fact]
    public async Task IsEnabledAsync_BooleanFeature_ReturnsTrue()
    {
        SetupFeature("Feature.Test", "true");
        var result = await _checker.IsEnabledAsync("Feature.Test");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_BooleanFeature_ReturnsFalse()
    {
        SetupFeature("Feature.Test", "false");
        var result = await _checker.IsEnabledAsync("Feature.Test");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_DisabledFeature_ReturnsFalse()
    {
        SetupFeature("Feature.Test", "true", isEnabled: false);
        var result = await _checker.IsEnabledAsync("Feature.Test");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_NonExistentFeature_ReturnsFalse()
    {
        _featureManagerMock.Setup(m => m.GetOrNullAsync("Unknown"))
            .ReturnsAsync((FeatureDefinitionRecord?)null);

        var result = await _checker.IsEnabledAsync("Unknown");
        result.ShouldBeFalse();
    }

    /// <summary>
    /// 定义按名查找不分大小写，provider 却按拿到的名字查值、建缓存键；值的写入与缓存失效用的都是定义自己的写法。
    /// 调用方的大小写与定义不同时，PostgreSQL 上管理员设的值永远查不到，用量统计也按大小写分成几份。
    /// </summary>
    [Fact]
    public async Task IsEnabledAsync_CallerCasingDiffers_ProvidersAndUsageSeeTheDefinitionsName()
    {
        _featureManagerMock.Setup(m => m.GetOrNullAsync("orders.advancedSearch"))
            .ReturnsAsync(new FeatureDefinitionRecord("Orders.AdvancedSearch", "Advanced search", null, "false", FeatureValueType.Boolean, null, true, null));
        var asked = new List<string>();
        _providers.Add(new StubFeatureValueProvider("Global", 100, requiresKey: false, resolve: name =>
        {
            asked.Add(name);
            return name == "Orders.AdvancedSearch" ? "true" : null;
        }));
        var usage = new Mock<IFeatureUsageService>();
        var checker = new FeatureChecker(_featureManagerMock.Object, _providers, usage.Object);

        var result = await checker.IsEnabledAsync("orders.advancedSearch");

        result.ShouldBeTrue();
        asked.ShouldBe(["Orders.AdvancedSearch"]);
        usage.Verify(u => u.RecordUsageAsync("Orders.AdvancedSearch", true, "FeatureChecker"), Times.Once);
    }

    /// <summary>
    /// Boolean 功能解析不出布尔值时按关闭处理：默认值写成 "0" / "off" 意图关闭，
    /// 不能因为「非空即开启」把 [RequireFeature] 守护的端点对所有人放行。
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("no")]
    [InlineData("disabled")]
    public async Task IsEnabledAsync_BooleanFeatureWithUnparsableValue_FailsClosed(string value)
    {
        SetupFeature("Feature.Test", value, FeatureValueType.Boolean);
        var result = await _checker.IsEnabledAsync("Feature.Test");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_StringFeatureWithAnyValue_StaysEnabled()
    {
        SetupFeature("Feature.Variant", "v2", FeatureValueType.String);
        var result = await _checker.IsEnabledAsync("Feature.Variant");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_NonBooleanWithValue_ReturnsTrue()
    {
        SetupFeature("Feature.MaxUsers", "100", FeatureValueType.Integer);
        var result = await _checker.IsEnabledAsync("Feature.MaxUsers");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task GetValueAsync_IntegerFeature_ReturnsValue()
    {
        SetupFeature("Feature.MaxUsers", "42", FeatureValueType.Integer);
        var result = await _checker.GetValueAsync<int>("Feature.MaxUsers");
        result.ShouldBe(42);
    }

    [Fact]
    public async Task GetValueAsync_InvalidType_ReturnsNull()
    {
        SetupFeature("Feature.Name", "hello", FeatureValueType.String);
        var result = await _checker.GetValueAsync<int>("Feature.Name");
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetValueAsync_String_ReturnsDefaultValue()
    {
        SetupFeature("Feature.Theme", "dark", FeatureValueType.String);
        var result = await _checker.GetValueAsync("Feature.Theme");
        result.ShouldBe("dark");
    }

    [Fact]
    public async Task GetValueAsync_ProviderOverridesDefault()
    {
        SetupFeature("Feature.Test", "false");

        var providerMock = new Mock<IFeatureValueProvider>();
        providerMock.Setup(p => p.Name).Returns("Test");
        providerMock.Setup(p => p.Priority).Returns(100);
        providerMock.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("true");

        _providers.Add(providerMock.Object);
        var checker = CreateChecker();

        var result = await checker.IsEnabledAsync("Feature.Test");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task GetValueAsync_HigherPriorityProviderWins()
    {
        SetupFeature("Feature.Test", "default");

        var lowProvider = new Mock<IFeatureValueProvider>();
        lowProvider.Setup(p => p.Name).Returns("Low");
        lowProvider.Setup(p => p.Priority).Returns(100);
        lowProvider.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("low-value");

        var highProvider = new Mock<IFeatureValueProvider>();
        highProvider.Setup(p => p.Name).Returns("High");
        highProvider.Setup(p => p.Priority).Returns(200);
        highProvider.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("high-value");

        _providers.Add(lowProvider.Object);
        _providers.Add(highProvider.Object);
        var checker = CreateChecker();

        var result = await checker.GetValueAsync("Feature.Test");
        result.ShouldBe("high-value");
    }

    [Fact]
    public async Task GetValueAsync_TenantUnset_FallsThroughToGlobal_BeforeDefault()
    {
        // The single-tenant case: the tenant provider never resolves, the deployment-wide
        // value must still beat the definition default.
        SetupFeature("Feature.Test", "false");

        var tenant = new Mock<IFeatureValueProvider>();
        tenant.Setup(p => p.Name).Returns("Tenant");
        tenant.Setup(p => p.Priority).Returns(200);
        tenant.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync((string?)null);

        var global = new Mock<IFeatureValueProvider>();
        global.Setup(p => p.Name).Returns("Global");
        global.Setup(p => p.Priority).Returns(100);
        global.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("true");

        _providers.Add(global.Object);
        _providers.Add(tenant.Object);
        var checker = CreateChecker();

        (await checker.IsEnabledAsync("Feature.Test")).ShouldBeTrue();
        tenant.Verify(p => p.GetOrNullAsync("Feature.Test"), Times.Once);
    }

    [Fact]
    public async Task GetValueAsync_TenantSet_ShadowsGlobal()
    {
        SetupFeature("Feature.Test", "false");

        var tenant = new Mock<IFeatureValueProvider>();
        tenant.Setup(p => p.Name).Returns("Tenant");
        tenant.Setup(p => p.Priority).Returns(200);
        tenant.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("false");

        var global = new Mock<IFeatureValueProvider>();
        global.Setup(p => p.Name).Returns("Global");
        global.Setup(p => p.Priority).Returns(100);
        global.Setup(p => p.GetOrNullAsync("Feature.Test")).ReturnsAsync("true");

        _providers.Add(global.Object);
        _providers.Add(tenant.Object);
        var checker = CreateChecker();

        (await checker.IsEnabledAsync("Feature.Test")).ShouldBeFalse();
        // Resolution stops at the first hit; the lower-priority provider is never consulted.
        global.Verify(p => p.GetOrNullAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IsAllEnabledAsync_AllEnabled_ReturnsTrue()
    {
        SetupFeature("Feature.A", "true");
        SetupFeature("Feature.B", "true");

        var result = await ((IFeatureChecker)_checker).IsAllEnabledAsync("Feature.A", "Feature.B");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task IsAllEnabledAsync_OneDisabled_ReturnsFalse()
    {
        SetupFeature("Feature.A", "true");
        SetupFeature("Feature.B", "false");

        var result = await ((IFeatureChecker)_checker).IsAllEnabledAsync("Feature.A", "Feature.B");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task IsAnyEnabledAsync_OneEnabled_ReturnsTrue()
    {
        SetupFeature("Feature.A", "false");
        SetupFeature("Feature.B", "true");

        var result = await ((IFeatureChecker)_checker).IsAnyEnabledAsync("Feature.A", "Feature.B");
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task IsAnyEnabledAsync_NoneEnabled_ReturnsFalse()
    {
        SetupFeature("Feature.A", "false");
        SetupFeature("Feature.B", "false");

        var result = await ((IFeatureChecker)_checker).IsAnyEnabledAsync("Feature.A", "Feature.B");
        result.ShouldBeFalse();
    }
}
