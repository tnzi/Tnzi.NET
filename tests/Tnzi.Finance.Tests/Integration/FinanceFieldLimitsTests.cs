using Tnzi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 会计核心里人填的文本超过列宽时答 400 点名字段，过账时由系统拼出来的凭证摘要收进列宽。
/// </summary>
/// <remarks>
/// ★ 测试库是 SQLite，不检查列宽：原样写入的超长值在这里「保存成功」，在 SQL Server / PostgreSQL 上
/// 则是一个不点名的截断 500。所以这些用例显式断言长度与零写入，而不是等数据库报错。
/// </remarks>
public class FinanceFieldLimitsTests : FinanceIntegrationTestBase
{
    private const string Doc = FinanceSourceTypes.Invoice;

    private Task<int> CountAsync<TEntity>() where TEntity : class, IEntity<Guid>
        => InScopeAsync<IRepository<TEntity, Guid>, int>(repo => repo.AsQueryable().IgnoreQueryFilters().CountAsync());

    [Fact]
    public async Task Comment_LongerThanTheColumn_Rejected400_AndNothingIsWritten()
    {
        var result = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
            s => s.PostAsync(Doc, Guid.NewGuid().ToString(), new CreateDocumentCommentDto { Body = new string('x', 4001) }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("comment");
        (await CountAsync<DocumentComment>()).ShouldBe(0);
    }

    [Fact]
    public async Task Comment_AtTheColumnWidth_IsAccepted()
    {
        var result = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
            s => s.PostAsync(Doc, Guid.NewGuid().ToString(), new CreateDocumentCommentDto { Body = new string('x', 4000) }));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Comment_DocumentKeyLongerThanTheColumn_Rejected400()
    {
        var result = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
            s => s.PostAsync(new string('t', 65), Guid.NewGuid().ToString(), new CreateDocumentCommentDto { Body = "hello" }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await CountAsync<DocumentComment>()).ShouldBe(0);
    }

    [Theory]
    [InlineData(261, null, null, "file name")]
    [InlineData(null, 129, null, "content type")]
    [InlineData(null, null, 501, "caption")]
    public async Task Attachment_FieldLongerThanTheColumn_Rejected400_NamingTheField(
        int? fileNameLength, int? contentTypeLength, int? captionLength, string field)
    {
        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(Doc, Guid.NewGuid().ToString(), new CreateDocumentAttachmentDto
            {
                FileId = Guid.NewGuid(),
                FileName = fileNameLength.HasValue ? new string('f', fileNameLength.Value) : "receipt.pdf",
                ContentType = contentTypeLength.HasValue ? new string('c', contentTypeLength.Value) : "application/pdf",
                Caption = captionLength.HasValue ? new string('k', captionLength.Value) : null,
                FileSize = 10,
            }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain(field);
        (await CountAsync<DocumentAttachment>()).ShouldBe(0);
    }

    [Fact]
    public async Task Attachment_DocumentIdLongerThanTheColumn_Rejected400()
    {
        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(Doc, new string('9', 65), new CreateDocumentAttachmentDto { FileId = Guid.NewGuid(), FileName = "a.pdf" }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        (await CountAsync<DocumentAttachment>()).ShouldBe(0);
    }

    [Fact]
    public async Task Attachment_FieldsAtTheColumnWidth_AreAccepted()
    {
        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(new string('t', 64), new string('9', 64), new CreateDocumentAttachmentDto
            {
                FileId = Guid.NewGuid(),
                FileName = new string('f', 260),
                ContentType = new string('c', 128),
                Caption = new string('k', 500),
            }));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    private async Task<(Guid CustomerId, Guid RevenueAccountId)> InvoicePrerequisitesAsync()
    {
        await SeedCoaAsync();
        var customer = await InScopeAsync<ICustomerService, Result<CustomerDto>>(
            s => s.CreateAsync(new CreateCustomerDto { Name = "Memo Customer" }));
        customer.Succeeded.ShouldBeTrue(customer.Message);
        return (customer.Data!.Id, await AccountIdByCodeAsync("4100"));
    }

    /// <summary>
    /// 草稿摘要顶到列宽是合法的，而过账给它加上 "Invoice: " 前缀：此前凭证摘要超宽，
    /// 这张发票在 SQL Server / PostgreSQL 上永远过不了账。
    /// </summary>
    [Fact]
    public async Task Invoice_WithAMemoAtTheColumnWidth_Posts_AndTheJournalMemoFitsTheColumn()
    {
        var (customerId, revenue) = await InvoicePrerequisitesAsync();
        var memo = new string('m', 500);

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 3, 10),
            Memo = memo,
            Lines = [new CreateInvoiceLineDto { Description = "Service", AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var entry = (await ReloadAsync<JournalEntry>(posted.Data!.JournalEntryId!.Value))!;
        entry.Memo!.Length.ShouldBeLessThanOrEqualTo(500);
        entry.Memo.ShouldStartWith("Invoice: mmm");
        (await ReloadAsync<Invoice>(posted.Data.Id))!.Memo.ShouldBe(memo);
    }

    [Fact]
    public async Task Invoice_DraftMemoLongerThanTheColumn_Rejected400()
    {
        var (customerId, revenue) = await InvoicePrerequisitesAsync();

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 3, 10),
            Memo = new string('m', 501),
            Lines = [new CreateInvoiceLineDto { Description = "Service", AccountId = revenue, Quantity = 1, UnitPrice = 100m }],
        }));

        draft.Succeeded.ShouldBeFalse();
        draft.Code.ShouldBe(400);
        draft.Message!.ShouldContain("memo");
        (await CountAsync<Invoice>()).ShouldBe(0);
    }

