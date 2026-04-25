namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Application-supplied rule that maps a column name to a semantic type
/// (e.g. domain-specific patterns like Canadian payroll terms <c>Cpp</c>/<c>Wsib</c>).
/// Register multiple implementations via DI; <see cref="HeuristicSqlColumnInferrer"/>
/// runs custom rules before its built-in patterns.
/// </summary>
public interface ISqlColumnInferenceRule
{
    /// <summary>
    /// Returns the inferred type name for <paramref name="columnName"/>, or <c>null</c>
    /// if this rule does not match (allowing the next rule / built-in patterns to run).
    /// </summary>
    string? TryInferType(string columnName);
}
