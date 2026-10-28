using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.Template.Services;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 生成产物、发送发票的那几秒里落地的「标记已付 / 作废」不能被一份更早的快照整行写回。
/// </summary>
/// <remarks>
/// <para>
/// 仓储读出的发票是游离快照，<c>UpdateAsync</c> 把它的每一列都写回；<c>Invoice</c> 没有并发令牌。
/// 产物生成要渲染模板、把 HTML 转成 PDF（无头浏览器，秒级）、存进 Storage，发送还要同步发一封邮件：
/// 此前两处都在这之后按快照整行写回，客户点下载的同时管理员标记已付，已收款的发票重新变成未付
/// （而 <c>InvoicePaidEvent</c> 已经发出去了）；两次并发重发各自读到同一个发送计数，各自写回加一，上限形同虚设。
/// </para>
/// <para>
/// 复现方式：PDF 转换器与通知的替身在被调用的那一刻，于另一个 scope 里做那件并发的事。
/// </para>
/// </remarks>
public partial class InvoiceConcurrentWriteTests : BillingIntegrationTestBase
{
    private readonly SlotTakenBeforeReservation _slotTaken = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IHtmlToPdfConverter> _converter = new();
    private Func<Task>? _whileConverting;
    private Func<Task>? _whileSending;
    private int _sendCalls;

