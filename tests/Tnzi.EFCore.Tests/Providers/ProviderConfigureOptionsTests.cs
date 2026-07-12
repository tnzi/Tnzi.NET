namespace Tnzi.EFCore.Tests.Providers;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Tnzi.EFCore.Providers;

/// <summary>
/// Task 3：provider 连接级选项（重试策略 / 命令超时）配置绑定与应用测试。
/// 不需要真连数据库：用真实 SQLite provider 检查 built options 中的 RelationalOptionsExtension。
/// </summary>
public class ProviderConfigureOptionsTests
{
    // ---- 配置绑定 ----

    [Fact]
    public void DbContextConfiguration_ShouldBind_RetryAndTimeoutFields()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Database:DbContexts:0:Name"] = "Default",
            ["Database:DbContexts:0:Provider"] = "Sqlite",
            ["Database:DbContexts:0:ConnectionString"] = "Data Source=app.db",
            ["Database:DbContexts:0:EnableRetryOnFailure"] = "true",
            ["Database:DbContexts:0:MaxRetryCount"] = "5",
            ["Database:DbContexts:0:CommandTimeout"] = "60",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();

        var config = configuration.GetSection("Database:DbContexts:0").Get<DbContextConfiguration>();

        Assert.NotNull(config);
        Assert.True(config!.EnableRetryOnFailure);
        Assert.Equal(5, config.MaxRetryCount);
        Assert.Equal(60, config.CommandTimeout);
    }

    [Fact]
    public void DbContextConfiguration_Defaults_ShouldBeConservative()
    {
        var config = new DbContextConfiguration();

        Assert.False(config.EnableRetryOnFailure);
        Assert.Null(config.MaxRetryCount);
        Assert.Null(config.CommandTimeout);
    }

    [Fact]
    public void BuildProviderConfigureOptions_ShouldMapFields()
    {
        var config = new DbContextConfiguration
        {
            EnableRetryOnFailure = true,
            MaxRetryCount = 4,
            CommandTimeout = 30
        };

        var options = config.BuildProviderConfigureOptions();

        Assert.True(options.EnableRetryOnFailure);
        Assert.Equal(4, options.MaxRetryCount);
        Assert.Equal(30, options.CommandTimeout);
        Assert.True(options.HasAny);
        Assert.True(options.ConflictsWithUnitOfWorkTransaction);
    }

    [Fact]
    public void DbProviderConfigureOptions_None_ShouldHaveNoEffect()
    {
        Assert.False(DbProviderConfigureOptions.None.HasAny);
        Assert.False(DbProviderConfigureOptions.None.ConflictsWithUnitOfWorkTransaction);
    }

    // ---- 配置器应用选项（真实 SQLite provider，不连库）----

    [Fact]
    public void Configure_Sqlite_ShouldApply_CommandTimeout()
    {
        var builder = new DbContextOptionsBuilder();

        DatabaseProviderFactory.Configure(
            builder,
            "Data Source=:memory:",
            DatabaseProvider.Sqlite,
            new DbProviderConfigureOptions { CommandTimeout = 45 });

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault();
        Assert.NotNull(relational);
        Assert.Equal(45, relational!.CommandTimeout);
    }

    [Fact]
    public void Configure_Sqlite_WithRetry_ShouldSkipRetryButKeepTimeout()
    {
        var builder = new DbContextOptionsBuilder();

        // SQLite 无重试策略：EnableRetryOnFailure 应被静默跳过（不抛异常），CommandTimeout 仍生效
        var ex = Record.Exception(() =>
            DatabaseProviderFactory.Configure(
                builder,
                "Data Source=:memory:",
                DatabaseProvider.Sqlite,
                new DbProviderConfigureOptions { EnableRetryOnFailure = true, MaxRetryCount = 3, CommandTimeout = 20 }));

        Assert.Null(ex);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().First();
        Assert.Equal(20, relational.CommandTimeout);
        // 未误设重试型 execution strategy
        Assert.Null(relational.ExecutionStrategyFactory);
    }

    [Fact]
    public void Configure_Sqlite_WithNullOptions_ShouldStillBuildValidOptions()
    {
        var builder = new DbContextOptionsBuilder();

        var ex = Record.Exception(() =>
            DatabaseProviderFactory.Configure(builder, "Data Source=:memory:", DatabaseProvider.Sqlite, options: null));

        Assert.Null(ex);
        Assert.NotNull(builder.Options.Extensions.OfType<RelationalOptionsExtension>().FirstOrDefault());
    }
}
