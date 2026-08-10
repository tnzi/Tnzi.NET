namespace Tnzi.Audit.Options;

/// <summary>
/// <see cref="DataDestructionOptions"/> 的启动期验证器。
/// </summary>
/// <remarks>
/// 这项能力会永久删除数据，配置写错的代价不对称：间隔配成 0 会让它不停地扫，
/// 批量配成 0 会让它看起来在跑却永远删不掉任何东西（后者更危险，因为它"没有报错"）。
/// </remarks>
public class DataDestructionOptionsValidator : OptionsValidatorBase<DataDestructionOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(DataDestructionOptions options, List<string> errors)
    {
        // 未启用时不校验其余字段：允许配置里留着空壳节点。
        if (!options.Enabled)
        {
            return;
        }

        if (options.IntervalHours <= 0)
        {
            AddError(errors, nameof(options.IntervalHours), "must be greater than zero.");
        }

        if (options.BatchSize <= 0)
        {
            AddError(errors, nameof(options.BatchSize), "must be greater than zero.");
        }
    }
}
