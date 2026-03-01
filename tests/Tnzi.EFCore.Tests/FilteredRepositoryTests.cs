
using Tnzi.Data.Filtering;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// Tests for repository FilterGroup integration (GetFilteredPagedListAsync, GetFilteredListAsync, CountAsync(FilterGroup))
/// and PagedQuery.Filter automatic application
/// </summary>
public class FilteredRepositoryTests : EFCoreTestBase
{
    private readonly IRepository<TestProduct, Guid> _productRepository;

    public FilteredRepositoryTests()
    {
        _productRepository = new EFCoreRepository<TestDbContext, TestProduct, Guid>(DbContext, null, ServiceProvider);
    }

    private async Task SeedProducts()
    {
        var products = new[]
        {
            new TestProduct { Id = Guid.NewGuid(), Name = "iPhone 15", Price = 999m, Stock = 50 },
            new TestProduct { Id = Guid.NewGuid(), Name = "iPhone 14", Price = 799m, Stock = 30 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Samsung Galaxy S24", Price = 899m, Stock = 80 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Google Pixel 8", Price = 699m, Stock = 25 },
            new TestProduct { Id = Guid.NewGuid(), Name = "OnePlus 12", Price = 599m, Stock = 100 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Laptop Pro", Price = 1999m, Stock = 10 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Laptop Air", Price = 1299m, Stock = 15 },
            new TestProduct { Id = Guid.NewGuid(), Name = "Tablet Mini", Price = 499m, Stock = 200 },
        };
        await _productRepository.InsertManyAsync(products);
    }

    #region GetFilteredPagedListAsync Tests

    [Fact]
    public async Task GetFilteredPagedListAsync_WithContainsFilter_ReturnsMatchingEntities()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereContains("Name", "iPhone");

        var query = new PagedQuery { PageIndex = 1, PageSize = 10 };
        var result = await _productRepository.GetFilteredPagedListAsync(filter, query);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, p => Assert.Contains("iPhone", p.Name));
    }

    [Fact]
    public async Task GetFilteredPagedListAsync_WithMultipleFilters_CombinesCorrectly()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereContains("Name", "iPhone")
            .WhereGreaterThanOrEqual("Price", 900);

        var query = new PagedQuery { PageIndex = 1, PageSize = 10 };
        var result = await _productRepository.GetFilteredPagedListAsync(filter, query);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("iPhone 15", result.Items[0].Name);
    }

    [Fact]
    public async Task GetFilteredPagedListAsync_WithAdditionalPredicate_CombinesBoth()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereGreaterThanOrEqual("Price", 500);

        var query = new PagedQuery { PageIndex = 1, PageSize = 10 };
        var result = await _productRepository.GetFilteredPagedListAsync(
            filter, query,
            additionalPredicate: p => p.Stock > 20);

        Assert.True(result.TotalCount > 0);
        Assert.All(result.Items, p =>
        {
            Assert.True(p.Price >= 500m);
            Assert.True(p.Stock > 20);
        });
    }

    [Fact]
    public async Task GetFilteredPagedListAsync_EmptyFilter_ReturnsAll()
    {
        await SeedProducts();

        var filter = FilterGroup.And();

        var query = new PagedQuery { PageIndex = 1, PageSize = 10 };
        var result = await _productRepository.GetFilteredPagedListAsync(filter, query);

        Assert.Equal(8, result.TotalCount);
    }

    [Fact]
    public async Task GetFilteredPagedListAsync_WithPaging_ReturnsCorrectPage()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereGreaterThan("Price", 0);

        var query = new PagedQuery { PageIndex = 1, PageSize = 3 };
        var page1 = await _productRepository.GetFilteredPagedListAsync(filter, query);

        Assert.Equal(8, page1.TotalCount);
        Assert.Equal(3, page1.Items.Count);
        Assert.Equal(3, page1.TotalPages);
    }

    #endregion

    #region GetFilteredListAsync Tests

    [Fact]
    public async Task GetFilteredListAsync_WithFilter_ReturnsMatchingEntities()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereContains("Name", "Laptop");

        var result = await _productRepository.GetFilteredListAsync(filter);

        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Contains("Laptop", p.Name));
    }

    [Fact]
    public async Task GetFilteredListAsync_EmptyFilter_ReturnsAll()
    {
        await SeedProducts();

        var filter = FilterGroup.And();
        var result = await _productRepository.GetFilteredListAsync(filter);

        Assert.Equal(8, result.Count);
    }

    [Fact]
    public async Task GetFilteredListAsync_NoMatches_ReturnsEmpty()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereEqual("Name", "NonExistent Product");

        var result = await _productRepository.GetFilteredListAsync(filter);

        Assert.Empty(result);
    }

    #endregion

    #region CountAsync(FilterGroup) Tests

    [Fact]
    public async Task CountAsync_WithFilter_ReturnsCorrectCount()
    {
        await SeedProducts();

        var filter = FilterGroup.And()
            .WhereGreaterThan("Price", 800);

        var count = await _productRepository.CountAsync(filter);

        // iPhone 15 (999), Samsung (899), Laptop Pro (1999), Laptop Air (1299)
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task CountAsync_EmptyFilter_ReturnsAllCount()
    {
        await SeedProducts();

        var filter = FilterGroup.And();
        var count = await _productRepository.CountAsync(filter);

        Assert.Equal(8, count);
    }

    #endregion

    #region PagedQuery.Filter Integration Tests

    [Fact]
    public async Task GetPagedListAsync_WithQueryFilter_AppliesFilterAutomatically()
    {
        await SeedProducts();

        var query = new PagedQuery
        {
            PageIndex = 1,
            PageSize = 10,
            Filter = FilterGroup.And().WhereContains("Name", "iPhone")
        };

        var result = await _productRepository.GetPagedListAsync(query);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, p => Assert.Contains("iPhone", p.Name));
    }

    [Fact]
    public async Task GetPagedListAsync_WithPredicateAndQueryFilter_CombinesBoth()
    {
        await SeedProducts();

        var query = new PagedQuery
        {
            PageIndex = 1,
            PageSize = 10,
            Filter = FilterGroup.And().WhereGreaterThan("Price", 700)
        };

        // Explicit predicate + PagedQuery.Filter
        var result = await _productRepository.GetPagedListAsync(
            predicate: p => p.Stock > 10,
            query: query);

        Assert.True(result.TotalCount > 0);
        Assert.All(result.Items, p =>
        {
            Assert.True(p.Price > 700m);
            Assert.True(p.Stock > 10);
        });
    }

    [Fact]
    public async Task GetPagedListAsync_WithNullFilter_WorksNormally()
    {
        await SeedProducts();

        var query = new PagedQuery
        {
            PageIndex = 1,
            PageSize = 10,
            Filter = null
        };

        var result = await _productRepository.GetPagedListAsync(query);

        Assert.Equal(8, result.TotalCount);
    }

    #endregion

    #region Or Filter Tests

    [Fact]
    public async Task GetFilteredListAsync_OrFilter_ReturnsEitherMatch()
    {
        await SeedProducts();

        var filter = FilterGroup.Or()
            .WhereContains("Name", "iPhone")
            .WhereContains("Name", "Laptop");

        var result = await _productRepository.GetFilteredListAsync(filter);

        Assert.Equal(4, result.Count); // 2 iPhones + 2 Laptops
    }

    #endregion
}
