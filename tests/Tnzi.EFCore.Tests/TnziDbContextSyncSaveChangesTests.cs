namespace Tnzi.EFCore.Tests;

using Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// TnziDbContext 同步 SaveChanges 禁用测试。
/// 框架的审计填充 / ID 生成 / 软删除转换管线只拦截了 SaveChangesAsync；
/// 同步 SaveChanges() 会绕过全部横切逻辑（软删实体被物理删除、审计字段与 ID 不填充），
/// 因此显式抛 NotSupportedException 锁定。
/// </summary>
public class TnziDbContextSyncSaveChangesTests : EFCoreTestBase
{
    [Fact]
    public void SaveChanges_ShouldThrowNotSupported_AndGuideToAsync()
    {
        DbContext.Products.Add(new TestProduct { Name = "sync-1", Price = 1m, Stock = 1 });

        var ex = Assert.Throws<NotSupportedException>(() => DbContext.SaveChanges());
        Assert.Contains("SaveChangesAsync", ex.Message);
    }

    [Fact]
    public void SaveChanges_WithAcceptAllChangesOnSuccess_ShouldThrowNotSupported()
    {
        DbContext.Products.Add(new TestProduct { Name = "sync-2", Price = 1m, Stock = 1 });

        Assert.Throws<NotSupportedException>(() => DbContext.SaveChanges(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public async Task SaveChangesAsync_ShouldStillWork_AndGenerateId()
    {
        var product = new TestProduct { Name = "async-ok", Price = 1m, Stock = 1 };
        DbContext.Products.Add(product);

        var count = await DbContext.SaveChangesAsync();

        Assert.Equal(1, count);
        Assert.NotEqual(Guid.Empty, product.Id);
    }
}
