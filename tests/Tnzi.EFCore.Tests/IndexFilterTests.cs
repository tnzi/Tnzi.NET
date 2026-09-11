namespace Tnzi.EFCore.Tests;

/// <summary>
/// <see cref="IndexFilter"/> 的跨库渲染与组合。
/// </summary>
/// <remarks>
/// 每条断言都<b>逐字</b>比对 SQL 而不是「包含某个片段」：这个类型存在的全部理由就是标识符引用
/// 与布尔字面量按库而异，一条只查子串的断言会把 PostgreSQL 的引号漏进 SQL Server 的分支而不报错。
/// </remarks>
public class IndexFilterTests
{
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[IsDeleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"IsDeleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`IsDeleted` = FALSE")]
    [InlineData(DatabaseProvider.Sqlite, "\"IsDeleted\" = 0")]
    public void NotDeleted_RendersTheProviderBooleanLiteral(DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, IndexFilter.NotDeleted().Build(provider));
    }

    /// <summary>
    /// 这一格是本类型的直接动因：工厂里没有它，于是消费方手拼了 PostgreSQL 的引号。
    /// </summary>
    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[TokenHash] <> '' AND [IsDeleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"TokenHash\" <> '' AND \"IsDeleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`TokenHash` <> '' AND `IsDeleted` = FALSE")]
    [InlineData(DatabaseProvider.Sqlite, "\"TokenHash\" <> '' AND \"IsDeleted\" = 0")]
    public void NotEmpty_AndNotDeleted_IsTheCaseTheFactoryCouldNotExpress(
        DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, IndexFilter.NotEmpty("TokenHash").AndNotDeleted().Build(provider));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[CorrelationCode] IS NOT NULL AND [IsDeleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"CorrelationCode\" IS NOT NULL AND \"IsDeleted\" = false")]
    public void NotNull_AndNotDeleted(DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, IndexFilter.NotNull("CorrelationCode").AndNotDeleted().Build(provider));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[Status] = 0 AND [IsDeleted] = 0")]
    [InlineData(DatabaseProvider.MySql, "`Status` = 0 AND `IsDeleted` = FALSE")]
    public void EqualTo_AndNotDeleted(DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, IndexFilter.EqualTo("Status", 0).AndNotDeleted().Build(provider));
    }

    [Theory]
    [InlineData(DatabaseProvider.SqlServer, "[IsDefault] = 1 AND [IsDeleted] = 0")]
    [InlineData(DatabaseProvider.PostgreSQL, "\"IsDefault\" = true AND \"IsDeleted\" = false")]
    [InlineData(DatabaseProvider.MySql, "`IsDefault` = TRUE AND `IsDeleted` = FALSE")]
    public void IsTrue_UsesTheSameBooleanTableAsTheFactory(DatabaseProvider provider, string expected)
    {
        Assert.Equal(expected, IndexFilter.IsTrue("IsDefault").AndNotDeleted().Build(provider));

        // 同一份字面量表：两处若各存一份，对不上的那天不会有任何东西报错。
        Assert.Equal(IndexFilterFactory.GetColumnTrueAndIsDeletedFalse("IsDefault", provider),         IndexFilter.IsTrue("IsDefault").AndNotDeleted().Build(provider));
    }

    [Fact]
    public void Null_IsThePairOfNotNull()
    {
        Assert.Equal("\"Code\" IS NULL", IndexFilter.Null("Code").Build(DatabaseProvider.PostgreSQL));
        Assert.Equal("\"Code\" IS NOT NULL", IndexFilter.NotNull("Code").Build(DatabaseProvider.PostgreSQL));
    }

    [Fact]
    public void NotEqualTo_DelegatesToTheFactory()
    {
        Assert.Equal(IndexFilterFactory.GetColumnNotEquals("Status", 3, DatabaseProvider.SqlServer),         IndexFilter.NotEqualTo("Status", 3).Build(DatabaseProvider.SqlServer));
    }

    [Fact]
    public void And_ChainsAnyNumberOfTerms_InOrder()
    {
        var sql = IndexFilter.NotNull("A")
            .And(IndexFilter.NotEmpty("B"))
            .And(IndexFilter.EqualTo("C", 7))
            .AndNotDeleted()
            .Build(DatabaseProvider.PostgreSQL);

        Assert.Equal("\"A\" IS NOT NULL AND \"B\" <> '' AND \"C\" = 7 AND \"IsDeleted\" = false", sql);
    }

    /// <summary>
    /// 不可变：链上的中间值可以复用，续两条不同的链互不影响。
    /// </summary>
    [Fact]
    public void EachStep_ReturnsANewInstance()
    {
        var shared = IndexFilter.NotNull("A");

        var one = shared.And(IndexFilter.EqualTo("B", 1));
        var two = shared.And(IndexFilter.EqualTo("C", 2));

        Assert.Equal("\"A\" IS NOT NULL", shared.Build(DatabaseProvider.Sqlite));
        Assert.Equal("\"A\" IS NOT NULL AND \"B\" = 1", one.Build(DatabaseProvider.Sqlite));
        Assert.Equal("\"A\" IS NOT NULL AND \"C\" = 2", two.Build(DatabaseProvider.Sqlite));
    }

    [Fact]
    public void Raw_IsTheEscapeHatch_AndStillGetsTheProvider()
    {
        var sql = IndexFilter.NotDeleted()
            .And(IndexFilter.Raw(p => $"{IndexFilterFactory.QuoteIdentifier("Len", p)} > 3"))
            .Build(DatabaseProvider.MySql);

        Assert.Equal("`IsDeleted` = FALSE AND `Len` > 3", sql);
    }

    [Fact]
    public void BlankColumnName_IsRejectedAtCompositionTime_NotAtRenderTime()
    {
        // 渲染是延迟的，所以一个空列名若不在这里拦下，会变成一句语法错误的 SQL
        // 出现在迁移里，而不是一条指着调用点的异常。
        Assert.Throws<ArgumentException>(() => IndexFilter.NotEmpty(" "));
        Assert.Throws<ArgumentException>(() => IndexFilter.NotNull(""));
        Assert.Throws<ArgumentException>(() => IndexFilter.EqualTo(" ", 1));
    }

    [Fact]
    public void UnsupportedProvider_Throws_RatherThanRenderingSomethingPlausible()
    {
        Assert.Throws<NotSupportedException>(
            () => IndexFilter.NotDeleted().Build((DatabaseProvider)999));
    }
}
