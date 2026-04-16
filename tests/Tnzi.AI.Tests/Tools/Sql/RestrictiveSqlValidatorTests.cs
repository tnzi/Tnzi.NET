using Tnzi.AI.Tools.Sql;
using Xunit;

namespace Tnzi.AI.Tests.Tools.Sql;

public class RestrictiveSqlValidatorTests
{
    private readonly RestrictiveSqlValidator _validator = new();

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("  select id from t ")]
    [InlineData("WITH cte AS (SELECT 1) SELECT * FROM cte")]
    [InlineData("SELECT COUNT(*) FROM orders WHERE created_at > '2024-01-01'")]
    public void Validate_AllowsSelect(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Theory]
    [InlineData("INSERT INTO users VALUES(1)")]
    [InlineData("UPDATE users SET x = 1")]
    [InlineData("DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("ALTER TABLE users ADD COLUMN x INT")]
    [InlineData("TRUNCATE TABLE users")]
    [InlineData("GRANT SELECT ON users TO foo")]
    [InlineData("EXEC sp_something")]
    public void Validate_RejectsNonSelect(string sql)
    {
        var result = _validator.Validate(sql);
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsMultiStatement()
    {
        var result = _validator.Validate("SELECT * FROM users; DELETE FROM users");
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_AllowsTrailingSemicolon()
    {
        var result = _validator.Validate("SELECT 1;");
        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void Validate_RejectsEmpty()
    {
        var result = _validator.Validate("");
        Assert.False(result.IsValid);
    }
}
