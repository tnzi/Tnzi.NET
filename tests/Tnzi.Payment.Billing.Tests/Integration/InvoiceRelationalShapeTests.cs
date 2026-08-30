using Microsoft.EntityFrameworkCore.Metadata;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 「零迁移」的验收测试：关系改由依赖端声明之后，**关系型 schema 一个字节都没变**。
/// </summary>
/// <remarks>
/// <para>
/// 拆分前 <c>PaymentConfiguration</c> 里写的是
/// <c>HasOne(p =&gt; p.Invoice).WithOne(i =&gt; i.Payment).HasForeignKey&lt;Invoice&gt;(i =&gt; i.PaymentId)</c>；
/// 现在写在 <c>InvoiceConfiguration</c> 里，是
/// <c>HasOne(i =&gt; i.Payment).WithOne().HasForeignKey&lt;Invoice&gt;(i =&gt; i.PaymentId)</c>。
/// 差别只在<b>从哪一侧声明</b>与<b>有没有反向导航</b>——两者都是 EF 的模型层概念，
/// 不落到 DDL 上：外键约束名只取「依赖表 + 主表 + 外键列」，与导航属性名无关。
/// </para>
/// <para>
/// 这条用例把那句话变成断言。它守的失效是：有人把关系写成了别的形状
/// （改成一对多、把外键挪到 <c>Payment.InvoiceId</c>、丢掉唯一索引），
/// 编译与业务测试都不会红，而下一次生成迁移会给每个既有部署一条 DDL。
/// </para>
/// </remarks>
public class InvoiceRelationalShapeTests : BillingIntegrationTestBase
{
    private IEntityType Invoice => DbContext.Model.FindEntityType(typeof(Invoice))!;

    /// <summary>外键在发票那一侧，指向支付的主键，且是**一对一**。</summary>
    [Fact]
    public void ForeignKeyLivesOnTheInvoiceSide_AndIsOneToOne()
    {
        var fk = Invoice.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(PaymentEntity));

        fk.Properties.Select(p => p.Name).ShouldBe([nameof(Entities.Invoice.PaymentId)]);
        fk.PrincipalKey.Properties.Select(p => p.Name).ShouldBe(["Id"]);
        fk.IsUnique.ShouldBeTrue();

        // 可空外键 → ClientSetNull，与拆分前一致（Invoice.PaymentId 一直是 Guid?）
        fk.IsRequired.ShouldBeFalse();
        fk.DeleteBehavior.ShouldBe(DeleteBehavior.ClientSetNull);
    }

    /// <summary>
    /// 外键约束名与拆分前逐字相同 —— 这才是「不产生迁移」的那一条。
    /// </summary>
    /// <remarks>
    /// EF 的默认约束名是 <c>FK_{依赖表}_{主表}_{外键列}</c>，三项都没变，所以名字没变。
    /// 这里刻意断言字面量而不是「名字非空」：改名同样是一条 DDL。
    /// </remarks>
    [Fact]
    public void ForeignKeyConstraintNameIsUnchanged()
    {
        var fk = Invoice.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(PaymentEntity));

        fk.GetConstraintName().ShouldBe("FK_Invoice_Payment_PaymentId");
    }

    /// <summary>
    /// <c>PaymentId</c> 上仍有那条<b>带过滤条件的唯一索引</b>：一笔支付至多一张发票。
    /// </summary>
    /// <remarks>
    /// 幂等最终靠它兜底 —— 应用层查重挡不住并发投递的同一个支付完成事件。
    /// 关系换一侧声明时最容易顺手丢掉的就是这个索引。
    /// </remarks>
    [Fact]
    public void UniqueIndexOnPaymentIdSurvives()
    {
        var index = Invoice.GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(Entities.Invoice.PaymentId));

        index.IsUnique.ShouldBeTrue();
        index.GetFilter().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// <c>Payment</c> 上没有指向发票的导航 —— 父模块这一侧确实断干净了。
    /// </summary>
    [Fact]
    public void PaymentHasNoNavigationBackToInvoice()
    {
        var payment = DbContext.Model.FindEntityType(typeof(PaymentEntity))!;

        payment.GetNavigations().Select(n => n.Name).ShouldNotContain("Invoice");
    }

    /// <summary>
    /// <c>Payment.InvoiceId</c> 这一列还在，而且**不是外键**。
    /// </summary>
    /// <remarks>
    /// 它拆分前也从未被赋值过（外键一直在发票那侧）。删掉它是一次 <c>DropColumn</c>，
    /// 会给每个既有部署换来一条迁移 —— 拆程序集不该动 schema，所以刻意留着。
    /// 这条用例把「留着」写成断言，免得日后有人把它当遗留清掉而没意识到那是一次迁移。
    /// </remarks>
    [Fact]
    public void PaymentKeepsItsUnusedInvoiceIdColumn_WhichIsNotAForeignKey()
    {
        var payment = DbContext.Model.FindEntityType(typeof(PaymentEntity))!;

        payment.FindProperty("InvoiceId").ShouldNotBeNull();
        payment.GetForeignKeys().SelectMany(f => f.Properties).Select(p => p.Name)
            .ShouldNotContain("InvoiceId");
    }

    /// <summary>明细仍挂在发票下，级联关系与拆分前一致。</summary>
    [Fact]
    public void LineItemsStillHangUnderTheInvoice()
    {
        var lineItem = DbContext.Model.FindEntityType(typeof(InvoiceLineItem))!;

        var fk = lineItem.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(Invoice));

        fk.Properties.Select(p => p.Name).ShouldBe([nameof(InvoiceLineItem.InvoiceId)]);
        fk.IsUnique.ShouldBeFalse();
    }
}