    protected Result<NotificationInfo> NotificationAnswer { get; set; } = Result.Success(new NotificationInfo());

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_slotTaken);

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        _converter.SetupGet(c => c.FileExtension).Returns(".pdf");
        _converter.SetupGet(c => c.ContentType).Returns("application/pdf");
        _converter
            .Setup(c => c.ConvertAsync(It.IsAny<string>(), It.IsAny<PdfConvertOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await RunOnce(ref _whileConverting);
                return [0x25, 0x50, 0x44, 0x46];
            });
        services.AddScoped(_ => _converter.Object);

        _notifications
            .Setup(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref _sendCalls);
                await RunOnce(ref _whileSending);
                return NotificationAnswer;
            });
        services.AddScoped(_ => _notifications.Object);
    }

    private static Task RunOnce(ref Func<Task>? action)
    {
        var pending = Interlocked.Exchange(ref action, null);
        return pending?.Invoke() ?? Task.CompletedTask;
    }

    private async Task<Invoice> CreateDraftAsync(Action<Invoice>? shape = null)
    {
        var created = await InScopeAsync<IPaymentInvoiceService, Result<PaymentInvoiceDto>>(s => s.CreateManualAsync(new CreatePaymentInvoiceDto
        {
            CustomerName = "Ada",
            CustomerEmail = "ada@example.com",
            InvoiceDate = DateTime.UtcNow,
            LineItems = [new InvoiceLineItemDto { Description = "Work", Quantity = 2, UnitPrice = 30m }]
        }));
        created.Succeeded.ShouldBeTrue(created.Message);

        if (shape != null)
        {
            using var scope = ServiceProvider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
            var row = await ctx.Set<Invoice>().FirstAsync(i => i.Id == created.Data!.Id);
            shape(row);
            await ctx.SaveChangesAsync();
        }

        return (await ReloadAsync<Invoice>(created.Data!.Id))!;
    }

    private Task<Result> MarkPaidAsync(Guid id) =>
        InScopeAsync<IPaymentInvoiceService, Result>(s => s.MarkAsPaidAsync(id, new MarkInvoicePaidDto { PaidAmount = 60m }));

    private Task<Result> SendAsync(Guid id) =>
        InScopeAsync<IPaymentInvoiceService, Result>(s => s.SendAsync(id, null, null));

    /// <summary>★ 客户下载发票（首次生成产物）的同时管理员标记已付：已付必须留下，产物指针照常写上。</summary>
    [Fact]
    public async Task MarkingPaidWhileTheDocumentIsGenerated_Survives()
    {
        var invoice = await CreateDraftAsync();
        _whileConverting = async () => (await MarkPaidAsync(invoice.Id)).Succeeded.ShouldBeTrue();

        var generated = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(invoice.Id));

        generated.Succeeded.ShouldBeTrue(generated.Message);
        var reloaded = await ReloadAsync<Invoice>(invoice.Id);
        reloaded!.Status.ShouldBe(InvoiceStatus.Paid);
        reloaded.PaidAmount.ShouldBe(60m);
        reloaded.PaidDate.ShouldNotBeNull();
        reloaded.PdfFilePath.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CancellingWhileTheDocumentIsGenerated_Survives()
    {
        var invoice = await CreateDraftAsync();
        _whileConverting = async () =>
            (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(invoice.Id, "duplicate"))).Succeeded.ShouldBeTrue();

        (await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(invoice.Id))).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Invoice>(invoice.Id);
        reloaded!.Status.ShouldBe(InvoiceStatus.Cancelled);
        reloaded.InternalNotes!.ShouldContain("duplicate");
    }

    /// <summary>★ 发送途中被标记已付：发送只把「还没发出」的发票推进为 Sent，已付的不能被降回未付。</summary>
    [Fact]
    public async Task MarkingPaidWhileTheInvoiceIsBeingSent_Survives()
    {
        var invoice = await CreateDraftAsync();
        _whileSending = async () => (await MarkPaidAsync(invoice.Id)).Succeeded.ShouldBeTrue();

        (await SendAsync(invoice.Id)).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Invoice>(invoice.Id);
        reloaded!.Status.ShouldBe(InvoiceStatus.Paid);
        reloaded.PaidAmount.ShouldBe(60m);
        reloaded.SendCount.ShouldBe(1);
        reloaded.LastSentTime.ShouldNotBeNull();
    }

    /// <summary>★ 只剩最后一个发送名额时两次并发重发：只有一次发得出去，计数不超过上限。</summary>
    [Fact]
    public async Task TwoConcurrentResends_ForTheLastSlot_SendOnlyOnce()
    {
        var invoice = await CreateDraftAsync(i => i.SendCount = PaymentConstants.MaxInvoiceSendCount - 1);
        Result? second = null;
        _whileSending = async () => second = await SendAsync(invoice.Id);

        var first = await SendAsync(invoice.Id);

        first.Succeeded.ShouldBeTrue(first.Message);
        second.ShouldNotBeNull();
        second!.Succeeded.ShouldBeFalse();
        second.Message.ShouldBe(ErrorCodes.InvoiceSendLimitReached);
        _sendCalls.ShouldBe(1);
        (await ReloadAsync<Invoice>(invoice.Id))!.SendCount.ShouldBe(PaymentConstants.MaxInvoiceSendCount);
    }

    /// <summary>
    /// ★ 读快照判定「还剩一个」与真正占下名额之间，另一个请求先占走了最后一个：这一次必须被拒，一封都不发。
    /// 判定只能落在占名额的那条条件更新里，读出来的快照说了不算。
    /// </summary>
    [Fact]
    public async Task AResendThatLosesTheLastSlotToAConcurrentOne_IsRefused()
    {
        var invoice = await CreateDraftAsync(i => i.SendCount = PaymentConstants.MaxInvoiceSendCount - 1);
        _slotTaken.Arm(PaymentConstants.MaxInvoiceSendCount);

        var result = await SendAsync(invoice.Id);

        _slotTaken.Fired.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.InvoiceSendLimitReached);
        _sendCalls.ShouldBe(0);
        (await ReloadAsync<Invoice>(invoice.Id))!.SendCount.ShouldBe(PaymentConstants.MaxInvoiceSendCount);
    }

    /// <summary>没发出去的那一次不占名额：通知失败时把预留的名额还回去，状态也不推进。</summary>
    [Fact]
    public async Task AFailedSend_GivesTheSlotBack()
    {
        var invoice = await CreateDraftAsync();
        NotificationAnswer = Result.Failure<NotificationInfo>("smtp down", 500);

        (await SendAsync(invoice.Id)).Succeeded.ShouldBeFalse();

        var reloaded = await ReloadAsync<Invoice>(invoice.Id);
        reloaded!.SendCount.ShouldBe(0);
        reloaded.LastSentTime.ShouldBeNull();
        reloaded.Status.ShouldBe(InvoiceStatus.Draft);
    }

    [Fact]
    public async Task ASuccessfulSend_MovesADraftToSent()
    {
        var invoice = await CreateDraftAsync();

        (await SendAsync(invoice.Id)).Succeeded.ShouldBeTrue();

        var reloaded = await ReloadAsync<Invoice>(invoice.Id);
        reloaded!.Status.ShouldBe(InvoiceStatus.Sent);
        reloaded.SendCount.ShouldBe(1);
        reloaded.PdfFilePath.ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// 占发送名额的那条 UPDATE 执行之前，用同一个连接先把发送计数写满：模拟另一个请求恰好在这一刻占走了最后一个名额。只触发一次。
    /// </summary>
    private sealed partial class SlotTakenBeforeReservation : DbCommandInterceptor
    {
        private int? _sendCount;

        public bool Fired { get; private set; }

        public void Arm(int sendCount) => _sendCount = sendCount;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Take(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Take(command);
            return result;
        }

        private void Take(DbCommand command)
        {
            if (_sendCount is not { } sendCount)
                return;

            var target = SendCountIncrement().Match(command.CommandText);
            if (!target.Success)
                return;

            _sendCount = null;
            Fired = true;

            using var taken = command.Connection!.CreateCommand();
            taken.Transaction = command.Transaction;
            taken.CommandText = $"UPDATE \"{target.Groups["table"].Value}\" SET \"SendCount\" = {sendCount}";
            taken.ExecuteNonQuery();
        }

        [GeneratedRegex("""^\s*UPDATE\s+"(?<table>[^"]*Invoice)"[\s\S]*"SendCount"\s*\+\s*1""", RegexOptions.IgnoreCase)]
        private static partial Regex SendCountIncrement();
    }

    /// <summary>已作废的发票不发，也不占名额。</summary>
    [Fact]
    public async Task SendingACancelledInvoice_IsRefused()
    {
        var invoice = await CreateDraftAsync(i => i.Status = InvoiceStatus.Cancelled);

        var result = await SendAsync(invoice.Id);

        result.Succeeded.ShouldBeFalse();
        _sendCalls.ShouldBe(0);
        (await ReloadAsync<Invoice>(invoice.Id))!.SendCount.ShouldBe(0);
    }
}
