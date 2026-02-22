// -----------------------------------------------------------------------
// <copyright file="FilterQueryableExtensionsTests.cs" company="Tnzi">
//     Copyright (c) Tnzi. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Tnzi.Data;
using Tnzi.Data.Filtering;

namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// FilterQueryableExtensions 单元测试
/// </summary>
public class FilterQueryableExtensionsTests
{
    #region Test Entities

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public bool IsActive { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    #endregion

    #region Test Data

    private static IQueryable<Product> GetTestData()
    {
        var products = new List<Product>
        {
            new() { Id = 1, Name = "iPhone", Price = 999, Stock = 100, IsActive = true, Category = "Electronics" },
            new() { Id = 2, Name = "iPad", Price = 799, Stock = 50, IsActive = true, Category = "Electronics" },
            new() { Id = 3, Name = "MacBook", Price = 1999, Stock = 30, IsActive = true, Category = "Computers" },
            new() { Id = 4, Name = "iMac", Price = 1499, Stock = 0, IsActive = false, Category = "Computers" },
            new() { Id = 5, Name = "AirPods", Price = 249, Stock = 200, IsActive = true, Category = "Accessories" },
        };
        return products.AsQueryable();
    }

    #endregion

    #region ApplyFilter (FilterGroup) Tests

    [Fact]
    public void ApplyFilter_NullFilterGroup_ShouldReturnOriginalQuery()
    {
        // Arrange
        var query = GetTestData();

        // Act
        var result = query.ApplyFilter((FilterGroup?)null);

        // Assert
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void ApplyFilter_EmptyFilterGroup_ShouldReturnOriginalQuery()
    {
        // Arrange
        var query = GetTestData();
        var filter = new FilterGroup();

        // Act
        var result = query.ApplyFilter(filter);

        // Assert
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void ApplyFilter_SingleRule_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        var filter = new FilterGroup()
            .AddRule("Category", FilterOperator.Equal, "Electronics");

        // Act
        var result = query.ApplyFilter(filter).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.Equal("Electronics", p.Category));
    }

    [Fact]
    public void ApplyFilter_MultipleRulesWithAnd_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        var filter = FilterGroup.And()
            .AddRule("Category", FilterOperator.Equal, "Electronics")
            .AddRule("Price", FilterOperator.LessThan, 900);

        // Act
        var result = query.ApplyFilter(filter).ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("iPad", result[0].Name);
    }

    [Fact]
    public void ApplyFilter_MultipleRulesWithOr_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        var filter = FilterGroup.Or()
            .AddRule("Name", FilterOperator.Equal, "iPhone")
            .AddRule("Name", FilterOperator.Equal, "MacBook");

