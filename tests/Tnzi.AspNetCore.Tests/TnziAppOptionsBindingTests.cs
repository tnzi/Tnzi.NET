using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// TnziApp 的 TnziOptions 注册语义：绑定 "Tnzi" 配置节（appsettings 可按环境覆盖），
/// 代码回调优先于配置节；AutoInitializeDatabase 默认开启。
/// </summary>
public class TnziAppOptionsBindingTests
{
    [Fact]
    public async Task TnziOptions_ShouldBindFromTnziConfigurationSection()
    {
        var app = await CreateAppAsync(new Dictionary<string, string?>
        {
            ["Tnzi:AutoInitializeDatabase"] = "false",
            ["Tnzi:LogModuleLoading"] = "false"
        });

        try
        {
            var options = app.Services.GetRequiredService<IOptions<TnziOptions>>().Value;
            Assert.False(options.AutoInitializeDatabase);
            Assert.False(options.LogModuleLoading);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TnziOptions_CodeCallback_ShouldWinOverConfigurationSection()
    {
        var app = await CreateAppAsync(
            new Dictionary<string, string?> { ["Tnzi:AutoInitializeDatabase"] = "true" },
            options => options.AutoInitializeDatabase = false);

        try
        {
            var options = app.Services.GetRequiredService<IOptions<TnziOptions>>().Value;
            Assert.False(options.AutoInitializeDatabase);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TnziOptions_AutoInitializeDatabase_ShouldDefaultToTrue()
    {
        var app = await CreateAppAsync(new Dictionary<string, string?>());

        try
        {
            var options = app.Services.GetRequiredService<IOptions<TnziOptions>>().Value;
            Assert.True(options.AutoInitializeDatabase);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task TnziOptions_ProductionGates_ShouldDefaultNullAndBind()
    {
        var appDefault = await CreateAppAsync(new Dictionary<string, string?>());
        try
        {
            var options = appDefault.Services.GetRequiredService<IOptions<TnziOptions>>().Value;
            // Unset by default → fall back to the legacy SkipDatabaseInitInProduction switch.
            Assert.Null(options.ApplyMigrationsInProduction);
            Assert.Null(options.SeedInProduction);
        }
        finally
        {
            await appDefault.DisposeAsync();
        }

        var appBound = await CreateAppAsync(new Dictionary<string, string?>
        {
            ["Tnzi:ApplyMigrationsInProduction"] = "true",
            ["Tnzi:SeedInProduction"] = "false",
        });
        try
        {
            var options = appBound.Services.GetRequiredService<IOptions<TnziOptions>>().Value;
            Assert.True(options.ApplyMigrationsInProduction);
            Assert.False(options.SeedInProduction);
        }
        finally
        {
            await appBound.DisposeAsync();
        }
    }

    private static async Task<WebApplication> CreateAppAsync(
        Dictionary<string, string?> settings, Action<TnziOptions>? configureOptions = null)
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

        settings["Database:AutoDiscoverDbContexts"] = "false";
        settings["AspNetCore:EnableForwardedHeaders"] = "false";
        builder.Configuration.AddInMemoryCollection(settings);

        return await TnziApp.CreateAsync<TestHostingLiteStartupModule>(builder, configureOptions);
    }
}
