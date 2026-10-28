namespace Tnzi.EFCore.Tests;

/// <summary>
/// 分页查询的 <c>orderBy</c> 只接受 EF 模型里已映射的标量属性；其余名字被跳过，不变成 500。
/// </summary>
/// <remarks>
/// ★ 白名单此前取自 CLR 公开属性：每个实体都继承的 <c>DomainEvents</c>（不映射）、导航、<c>[NotMapped]</c> 计算属性
/// 都能通过，生成 <c>OrderBy(e =&gt; (object)e.DomainEvents)</c> 后 EF 执行时翻译失败抛
/// <see cref="InvalidOperationException"/>。把请求里的 <c>PagedQuery</c> 直接交给仓储分页的端点，
/// 一个 <c>orderBy=DomainEvents</c> 就是 500，而代码的意图是跳过非法属性。
/// </remarks>
public class RepositoryOrderByWhitelistTests : EFCoreTestBase
{
    private readonly IRepository<TestProduct, Guid> _repository;

    public RepositoryOrderByWhitelistTests()
    {
        _repository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(DbContext, null, ServiceProvider);
    }

    [Fact]
    public async Task UnmappedProperty_IsSkipped_AndThePageFallsBackToKeyOrder()
    {
        await SeedAsync();

        var page = await _repository.GetPagedListAsync(new PagedQuery { PageIndex = 1, PageSize = 10, OrderBy = "DomainEvents" });

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(page.Items.Select(p => p.Id).OrderBy(id => id.ToString()), page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task UnmappedProperty_AmongValidOnes_DoesNotBreakTheValidOrdering()
    {
        await SeedAsync();

        var page = await _repository.GetPagedListAsync(new PagedQuery { PageIndex = 1, PageSize = 10, OrderBy = "DomainEvents DESC, Price DESC" });

        Assert.Equal([30m, 20m, 10m], page.Items.Select(p => p.Price));
    }

    [Fact]
    public async Task MappedProperty_IsMatchedCaseInsensitively()
    {
        await SeedAsync();

        var page = await _repository.GetPagedListAsync(new PagedQuery { PageIndex = 1, PageSize = 10, OrderBy = "name desc" });

        Assert.Equal(["c", "b", "a"], page.Items.Select(p => p.Name));
    }

    private async Task SeedAsync()
    {
        await _repository.InsertManyAsync(
        [
            new TestProduct { Name = "b", Price = 20m, Stock = 1 },
            new TestProduct { Name = "a", Price = 10m, Stock = 1 },
            new TestProduct { Name = "c", Price = 30m, Stock = 1 },
        ]);
    }
}
