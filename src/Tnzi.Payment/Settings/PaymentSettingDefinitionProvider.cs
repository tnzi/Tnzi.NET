namespace Tnzi.Payment.Settings;

/// <summary>
/// Payment 模块内置配置定义 — Payment 组，映射 PaymentOptions（配置节 "Payment"）。
/// 全部字段经 IOptionsMonitor.CurrentValue 运行时消费（PaymentService / RefundService）。
/// </summary>
public class PaymentSettingDefinitionProvider : ISettingDefinitionProvider
{
    private const string I18nBase = "admin.modules.system.settings";

    public IReadOnlyList<SettingDefinitionGroup> GetGroups() =>
    [
        new SettingDefinitionGroup
        {
            Key = "payment-general",
            ModuleName = "Payment",
            DisplayName = "Payment",
            I18nKey = $"{I18nBase}.groups.paymentGeneral",
            Icon = "mdi:credit-card-settings-outline",
            Order = 500,
            Fields =
            [
                new SettingFieldDefinition
                {
                    Key = "Payment:DefaultCurrency", Label = "Default Currency", Type = SettingFieldType.String,
                    I18nKey = $"{I18nBase}.fields.defaultCurrency",
                    DefaultValueAccessor = () => new PaymentOptions().DefaultCurrency,
                },
                new SettingFieldDefinition
                {
                    Key = "Payment:DefaultNotifyUrl", Label = "Default Notify URL", Type = SettingFieldType.String,
                    I18nKey = $"{I18nBase}.fields.defaultNotifyUrl",
                    DefaultValueAccessor = () => new PaymentOptions().DefaultNotifyUrl,
                },
                new SettingFieldDefinition
                {
                    Key = "Payment:EnableRefundApproval", Label = "Enable Refund Approval", Type = SettingFieldType.Boolean,
                    I18nKey = $"{I18nBase}.fields.enableRefundApproval",
                    DefaultValueAccessor = () => new PaymentOptions().EnableRefundApproval.ToString().ToLowerInvariant(),
                },
                new SettingFieldDefinition
                {
                    Key = "Payment:RefundApprovalThreshold", Label = "Refund Approval Threshold", Type = SettingFieldType.Decimal, Min = 0,
                    I18nKey = $"{I18nBase}.fields.refundApprovalThreshold",
                    DefaultValueAccessor = () => new PaymentOptions().RefundApprovalThreshold.ToString(CultureInfo.InvariantCulture),
                },
                new SettingFieldDefinition
                {
                    Key = "Payment:MaxRefundAmountPerDay", Label = "Max Refund Amount Per Day", Type = SettingFieldType.Decimal, Min = 0,
                    I18nKey = $"{I18nBase}.fields.maxRefundAmountPerDay",
                    DefaultValueAccessor = () => new PaymentOptions().MaxRefundAmountPerDay.ToString(CultureInfo.InvariantCulture),
                },
            ],
        },
    ];
}
