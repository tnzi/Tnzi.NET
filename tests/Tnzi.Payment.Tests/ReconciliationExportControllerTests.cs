using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tnzi.Payment.Controllers.Admin;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 对账导出的截断要传到拿文件的人手里，摘要端点不再把整份 CSV 塞进 JSON。
/// </summary>
/// <remarks>
/// 服务层早就算出了 <c>Truncated</c> / <c>MatchedRecords</c>，但下载只回文件本身：超过单次上限的那个月下载下来看起来完整，
/// 少掉的行被读成对账差异，截断只留在服务端的一行日志里。摘要端点（「不下载文件」）则把最多 5 万行的 CSV 原样放进响应体。
/// </remarks>
public class ReconciliationExportControllerTests
{
    private static DefaultPaymentStatisticsAdminController CreateController(ReconciliationExportResultDto export)
    {
        var service = new Mock<IPaymentStatisticsService>();
        service
            .Setup(s => s.ExportReconciliationAsync(It.IsAny<ReconciliationQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(export));

        return new DefaultPaymentStatisticsAdminController(service.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static ReconciliationExportResultDto TruncatedExport() => new()
    {
        CsvContent = "TradeNo\r\nTRD001\r\n",
        FileName = "reconciliation_20261001_20261031.csv",
        TotalRecords = 50000,
        MatchedRecords = 60000,
        Truncated = true
    };

    [Fact]
    public async Task TheDownload_ReportsTruncationInItsHeaders()
    {
        var controller = CreateController(TruncatedExport());

        var response = await controller.ExportReconciliation(new ReconciliationQueryDto());

        response.ShouldBeOfType<FileContentResult>();
        var headers = controller.Response.Headers;
        headers[PaymentConstants.ReconciliationTruncatedHeader].ToString().ShouldBe("true");
        headers[PaymentConstants.ReconciliationMatchedRecordsHeader].ToString().ShouldBe("60000");
        headers[PaymentConstants.ReconciliationTotalRecordsHeader].ToString().ShouldBe("50000");
    }

    [Fact]
    public async Task TheSummary_LeavesTheFileOut()
    {
        var controller = CreateController(TruncatedExport());

        var summary = await controller.GetReconciliationSummary(new ReconciliationQueryDto());

        summary.Data!.CsvContent.ShouldBeNull();
        summary.Data.Truncated.ShouldBeTrue();
        summary.Data.MatchedRecords.ShouldBe(60000);
        summary.Data.TotalRecords.ShouldBe(50000);
    }
}
