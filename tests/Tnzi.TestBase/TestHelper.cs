
namespace Tnzi.TestBase;

/// <summary>
/// 测试辅助类，提供测试常用的辅助方法
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// 为 SQLite 数据库应用 UTC 时间转换器
    /// 用于解决 SQLite 不支持 DateTime.Kind 的问题
    /// </summary>
    /// <param name="modelBuilder">模型构建器</param>
    /// <param name="providerName">数据库提供程序名称</param>
    public static void ApplySqliteUtcDateTimeConverter(ModelBuilder modelBuilder, string? providerName)
    {
        if (providerName != "Microsoft.EntityFrameworkCore.Sqlite") return;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var properties = entityType.ClrType.GetProperties()
                .Where(p => p.PropertyType == typeof(DateTime) || p.PropertyType == typeof(DateTime?));

            foreach (var property in properties)
            {
                if (property.PropertyType == typeof(DateTime))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion(new ValueConverter<DateTime, DateTime>(
                            v => v.ToUniversalTime(),
                            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }
                else if (property.PropertyType == typeof(DateTime?))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name)
                        .HasConversion(new ValueConverter<DateTime?, DateTime?>(
                            v => v.HasValue ? v.Value.ToUniversalTime() : v,
                            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
                }
            }
        }
    }

    /// <summary>
    /// 获取默认的测试用户 ID
    /// </summary>
    public static readonly Guid DefaultTestUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// 获取默认的测试用户名
    /// </summary>
    public static readonly string DefaultTestUserName = "TestUser";

    /// <summary>
    /// 获取默认的测试用户角色
    /// </summary>
    public static readonly string[] DefaultTestUserRoles = new[] { "Admin" };
}