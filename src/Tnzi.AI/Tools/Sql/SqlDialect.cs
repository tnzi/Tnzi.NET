namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// SQL dialect hint used by <see cref="ISqlValidator"/> to choose the right parsing strategy.
/// T-SQL is validated with a full AST parser (Microsoft.SqlServer.TransactSql.ScriptDom);
/// other dialects fall back to a tokenizer that strips literals/comments before keyword checks
/// and is gated behind <see cref="SqlToolOptions.AllowNonTSqlDialects"/>.
/// </summary>
public enum SqlDialect
{
    TSql = 0,
    PostgreSql = 1,
    MySql = 2,
    Sqlite = 3,
    Oracle = 4,
    Other = 99
}