        // Act
        var result = query.ApplyFilter(filter).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "iPhone");
        Assert.Contains(result, p => p.Name == "MacBook");
    }

    [Fact]
    public void ApplyFilter_NestedGroups_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        // (Category == Electronics AND Price < 900) OR (Category == Accessories)
        var filter = FilterGroup.Or()
            .AddGroup(LogicalOperator.And, g => g
                .AddRule("Category", FilterOperator.Equal, "Electronics")
                .AddRule("Price", FilterOperator.LessThan, 900))
            .AddRule("Category", FilterOperator.Equal, "Accessories");

        // Act
        var result = query.ApplyFilter(filter).ToList();

        // Assert
        Assert.Equal(2, result.Count); // iPad and AirPods
        Assert.Contains(result, p => p.Name == "iPad");
        Assert.Contains(result, p => p.Name == "AirPods");
    }

    #endregion

    #region ApplyFilter (FilterRule) Tests

    [Fact]
    public void ApplyFilter_SingleRule_ContainsOperator_ShouldWork()
    {
        // Arrange
        var query = GetTestData();
        var rule = FilterRule.Contains("Name", "Mac");

        // Act
        var result = query.ApplyFilter(rule).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "MacBook");
        Assert.Contains(result, p => p.Name == "iMac");
    }

    [Fact]
    public void ApplyFilter_SingleRule_GreaterThan_ShouldWork()
    {
        // Arrange
        var query = GetTestData();
        var rule = FilterRule.GreaterThan("Price", 1000);

        // Act
        var result = query.ApplyFilter(rule).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, p => Assert.True(p.Price > 1000));
    }

    [Fact]
    public void ApplyFilter_SingleRule_InOperator_ShouldWork()
    {
        // Arrange
        var query = GetTestData();
        var rule = new FilterRule("Name", FilterOperator.In, new[] { "iPhone", "iPad", "MacBook" });

        // Act
        var result = query.ApplyFilter(rule).ToList();

        // Assert
        Assert.Equal(3, result.Count);
    }

    #endregion

    #region ApplyFilterFromJson Tests

    [Fact]
    public void ApplyFilterFromJson_NullJson_ShouldReturnOriginalQuery()
    {
        // Arrange
        var query = GetTestData();

        // Act
        var result = query.ApplyFilterFromJson(null);

        // Assert
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void ApplyFilterFromJson_EmptyJson_ShouldReturnOriginalQuery()
    {
        // Arrange
        var query = GetTestData();

        // Act
        var result = query.ApplyFilterFromJson("");

        // Assert
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void ApplyFilterFromJson_ValidJson_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        var filter = new FilterGroup()
            .AddRule("Category", FilterOperator.Equal, "Electronics");
        var json = JsonSerializer.Serialize(filter);

        // Act
        var result = query.ApplyFilterFromJson(json).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ApplyFilterFromJson_CaseInsensitivePropertyNames_ShouldWork()
    {
        // Arrange
        var query = GetTestData();
        // JSON 使用小写属性名
        var json = """
        {
            "logic": "And",
            "rules": [
                {
                    "field": "Category",
                    "operator": "Equal",
                    "value": "Electronics"
                }
            ]
        }
        """;

        // Act
        var result = query.ApplyFilterFromJson(json).ToList();

        // Assert
        Assert.Equal(2, result.Count);
    }

    #endregion

    #region TryApplyFilterFromJson Tests

    [Fact]
    public void TryApplyFilterFromJson_NullJson_ShouldReturnOriginalQuery()
    {
        // Arrange
        var query = GetTestData();

        // Act
        var result = query.TryApplyFilterFromJson(null, out var error);

        // Assert
        Assert.Null(error);
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void TryApplyFilterFromJson_ValidJson_ShouldFilterCorrectly()
    {
        // Arrange
        var query = GetTestData();
        var filter = new FilterGroup()
            .AddRule("IsActive", FilterOperator.Equal, true);
        var json = JsonSerializer.Serialize(filter);

        // Act
        var result = query.TryApplyFilterFromJson(json, out var error).ToList();

        // Assert
        Assert.Null(error);
        Assert.Equal(4, result.Count);
        Assert.All(result, p => Assert.True(p.IsActive));
    }

    [Fact]
    public void TryApplyFilterFromJson_InvalidJson_ShouldReturnOriginalQueryWithError()
    {
        // Arrange
        var query = GetTestData();
        var invalidJson = "{ invalid json }";

        // Act
        var result = query.TryApplyFilterFromJson(invalidJson, out var error);

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Invalid JSON format", error);
        Assert.Equal(5, result.Count());
    }

    [Fact]
    public void TryApplyFilterFromJson_InvalidProperty_ShouldReturnOriginalQueryWithError()
    {
        // Arrange
        var query = GetTestData();
        var json = """
        {
            "Logic": "And",
            "Rules": [
                {
                    "Field": "InvalidProperty",
                    "Operator": "Equal",
                    "Value": "test"
                }
            ]
        }
        """;

        // Act
        var result = query.TryApplyFilterFromJson(json, out var error);

        // Assert
        Assert.NotNull(error);
        Assert.Contains("InvalidProperty", error);
        Assert.Equal(5, result.Count());
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ApplyFilter_ChainedFilters_ShouldWork()
    {
        // Arrange
        var query = GetTestData();
        var filter1 = new FilterGroup().AddRule("IsActive", FilterOperator.Equal, true);
        var filter2 = new FilterGroup().AddRule("Price", FilterOperator.LessThan, 500);

        // Act - 链式应用多个过滤器
        var result = query
            .ApplyFilter(filter1)
            .ApplyFilter(filter2)
            .ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal("AirPods", result[0].Name);
    }

    [Fact]
    public void ApplyFilter_WithZeroResults_ShouldReturnEmptyQuery()
    {
        // Arrange
        var query = GetTestData();
        var filter = new FilterGroup().AddRule("Price", FilterOperator.GreaterThan, 10000);

        // Act
        var result = query.ApplyFilter(filter).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion
}
