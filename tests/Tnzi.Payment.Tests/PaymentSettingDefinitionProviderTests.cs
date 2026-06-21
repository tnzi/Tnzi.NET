using Tnzi.Payment.Options;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PaymentOptions 配置中心特性测试 — 验证 [RuntimeSettingGroup]/[RuntimeSetting] 特性派生的分组符合配置中心契约
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
    public void Group_HasFiveFields()
    {
        Assert.Equal(5, _group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _group.Fields;
        Assert.Contains(fields, f => f.Key == "Payment:DefaultCurrency");
        Assert.Contains(fields, f => f.Key == "Payment:DefaultNotifyUrl");
        Assert.Contains(fields, f => f.Key == "Payment:EnableRefundApproval");
        Assert.Contains(fields, f => f.Key == "Payment:RefundApprovalThreshold");
        Assert.Contains(fields, f => f.Key == "Payment:MaxRefundAmountPerDay");
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
