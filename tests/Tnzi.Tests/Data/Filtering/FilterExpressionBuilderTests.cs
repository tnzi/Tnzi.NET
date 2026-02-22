
namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// FilterExpressionBuilder 单元测试
/// </summary>
public class FilterExpressionBuilderTests
{
    #region 测试实体

    private class TestEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? CategoryId { get; set; }
        public TestStatus Status { get; set; }
        public TestAuthor? Author { get; set; }
    }

    private class TestAuthor
    {
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
    }

    private enum TestStatus
    {
        Draft = 0,
        Published = 1,
        Archived = 2
    }

    #endregion

    #region 基础操作符测试

    [Fact]
    public void Build_EqualOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.Equal, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test" }));
        Assert.False(compiled(new TestEntity { Name = "Other" }));
    }

    [Fact]
    public void Build_NotEqualOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.NotEqual, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { Name = "Test" }));
        Assert.True(compiled(new TestEntity { Name = "Other" }));
    }

    [Fact]
    public void Build_GreaterThanOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.GreaterThan, 18);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Age = 20 }));
        Assert.False(compiled(new TestEntity { Age = 18 }));
        Assert.False(compiled(new TestEntity { Age = 15 }));
    }

    [Fact]
    public void Build_GreaterThanOrEqualOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.GreaterThanOrEqual, 18);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Age = 20 }));
        Assert.True(compiled(new TestEntity { Age = 18 }));
        Assert.False(compiled(new TestEntity { Age = 15 }));
    }

    [Fact]
    public void Build_LessThanOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.LessThan, 18);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { Age = 20 }));
        Assert.False(compiled(new TestEntity { Age = 18 }));
        Assert.True(compiled(new TestEntity { Age = 15 }));
    }

    [Fact]
    public void Build_LessThanOrEqualOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.LessThanOrEqual, 18);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { Age = 20 }));
        Assert.True(compiled(new TestEntity { Age = 18 }));
        Assert.True(compiled(new TestEntity { Age = 15 }));
    }

    #endregion

    #region 字符串操作符测试

    [Fact]
    public void Build_ContainsOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.Contains, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test" }));
        Assert.True(compiled(new TestEntity { Name = "MyTest123" }));
        Assert.False(compiled(new TestEntity { Name = "Other" }));
    }

    [Fact]
    public void Build_NotContainsOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.NotContains, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { Name = "Test" }));
        Assert.True(compiled(new TestEntity { Name = "Other" }));
    }

    [Fact]
    public void Build_StartsWithOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.StartsWith, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test123" }));
        Assert.False(compiled(new TestEntity { Name = "MyTest" }));
    }

    [Fact]
    public void Build_EndsWithOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.EndsWith, "Test");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "MyTest" }));
        Assert.False(compiled(new TestEntity { Name = "Test123" }));
    }

    #endregion

    #region In/NotIn 操作符测试

    [Fact]
    public void Build_InOperator_WithIntArray_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.In, new[] { 18, 20, 25 });

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Age = 18 }));
        Assert.True(compiled(new TestEntity { Age = 20 }));
        Assert.False(compiled(new TestEntity { Age = 19 }));
    }

    [Fact]
    public void Build_InOperator_WithStringArray_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Name", FilterOperator.In, new[] { "Alice", "Bob", "Charlie" });

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Alice" }));
        Assert.False(compiled(new TestEntity { Name = "David" }));
    }

    [Fact]
    public void Build_NotInOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.NotIn, new[] { 18, 20, 25 });

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { Age = 18 }));
        Assert.True(compiled(new TestEntity { Age = 19 }));
    }

    #endregion

    #region IsNull/IsNotNull 操作符测试

    [Fact]
    public void Build_IsNullOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("CategoryId", FilterOperator.IsNull, null);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { CategoryId = null }));
        Assert.False(compiled(new TestEntity { CategoryId = Guid.NewGuid() }));
    }

    [Fact]
    public void Build_IsNotNullOperator_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("CategoryId", FilterOperator.IsNotNull, null);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.False(compiled(new TestEntity { CategoryId = null }));
        Assert.True(compiled(new TestEntity { CategoryId = Guid.NewGuid() }));
    }

    #endregion

    #region 嵌套属性测试

    [Fact]
    public void Build_NestedProperty_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Author.Name", FilterOperator.Equal, "John");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Author = new TestAuthor { Name = "John" } }));
        Assert.False(compiled(new TestEntity { Author = new TestAuthor { Name = "Jane" } }));
    }

    [Fact]
    public void Build_NestedProperty_Contains_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Author.Country", FilterOperator.Contains, "United");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Author = new TestAuthor { Country = "United States" } }));
        Assert.False(compiled(new TestEntity { Author = new TestAuthor { Country = "Canada" } }));
    }

    #endregion

    #region FilterGroup 测试

    [Fact]
    public void Build_FilterGroup_AndLogic_ShouldWork()
    {
        // Arrange
        var group = FilterGroup.And()
            .WhereEqual("Name", "Test")
            .WhereGreaterThan("Age", 18);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(group);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test", Age = 20 }));
        Assert.False(compiled(new TestEntity { Name = "Test", Age = 15 }));
        Assert.False(compiled(new TestEntity { Name = "Other", Age = 20 }));
    }

    [Fact]
    public void Build_FilterGroup_OrLogic_ShouldWork()
    {
        // Arrange
        var group = FilterGroup.Or()
            .WhereEqual("Name", "Test")
            .WhereGreaterThan("Age", 30);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(group);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test", Age = 20 }));
        Assert.True(compiled(new TestEntity { Name = "Other", Age = 35 }));
        Assert.False(compiled(new TestEntity { Name = "Other", Age = 20 }));
    }

    [Fact]
    public void Build_FilterGroup_NestedGroups_ShouldWork()
    {
        // Arrange: (Name == "Test" AND Age > 18) OR (IsActive == true)
        var group = FilterGroup.Or()
            .AddGroup(FilterGroup.And()
                .WhereEqual("Name", "Test")
                .WhereGreaterThan("Age", 18))
            .AddRule("IsActive", FilterOperator.Equal, true);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(group);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Test", Age = 20, IsActive = false }));
        Assert.True(compiled(new TestEntity { Name = "Other", Age = 10, IsActive = true }));
        Assert.False(compiled(new TestEntity { Name = "Other", Age = 10, IsActive = false }));
    }

    [Fact]
    public void Build_EmptyFilterGroup_ShouldReturnTrue()
    {
        // Arrange
        var group = FilterGroup.And();

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(group);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Any" }));
    }

    [Fact]
    public void Build_NullFilterGroup_ShouldReturnTrue()
    {
        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>((FilterGroup?)null);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Name = "Any" }));
    }

    #endregion

    #region 类型转换测试

    [Fact]
    public void Build_StringToGuid_ShouldWork()
    {
        // Arrange
        var guidValue = Guid.NewGuid();
        var rule = new FilterRule("CategoryId", FilterOperator.Equal, guidValue.ToString());

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { CategoryId = guidValue }));
        Assert.False(compiled(new TestEntity { CategoryId = Guid.NewGuid() }));
    }

    [Fact]
    public void Build_StringToEnum_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Status", FilterOperator.Equal, "Published");

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Status = TestStatus.Published }));
        Assert.False(compiled(new TestEntity { Status = TestStatus.Draft }));
    }

    [Fact]
    public void Build_IntToEnum_ShouldWork()
    {
        // Arrange
        var rule = new FilterRule("Status", FilterOperator.Equal, 1);

        // Act
        var expression = FilterExpressionBuilder.Build<TestEntity>(rule);
        var compiled = expression.Compile();

        // Assert
        Assert.True(compiled(new TestEntity { Status = TestStatus.Published }));
        Assert.False(compiled(new TestEntity { Status = TestStatus.Draft }));
    }

    #endregion

    #region 错误处理测试

    [Fact]
    public void Build_InvalidProperty_ShouldThrow()
    {
        // Arrange
        var rule = new FilterRule("InvalidProperty", FilterOperator.Equal, "Test");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            FilterExpressionBuilder.Build<TestEntity>(rule));
    }

    [Fact]
    public void Build_ContainsOnNonString_ShouldThrow()
    {
        // Arrange
        var rule = new FilterRule("Age", FilterOperator.Contains, "Test");

        // Act & Assert
        Assert.Throws<NotSupportedException>(() =>
            FilterExpressionBuilder.Build<TestEntity>(rule));
    }

    [Fact]
    public void TryBuild_InvalidRule_ShouldReturnFalse()
    {
        // Arrange
        var group = FilterGroup.And()
            .AddRule("InvalidProperty", FilterOperator.Equal, "Test");

        // Act
        var result = FilterExpressionBuilder.TryBuild<TestEntity>(group, out var expression, out var error);

        // Assert
        Assert.False(result);
        Assert.Null(expression);
        Assert.NotNull(error);
    }

    #endregion
}