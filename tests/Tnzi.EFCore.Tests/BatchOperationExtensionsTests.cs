// -----------------------------------------------------------------------
// <copyright file="BatchOperationExtensionsTests.cs" company="Tnzi">
//     Copyright (c) Tnzi. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Tnzi.EFCore.Extensions;
using Tnzi.EFCore.Tests.TestEntities;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// BatchOperationExtensions 集成测试
/// 使用 SQLite 内存数据库验证批量操作功能
/// </summary>
public class BatchOperationExtensionsTests : EFCoreTestBase
{
    #region Test Data Setup

    private async Task SeedProductsAsync()
    {
        DbContext.Products.AddRange(
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 1", Price = 100, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 2", Price = 200, Stock = 20 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 3", Price = 300, Stock = 30 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 4", Price = 400, Stock = 0 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 5", Price = 500, Stock = 0 }
        );
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedSoftDeletableProductsAsync()
    {
        DbContext.SoftDeletableProducts.AddRange(
            new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "Soft Product 1", Price = 100, Stock = 10, IsDeleted = false },
            new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "Soft Product 2", Price = 200, Stock = 20, IsDeleted = false },
            new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "Soft Product 3", Price = 300, Stock = 30, IsDeleted = false },
            new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "Soft Product 4", Price = 400, Stock = 0, IsDeleted = false },
            new TestSoftDeletableProduct { Id = Guid.NewGuid(), Name = "Soft Product 5", Price = 500, Stock = 0, IsDeleted = false }
        );
        await DbContext.SaveChangesAsync();
    }

    #endregion

    #region BatchDeleteAsync (IQueryable) Tests

    [Fact]
    public async Task BatchDeleteAsync_IQueryable_ShouldDeleteMatchingRecords()
    {
        // Arrange
        await SeedProductsAsync();
        
        // Act - 删除库存为 0 的产品
        var deletedCount = await DbContext.Products
            .Where(p => p.Stock == 0)
            .BatchDeleteAsync();
        
        // Assert
        Assert.Equal(2, deletedCount);
        
        var remainingProducts = await DbContext.Products.CountAsync();
        Assert.Equal(3, remainingProducts);
    }

    [Fact]
    public async Task BatchDeleteAsync_IQueryable_NoMatch_ShouldReturnZero()
    {
        // Arrange
        await SeedProductsAsync();
        
        // Act - 尝试删除不存在的记录
        var deletedCount = await DbContext.Products
            .Where(p => p.Price > 1000)
            .BatchDeleteAsync();
        
        // Assert
        Assert.Equal(0, deletedCount);
        
        var remainingProducts = await DbContext.Products.CountAsync();
        Assert.Equal(5, remainingProducts);
    }

    [Fact]
    public async Task BatchDeleteAsync_IQueryable_EmptyTable_ShouldReturnZero()
    {
        // Arrange - 空表
        
        // Act
        var deletedCount = await DbContext.Products
            .Where(p => p.Stock == 0)
            .BatchDeleteAsync();
        
        // Assert
        Assert.Equal(0, deletedCount);
    }

    #endregion

    #region BatchDeleteAsync (DbSet) Tests

    [Fact]
    public async Task BatchDeleteAsync_DbSet_ShouldDeleteMatchingRecords()
    {
        // Arrange
        await SeedProductsAsync();
        
        // Act - 使用 DbSet 扩展删除价格 < 250 的产品
        var deletedCount = await DbContext.Products
            .BatchDeleteAsync(p => p.Price < 250);
        
        // Assert
        Assert.Equal(2, deletedCount);
        
        var remainingProducts = await DbContext.Products.CountAsync();
        Assert.Equal(3, remainingProducts);
    }

    [Fact]
    public async Task BatchDeleteAsync_DbSet_AllRecords_ShouldDeleteAll()
    {
        // Arrange
        await SeedProductsAsync();
        
        // Act - 删除所有记录
        var deletedCount = await DbContext.Products
            .BatchDeleteAsync(_ => true);
        
        // Assert
        Assert.Equal(5, deletedCount);
        
        var remainingProducts = await DbContext.Products.CountAsync();
        Assert.Equal(0, remainingProducts);
    }

    #endregion

    #region BatchSoftDeleteAsync (IQueryable) Tests

    [Fact]
    public async Task BatchSoftDeleteAsync_IQueryable_ShouldSetIsDeletedToTrue()
    {
        // Arrange
        await SeedSoftDeletableProductsAsync();
        
        // Act - 软删除库存为 0 的产品
        var deletedCount = await DbContext.SoftDeletableProducts
            .Where(p => p.Stock == 0)
            .BatchSoftDeleteAsync();
        
        // Assert
        Assert.Equal(2, deletedCount);
        
        // 验证 IsDeleted 被设置为 true
        var softDeletedProducts = await DbContext.SoftDeletableProducts
            .IgnoreQueryFilters()  // 忽略软删除过滤器
            .Where(p => p.IsDeleted)
            .CountAsync();
        Assert.Equal(2, softDeletedProducts);
        
        // 验证未删除的数量
        var activeProducts = await DbContext.SoftDeletableProducts
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .CountAsync();
        Assert.Equal(3, activeProducts);
    }

    [Fact]
    public async Task BatchSoftDeleteAsync_IQueryable_NoMatch_ShouldReturnZero()
    {
        // Arrange
        await SeedSoftDeletableProductsAsync();
        
        // Act - 尝试软删除不存在的记录
        var deletedCount = await DbContext.SoftDeletableProducts
            .Where(p => p.Price > 1000)
            .BatchSoftDeleteAsync();
        
        // Assert
        Assert.Equal(0, deletedCount);
    }

    #endregion

    #region BatchSoftDeleteAsync (DbSet) Tests

    [Fact]
    public async Task BatchSoftDeleteAsync_DbSet_ShouldSetIsDeletedToTrue()
    {
        // Arrange
        await SeedSoftDeletableProductsAsync();
        
        // Act - 使用 DbSet 扩展软删除价格 > 350 的产品
        var deletedCount = await DbContext.SoftDeletableProducts
            .BatchSoftDeleteAsync(p => p.Price > 350);
        
        // Assert
        Assert.Equal(2, deletedCount);
        
        // 验证 IsDeleted 被设置为 true
        var softDeletedProducts = await DbContext.SoftDeletableProducts
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .CountAsync();
        Assert.Equal(2, softDeletedProducts);
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task BatchDeleteAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        await SeedProductsAsync();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        // Act & Assert - TaskCanceledException 继承自 OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DbContext.Products
                .Where(p => p.Stock == 0)
                .BatchDeleteAsync(cts.Token);
        });
    }

    #endregion
}
