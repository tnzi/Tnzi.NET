
namespace Tnzi.EFCore.Tests;

/// <summary>
/// EfCoreRepository 集成测试
/// </summary>
public class EfCoreRepositoryTests : EFCoreTestBase
{
    private readonly IRepository<TestProduct, Guid> _productRepository;
    private readonly IRepository<TestUser, Guid> _userRepository;

    public EfCoreRepositoryTests()
    {
        _productRepository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(DbContext, null, ServiceProvider);
        _userRepository = new EFCoreRepository<TestDbContext, TestUser, Guid>(DbContext, null, ServiceProvider);
    }

    #region 基本 CRUD 测试

    [Fact]
    public async Task InsertAsync_ShouldAddEntityToDatabase()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "Test Product",
            Price = 99.99m,
            Stock = 100
        };

        // Act
        await _productRepository.InsertAsync(product);

        // Assert
        var saved = await DbContext.Products.FindAsync(product.Id);
        Assert.NotNull(saved);
        Assert.Equal("Test Product", saved.Name);
    }

    [Fact]
    public async Task InsertManyAsync_ShouldAddMultipleEntities()
    {
        // Arrange
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 1", Price = 10m, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 2", Price = 20m, Stock = 20 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 3", Price = 30m, Stock = 30 }
        };

        // Act
        await _productRepository.InsertManyAsync(products);

        // Assert
        var count = await DbContext.Products.CountAsync();
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyEntity()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "Original Name",
            Price = 50m,
            Stock = 50
        };

        await _productRepository.InsertAsync(product);

        // Act
        product.Name = "Updated Name";
        product.Price = 75m;
        await _productRepository.UpdateAsync(product);

        // Assert
        var updated = await DbContext.Products.FindAsync(product.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
        Assert.Equal(75m, updated.Price);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveEntity()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "To Delete",
            Price = 100m,
            Stock = 100
        };

        await _productRepository.InsertAsync(product);

        // Act
        await _productRepository.DeleteAsync(product);

        // Assert
        var deleted = await DbContext.Products.FindAsync(product.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeleteAsync_WithPredicate_ShouldRemoveMatchingEntities()
    {
        // Arrange
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "Cheap", Price = 5m, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Expensive", Price = 100m, Stock = 5 }
        };

        await _productRepository.InsertManyAsync(products);

        // Act
        await _productRepository.DeleteAsync(p => p.Price < 10m);

        // Assert
        var remaining = await DbContext.Products.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("Expensive", remaining[0].Name);
    }

    #endregion

    #region 查询操作测试

    [Fact]
    public async Task FindAsync_WithId_ShouldReturnEntity()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "Find Me",
            Price = 50m,
            Stock = 50
        };

        await _productRepository.InsertAsync(product);

        // Act
        var found = await _productRepository.FindAsync(product.Id);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(product.Id, found.Id);
        Assert.Equal("Find Me", found.Name);
    }

    [Fact]
    public async Task FindAsync_WithPredicate_ShouldReturnMatchingEntity()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "Unique Product",
            Price = 99m,
            Stock = 10
        };

        await _productRepository.InsertAsync(product);

        // Act
        var found = await _productRepository.FindAsync(p => p.Name == "Unique Product");

        // Assert
        Assert.NotNull(found);
        Assert.Equal(product.Id, found.Id);
    }

    [Fact]
    public async Task AnyAsync_WithMatchingData_ShouldReturnTrue()
    {
        // Arrange
        var product = new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = "Exists",
            Price = 50m,
            Stock = 50
        };

        await _productRepository.InsertAsync(product);

        // Act
        var exists = await _productRepository.AnyAsync(p => p.Name == "Exists");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task CountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 1", Price = 10m, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 2", Price = 20m, Stock = 20 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 3", Price = 30m, Stock = 30 }
        };

        await _productRepository.InsertManyAsync(products);

        // Act
        var count = await _productRepository.CountAsync();
        var expensiveCount = await _productRepository.CountAsync(p => p.Price > 15m);

        // Assert
        Assert.Equal(3, count);
        Assert.Equal(2, expensiveCount);
    }

    [Fact]
    public async Task ToListAsync_ShouldReturnAllEntities()
    {
        // Arrange
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 1", Price = 10m, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Product 2", Price = 20m, Stock = 20 }
        };

        await _productRepository.InsertManyAsync(products);

        // Act
        var list = await _productRepository.ToListAsync();

        // Assert
        Assert.Equal(2, list.Count);
    }

    #endregion

    #region 分页查询测试

    [Fact]
    public async Task GetPagedListAsync_ShouldReturnCorrectPage()
    {
        // Arrange
        var products = Enumerable.Range(1, 25).Select(i => new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Product {i}",
            Price = i * 10m,
            Stock = i
        }).ToArray();

        await _productRepository.InsertManyAsync(products);

        // Act
        var query1 = new PagedQuery { PageIndex = 1, PageSize = 10 };
        var page1 = await _productRepository.GetPagedListAsync(query1);

        var query2 = new PagedQuery { PageIndex = 2, PageSize = 10 };
        var page2 = await _productRepository.GetPagedListAsync(query2);

        var query3 = new PagedQuery { PageIndex = 3, PageSize = 10 };
        var page3 = await _productRepository.GetPagedListAsync(query3);

        // Assert
        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);

        Assert.Equal(25, page2.TotalCount);
        Assert.Equal(10, page2.Items.Count);

        Assert.Equal(25, page3.TotalCount);
        Assert.Equal(5, page3.Items.Count);
    }

    [Fact]
    public async Task GetPagedListAsync_WithPredicate_ShouldFilterResults()
    {
        // Arrange
        var products = Enumerable.Range(1, 20).Select(i => new TestProduct
        {
            Id = Guid.NewGuid(),
            Name = $"Product {i}",
            Price = i * 10m,
            Stock = i
        }).ToArray();

        await _productRepository.InsertManyAsync(products);

        // Act
        var query = new PagedQuery { PageIndex = 1, PageSize = 5 };
        var pagedList = await _productRepository.GetPagedListAsync(
            predicate: p => p.Price > 100m,
            query: query);

        // Assert
        Assert.True(pagedList.TotalCount < 20);
        Assert.All(pagedList.Items, p => Assert.True(p.Price > 100m));
    }

    #endregion
}