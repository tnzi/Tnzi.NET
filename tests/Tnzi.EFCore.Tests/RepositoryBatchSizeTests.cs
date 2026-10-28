using Microsoft.Extensions.Options;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// <c>EFCore:BatchSize</c> 不影响 <c>InsertManyAsync</c> / <c>UpdateManyAsync</c> / <c>DeleteManyAsync</c>：
/// 实体本就全部在内存里，一次保存全部写出。
/// </summary>
/// <remarks>
/// 这三处此前按 <c>BatchSize</c> 分块逐次 <c>AddRange</c> / <c>UpdateRange</c> / <c>RemoveRange</c>，再统一保存一次：
/// 跟踪状态与一次调用完全相同，分块什么都没省，却让人以为调小它能减少内存或往返。
/// 批次大小只服务带 <c>[FileField]</c> 的按条件硬删的分批加载。
/// </remarks>
public class RepositoryBatchSizeTests : EFCoreTestBase
{
    private readonly IRepository<TestProduct, Guid> _repository;

    public RepositoryBatchSizeTests()
    {
        var options = new OptionsWrapper<EFCoreOptions>(new EFCoreOptions { BatchSize = 2 });
        _repository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(DbContext, options, ServiceProvider);
    }

    [Fact]
    public async Task ManyOperations_LargerThanTheBatchSize_WriteEveryEntity()
    {
        var products = Enumerable.Range(1, 5).Select(i => new TestProduct { Name = $"p{i}", Price = i, Stock = i }).ToList();

        await _repository.InsertManyAsync(products);
        Assert.Equal(5, await DbContext.Products.AsNoTracking().CountAsync());

        foreach (var product in products)
        {
            product.Stock = 100;
        }

        await _repository.UpdateManyAsync(products);
        Assert.Equal(5, await DbContext.Products.AsNoTracking().CountAsync(p => p.Stock == 100));

        await _repository.DeleteManyAsync(products);
        Assert.Equal(0, await DbContext.Products.AsNoTracking().CountAsync());
    }
}
