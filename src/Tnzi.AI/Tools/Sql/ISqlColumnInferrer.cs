using System.Collections.Generic;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Infers semantic column types for the result set of a SELECT statement,
/// used by UI components (e.g. data tables) to render appropriate cell renderers.
/// </summary>
public interface ISqlColumnInferrer
{
    IReadOnlyList<QueryColumn> Infer(string sql, IReadOnlyList<string> resultColumnNames);
}
