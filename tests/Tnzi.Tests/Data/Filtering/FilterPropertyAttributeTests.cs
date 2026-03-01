
namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// Tests for FilterExpressionBuilder.BuildFromDto and [FilterProperty] attribute
/// </summary>
public class FilterPropertyAttributeTests
{
    #region Test Types

    private class ProductEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string? Category { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public ProductStatus Status { get; set; }
    }

    private enum ProductStatus
    {
        Draft = 0,
        Published = 1,
        Archived = 2
    }

    private class ProductQueryDto
    {
        [FilterProperty(Operator = FilterOperator.Contains)]
        public string? Name { get; set; }

        [FilterProperty(Operator = FilterOperator.GreaterThanOrEqual)]
        public decimal? Price { get; set; }

        [FilterProperty(Operator = FilterOperator.LessThanOrEqual)]
        public int? Stock { get; set; }

        [FilterProperty(Operator = FilterOperator.Equal)]
        public string? Category { get; set; }

        [FilterProperty(Operator = FilterOperator.Equal)]
        public bool? IsActive { get; set; }

        [FilterProperty(Operator = FilterOperator.Equal)]
        public ProductStatus? Status { get; set; }
    }

    private class ProductQueryWithMappingDto
    {
        [FilterProperty("Name", Operator = FilterOperator.Contains)]
        public string? Keyword { get; set; }

        [FilterProperty("Price", Operator = FilterOperator.GreaterThanOrEqual)]
        public decimal? MinPrice { get; set; }

        [FilterProperty("Price", Operator = FilterOperator.LessThanOrEqual)]
        public decimal? MaxPrice { get; set; }
    }

    private class DtoWithoutAttributes
    {
        public string? Name { get; set; }
        public int? Age { get; set; }
    }

    #endregion

    #region BuildFromDto Tests

