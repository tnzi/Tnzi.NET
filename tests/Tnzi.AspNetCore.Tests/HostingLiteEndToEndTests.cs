using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Authorization;
using Tnzi.Hosting;
using Tnzi.Identity;
using Tnzi.Identity.Services;
using Tnzi.Storage;
using Tnzi.System;
using Tnzi.Modules;
using Tnzi.AI;
using Tnzi.Audit;
using Tnzi.Feature;
using Tnzi.Notification;
using Tnzi.Notification.Services;
using Tnzi.Storage.Services;
using Tnzi.Template;
using Tnzi.Storage.Providers;
using Tnzi.System.Entities;

namespace Tnzi.AspNetCore.Tests;

public class HostingLiteEndToEndTests
{
    [Fact]
    public async Task HostingLiteModule_Swagger_ShouldExposeExpectedCapabilities()
    {
        await AssertSwaggerPathsAsync<TestHostingLiteStartupModule>(
            CreateBaseSettings(("Localization:Enabled", "true")),
            requiredDocuments:
            [
                ("system", ["/api/localization/cultures"]),
                // SystemModule（lite 依赖）的用户侧 DefaultAppearanceController 贡献 user 组：
                // 任何已登录用户读取全局管理端主题。auth 组仍需 Identity，故 lite 无 auth。
                ("user", ["/api/appearance/admin-theme"]),
            ],
            forbiddenDocuments: ["auth"],
            forbiddenPathsInDocuments: []);
    }

    [Fact]
    public async Task HostingLiteModule_ShouldServeDiscoveredControllerThroughApiPrefix()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false"
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync("/api/e2e/hello");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Success", body);
            Assert.Contains("hello", body);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ControllerFilter_DisabledControllers_ShouldRemoveMatchedControllerFromEndpoints()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:ControllerFilter:DisabledControllers:0"] = "TestHostingLiteController"
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync("/api/e2e/hello");

            Assert.Equal(global::System.Net.HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PathBase_ShouldStripPrefixAndRouteCorrectly()
    {
        // 模拟：反向代理不剥离路径，或本地使用同一配置验证 PathBase 行为
        // PathBase="/subapp" + ApiPathPrefix="" → 路由为 /e2e/hello
        // 客户端访问 /subapp/e2e/hello → UsePathBase 剥离 /subapp → Path=/e2e/hello → 匹配 ✓
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:PathBase"] = "/subapp",
            ["AspNetCore:ApiPathPrefix"] = ""
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            // 带 PathBase 前缀访问 → 应成功
            var response = await client.GetAsync("/subapp/e2e/hello");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Success", body);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task PathBase_WithApiPathPrefix_ShouldCombineCorrectly()
    {
        // 模拟：应用挂在 /myapp 子路径下，API 保留 /api 前缀
        // PathBase="/myapp" + ApiPathPrefix="api" → 路由为 /api/e2e/hello
        // 客户端访问 /myapp/api/e2e/hello → UsePathBase 剥离 /myapp → Path=/api/e2e/hello → 匹配 ✓
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:PathBase"] = "/myapp",
            ["AspNetCore:ApiPathPrefix"] = "api"
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            // 带 PathBase + ApiPathPrefix 访问 → 应成功
            var response = await client.GetAsync("/myapp/api/e2e/hello");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Success", body);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task HostingLiteModule_ShouldServeDefaultLocalizationController()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["Localization:Enabled"] = "true",
            ["Localization:DefaultCulture"] = "en",
            ["Localization:SupportedCultures:0"] = "en",
            ["Localization:SupportedCultures:1"] = "zh-CN"
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();
            var response = await client.GetAsync("/api/localization/cultures");

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"name\":\"en\"", body);
            Assert.Contains("\"name\":\"zh-CN\"", body);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task ControllerFilter_DisabledControllers_ShouldNotRenderFilteredSwaggerGroup()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["AspNetCore:ControllerFilter:DisabledControllers:0"] = "HiddenGroupController",
            ["Swagger:Enabled"] = "true",
            ["Swagger:EnableXmlComments"] = "false"
        });

        var app = await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();

            var visibleSwagger = await client.GetAsync("/swagger/visible/swagger.json");
            var hiddenSwagger = await client.GetAsync("/swagger/hidden/swagger.json");

            Assert.Equal(global::System.Net.HttpStatusCode.OK, visibleSwagger.StatusCode);
            Assert.Equal(global::System.Net.HttpStatusCode.NotFound, hiddenSwagger.StatusCode);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task HostingAuthModule_Swagger_ShouldExposeExpectedCapabilities()
    {
        await AssertSwaggerPathsAsync<TestHostingAuthStartupModule>(
            CreateBaseSettings(("Localization:Enabled", "true")),
            requiredDocuments:
            [
                ("auth", ["/api/auth/login"]),
                ("user", ["/api/users/profile"]),
                ("system", ["/api/localization/cultures"]),
            ],
            forbiddenDocuments: [],
            forbiddenPathsInDocuments:
            [
                ("user", ["/api/files/upload", "/api/notifications/inbox"]),
            ]);
    }

    [Fact]
    public async Task HostingStorageModule_Swagger_ShouldExposeExpectedCapabilities()
    {
        await AssertSwaggerPathsAsync<TestHostingStorageStartupModule>(
            CreateBaseSettings(("Localization:Enabled", "true")),
            requiredDocuments:
            [
                ("user", ["/api/files/upload"]),
                ("system", ["/api/localization/cultures"]),
            ],
            forbiddenDocuments: ["auth"],
            forbiddenPathsInDocuments:
            [
                ("user", ["/api/users/profile", "/api/notifications/inbox"]),
            ]);
    }

