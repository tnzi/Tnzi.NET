
namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Options for the AI SQL tool suite (validator, executor, schema inspector).
/// Bind from configuration section <c>AI:Sql</c>.
/// </summary>
public sealed class SqlToolOptions
{
    /// <summary>Default SQL dialect when execution callers do not specify one. Default <see cref="SqlDialect.TSql"/>.</summary>
    public SqlDialect DefaultDialect { get; set; } = SqlDialect.TSql;

    /// <summary>
    /// Allow validation of non-T-SQL dialects via the fallback tokenizer.
    /// Default <c>false</c> - only T-SQL passes validation by default because the fallback
    /// has weaker guarantees than a real AST parser.
    /// </summary>
    public bool AllowNonTSqlDialects { get; set; } = false;

    /// <summary>Default row cap when callers do not override. Default 1000.</summary>
    public int DefaultMaxRows { get; set; } = 1000;

    /// <summary>Hard upper bound on caller-supplied <c>MaxRows</c>. Default 10000.</summary>
    public int MaxAllowedMaxRows { get; set; } = 10000;

    /// <summary>Default execution timeout in seconds when callers do not override. Default 30.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>Hard upper bound on caller-supplied <c>TimeoutSeconds</c>. Default 60.</summary>
    public int MaxAllowedTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Aggregate byte budget for serialized result rows; once exceeded the executor stops
    /// reading and marks the result <c>Truncated</c>. Default 10 MB.
    /// </summary>
    public long MaxResultBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>
    /// Maximum SQL text length accepted by the validator. Prevents pathological inputs from
    /// the LLM. Default 64 KB.
    /// </summary>
    public int MaxSqlLength { get; set; } = 64 * 1024;

    /// <summary>
    /// Optional whitelist of named connection strings the executor will accept.
    /// Empty means "any" - production deployments should pin this to dedicated read-only DSNs.
    /// </summary>
    public string[] AllowedConnectionNames { get; set; } = Array.Empty<string>();
}

public sealed class SqlToolOptionsValidator : OptionsValidatorBase<SqlToolOptions>
{
    protected override void ValidateOptions(SqlToolOptions options, List<string> errors)
    {
        if (options.DefaultMaxRows <= 0)
            AddError(errors, nameof(options.DefaultMaxRows), "Must be positive");
        if (options.MaxAllowedMaxRows <= 0)
            AddError(errors, nameof(options.MaxAllowedMaxRows), "Must be positive");
        if (options.DefaultMaxRows > options.MaxAllowedMaxRows)
            AddError(errors, nameof(options.DefaultMaxRows), "Cannot exceed MaxAllowedMaxRows");

        if (options.DefaultTimeoutSeconds <= 0)
            AddError(errors, nameof(options.DefaultTimeoutSeconds), "Must be positive");
        if (options.MaxAllowedTimeoutSeconds <= 0)
            AddError(errors, nameof(options.MaxAllowedTimeoutSeconds), "Must be positive");
        if (options.DefaultTimeoutSeconds > options.MaxAllowedTimeoutSeconds)
            AddError(errors, nameof(options.DefaultTimeoutSeconds), "Cannot exceed MaxAllowedTimeoutSeconds");

        if (options.MaxResultBytes <= 0)
            AddError(errors, nameof(options.MaxResultBytes), "Must be positive");
        if (options.MaxSqlLength <= 0)
            AddError(errors, nameof(options.MaxSqlLength), "Must be positive");
    }
}
