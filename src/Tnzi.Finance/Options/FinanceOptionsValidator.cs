namespace Tnzi.Finance.Options;

/// <summary>
/// 财务模块配置验证器
/// </summary>
public class FinanceOptionsValidator : OptionsValidatorBase<FinanceOptions>
{
    protected override void ValidateOptions(FinanceOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.BaseCurrency))
            errors.Add("BaseCurrency is required.");
        else if (options.BaseCurrency.Length > 8)
            errors.Add("BaseCurrency must not exceed 8 characters.");

        if (options.BaseCurrencyDecimals is < 0 or > 4)
            errors.Add("BaseCurrencyDecimals must be between 0 and 4.");

        // 账龄切分点：留空 = 用默认 30/60/90；配了就必须是完整的三个切分点，
        // 拒绝而不是静默回退——悄悄用回默认值会让人以为自己配的口径生效了。
        var cuts = options.AgingBucketDays;
        if (cuts is { Length: > 0 })
        {
            if (cuts.Length != 3)
                errors.Add("AgingBucketDays must be empty (to use 30/60/90) or contain exactly three cut-offs (the bucket count is fixed at five).");
            else if (cuts[0] <= 0 || cuts[1] <= cuts[0] || cuts[2] <= cuts[1])
                errors.Add("AgingBucketDays must be positive and strictly ascending, e.g. [30, 60, 90].");
        }

        if (options.DunningOverdueDays <= 0)
            errors.Add("DunningOverdueDays must be greater than zero.");
        if (options.DunningFinalNoticeDays <= options.DunningOverdueDays)
            errors.Add("DunningFinalNoticeDays must be greater than DunningOverdueDays.");
        if (options.DunningMinimumAmount < 0)
            errors.Add("DunningMinimumAmount must not be negative.");

        if (options.MaxAttachmentsPerDocument <= 0)
            errors.Add("MaxAttachmentsPerDocument must be greater than zero.");

        if (options.RoundingTolerance < 0)
            errors.Add("RoundingTolerance cannot be negative.");

        // 报表按凭证号字符串排序依赖补零（无补零时 "JE-10" 会排在 "JE-9" 之前）
        if (options.JournalNumberPadding is < 1 or > 12)
            errors.Add("JournalNumberPadding must be between 1 and 12.");

        if (options.MaxLinesPerEntry <= 1)
            errors.Add("MaxLinesPerEntry must be greater than 1.");

        if (options.DefaultPaymentTermsDays < 0)
            errors.Add("DefaultPaymentTermsDays cannot be negative.");

        if (options.ReportExportMaxRows <= 0)
            errors.Add("ReportExportMaxRows must be greater than 0.");

        if (options.BankMatchDateWindowDays is < 0 or > 90)
            errors.Add("BankMatchDateWindowDays must be between 0 and 90.");

        if (options.BankImportMaxRows <= 0)
            errors.Add("BankImportMaxRows must be greater than 0.");
    }
}
