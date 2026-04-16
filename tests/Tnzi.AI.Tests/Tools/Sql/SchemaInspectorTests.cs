using System.Threading.Tasks;
using Tnzi.AI.Tools.Sql;
using Xunit;

namespace Tnzi.AI.Tests.Tools.Sql;

public class SchemaInspectorTests
{
    [Fact]
    public async Task ListTables_UsesInformationSchema()
    {
        var fake = new FakeSqlExecutor(new QueryResult(
            new[] { new QueryColumn("table_schema", "string"), new QueryColumn("table_name", "string"), new QueryColumn("comment", "string") },
            new[] { new object?[] { "public", "users", null } },
            false, 1, ""));
        var inspector = new SchemaInspector(fake);
        var tables = await inspector.ListTablesAsync();
        Assert.Single(tables);
        Assert.Equal("users", tables[0].Name);
    }

    [Fact]
    public async Task ListTables_ReturnsEmptyWhenNoRows()
    {
        var fake = new FakeSqlExecutor(new QueryResult(
            new[] { new QueryColumn("table_schema", "string"), new QueryColumn("table_name", "string"), new QueryColumn("comment", "string") },
            System.Array.Empty<object?[]>(),
            false, 1, ""));
        var inspector = new SchemaInspector(fake);
        var tables = await inspector.ListTablesAsync();
        Assert.Empty(tables);
    }

    [Fact]
    public async Task ListColumns_RejectsUnsafeTableName()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = new SchemaInspector(fake);
        await Assert.ThrowsAsync<ArgumentException>(
            () => inspector.ListColumnsAsync("users; DROP TABLE users--"));
    }

    [Fact]
    public async Task ListDistinctValues_RejectsUnsafeIdentifiers()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = new SchemaInspector(fake);
        await Assert.ThrowsAsync<ArgumentException>(
            () => inspector.ListDistinctValuesAsync("users", "col' OR '1'='1"));
    }

    private sealed class FakeSqlExecutor(QueryResult result) : IReadOnlySqlExecutor
    {
        public Task<QueryResult> ExecuteAsync(string sql, ReadOnlySqlExecutionOptions? options = null, System.Threading.CancellationToken ct = default)
            => Task.FromResult(result);
    }
}

public class HeuristicSqlColumnInferrerTests
{
    private readonly HeuristicSqlColumnInferrer _sut = new();

    [Theory]
    [InlineData("CreatedAt", "date")]
    [InlineData("created_at", "date")]
    [InlineData("StartDate", "date")]
    [InlineData("start_date", "date")]
    [InlineData("ExpiryTime", "date")]
    public void Infer_DateColumns(string columnName, string expectedType)
    {
        var result = _sut.Infer("SELECT 1", [columnName]);
        Assert.Equal(expectedType, result[0].InferredType);
    }

    [Theory]
    [InlineData("IsActive", "boolean")]
    [InlineData("is_active", "boolean")]
    [InlineData("HasChildren", "boolean")]
    [InlineData("has_children", "boolean")]
    public void Infer_BooleanColumns(string columnName, string expectedType)
    {
        var result = _sut.Infer("SELECT 1", [columnName]);
        Assert.Equal(expectedType, result[0].InferredType);
    }

    [Theory]
    [InlineData("TotalAmount", "currency")]
    [InlineData("BasePay", "currency")]
    [InlineData("CommissionFee", "currency")]
    [InlineData("GrossRevenue", "currency")]
    public void Infer_CurrencyColumns(string columnName, string expectedType)
    {
        var result = _sut.Infer("SELECT 1", [columnName]);
        Assert.Equal(expectedType, result[0].InferredType);
    }

    [Theory]
    [InlineData("CandidateName", "string")]
    [InlineData("Description", "string")]
    [InlineData("Notes", "string")]
    public void Infer_DefaultsToString(string columnName, string expectedType)
    {
        var result = _sut.Infer("SELECT 1", [columnName]);
        Assert.Equal(expectedType, result[0].InferredType);
    }

    [Fact]
    public void Infer_PreservesColumnNames()
    {
        var result = _sut.Infer("SELECT id, name FROM t", ["id", "name"]);
        Assert.Equal(2, result.Count);
        Assert.Equal("id", result[0].Name);
        Assert.Equal("name", result[1].Name);
    }

    [Fact]
    public void Infer_EmptyColumns_ReturnsEmpty()
    {
        var result = _sut.Infer("SELECT 1", []);
        Assert.Empty(result);
    }
}
