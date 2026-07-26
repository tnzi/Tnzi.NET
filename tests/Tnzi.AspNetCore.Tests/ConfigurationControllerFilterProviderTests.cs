using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Tnzi.AspNetCore.Mvc.Conventions;

namespace Tnzi.AspNetCore.Tests;

public class ConfigurationControllerFilterProviderTests
{
    [Fact]
    public void NoFilters_AllControllersRetained()
    {
        // Arrange
        var options = new ControllerFilterOptions();
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Equal(2, context.Result.Controllers.Count);
    }

    [Fact]
    public void DisabledControllers_WildcardAdmin_RemovesAdminControllers()
    {
        // Arrange
        var options = new ControllerFilterOptions { DisabledControllers = ["*Admin*"] };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController), typeof(BazController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Equal(2, context.Result.Controllers.Count);
        Assert.All(context.Result.Controllers, c => Assert.DoesNotContain("Admin", c.ControllerType.Name));
    }

    [Fact]
    public void DisabledControllers_PrefixWildcard_MatchesPrecisely()
    {
        // Arrange
        var options = new ControllerFilterOptions { DisabledControllers = ["DefaultAuth*"] };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(DefaultAuthController), typeof(DefaultAuthAdminController), typeof(DefaultStorageController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Single(context.Result.Controllers);
        Assert.Equal(nameof(DefaultStorageController), context.Result.Controllers[0].ControllerType.Name);
    }

    [Fact]
    public void DisabledAssemblies_RemovesEntireAssembly()
    {
        // Arrange - 所有测试 Controller 都在当前程序集
        var assemblyName = typeof(FooController).Assembly.GetName().Name!;
        var options = new ControllerFilterOptions { DisabledAssemblies = [assemblyName] };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Empty(context.Result.Controllers);
    }

    [Fact]
    public void ControllerPredicate_CustomFilter_Works()
    {
        // Arrange - 只保留名称以 "Foo" 开头的 Controller
        var options = new ControllerFilterOptions
        {
            ControllerPredicate = type => type.Name.StartsWith("Foo", StringComparison.OrdinalIgnoreCase)
        };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController), typeof(BazController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Single(context.Result.Controllers);
        Assert.Equal(nameof(FooController), context.Result.Controllers[0].ControllerType.Name);
    }

    [Fact]
    public void MixedRules_BothApplied()
    {
        // Arrange - 按程序集禁用不会命中（使用不存在的程序集名），按名称禁用 Admin
        var options = new ControllerFilterOptions
        {
            DisabledAssemblies = ["NonExistent.Assembly"],
            DisabledControllers = ["*Admin*"]
        };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController), typeof(BazController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Equal(2, context.Result.Controllers.Count);
        Assert.DoesNotContain(context.Result.Controllers, c => c.ControllerType.Name.Contains("Admin"));
    }

    [Fact]
    public void DisabledControllers_CaseInsensitive()
    {
        // Arrange - 小写模式应匹配 PascalCase 的 Controller 名称
        var options = new ControllerFilterOptions { DisabledControllers = ["*admin*"] };
        var provider = new ConfigurationControllerFilterProvider(options);
        var context = CreateContext(typeof(FooController), typeof(BarAdminController));

        // Act
        provider.OnProvidersExecuting(context);

        // Assert
        Assert.Single(context.Result.Controllers);
        Assert.Equal(nameof(FooController), context.Result.Controllers[0].ControllerType.Name);
    }

    #region Helpers

    private static ApplicationModelProviderContext CreateContext(params Type[] controllerTypes)
    {
        var typeInfos = controllerTypes.Select(t => t.GetTypeInfo()).ToList();
        var context = new ApplicationModelProviderContext(typeInfos);

        // ApplicationModelProviderContext.Result 是只读的，但 Controllers 列表可修改
        // 需要先清空默认生成的条目，然后手动添加
        context.Result.Controllers.Clear();
        foreach (var type in controllerTypes)
        {
            var controllerModel = new ControllerModel(type.GetTypeInfo(), Array.Empty<object>())
            {
                ControllerName = type.Name
            };
            context.Result.Controllers.Add(controllerModel);
        }

        return context;
    }

    // 测试用 Controller 类型
    private class FooController : Controller { }
    private class BarAdminController : Controller { }
    private class BazController : Controller { }
    private class DefaultAuthController : Controller { }
    private class DefaultAuthAdminController : Controller { }
    private class DefaultStorageController : Controller { }

    #endregion
}
