using Tnzi.Data;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 支付列表（用户端「我的支付」与管理端订单列表）按建单时间倒序分页。
/// </summary>
/// <remarks>
/// 此前查询没有任何 ORDER BY：数据库按执行计划给行，翻页时同一笔支付可能出现两次或被跳过，
/// 最新的单也不一定在第一页。退款、订阅、发票列表一直是倒序的，只有支付列表漏了。
/// </remarks>
public class PaymentListOrderTests : PaymentIntegrationTestBase
{
    private async Task SeedAsync(string tradeNo, DateTime creationTime)
    {
        await SeedAsync(new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = "ORDER-" + tradeNo,
            BusinessType = BusinessType.Order,
            OriginalAmount = 100m,
            PayableAmount = 100m,
            Currency = "USD",
            Status = PaymentStatus.Pending,
            ChannelCode = "Null",
            PaymentMethod = PaymentMethod.CreditCard,
            CreationTime = creationTime
        });
    }

    private Task<Result<IPagedList<PaymentDto>>> ListAsync(int pageIndex, int pageSize) =>
        InScopeAsync<IPaymentService, Result<IPagedList<PaymentDto>>>(svc =>
            svc.GetPaymentListAsync(new PaymentQueryDto { PageIndex = pageIndex, PageSize = pageSize }));

    [Fact]
    public async Task ThePaymentList_IsNewestFirstAcrossPages()
    {
        var now = DateTime.UtcNow;
        // 按时间正序插入：没有 ORDER BY 时 SQLite 按插入顺序返回，最老的会排在第一页
        await SeedAsync("LIST-OLD", now.AddDays(-3));
        await SeedAsync("LIST-MID", now.AddDays(-2));
        await SeedAsync("LIST-NEW", now.AddDays(-1));

        var first = await ListAsync(1, 2);
        var second = await ListAsync(2, 2);

        first.Data!.Items.Select(p => p.TradeNo).ShouldBe(["LIST-NEW", "LIST-MID"]);
        second.Data!.Items.Select(p => p.TradeNo).ShouldBe(["LIST-OLD"]);
    }
}