    [Fact]
    public async Task HostingModule_Swagger_ShouldExposeExpectedCapabilities()
    {
        await AssertSwaggerPathsAsync<TestHostingFullStartupModule>(
            CreateBaseSettings(("Localization:Enabled", "true")),
            requiredDocuments:
            [
                ("auth", ["/api/auth/login"]),
                ("user", ["/api/users/profile", "/api/files/upload", "/api/notifications/inbox"]),
                ("system", ["/api/localization/cultures"]),
            ],
            forbiddenDocuments: [],
            forbiddenPathsInDocuments: []);
    }

    private static Dictionary<string, string?> CreateBaseSettings(params (string Key, string? Value)[] additionalSettings)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:AutoDiscoverDbContexts"] = "false",
            ["AspNetCore:EnableForwardedHeaders"] = "false",
            ["Swagger:Enabled"] = "true",
            ["Swagger:EnableXmlComments"] = "false",
            ["AI:DefaultProvider"] = "Test",
            ["AI:Providers:Test:Enabled"] = "true",
            ["AI:Providers:Test:ApiKey"] = "test-key",
            ["AI:Providers:Test:DefaultModel"] = "test-model",
            ["AI:Providers:DeepSeek:Enabled"] = "false"
        };

        foreach (var (key, value) in additionalSettings)
        {
            settings[key] = value;
        }

        return settings;
    }

    private static async Task AssertSwaggerPathsAsync<TStartupModule>(
        Dictionary<string, string?> settings,
        (string Group, string[] Paths)[] requiredDocuments,
        string[] forbiddenDocuments,
        (string Group, string[] Paths)[] forbiddenPathsInDocuments)
        where TStartupModule : ITnziModule
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });

        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = false;
            options.ValidateScopes = false;
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);

        var app = await TnziApp.CreateAsync<TStartupModule>(builder);
        await app.StartAsync();

        try
        {
            var client = app.GetTestClient();
            foreach (var (group, paths) in requiredDocuments)
            {
                var response = await client.GetAsync($"/swagger/{group}/swagger.json");
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();
                foreach (var path in paths)
                {
                    Assert.Contains($"\"{path}\"", body);
                }
            }

            foreach (var group in forbiddenDocuments)
            {
                var response = await client.GetAsync($"/swagger/{group}/swagger.json");
                Assert.Equal(global::System.Net.HttpStatusCode.NotFound, response.StatusCode);
            }

            foreach (var (group, paths) in forbiddenPathsInDocuments)
            {
                var response = await client.GetAsync($"/swagger/{group}/swagger.json");
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync();
                foreach (var path in paths)
                {
                    Assert.DoesNotContain($"\"{path}\"", body);
                }
            }
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}

[DependsOn(typeof(SystemModule))]
public sealed class TestHostingLiteStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);

        return base.ConfigureServicesAsync(context);
    }
}

[DependsOn(typeof(IdentityModule), typeof(AuthorizationModule), typeof(SystemModule))]
public sealed class TestHostingAuthStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);
        HostingTestSetup.RegisterAuthMocks(context.Services);
        return base.ConfigureServicesAsync(context);
    }
}

[DependsOn(typeof(StorageModule), typeof(SystemModule))]
public sealed class TestHostingStorageStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);
        HostingTestSetup.RegisterStorageMocks(context.Services);
        return base.ConfigureServicesAsync(context);
    }
}

[DependsOn(
    typeof(IdentityModule), typeof(AuthorizationModule), typeof(StorageModule),
    typeof(NotificationModule), typeof(TemplateModule), typeof(AIModule),
    typeof(AuditModule), typeof(SystemModule), typeof(FeatureModule))]
public sealed class TestHostingFullStartupModule : HostingModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        HostingTestSetup.RegisterCommonMocks(context.Services);
        HostingTestSetup.RegisterAuthMocks(context.Services);
        HostingTestSetup.RegisterStorageMocks(context.Services);
        HostingTestSetup.RegisterNotificationMocks(context.Services);
        return base.ConfigureServicesAsync(context);
    }
}

internal static class HostingTestSetup
{
    public static void RegisterCommonMocks(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<INotificationService>());
        services.AddScoped(_ => Mock.Of<IFileStorage>());
        services.AddScoped(_ => Mock.Of<IRepository<Setting, Guid>>());
        services.AddScoped(_ => Mock.Of<IRepository<AccessLog, Guid>>());
    }

    public static void RegisterAuthMocks(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<ITwoFactorService>());
        services.AddScoped(_ => Mock.Of<IAuthService>());
        services.AddScoped(_ => Mock.Of<IRegistrationService>());
        services.AddScoped(_ => Mock.Of<IPasswordService>());
        services.AddScoped(_ => Mock.Of<IUserService>());
    }

    public static void RegisterStorageMocks(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<IFileStorageService>());
        services.AddScoped(_ => Mock.Of<IFileShareService>());
        services.AddScoped(_ => Mock.Of<IFileVersionService>());
        services.AddScoped(_ => Mock.Of<IFileChunkUploadService>());
    }

    public static void RegisterNotificationMocks(IServiceCollection services)
    {
        services.AddScoped(_ => Mock.Of<IUserNotificationService>());
    }
}

[ApiController]
[Route("e2e/hello")]
public sealed class TestHostingLiteController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get()
    {
        return Ok("hello", "Success");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "visible")]
[Route("e2e/swagger-visible")]
public sealed class VisibleGroupController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get()
    {
        return Ok("visible", "Success");
    }
}

[ApiController]
[ApiExplorerSettings(GroupName = "hidden")]
[Route("e2e/swagger-hidden")]
public sealed class HiddenGroupController : ApiControllerBase
{
    [HttpGet]
    public ApiResult<string> Get()
    {
        return Ok("hidden", "Success");
    }
}
