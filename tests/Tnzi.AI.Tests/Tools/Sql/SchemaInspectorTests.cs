using System.Threading.Tasks;
using Tnzi.AI.Tools.Sql;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Tools.Sql;

public class SchemaInspectorTests
{
    private static SchemaInspector CreateInspector(IReadOnlySqlExecutor executor, SqlDialect dialect = SqlDialect.TSql)
    {
        var providers = new ISqlSchemaProvider[]
        {
            new TSqlSchemaProvider(),
            new PostgreSqlSchemaProvider(),
            new MySqlSchemaProvider(),
            new SqliteSchemaProvider()
        };
        var options = MsOptions.Create(new SqlToolOptions { DefaultDialect = dialect, AllowNonTSqlDialects = true });
        return new SchemaInspector(executor, providers, options);
    }

    [Fact]
    public async Task ListTables_UsesInformationSchema()
    {
        var fake = new FakeSqlExecutor(new QueryResult(
            new[] { new QueryColumn("table_schema", "string"), new QueryColumn("table_name", "string"), new QueryColumn("comment", "string") },
            new[] { new object?[] { "public", "users", null } },
            false, 1, ""));
        var inspector = CreateInspector(fake);
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
        var inspector = CreateInspector(fake);
        var tables = await inspector.ListTablesAsync();
        Assert.Empty(tables);
    }

    [Fact]
    public async Task ListColumns_RejectsUnsafeTableName()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = CreateInspector(fake);
        await Assert.ThrowsAsync<ArgumentException>(
            () => inspector.ListColumnsAsync("users; DROP TABLE users--"));
    }

    [Fact]
    public async Task ListDistinctValues_RejectsUnsafeIdentifiers()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = CreateInspector(fake);
        await Assert.ThrowsAsync<ArgumentException>(
            () => inspector.ListDistinctValuesAsync("users", "col' OR '1'='1"));
    }

    [Theory]
    [InlineData(SqlDialect.TSql, "[users]", "TOP 5")]
    [InlineData(SqlDialect.PostgreSql, "\"users\"", "LIMIT 5")]
    [InlineData(SqlDialect.MySql, "`users`", "LIMIT 5")]
    [InlineData(SqlDialect.Sqlite, "\"users\"", "LIMIT 5")]
    public async Task ListDistinctValues_GeneratesDialectSpecificSql(
        SqlDialect dialect, string expectedTableQuoting, string expectedLimit)
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = CreateInspector(fake, dialect);
        await inspector.ListDistinctValuesAsync("users", "col", limit: 5);

        Assert.Contains(expectedTableQuoting, fake.LastSql);
        Assert.Contains(expectedLimit, fake.LastSql);
    }

    [Fact]
    public async Task SqliteDialect_UsesPragmaTableInfo_NotInformationSchema()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = CreateInspector(fake, SqlDialect.Sqlite);
        await inspector.ListColumnsAsync("users");
        Assert.Contains("pragma_table_info", fake.LastSql);
        Assert.DoesNotContain("information_schema", fake.LastSql);
    }

    [Fact]
    public async Task SqliteDialect_ListTables_UsesSqliteMaster()
    {
        var fake = new FakeSqlExecutor(new QueryResult([], [], false, 0, ""));
        var inspector = CreateInspector(fake, SqlDialect.Sqlite);
        await inspector.ListTablesAsync();
        Assert.Contains("sqlite_master", fake.LastSql);
        Assert.DoesNotContain("information_schema", fake.LastSql);
    }

    private sealed class FakeSqlExecutor(QueryResult result) : IReadOnlySqlExecutor
    {
        public string LastSql { get; private set; } = "";

        public Task<QueryResult> ExecuteAsync(string sql, ReadOnlySqlExecutionOptions? options = null, System.Threading.CancellationToken ct = default)
        {
            LastSql = sql;
            return Task.FromResult(result);
        }
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

    [Fact]
    public void Infer_AppliesCustomRulesFirst()
    {
        // Apps can register domain-specific rules (e.g. Cpp/Wsib for Canadian payroll).
        var customRule = new TestRule(name => name == "Cpp" ? "currency" : null);
        var sut = new HeuristicSqlColumnInferrer(new[] { customRule });

        var result = sut.Infer("SELECT 1", ["Cpp", "Random"]);
        Assert.Equal("currency", result[0].InferredType);
        Assert.Equal("string", result[1].InferredType);
    }

    private sealed class TestRule(Func<string, string?> impl) : ISqlColumnInferenceRule
    {
        public string? TryInferType(string columnName) => impl(columnName);
    }
}
