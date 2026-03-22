
namespace Tnzi.Tests.Modules;

/// <summary>
/// 模块状态跟踪测试
/// </summary>
public class ModuleStatusTests
{
    [Fact]
    public void ModuleDescriptor_IsEnabled_ShouldBeFalseInitially()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);

        // Assert
        Assert.False(descriptor.IsEnabled);
        Assert.Null(descriptor.InitializationStartTime);
        Assert.Null(descriptor.InitializationEndTime);
    }

    [Fact]
    public void ModuleExtensions_IsEnabled_ShouldReturnCorrectValue()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);

        // 使用反射设置internal属性（仅用于测试）
        var isEnabledProperty = typeof(ModuleDescriptor).GetProperty("IsEnabled",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        isEnabledProperty?.SetValue(descriptor, true);

        // Act
        var isEnabled = descriptor.IsEnabled();

        // Assert
        Assert.True(isEnabled);
    }

    [Fact]
    public void ModuleExtensions_GetInitializationDuration_ShouldReturnNullWhenNotInitialized()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);

        // Act
        var duration = descriptor.GetInitializationDuration();

        // Assert
        Assert.Null(duration);
    }

    [Fact]
    public void ModuleExtensions_GetInitializationDuration_ShouldReturnCorrectDuration()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);
        var startTime = DateTime.UtcNow.AddSeconds(-2);
        var endTime = DateTime.UtcNow;

        // 使用反射设置internal属性（仅用于测试）
        var startTimeProperty = typeof(ModuleDescriptor).GetProperty("InitializationStartTime",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var endTimeProperty = typeof(ModuleDescriptor).GetProperty("InitializationEndTime",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        startTimeProperty?.SetValue(descriptor, startTime);
        endTimeProperty?.SetValue(descriptor, endTime);

        // Act
        var duration = descriptor.GetInitializationDuration();

        // Assert
        Assert.NotNull(duration);
        Assert.True(duration.Value.TotalSeconds >= 1.9 && duration.Value.TotalSeconds <= 2.1);
    }

    [Fact]
    public void ModuleExtensions_GetInitializationStartTime_ShouldReturnCorrectValue()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);
        var startTime = DateTime.UtcNow;

        // 使用反射设置internal属性（仅用于测试）
        var startTimeProperty = typeof(ModuleDescriptor).GetProperty("InitializationStartTime",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        startTimeProperty?.SetValue(descriptor, startTime);

        // Act
        var retrieved = descriptor.GetInitializationStartTime();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(startTime, retrieved.Value, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void ModuleExtensions_GetInitializationEndTime_ShouldReturnCorrectValue()
    {
        // Arrange
        var module = new TestModule();
        var descriptor = new ModuleDescriptor(typeof(TestModule), module);
        var endTime = DateTime.UtcNow;

        // 使用反射设置internal属性（仅用于测试）
        var endTimeProperty = typeof(ModuleDescriptor).GetProperty("InitializationEndTime",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        endTimeProperty?.SetValue(descriptor, endTime);

        // Act
        var retrieved = descriptor.GetInitializationEndTime();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(endTime, retrieved.Value, TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void ModuleExtensions_GetInitializationDuration_ShouldReturnNullForNonModuleDescriptor()
    {
        // Arrange
        IModuleDescriptor module = new TestModuleDescriptor();

        // Act
        var duration = module.GetInitializationDuration();

        // Assert
        Assert.Null(duration);
    }
}

/// <summary>
/// 测试模块
/// </summary>
public class TestModule : TnziCoreModule
{
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;

    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
        => Task.CompletedTask;

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
        => Task.CompletedTask;

    public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
        => Task.CompletedTask;
}

/// <summary>
/// 测试用的非ModuleDescriptor实现
/// </summary>
public class TestModuleDescriptor : IModuleDescriptor
{
    public Type Type => typeof(TestModule);
    public ITnziModule Instance => new TestModule();
    public Assembly Assembly => typeof(TestModule).Assembly;
    public IReadOnlyList<IModuleDescriptor> Dependencies => Array.Empty<IModuleDescriptor>();
    public bool IsEnabled => false;
    public ModuleInitializationState InitializationState => ModuleInitializationState.NotStarted;
    public Exception? InitializationError => null;
    public ModuleManifest Manifest => ModuleManifest.Empty;

    public void AddDependency(IModuleDescriptor descriptor) { }
}