    [Fact]
    public void BuildFromDto_AllNullValues_ReturnsAlwaysTrue()
    {
        var dto = new ProductQueryDto();

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "Anything", Price = 999m }));
    }

    [Fact]
    public void BuildFromDto_SingleStringFilter_FiltersCorrectly()
    {
        var dto = new ProductQueryDto { Name = "Phone" };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "iPhone 15" }));
        Assert.True(compiled(new ProductEntity { Name = "Phone Case" }));
        Assert.False(compiled(new ProductEntity { Name = "Laptop" }));
    }

    [Fact]
    public void BuildFromDto_DecimalFilter_FiltersCorrectly()
    {
        var dto = new ProductQueryDto { Price = 100m };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "Expensive", Price = 150m }));
        Assert.True(compiled(new ProductEntity { Name = "Exact", Price = 100m }));
        Assert.False(compiled(new ProductEntity { Name = "Cheap", Price = 50m }));
    }

    [Fact]
    public void BuildFromDto_MultipleFilters_CombinesWithAnd()
    {
        var dto = new ProductQueryDto
        {
            Name = "Phone",
            Price = 100m,
            IsActive = true
        };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        // All conditions must match
        Assert.True(compiled(new ProductEntity { Name = "iPhone 15", Price = 150m, IsActive = true }));
        Assert.False(compiled(new ProductEntity { Name = "iPhone 15", Price = 50m, IsActive = true })); // Price too low
        Assert.False(compiled(new ProductEntity { Name = "Laptop", Price = 150m, IsActive = true })); // Name doesn't match
        Assert.False(compiled(new ProductEntity { Name = "iPhone 15", Price = 150m, IsActive = false })); // Not active
    }

    [Fact]
    public void BuildFromDto_EnumFilter_FiltersCorrectly()
    {
        var dto = new ProductQueryDto { Status = ProductStatus.Published };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Status = ProductStatus.Published }));
        Assert.False(compiled(new ProductEntity { Status = ProductStatus.Draft }));
        Assert.False(compiled(new ProductEntity { Status = ProductStatus.Archived }));
    }

    [Fact]
    public void BuildFromDto_WithPropertyMapping_MapsCorrectly()
    {
        var dto = new ProductQueryWithMappingDto
        {
            Keyword = "Phone",
            MinPrice = 50m,
            MaxPrice = 200m
        };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryWithMappingDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "iPhone 15", Price = 100m }));
        Assert.False(compiled(new ProductEntity { Name = "iPhone 15", Price = 300m })); // Too expensive
        Assert.False(compiled(new ProductEntity { Name = "iPhone 15", Price = 30m })); // Too cheap
        Assert.False(compiled(new ProductEntity { Name = "Laptop", Price = 100m })); // Name doesn't match
    }

    [Fact]
    public void BuildFromDto_EmptyString_SkipsFilter()
    {
        var dto = new ProductQueryDto { Name = "" };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        // Empty string should be skipped, so all entities match
        Assert.True(compiled(new ProductEntity { Name = "Anything" }));
    }

    [Fact]
    public void BuildFromDto_WhitespaceString_SkipsFilter()
    {
        var dto = new ProductQueryDto { Name = "   " };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "Anything" }));
    }

    [Fact]
    public void BuildFromDto_DtoWithoutAttributes_ReturnsAlwaysTrue()
    {
        var dto = new DtoWithoutAttributes { Name = "Test", Age = 25 };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, DtoWithoutAttributes>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "Anything" }));
    }

    [Fact]
    public void BuildFromDto_OrLogic_CombinesWithOr()
    {
        var dto = new ProductQueryDto
        {
            Name = "Phone",
            Category = "Electronics"
        };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto, LogicalOperator.Or);
        var compiled = expression.Compile();

        // Either condition should match
        Assert.True(compiled(new ProductEntity { Name = "iPhone 15", Category = "Other" })); // Name matches
        Assert.True(compiled(new ProductEntity { Name = "Laptop", Category = "Electronics" })); // Category matches
        Assert.False(compiled(new ProductEntity { Name = "Laptop", Category = "Other" })); // Neither matches
    }

    [Fact]
    public void BuildFromDto_NullDto_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(null!));
    }

    [Fact]
    public void BuildFromDto_DefaultValueSkipped_IntStock()
    {
        // Stock = 0 (default for int) should be skipped
        var dto = new ProductQueryDto { Stock = 0 };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        // Default value is skipped, so all entities match
        Assert.True(compiled(new ProductEntity { Stock = 100 }));
    }

    [Fact]
    public void BuildFromDto_NonDefaultIntValue_AppliesFilter()
    {
        var dto = new ProductQueryDto { Stock = 50 };

        var expression = FilterExpressionBuilder.BuildFromDto<ProductEntity, ProductQueryDto>(dto);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Stock = 30 })); // 30 <= 50
        Assert.True(compiled(new ProductEntity { Stock = 50 })); // 50 <= 50
        Assert.False(compiled(new ProductEntity { Stock = 100 })); // 100 > 50
    }

    #endregion

    #region BuildGroupFromDto Tests

    [Fact]
    public void BuildGroupFromDto_CreatesCorrectFilterGroup()
    {
        var dto = new ProductQueryDto
        {
            Name = "Phone",
            Price = 100m
        };

        var group = FilterExpressionBuilder.BuildGroupFromDto(dto);

        Assert.True(group.HasFilters);
        Assert.Equal(2, group.Rules.Count);
        Assert.Equal(LogicalOperator.And, group.Logic);
    }

    [Fact]
    public void BuildGroupFromDto_AllNull_ReturnsEmptyGroup()
    {
        var dto = new ProductQueryDto();

        var group = FilterExpressionBuilder.BuildGroupFromDto(dto);

        Assert.False(group.HasFilters);
        Assert.Empty(group.Rules);
    }

    [Fact]
    public void BuildGroupFromDto_CanBeExtended()
    {
        var dto = new ProductQueryDto { Name = "Phone" };

        var group = FilterExpressionBuilder.BuildGroupFromDto(dto);
        // Extend with additional manual rules
        group.WhereGreaterThan("Price", 50);

        var expression = FilterExpressionBuilder.Build<ProductEntity>(group);
        var compiled = expression.Compile();

        Assert.True(compiled(new ProductEntity { Name = "iPhone", Price = 100m }));
        Assert.False(compiled(new ProductEntity { Name = "iPhone", Price = 30m })); // Price too low
    }

    #endregion

    #region FilterPropertyAttribute Tests

    [Fact]
    public void FilterPropertyAttribute_DefaultOperator_IsEqual()
    {
        var attr = new FilterPropertyAttribute();
        Assert.Equal(FilterOperator.Equal, attr.Operator);
        Assert.Null(attr.EntityProperty);
    }

    [Fact]
    public void FilterPropertyAttribute_WithEntityProperty_SetsCorrectly()
    {
        var attr = new FilterPropertyAttribute("ProductName");
        Assert.Equal("ProductName", attr.EntityProperty);
    }

    [Fact]
    public void FilterPropertyAttribute_WithOperator_SetsCorrectly()
    {
        var attr = new FilterPropertyAttribute { Operator = FilterOperator.Contains };
        Assert.Equal(FilterOperator.Contains, attr.Operator);
    }

    #endregion
}
