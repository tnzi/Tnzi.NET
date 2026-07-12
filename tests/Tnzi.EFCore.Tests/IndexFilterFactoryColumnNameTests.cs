namespace Tnzi.EFCore.Tests;

/// <summary>
/// Task 4：IndexFilterFactory 自定义列名重载测试。
/// 消费者用 HasColumnName / snake_case 命名约定时，过滤器 SQL 必须指向实际列名，而非属性名。
/// </summary>
public class IndexFilterFactoryColumnNameTests
{
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[is_deleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"is_deleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`is_deleted` = FALSE")]
    [InlineData(DatabaseProvider.Sqlite, "\"is_deleted\" = 0")]
    public void GetIsDeletedFalse_WithCustomColumn_ShouldQuoteAndCompare(DatabaseProvider provider, string expected)
    {
        var sql = IndexFilterFactory.GetIsDeletedFalse("is_deleted", provider);
        Assert.Equal(expected, sql);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[biz_code] IS NOT NULL AND [is_deleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"biz_code\" IS NOT NULL AND \"is_deleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`biz_code` IS NOT NULL AND `is_deleted` = FALSE")]
    [InlineData(DatabaseProvider.Sqlite, "\"biz_code\" IS NOT NULL AND \"is_deleted\" = 0")]
    public void GetCodeNotNullAndIsDeletedFalse_WithCustomColumns_ShouldQuoteBoth(DatabaseProvider provider, string expected)
    {
        var sql = IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse("biz_code", "is_deleted", provider);
        Assert.Equal(expected, sql);
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[phone] IS NOT NULL AND [is_deleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"phone\" IS NOT NULL AND \"is_deleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`phone` IS NOT NULL AND `is_deleted` = FALSE")]
    [InlineData(DatabaseProvider.Sqlite, "\"phone\" IS NOT NULL AND \"is_deleted\" = 0")]
    public void GetColumnNotNullAndIsDeletedFalse_WithCustomIsDeletedColumn_ShouldQuoteBoth(DatabaseProvider provider, string expected)
    {
        var sql = IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("phone", "is_deleted", provider);
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void CustomColumnOverload_ShouldMatchDefault_WhenColumnEqualsPropertyName()
    {
        // 传入与属性名相同的列名时，应与硬编码的默认重载结果完全一致（向后兼容）
        foreach (var provider in IndexFilterFactory.GetSupportedProviders())
        {
            Assert.Equal(
                IndexFilterFactory.GetIsDeletedFalse(provider),
                IndexFilterFactory.GetIsDeletedFalse("IsDeleted", provider));

            Assert.Equal(
                IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse(provider),
                IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse("Code", "IsDeleted", provider));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetIsDeletedFalse_WithBlankColumn_ShouldThrow(string? column)
    {
        Assert.ThrowsAny<ArgumentException>(() => IndexFilterFactory.GetIsDeletedFalse(column!, DatabaseProvider.Sqlite));
    }
}
