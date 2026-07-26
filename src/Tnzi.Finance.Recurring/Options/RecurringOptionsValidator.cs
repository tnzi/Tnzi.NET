namespace Tnzi.Finance.Recurring.Options;

/// <summary>
/// 周期性单据配置校验
/// </summary>
public class RecurringOptionsValidator : OptionsValidatorBase<RecurringOptions>
{
    protected override void ValidateOptions(RecurringOptions options, List<string> errors)
    {
        if (options.MaxCatchUpPerRun <= 0)
            errors.Add("MaxCatchUpPerRun must be greater than zero.");
        if (options.MaxCatchUpPerRun > 500)
            errors.Add("MaxCatchUpPerRun above 500 would let a mis-configured anchor date flood the ledger.");
        if (options.SweepIntervalMinutes is > 0 and < 5)
            errors.Add("SweepIntervalMinutes below 5 sweeps far more often than any billing cycle needs; use 0 to drive it externally.");
    }
}
