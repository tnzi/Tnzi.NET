using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Payment.Billing.Events;
using Tnzi.Payment.Services;
using Tnzi.Template.Options;
using Tnzi.Template.Services;
using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 订阅收款的收据、退款的贷项通知单、开票时快照的卖方资料，以及按票据种类走模板的邮件。
/// 模板是本模块随包发布的那几份文件，经真实的 Razor 引擎渲染。
/// </summary>
public class ReceiptsAndCreditNotesTests : BillingIntegrationTestBase
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime PeriodStart = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100 });
    private readonly Mock<INotificationService> _notifications = new();
    private readonly List<CreateNotificationRequest> _sent = [];
    private readonly string _logoPath = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.png");

    // 卖方名称经 IOptionsSnapshot 每个作用域重读：改它就是「开票之后改了配置」
    private string _sellerName = "Northwind Software Inc.";
    private bool _issueCreditNotes;
    private string? _accentColor;

    public ReceiptsAndCreditNotesTests()
    {
        // 一个 1x1 的 PNG：标志按文件头认格式，嵌成 data URI
        File.WriteAllBytes(_logoPath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.Configure<InvoiceOptions>(o =>
        {
            o.ReceiptsForSubscriptionPayments = true;
            o.IssueCreditNotes = _issueCreditNotes;
            o.Seller = new InvoiceSellerOptions
            {
                Name = _sellerName,
                AddressLines = ["100 King Street West", "Toronto ON M5X 1A9", "Canada"],
                Phone = "+1 416 555 0100",
                Email = "billing@northwind.example",
                Website = "https://northwind.example",
                LogoPath = _logoPath,
                TaxRegistrations = [new InvoiceTaxRegistration { Label = "GST/HST", Number = "123456789 RT0001" }],
                AccentColor = _accentColor
            };
        });

        services.AddScoped<IPaymentLineDescriber, FixedPeriodDescriber>();
        services.AddScoped<CreditNoteIssuingHandler>();

        _notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CreateNotificationRequest request, CancellationToken _) => _sent.Add(request))
            .ReturnsAsync(Result.Success(new NotificationInfo()));
        services.AddScoped(_ => _notifications.Object);

        // 渲染随包发布的模板文件（复制到输出目录的 Templates/Payment/Invoice/）
        var store = new Mock<ITemplateStoreService>();
        store
            .Setup(s => s.GetTemplateAsync(It.IsAny<string>(), "Payment", "Invoice", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string _, string _, CancellationToken _) =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Templates", "Payment", "Invoice", name + ".cshtml");
                return File.Exists(path)
                    ? Result<TemplateEntity>.Success(new TemplateEntity { TemplateName = name, Module = "Payment", Category = "Invoice", ContentTemplate = File.ReadAllText(path), IsActive = true })
                    : Result<TemplateEntity>.Failure("TEMPLATE_NOT_FOUND", 404);
            });
        var engine = new RazorTemplateEngine(
            Microsoft.Extensions.Options.Options.Create(new TemplateOptions { TemplateRootPath = Path.GetTempPath() }),
            NullLogger<RazorTemplateEngine>.Instance,
            _cache);
        services.AddScoped<ITemplateRenderService>(sp => new TemplateRenderService(sp, engine, store.Object));
    }

    public override void Dispose()
    {
        _cache.Dispose();
        File.Delete(_logoPath);
        base.Dispose();
    }

    private async Task<PaymentEntity> SeedSubscriptionPaymentAsync()
    {
        var payment = SucceededPayment(339m);
        payment.BusinessType = BusinessType.Subscription;
        payment.BusinessOrderNo = "SUB-1";
        payment.Currency = "CAD";
        payment.OriginalAmount = 300m;
        payment.TaxAmount = 39m;
        payment.UserId = Owner;
        payment.PaymentMethodBrand = "visa";
        payment.PaymentMethodLast4 = "4242";
        payment.TaxLinesJson = """[{"name":"HST","rate":13,"amount":39,"registrationNumber":"123456789 RT0001"}]""";
        await SeedAsync(payment);
        return payment;
    }

    private Task<Result<PaymentInvoiceDto>> IssueAsync(Guid paymentId) =>
        InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(paymentId));

    private async Task<string> RenderAsync(Guid invoiceId)
    {
        var generated = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(invoiceId));
        generated.Succeeded.ShouldBeTrue(generated.Message);
        return await File.ReadAllTextAsync((await ReloadAsync<Invoice>(invoiceId))!.PdfFilePath!);
    }

    [Fact]
    public async Task ASubscriptionPayment_GetsAReceipt_WithTheSellerTaxLinesCardAndServicePeriod()
    {
        var payment = await SeedSubscriptionPaymentAsync();

        var issued = await IssueAsync(payment.Id);

        issued.Succeeded.ShouldBeTrue(issued.Message);
        var receipt = (await ReloadAsync<Invoice>(issued.Data!.Id))!;
        receipt.Type.ShouldBe(InvoiceType.Receipt);
        receipt.TemplateName.ShouldBe("ReceiptDefault");
        receipt.PaymentMethodLast4.ShouldBe("4242");
        receipt.SellerSnapshotJson.ShouldNotBeNull().ShouldContain("Northwind Software Inc.");

        var html = await RenderAsync(receipt.Id);
        html.ShouldContain("<h2>Receipt</h2>");
        html.ShouldContain("Northwind Software Inc.");
        html.ShouldContain("Toronto ON M5X 1A9");
        html.ShouldContain("GST/HST: 123456789 RT0001");
        html.ShouldContain("HST 13% (123456789 RT0001)");
        html.ShouldContain("visa ending in 4242");
        html.ShouldContain("Oct 1, 2026");
        html.ShouldContain("Nov 1, 2026");
        html.ShouldContain("data:image/png;base64,");
        html.ShouldContain("339.00");
        html.ShouldContain("Total Paid", customMessage: "rendered by the shipped template, not the built-in fallback");
        html.ShouldContain(TitleColor(InvoiceSellerOptions.DefaultAccentColor), customMessage: "no accent configured: today's look");
    }

    private const string WithStorageOverage = "with-storage-overage";

    [Fact]
    public async Task AReceiptWithStorageOverage_PrintsItsOwnLine_WithQuantityAndUnitPrice()
    {
        var payment = SucceededPayment(339m);
        payment.BusinessType = BusinessType.Subscription;
        payment.Currency = "CAD";
        payment.OriginalAmount = 300m;
        payment.TaxAmount = 39m;
        payment.ExtraData = WithStorageOverage;
        payment.TaxLinesJson = """[{"name":"HST","rate":13,"amount":39,"registrationNumber":"123456789 RT0001"}]""";
        await SeedAsync(payment);

        var issued = await IssueAsync(payment.Id);
        issued.Succeeded.ShouldBeTrue(issued.Message);

        var lines = await InScopeAsync<IRepository<InvoiceLineItem, Guid>, List<InvoiceLineItem>>(repo =>
            repo.AsQueryable().Where(l => l.InvoiceId == issued.Data!.Id).OrderBy(l => l.LineNumber).ToListAsync());
        lines.Count.ShouldBe(2);
        lines[1].Kind.ShouldBe("storage-overage");
        lines[1].Quantity.ShouldBe(50m);

        var html = await RenderAsync(issued.Data!.Id);
        html.ShouldContain("Storage above 250 GB included (GB-month)");
        html.ShouldContain("50 &times; 0.20");
        html.ShouldContain("HST 13%", customMessage: "the tax line covers the whole payment, the overage included");
        html.ShouldContain("339.00");
    }

    [Fact]
    public async Task AReceiptWithoutOverage_ShowsNoQuantityLine()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var issued = await IssueAsync(payment.Id);

        var html = await RenderAsync(issued.Data!.Id);

        html.ShouldNotContain("&times;");
        html.ShouldNotContain("GB-month");
    }

    private static string TitleColor(string color) => $".invoice-title h2 {{ font-size: 28px; font-weight: 700; color: {color};";

    [Fact]
    public async Task TheConfiguredAccentColour_TitlesTheInvoiceReceiptAndCreditNote()
    {
        _accentColor = " #0B5FFF ";
        var subscription = await SeedSubscriptionPaymentAsync();
        var receipt = await IssueAsync(subscription.Id);
        var order = SucceededPayment(113m);
        await SeedAsync(order);
        var invoice = await IssueAsync(order.Id);
        var creditNote = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateCreditNoteAsync(new CreateCreditNoteDto
        {
            RefundId = Guid.NewGuid(),
            RefundNo = "RF-ACCENT",
            PaymentId = subscription.Id,
            Amount = 100m,
            Currency = "CAD",
            RefundTime = DateTime.UtcNow
        }));
        creditNote.Succeeded.ShouldBeTrue(creditNote.Message);

        foreach (var id in new[] { receipt.Data!.Id, invoice.Data!.Id, creditNote.Data!.Id })
        {
            var html = await RenderAsync(id);
            html.ShouldContain(TitleColor("#0B5FFF"));
            html.ShouldNotContain(InvoiceSellerOptions.DefaultAccentColor);
        }
    }

    [Fact]
    public async Task TheCustomerBlock_PrintsTheBillingPartysCompanyAddressAndTaxId()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id, new CreatePaymentInvoiceDto
        {
            CustomerName = "Whitfield & Partners",
            CustomerCompany = "Whitfield & Partners LLP",
            CustomerAddress = "100 King St W\n Toronto ON M5X 1A9 \n",
            CustomerTaxId = "987654321 RT0001"
        }));

        var html = await RenderAsync(issued.Data!.Id);

        html.ShouldContain("Whitfield &amp; Partners LLP");
        html.ShouldContain("<p>100 King St W</p>");
        html.ShouldContain("<p>Toronto ON M5X 1A9</p>");
        html.ShouldContain("<p>Tax ID: 987654321 RT0001</p>");
    }

    [Fact]
    public async Task AnAccentColourThatIsNotAHexColour_NeverReachesTheStyleSheet()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var issued = await IssueAsync(payment.Id);
        // 快照在数据库里：被改写成能结束这条样式声明的东西时，渲染退回出厂色
        await InScopeAsync<IRepository<Invoice, Guid>, int>(repo => repo.AsQueryable()
            .Where(i => i.Id == issued.Data!.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.SellerSnapshotJson,
                """{"name":"Northwind Software Inc.","accentColor":"red; } body { display: none"}""")));

        var html = await RenderAsync(issued.Data!.Id);

        html.ShouldContain(TitleColor(InvoiceSellerOptions.DefaultAccentColor));
        html.ShouldNotContain("display: none");
    }

    [Fact]
    public async Task TheDefaultInvoiceTemplate_RendersTheSellerBlockAndTaxLines()
    {
        var payment = SucceededPayment(113m);
        payment.OriginalAmount = 100m;
        payment.TaxAmount = 13m;
        payment.TaxLinesJson = """[{"name":"HST","rate":13,"amount":13}]""";
        await SeedAsync(payment);
        var issued = await IssueAsync(payment.Id);
        (await ReloadAsync<Invoice>(issued.Data!.Id))!.Type.ShouldBe(InvoiceType.Standard);

        var html = await RenderAsync(issued.Data.Id);

        html.ShouldContain("Thank you for your business.", customMessage: "rendered by the shipped template, not the built-in fallback");
        html.ShouldContain("<h2>Invoice</h2>");
        html.ShouldContain("+1 416 555 0100");
        html.ShouldContain("https://northwind.example");
        html.ShouldContain("HST 13%");
    }

    [Fact]
    public async Task TheSellerIsSnapshotted_SoAReissuedDocumentKeepsTheSellerItWasIssuedBy()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var issued = await IssueAsync(payment.Id);

        _sellerName = "Renamed Corp";

        (await RenderAsync(issued.Data!.Id)).ShouldContain("Northwind Software Inc.");
    }

    [Fact]
    public async Task ARefund_GetsACreditNoteAgainstTheOriginal_WithProportionalTax_OncePerRefund()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var original = await IssueAsync(payment.Id);
        var request = new CreateCreditNoteDto
        {
            RefundId = Guid.NewGuid(),
            RefundNo = "RF-1",
            PaymentId = payment.Id,
            Amount = 169.5m,
            Currency = "CAD",
            RefundTime = DateTime.UtcNow
        };

        var first = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateCreditNoteAsync(request));
        var again = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateCreditNoteAsync(request));

        first.Succeeded.ShouldBeTrue(first.Message);
        again.Data!.Id.ShouldBe(first.Data!.Id, "one refund, one credit note");

        var note = (await ReloadAsync<Invoice>(first.Data.Id))!;
        note.Type.ShouldBe(InvoiceType.CreditNote);
        note.OriginalInvoiceId.ShouldBe(original.Data!.Id);
        note.PaymentId.ShouldBeNull("the payment's one invoice is the original");
        note.DueAmount.ShouldBe(169.5m);
        note.TaxAmount.ShouldBe(19.5m, "half the money back is half the tax back");
        note.Amount.ShouldBe(150m);
        note.BillingAccountId.ShouldBe(Owner.ToString());

        var html = await RenderAsync(note.Id);
        html.ShouldContain("Total Credited", customMessage: "rendered by the shipped template, not the built-in fallback");
        html.ShouldContain($"Credits invoice #{original.Data.InvoiceNo}");
        html.ShouldContain("HST 13%");

        (await ReloadAsync<Invoice>(original.Data.Id))!.Status.ShouldBe(InvoiceStatus.Paid, "the original stays as issued");
    }

    [Fact]
    public async Task ACreditNote_NeedsAnOriginalToCredit()
    {
        var payment = await SeedSubscriptionPaymentAsync();

        var refused = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateCreditNoteAsync(new CreateCreditNoteDto
        {
            RefundId = Guid.NewGuid(),
            PaymentId = payment.Id,
            Amount = 10m,
            Currency = "CAD"
        }));

        refused.Code.ShouldBe(404);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASucceededRefundEvent_IssuesAndSendsACreditNote_OnlyWhenTurnedOn(bool turnedOn)
    {
        _issueCreditNotes = turnedOn;
        var payment = await SeedSubscriptionPaymentAsync();
        await IssueAsync(payment.Id);

        await InScopeAsync<CreditNoteIssuingHandler, bool>(async handler =>
        {
            await handler.HandleAsync(new RefundProcessedEvent
            {
                RefundId = Guid.NewGuid(),
                RefundNo = "RF-EVT",
                PaymentId = payment.Id,
                TradeNo = payment.TradeNo,
                Amount = 339m,
                Currency = "CAD",
                CompletedTime = DateTime.UtcNow,
                Succeeded = true
            });
            return true;
        });

        var notes = await InScopeAsync<IRepository<Invoice, Guid>, List<Invoice>>(repo =>
            repo.AsQueryable().AsNoTracking().Where(i => i.Type == InvoiceType.CreditNote).ToListAsync());
        notes.Count.ShouldBe(turnedOn ? 1 : 0);
        _sent.Any(m => m.TemplateName == InvoiceEmailTemplates.CreditNote).ShouldBe(turnedOn);
    }

    [Fact]
    public async Task SendingAReceipt_UsesTheReceiptEmailTemplate()
    {
        var payment = await SeedSubscriptionPaymentAsync();
        var issued = await IssueAsync(payment.Id);

        var sent = await InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(issued.Data!.Id));

        sent.Succeeded.ShouldBeTrue(sent.Message);
        var message = _sent.ShouldHaveSingleItem();
        message.TemplateName.ShouldBe(InvoiceEmailTemplates.Receipt);
        message.Subject.ShouldBe($"Receipt {issued.Data!.InvoiceNo}");
        message.TemplateVariables!["CardLast4"].ShouldBe("4242");
        message.TemplateVariables["SellerName"].ShouldBe("Northwind Software Inc.");
        message.Content.ShouldNotBeNull().ShouldContain("receipt");
        message.Attachments.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(InvoiceEmailTemplates.Invoice, "Northwind Software Inc.", "invoice INV-1 from Northwind Software Inc. for CAD 339.00")]
    [InlineData(InvoiceEmailTemplates.Receipt, "Northwind Software Inc.", "received CAD 339.00 on 2026-10-01 from your visa ending in 4242")]
    [InlineData(InvoiceEmailTemplates.CreditNote, "Northwind Software Inc.", "has refunded CAD 339.00 against invoice INV-0")]
    [InlineData(InvoiceEmailTemplates.Receipt, "", "We received CAD 339.00")]
    [InlineData(InvoiceEmailTemplates.CreditNote, "", "We have refunded CAD 339.00")]
    public async Task TheShippedEmailTemplates_RenderTheVariablesTheSenderSupplies(string templateName, string sellerName, string expected)
    {
        // 通知模块按名在 Notification/Email 下找这几份文件；front matter 由模板存储剥掉，这里照做
        var path = Path.Combine(AppContext.BaseDirectory, "Templates", "Notification", "Email", templateName + ".cshtml");
        File.Exists(path).ShouldBeTrue($"{templateName}.cshtml ships with the package");
        var source = await File.ReadAllTextAsync(path);
        source.ShouldNotContain("subject:", Case.Insensitive, "a front-matter subject would override the per-document subject the sender sets");
        var body = source[(source.IndexOf("---", 3, StringComparison.Ordinal) + 3)..];

        var engine = new RazorTemplateEngine(
            Microsoft.Extensions.Options.Options.Create(new TemplateOptions { TemplateRootPath = Path.GetTempPath() }),
            NullLogger<RazorTemplateEngine>.Instance,
            _cache);
        var html = await engine.RenderAsync(body, new Dictionary<string, object>
        {
            ["CustomerName"] = "Ada",
            ["DocumentTitle"] = "Receipt",
            ["DocumentNo"] = "INV-1",
            ["Amount"] = "339.00",
            ["Currency"] = "CAD",
            ["SellerName"] = sellerName,
            ["PaidDate"] = "2026-10-01",
            ["CardBrand"] = "visa",
            ["CardLast4"] = "4242",
            ["OriginalInvoiceNo"] = "INV-0"
        });

        System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ").ShouldContain(expected);
        html.ShouldContain("Hi Ada");
    }

    /// <summary>订阅模块那一侧的描述器的替身：一行，带固定服务期。</summary>
    private sealed class FixedPeriodDescriber : IPaymentLineDescriber
    {
        public Task<IReadOnlyList<PaymentLineDescription>?> DescribeAsync(PaymentLineContext payment, CancellationToken cancellationToken = default)
        {
            if (payment.BusinessType != BusinessType.Subscription)
                return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(null);

            // 一笔带着上一期超额存储费的续费：计划一行 + 数量为 GB-月的存储一行（与订阅模块的描述器同形）
            if (payment.ExtraData == WithStorageOverage)
            {
                IReadOnlyList<PaymentLineDescription> withStorage =
                [
                    new PaymentLineDescription { Description = "Firm plan", UnitPrice = 290m, Amount = 290m, TaxAmount = 37.70m, PeriodStart = PeriodStart, PeriodEnd = PeriodEnd },
                    new PaymentLineDescription
                    {
                        Description = "Storage above 250 GB included (GB-month)",
                        Quantity = 50m,
                        UnitPrice = 0.20m,
                        Amount = 10m,
                        TaxAmount = 1.30m,
                        PeriodStart = PeriodStart.AddMonths(-1),
                        PeriodEnd = PeriodStart,
                        Kind = "storage-overage"
                    }
                ];
                return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(withStorage);
            }

            IReadOnlyList<PaymentLineDescription> lines =
                [new PaymentLineDescription { Description = "Firm plan", UnitPrice = payment.OriginalAmount, Amount = payment.OriginalAmount, TaxAmount = payment.TaxAmount, PeriodStart = PeriodStart, PeriodEnd = PeriodEnd }];
            return Task.FromResult<IReadOnlyList<PaymentLineDescription>?>(lines);
        }
    }
}

/// <summary>
/// 默认配置（不开收据、不配卖方资料）：订阅收款照旧开发票，卖方取既有的四个公司字段。
/// </summary>
public class InvoiceDefaultsUnchangedTests : BillingIntegrationTestBase
{
    [Fact]
    public async Task WithoutTheNewOptions_ASubscriptionPaymentStillGetsAnInvoiceFromTheCompanyFields()
    {
        var payment = SucceededPayment();
        payment.BusinessType = BusinessType.Subscription;
        await SeedAsync(payment);

        var issued = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateFromPaymentAsync(payment.Id));

        issued.Succeeded.ShouldBeTrue(issued.Message);
        var invoice = (await ReloadAsync<Invoice>(issued.Data!.Id))!;
        invoice.Type.ShouldBe(InvoiceType.Standard);
        invoice.TemplateName.ShouldBe("InvoiceDefault");
        invoice.SellerSnapshotJson.ShouldNotBeNull().ShouldContain("Test Co");
    }
}
