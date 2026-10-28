using Tnzi.Template.Services;
using System.Text;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 票据产物的格式跟着这台宿主的转换器走：有 PDF 转换器时发票、收据、贷项通知单都是 <c>application/pdf</c> / <c>.pdf</c>，
/// 此前以 HTML 生成的产物在下一次下载时按新格式重出，而不是把 HTML 字节标成 PDF 交出去。
/// </summary>
public class InvoiceDocumentFormatTests : BillingIntegrationTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<InvoiceOptions>(o => o.ReceiptsForSubscriptionPayments = true);
        services.AddScoped<IHtmlToPdfConverter, FakePdfConverter>();
    }

    private async Task<PaymentEntity> SeedAsync(BusinessType type)
    {
        var payment = SucceededPayment(113m);
        payment.BusinessType = type;
        await SeedAsync(payment);
        return payment;
    }

    private Task<Result<InvoiceDocumentDto>> DownloadAsync(Guid invoiceId) =>
        InScopeAsync<IPaymentInvoiceService, Result<InvoiceDocumentDto>>(s => s.GetPdfContentAsync(invoiceId));

    private static void ShouldBeAPdf(Result<InvoiceDocumentDto> download)
    {
        download.Succeeded.ShouldBeTrue(download.Message);
        download.Data!.ContentType.ShouldBe("application/pdf");
        download.Data.FileName.ShouldEndWith(".pdf");
        Encoding.ASCII.GetString(download.Data.Content, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public async Task InvoicesReceiptsAndCreditNotes_AreAllPdf()
    {
        var order = await SeedAsync(BusinessType.Order);
        var invoice = (await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(order.Id))).Data!;
        var subscription = await SeedAsync(BusinessType.Subscription);
        var receipt = (await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(subscription.Id))).Data!;
        receipt.Type.ShouldBe(InvoiceType.Receipt);
        var creditNote = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateCreditNoteAsync(new CreateCreditNoteDto
        {
            RefundId = Guid.NewGuid(),
            PaymentId = subscription.Id,
            Amount = 113m,
            Currency = "USD"
        }));
        creditNote.Succeeded.ShouldBeTrue(creditNote.Message);

        ShouldBeAPdf(await DownloadAsync(invoice.Id));
        ShouldBeAPdf(await DownloadAsync(receipt.Id));
        ShouldBeAPdf(await DownloadAsync(creditNote.Data!.Id));
    }

    [Fact]
    public async Task ADocumentGeneratedAsHtmlBeforeAPdfConverterWasInstalled_IsRegeneratedAsPdf()
    {
        var order = await SeedAsync(BusinessType.Order);
        var invoice = (await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(order.Id))).Data!;
        var htmlFile = Path.Combine(Path.GetTempPath(), $"{invoice.InvoiceNo}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(htmlFile, "<html>old</html>");
        try
        {
            await InScopeAsync<IRepository<Invoice, Guid>, int>(repo => repo.AsQueryable()
                .Where(i => i.Id == invoice.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.PdfFilePath, htmlFile)));

            ShouldBeAPdf(await DownloadAsync(invoice.Id));
            (await ReloadAsync<Invoice>(invoice.Id))!.PdfFilePath.ShouldEndWith(".pdf");
        }
        finally
        {
            File.Delete(htmlFile);
        }
    }

    private sealed class FakePdfConverter : IHtmlToPdfConverter
    {
        public string ContentType => "application/pdf";

        public string FileExtension => ".pdf";

        public Task<byte[]> ConvertAsync(string htmlContent, PdfConvertOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Encoding.ASCII.GetBytes("%PDF-1.7 " + htmlContent.Length));
    }
}
