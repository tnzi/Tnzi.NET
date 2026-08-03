using Microsoft.EntityFrameworkCore.Design;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;

namespace Tnzi.Identity.IntegrationTests;

/// <summary>
/// 设计时 DbContext 工厂，用于生成迁移并支持 MultiTenancy 开关验证。
/// </summary>
public class TestIdentityDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TestIdentityDbContext>
{
    public TestIdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestIdentityDbContext>();
        optionsBuilder.UseSqlite("Data Source=test_identity_migrations.db");

        var multiTenancyEnabled = bool.TryParse(
            Environment.GetEnvironmentVariable("MultiTenancy__Enabled"),
            out var enabled) && enabled;

        var multiTenancyOptions = Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions
        {
            Enabled = multiTenancyEnabled
        });

        return new TestIdentityDbContext(
            optionsBuilder.Options,
            new DesignTimeCurrentUser(),
            multiTenancyOptions);
    }
}