    /// <summary>跨币种转账在摘要后面加 " (out)" / " (in)" / " (fx)"：同一个超宽问题，在后缀一侧。</summary>
    [Fact]
    public async Task CrossCurrencyTransfer_WithAMemoAtTheColumnWidth_Posts_AndEveryVoucherMemoFits()
    {
        await SeedCoaAsync();
        var usdBank = await AccountIdByCodeAsync("1120");
        var eur = await CreateAccountAsync(new CreateAccountDto
        {
            Code = "1121",
            Name = "EUR Wallet",
            RootType = AccountRootType.Asset,
            Currency = "EUR",
            ParentId = await AccountIdByCodeAsync("1100"),
            SubType = "Bank",
            CashFlowActivity = CashFlowActivity.CashEquivalent,
        });

        var draft = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.CreateDraftAsync(new CreateTransferDto
        {
            FromAccountId = usdBank,
            ToAccountId = eur,
            TransferDate = new DateTime(2026, 3, 10),
            Currency = "USD",
            Amount = 1080m,
            TargetCurrency = "EUR",
            TargetAmount = 1000m,
            TargetExchangeRate = 1.10m,
            Memo = new string('t', 500),
        }));
        draft.Succeeded.ShouldBeTrue(draft.Message);

        var posted = await InScopeAsync<ITransferService, Result<TransferDto>>(s => s.PostAsync(draft.Data!.Id));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var entries = await InScopeAsync<ILedgerPostingService, Result<List<JournalEntryDto>>>(
            s => s.GetBySourceAsync(nameof(Transfer), posted.Data!.Id.ToString()));
        entries.Data!.Count.ShouldBe(3);
        entries.Data.ShouldAllBe(e => e.Memo != null && e.Memo.Length <= 500);
    }

    /// <summary>
    /// 行描述与摘要同一种列宽问题，在同一个草稿入口：人填的超长值 400 点名行号，不写任何东西。
    /// </summary>
    [Fact]
    public async Task Invoice_LineDescriptionLongerThanTheColumn_Rejected400_NamingTheLine()
    {
        var (customerId, revenue) = await InvoicePrerequisitesAsync();

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 3, 10),
            Lines =
            [
                new CreateInvoiceLineDto { Description = "Service", AccountId = revenue, Quantity = 1, UnitPrice = 100m },
                new CreateInvoiceLineDto { Description = new string('d', 501), AccountId = revenue, Quantity = 1, UnitPrice = 100m },
            ],
        }));

        draft.Succeeded.ShouldBeFalse();
        draft.Code.ShouldBe(400);
        draft.Message!.ShouldContain("Line 2");
        draft.Message!.ShouldContain("description");
        (await CountAsync<Invoice>()).ShouldBe(0);
    }

    /// <summary>
    /// 目录项描述的列宽（1000）比行描述（500）宽：行没填描述时缺省抄过来的那一份收进列宽，
    /// 而不是让每一张用到这个目录项的单据在 SQL Server / PostgreSQL 上 500。
    /// </summary>
    [Fact]
    public async Task Invoice_LineDefaultedFromALongItemDescription_FitsTheColumn()
    {
        var (customerId, revenue) = await InvoicePrerequisitesAsync();
        var itemDescription = new string('i', 1000);
        var item = await InScopeAsync<IItemService, Result<ItemDto>>(
            s => s.CreateAsync(new CreateItemDto { Name = "Consulting", Description = itemDescription, IncomeAccountId = revenue }));
        item.Succeeded.ShouldBeTrue(item.Message);

        var draft = await InScopeAsync<IInvoiceService, Result<InvoiceDto>>(s => s.CreateDraftAsync(new CreateInvoiceDto
        {
            CustomerId = customerId,
            DocDate = new DateTime(2026, 3, 10),
            Lines = [new CreateInvoiceLineDto { ItemId = item.Data!.Id, Quantity = 1, UnitPrice = 100m }],
        }));

        draft.Succeeded.ShouldBeTrue(draft.Message);
        draft.Data!.Lines.Single().Description.ShouldBe(itemDescription[..500]);
    }

    [Theory]
    [InlineData(129, null, "reference")]
    [InlineData(null, 33, "payment method")]
    public async Task Payment_ReferenceOrPaymentMethodLongerThanTheColumn_Rejected400_NamingTheField(
        int? referenceLength, int? methodLength, string field)
    {
        var (customerId, _) = await InvoicePrerequisitesAsync();

        var draft = await InScopeAsync<IPaymentEntryService, Result<PaymentEntryDto>>(s => s.CreateDraftAsync(new CreatePaymentEntryDto
        {
            Direction = PaymentDirection.Inbound,
            PartyType = FinancePartyType.Customer,
            PartyId = customerId,
            DocDate = new DateTime(2026, 3, 10),
            Amount = 100m,
            Reference = referenceLength.HasValue ? new string('r', referenceLength.Value) : null,
            PaymentMethod = methodLength.HasValue ? new string('p', methodLength.Value) : null,
        }));

        draft.Succeeded.ShouldBeFalse();
        draft.Code.ShouldBe(400);
        draft.Message!.ShouldContain(field);
        (await CountAsync<PaymentEntry>()).ShouldBe(0);
    }

    [Fact]
    public async Task Reversal_MemoLongerThanTheColumn_Rejected400()
    {
        await SeedCoaAsync();
        var posted = await PostLedgerAsync(SimpleSale(100m));
        posted.Succeeded.ShouldBeTrue(posted.Message);

        var reversed = await InScopeAsync<IJournalEntryService, Result<JournalEntryDto>>(
            s => s.ReverseAsync(posted.Data!.Id, new ReverseJournalEntryDto { Memo = new string('r', 501) }));

        reversed.Succeeded.ShouldBeFalse();
        reversed.Code.ShouldBe(400);
        (await ReloadAsync<JournalEntry>(posted.Data!.Id))!.Status.ShouldBe(JournalEntryStatus.Posted);
    }

    /// <summary>经 <see cref="ILedgerPostingService"/> 过账的摘要来自调用方拼接，同样收进列宽而不是在数据库那一步 500。</summary>
    [Fact]
    public async Task LedgerPosting_WithAnOverlongComposedMemo_FitsTheColumn()
    {
        await SeedCoaAsync();
        var request = SimpleSale(100m);
        request.Memo = new string('s', 520);

        var posted = await PostLedgerAsync(request);

        posted.Succeeded.ShouldBeTrue(posted.Message);
        (await ReloadAsync<JournalEntry>(posted.Data!.Id))!.Memo!.Length.ShouldBe(500);
    }
}
