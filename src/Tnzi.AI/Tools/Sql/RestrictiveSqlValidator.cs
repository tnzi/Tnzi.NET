using System.Collections.Frozen;
using System.IO;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Default <see cref="ISqlValidator"/>. T-SQL statements are validated by walking the AST
/// produced by <see cref="TSql160Parser"/>; only single SELECT/CTE statements are allowed
/// and a deny-list of dangerous functions/procedures (xp_*/sp_oacreate/OPENROWSET/...) is
/// enforced. Non-T-SQL dialects fall back to a tokenizer that strips string literals and
/// comments before keyword matching, and is gated behind <see cref="SqlToolOptions.AllowNonTSqlDialects"/>.
/// </summary>
public sealed partial class RestrictiveSqlValidator : ISqlValidator
{
    private readonly SqlToolOptions _options;

    /// <summary>Function names that allow data exfiltration, file IO, sleep/DoS, or remote execution.</summary>
    private static readonly FrozenSet<string> DangerousFunctions = new[]
    {
        // PostgreSQL
        "pg_read_file", "pg_read_binary_file", "pg_write_file", "pg_sleep", "pg_sleep_for", "pg_sleep_until",
        "pg_terminate_backend", "pg_cancel_backend", "pg_ls_dir", "pg_stat_file",
        "lo_import", "lo_export", "lo_create",
        "dblink", "dblink_exec", "dblink_connect",
        // MySQL
        "load_file", "sleep", "benchmark", "get_lock", "release_lock",
        // SQL Server (callable as functions)
        "openrowset", "opendatasource", "openquery", "openxml",
        "fn_xe_file_target_read_file", "fn_get_audit_file", "fn_trace_gettable",
        // Cross-DB shell-ish
        "system", "exec", "execute_immediate", "shell"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Procedure name prefixes that imply risky/system-level operations.</summary>
    private static readonly string[] DangerousProcedurePrefixes =
    {
        "xp_", "sp_oacreate", "sp_oamethod", "sp_oadestroy", "sp_oasetproperty",
        "sp_configure", "sp_executesql", "sp_addextendedproc",
        "sp_addlinkedserver", "sp_serveroption"
    };

    /// <summary>Forbidden top-level keywords for the tokenizer fallback.</summary>
    private static readonly FrozenSet<string> ForbiddenKeywords = new[]
    {
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
        "GRANT", "REVOKE", "EXEC", "EXECUTE", "MERGE", "CALL", "REPLACE",
        "RENAME", "ATTACH", "DETACH", "VACUUM", "PRAGMA", "REINDEX",
        "COPY", "BACKUP", "RESTORE", "SHUTDOWN", "DBCC"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public RestrictiveSqlValidator(IOptions<SqlToolOptions> options)
    {
        _options = Check.NotNull(options).Value;
    }

    public SqlValidationResult Validate(string sql, SqlDialect dialect = SqlDialect.TSql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return new SqlValidationResult(false, "SQL is empty");

        if (sql.Length > _options.MaxSqlLength)
            return new SqlValidationResult(false, $"SQL exceeds max length ({_options.MaxSqlLength} chars)");

        return dialect == SqlDialect.TSql
            ? ValidateTSql(sql)
            : _options.AllowNonTSqlDialects
                ? ValidateNonTSql(sql, dialect)
                : new SqlValidationResult(false,
                    $"Dialect {dialect} validation requires SqlToolOptions.AllowNonTSqlDialects=true and is disabled by default for safety.");
    }

    // ─── T-SQL AST validation ─────────────────────────────────────────────────

    private static SqlValidationResult ValidateTSql(string sql)
    {
        // Pre-parse text guards for constructs ScriptDom does not surface as dedicated AST nodes
        // (OPENDATASOURCE is parsed as a four-part SchemaObjectName, not OpenRowsetTableReference).
        if (PreParseDeniedConstructs().IsMatch(sql))
            return new SqlValidationResult(false, "Denied SQL construct (e.g. OPENDATASOURCE) detected.");

        var parser = new TSql160Parser(initialQuotedIdentifiers: false);
        using var reader = new StringReader(sql);
        var fragment = parser.Parse(reader, out var errors);

        if (errors is { Count: > 0 })
            return new SqlValidationResult(false, $"SQL parse error at L{errors[0].Line} C{errors[0].Column}: {errors[0].Message}");

        if (fragment is not TSqlScript script)
            return new SqlValidationResult(false, "Unexpected AST root node");

        if (script.Batches.Count != 1)
            return new SqlValidationResult(false, $"Multi-batch SQL rejected ({script.Batches.Count} batches)");

        var batch = script.Batches[0];
        if (batch.Statements.Count != 1)
            return new SqlValidationResult(false, $"Multi-statement SQL rejected ({batch.Statements.Count} statements)");

        var stmt = batch.Statements[0];
        if (stmt is not SelectStatement)
            return new SqlValidationResult(false, $"Only SELECT statements allowed; got {stmt.GetType().Name}");

        var visitor = new DangerousNodeVisitor();
        stmt.Accept(visitor);
        return visitor.RejectionReason is not null
            ? new SqlValidationResult(false, visitor.RejectionReason)
            : new SqlValidationResult(true, null);
    }

    private sealed class DangerousNodeVisitor : TSqlFragmentVisitor
    {
        public string? RejectionReason { get; private set; }

        private void Reject(string reason) => RejectionReason ??= reason;

        public override void Visit(FunctionCall node)
        {
            var name = node.FunctionName?.Value;
            if (name is null) return;

            if (DangerousFunctions.Contains(name))
            {
                Reject($"Function '{name}' is denied by SQL safety policy.");
                return;
            }

            foreach (var prefix in DangerousProcedurePrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    Reject($"Function '{name}' has a denied prefix '{prefix}'.");
                    return;
                }
            }
        }

        public override void Visit(SchemaObjectFunctionTableReference node)
        {
            var baseId = node.SchemaObject?.BaseIdentifier?.Value;
            if (baseId is not null && DangerousFunctions.Contains(baseId))
            {
                Reject($"Table-valued function '{baseId}' is denied by SQL safety policy.");
            }
        }

        public override void Visit(OpenRowsetTableReference node) => Reject("OPENROWSET is denied.");

        public override void Visit(OpenQueryTableReference node) => Reject("OPENQUERY is denied.");

        public override void Visit(OpenXmlTableReference node) => Reject("OPENXML is denied.");

        public override void Visit(ExecuteStatement node) => Reject("EXECUTE statements are not allowed.");

        public override void Visit(ExecuteSpecification node) => Reject("EXECUTE specifications are not allowed.");
    }

    // ─── Non-T-SQL tokenizer fallback ─────────────────────────────────────────

    private static SqlValidationResult ValidateNonTSql(string sql, SqlDialect dialect)
    {
        var stripped = StripLiteralsAndComments(sql);
        var trimmed = stripped.Trim();
        if (trimmed.Length == 0)
            return new SqlValidationResult(false, "SQL is empty after stripping comments/literals");

        var upper = trimmed.ToUpperInvariant();

        if (!upper.StartsWith("SELECT", StringComparison.Ordinal) && !upper.StartsWith("WITH ", StringComparison.Ordinal))
            return new SqlValidationResult(false, $"Only SELECT/WITH allowed; got: {trimmed[..Math.Min(20, trimmed.Length)]}");

        var trimEnd = trimmed.TrimEnd();
        var idx = trimEnd.IndexOf(';');
        if (idx >= 0 && idx < trimEnd.Length - 1)
            return new SqlValidationResult(false, "Multi-statement SQL rejected");

        var tokens = upper.Split(
            new[] { ' ', '\t', '\n', '\r', '(', ')', ',', ';', '\'', '"', '`', '[', ']' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (ForbiddenKeywords.Contains(token))
                return new SqlValidationResult(false, $"Forbidden keyword: {token}");
            if (DangerousFunctions.Contains(token))
                return new SqlValidationResult(false, $"Dangerous function: {token}");

            foreach (var prefix in DangerousProcedurePrefixes)
            {
                if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new SqlValidationResult(false, $"Dangerous procedure prefix: {token}");
            }
        }

        if (IntoOutFilePattern().IsMatch(upper))
            return new SqlValidationResult(false, "INTO OUTFILE / INTO DUMPFILE is denied.");

        if (CopyToFromPattern().IsMatch(upper))
            return new SqlValidationResult(false, "COPY ... TO/FROM is denied.");

        return new SqlValidationResult(true, null);
    }

    /// <summary>
    /// Strips comments and string-literal interiors from SQL so the tokenizer fallback
    /// only sees structural keywords. Handles:
    /// <list type="bullet">
    ///   <item>Block comments <c>/* ... */</c></item>
    ///   <item>Line comments <c>-- ...</c> (ANSI) and <c>#...</c> (MySQL)</item>
    ///   <item>Single-quoted string literals - both <c>''</c> doubled-quote escape (ANSI/PG/SQL Server)
    ///         and backslash escapes (<c>\'</c>, <c>\\</c>) used by MySQL in default mode.</item>
    /// </list>
    /// Single quotes themselves are preserved so token boundaries survive.
    /// <para>Known limitations (documented for the tokenizer fallback path only - gated behind
    /// <see cref="SqlToolOptions.AllowNonTSqlDialects"/>=true): PostgreSQL dollar-quoted strings
    /// (<c>$tag$...$tag$</c>) are NOT stripped; do not rely on the tokenizer if your AI surface
    /// can produce them.</para>
    /// </summary>
    internal static string StripLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var i = 0;
        while (i < sql.Length)
        {
            var ch = sql[i];

            if (ch == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) break;
                sb.Append(' ');
                i = end + 2;
                continue;
            }

            // ANSI -- and MySQL # line comments - both run to end-of-line.
            if ((ch == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
                || ch == '#')
            {
                var end = sql.IndexOf('\n', i + 1);
                sb.Append(' ');
                i = end < 0 ? sql.Length : end + 1;
                continue;
            }

            if (ch == '\'')
            {
                sb.Append('\'');
                i++;
                while (i < sql.Length)
                {
                    // MySQL backslash escape - skip the following char without treating it as a delimiter.
                    if (sql[i] == '\\' && i + 1 < sql.Length)
                    {
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'')
                    {
                        // ANSI doubled-quote escape - two single quotes inside a literal.
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }
                        sb.Append('\'');
                        i++;
                        break;
                    }
                    i++;
                }
                continue;
            }

            sb.Append(ch);
            i++;
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"\bINTO\s+(OUTFILE|DUMPFILE)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex IntoOutFilePattern();

    [GeneratedRegex(@"\bCOPY\s+\w+\s+(TO|FROM)\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex CopyToFromPattern();

    /// <summary>
    /// Pre-parse text guard for T-SQL constructs that ScriptDom does not surface as dedicated
    /// AST visitor nodes (so the visitor cannot reject them). Run before the parser to fail fast.
    /// </summary>
    [GeneratedRegex(@"\bOPENDATASOURCE\s*\(", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex PreParseDeniedConstructs();
}
