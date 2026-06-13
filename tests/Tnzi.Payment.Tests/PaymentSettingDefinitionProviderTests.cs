using Tnzi.Payment.Options;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PaymentSettingDefinitionProvider 结构测试 — 验证 provider 注册字段符合配置中心契约
/// </summary>
public class PaymentSettingDefinitionProviderTests
{
    private readonly PaymentSettingDefinitionProvider _provider = new();

    [Fact]
    public void GetGroups_ReturnsOneGroup()
    {
        var groups = _provider.GetGroups();
        Assert.Single(groups);
    }

    [Fact]
    public void Group_HasExpectedKey()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("payment-general", group.Key);
    }

    [Fact]
    public void Group_HasExpectedModuleName()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal("Payment", group.ModuleName);
    }

    [Fact]
    public void Group_HasExpectedOrder()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(500, group.Order);
    }

    [Fact]
    public void Group_HasFiveFields()
    {
        var group = _provider.GetGroups()[0];
        Assert.Equal(5, group.Fields.Count);
    }

    [Fact]
    public void Fields_HaveCorrectKeys()
    {
        var fields = _provider.GetGroups()[0].Fields;
        Assert.Contains(fields, f => f.Key == "Payment:DefaultCurrency");
        Assert.Contains(fields, f => f.Key == "Payment:DefaultNotifyUrl");
        Assert.Contains(fields, f => f.Key == "Payment:EnableRefundApproval");
        Assert.Contains(fields, f => f.Key == "Payment:RefundApprovalThreshold");
        Assert.Contains(fields, f => f.Key == "Payment:MaxRefundAmountPerDay");
    }

    [Fact]
    public void Fields_HaveCorrectTypes()
    {
        var fields = _provider.GetGroups()[0].Fields;
        Assert.Equal(SettingFieldType.String, fields.First(f => f.Key == "Payment:DefaultCurrency").Type);
        Assert.Equal(SettingFieldType.String, fields.First(f => f.Key == "Payment:DefaultNotifyUrl").Type);
        Assert.Equal(SettingFieldType.Boolean, fields.First(f => f.Key == "Payment:EnableRefundApproval").Type);
        Assert.Equal(SettingFieldType.Decimal, fields.First(f => f.Key == "Payment:RefundApprovalThreshold").Type);
        Assert.Equal(SettingFieldType.Decimal, fields.First(f => f.Key == "Payment:MaxRefundAmountPerDay").Type);
    }

    [Fact]
    public void DefaultValueAccessors_ReturnExpectedDefaults()
    {
        var fields = _provider.GetGroups()[0].Fields;

        var currency = fields.First(f => f.Key == "Payment:DefaultCurrency");
        Assert.NotNull(currency.DefaultValueAccessor);
        Assert.Equal("USD", currency.DefaultValueAccessor!());

        var approval = fields.First(f => f.Key == "Payment:EnableRefundApproval");
        Assert.NotNull(approval.DefaultValueAccessor);
        Assert.Equal("true", approval.DefaultValueAccessor!());

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
        var fields = _provider.GetGroups()[0].Fields;
        Assert.All(fields, f => Assert.NotNull(f.I18nKey));
    }
}
