using Tnzi.Payment.Options;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PaymentOptions 配置中心特性测试 - 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
/// </summary>
public class PaymentSettingDefinitionProviderTests
{
    private readonly SettingDefinitionGroup _group =
        RuntimeSettingMetadataExtractor.Extract(typeof(PaymentOptions))!;

    [Fact]
    public void Extract_ReturnsNonNull()
    {
        Assert.NotNull(_group);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        Assert.Equal("payment-general", _group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        Assert.Equal("Payment", _group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        Assert.Equal(500, _group.Order);
    }

    [Fact]
    public void Group_HasExpectedFieldCount()
    {
        // 5 个退款/通知字段 + 后台任务运营字段（AutoCloseExpireMinutes / OfflineExpireDays /
        // BackgroundTaskIntervalMinutes / RefundReconcileLookbackDays）+ 渠道与回跳字段
        // （DefaultChannelCode / DefaultReturnUrl）。全部经父 IOptionsMonitor<PaymentOptions> 热消费。
        Assert.Equal(11, _group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _group.Fields;
        Assert.Contains(fields, f => f.Key == "Payment:DefaultCurrency");
        Assert.Contains(fields, f => f.Key == "Payment:DefaultChannelCode");
        Assert.Contains(fields, f => f.Key == "Payment:DefaultReturnUrl");
        Assert.Contains(fields, f => f.Key == "Payment:DefaultNotifyUrl");
        Assert.Contains(fields, f => f.Key == "Payment:EnableRefundApproval");
        Assert.Contains(fields, f => f.Key == "Payment:RefundApprovalThreshold");
        Assert.Contains(fields, f => f.Key == "Payment:MaxRefundAmountPerDay");
        Assert.Contains(fields, f => f.Key == "Payment:AutoCloseExpireMinutes");
        Assert.Contains(fields, f => f.Key == "Payment:OfflineExpireDays");
        Assert.Contains(fields, f => f.Key == "Payment:BackgroundTaskIntervalMinutes");
        Assert.Contains(fields, f => f.Key == "Payment:RefundReconcileLookbackDays");
    }

    /// <summary>
    /// 税务配置曾被标注 KEEP-STATIC（无运行时消费者）。接线 IPaymentTaxCalculator 后它才允许出现在配置中心，
    /// 这条测试守住"暴露的配置必须真的生效"这个约定。
    /// </summary>
    [Fact]
    public void TaxGroup_IsExposedWithConsumedFields()
    {
        var taxGroup = RuntimeSettingMetadataExtractor.Extract(typeof(TaxOptions));

        Assert.NotNull(taxGroup);
        Assert.Equal("payment-tax", taxGroup!.Key);
        Assert.Contains(taxGroup.Fields, f => f.Key == "Payment:Tax:Enabled");
        Assert.Contains(taxGroup.Fields, f => f.Key == "Payment:Tax:DefaultTaxRate");
        Assert.Contains(taxGroup.Fields, f => f.Key == "Payment:Tax:TaxIncluded");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var fields = _group.Fields;
        Assert.Equal(SettingFieldType.String, fields.First(f => f.Key == "Payment:DefaultCurrency").Type);
        Assert.Equal(SettingFieldType.String, fields.First(f => f.Key == "Payment:DefaultNotifyUrl").Type);
        Assert.Equal(SettingFieldType.Boolean, fields.First(f => f.Key == "Payment:EnableRefundApproval").Type);
        Assert.Equal(SettingFieldType.Decimal, fields.First(f => f.Key == "Payment:RefundApprovalThreshold").Type);
        Assert.Equal(SettingFieldType.Decimal, fields.First(f => f.Key == "Payment:MaxRefundAmountPerDay").Type);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var fields = _group.Fields;

        var currency = fields.First(f => f.Key == "Payment:DefaultCurrency");
        Assert.NotNull(currency.DefaultValueAccessor);
        Assert.Equal("USD", currency.DefaultValueAccessor!());

        var approval = fields.First(f => f.Key == "Payment:EnableRefundApproval");
        Assert.NotNull(approval.DefaultValueAccessor);
        Assert.Equal("True", approval.DefaultValueAccessor!());

        var threshold = fields.First(f => f.Key == "Payment:RefundApprovalThreshold");
        Assert.NotNull(threshold.DefaultValueAccessor);
        Assert.Equal("1000", threshold.DefaultValueAccessor!());

        var maxRefund = fields.First(f => f.Key == "Payment:MaxRefundAmountPerDay");
        Assert.NotNull(maxRefund.DefaultValueAccessor);
        Assert.Equal("10000", maxRefund.DefaultValueAccessor!());
    }

    [Fact]
    public void Fields_HaveI18nKeys()
    {
        Assert.All(_group.Fields, f => Assert.NotNull(f.I18nKey));
    }
}
