namespace Tnzi.Payment.Options;

/// <summary>
/// <see cref="BillingAccountOptions"/> 验证器
/// </summary>
public class BillingAccountOptionsValidator : OptionsValidatorBase<BillingAccountOptions>
{
    protected override void ValidateOptions(BillingAccountOptions options, List<string> errors)
    {
        if (!Enum.IsDefined(options.Scope))
            errors.Add("Payment:BillingAccount:Scope must be User, Installation or Tenant.");

        if (options.Scope == BillingAccountScope.Installation)
        {
            if (string.IsNullOrWhiteSpace(options.InstallationAccountId))
                errors.Add("Payment:BillingAccount:InstallationAccountId is required when Scope is Installation.");
            else if (options.InstallationAccountId.Trim().Length > BillingAccountRef.IdMaxLength)
                errors.Add($"Payment:BillingAccount:InstallationAccountId cannot exceed {BillingAccountRef.IdMaxLength} characters.");
        }
    }
}
