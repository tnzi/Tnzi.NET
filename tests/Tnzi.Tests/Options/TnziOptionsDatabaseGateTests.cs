namespace Tnzi.Tests.Options;

/// <summary>
/// <see cref="TnziOptions.ShouldApplyMigrations"/> / <see cref="TnziOptions.ShouldSeed"/> 的
/// 生产环境闸门语义：新选项（ApplyMigrationsInProduction / SeedInProduction）与旧
/// SkipDatabaseInitInProduction 的向后兼容叠加。
/// </summary>
public class TnziOptionsDatabaseGateTests
{
    [Fact]
    public void NonProduction_ShouldAlwaysRunBoth()
    {
        var options = new TnziOptions { SkipDatabaseInitInProduction = true };
        Assert.True(options.ShouldApplyMigrations(isProduction: false));
        Assert.True(options.ShouldSeed(isProduction: false));
    }

    [Fact]
    public void Production_LegacyDefault_ShouldSkipBoth()
    {
        // Existing appsettings that never touched the new options: default skip-all preserved.
        var options = new TnziOptions(); // Skip=true, new options null
        Assert.False(options.ShouldApplyMigrations(isProduction: true));
        Assert.False(options.ShouldSeed(isProduction: true));
    }

    [Fact]
    public void Production_LegacySkipFalse_ShouldRunBoth()
    {
        // Existing appsettings with SkipDatabaseInitInProduction=false: run-all preserved.
        var options = new TnziOptions { SkipDatabaseInitInProduction = false };
        Assert.True(options.ShouldApplyMigrations(isProduction: true));
        Assert.True(options.ShouldSeed(isProduction: true));
    }

    [Fact]
    public void Production_MigrateOnly_ShouldMigrateButNotSeed()
    {
        // New capability: migrate in production without seeding.
        var options = new TnziOptions
        {
            SkipDatabaseInitInProduction = true,
            ApplyMigrationsInProduction = true,
        };
        Assert.True(options.ShouldApplyMigrations(isProduction: true));
        Assert.False(options.ShouldSeed(isProduction: true));
    }

    [Fact]
    public void Production_MigrateAndSeedExplicit_ShouldRunBoth()
    {
        var options = new TnziOptions
        {
            SkipDatabaseInitInProduction = true,
            ApplyMigrationsInProduction = true,
            SeedInProduction = true,
        };
        Assert.True(options.ShouldApplyMigrations(isProduction: true));
        Assert.True(options.ShouldSeed(isProduction: true));
    }

    [Fact]
    public void Production_ExplicitFalse_ShouldOverrideLegacySwitch()
    {
        // Even with legacy Skip=false (which would run all), an explicit new option wins.
        var options = new TnziOptions
        {
            SkipDatabaseInitInProduction = false,
            ApplyMigrationsInProduction = false,
            SeedInProduction = false,
        };
        Assert.False(options.ShouldApplyMigrations(isProduction: true));
        Assert.False(options.ShouldSeed(isProduction: true));
    }
}
