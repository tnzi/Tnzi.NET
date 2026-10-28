using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tnzi.Payment.Billing.Events;

namespace Tnzi.Payment.Billing.Tests.Integration;

/// <summary>
/// 支付完成事件被并发处理两次时，自动发送至多发一封。
/// </summary>
/// <remarks>
/// 被保护的缺陷：处理器先看发票的发送计数与发送时间，再调发送；发送的名额占用只比上限不比首发。
/// 分布式总线至少投递一次，同一事件的两次处理都看到 0，各发一封。这里把第二次处理整个插进第一次的
/// 「看过之后、发出之前」：一处在处理器拿到发票之后，一处在发票服务认领名额的那条条件写执行之前。
/// </remarks>
public class InvoiceAutoSendRaceTests : InvoiceDocumentFixture
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly SendSlotInterleaver _interleaver = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interleaver);

    [Fact]
    public async Task A_second_handling_that_lands_after_the_first_read_the_invoice_does_not_send_again()
    {
        var completed = await SeedCompletedPaymentAsync();

        using (var scope = ServiceProvider.CreateScope())
        {
            var service = new InterleavingInvoiceService(
                scope.ServiceProvider.GetRequiredService<IPaymentInvoiceService>(),
                afterCreate: () => HandleAsync(completed));
            await CreateHandler(scope, service).HandleAsync(completed);
        }

        await ShouldHaveSentOnceAsync(completed.PaymentId);
    }

    [Fact]
    public async Task A_second_handling_that_lands_before_the_first_claims_its_send_slot_does_not_send_again()
    {
        var completed = await SeedCompletedPaymentAsync();

        _interleaver.Arm(() => HandleAsync(completed));
        await HandleAsync(completed);
        _interleaver.Ran.ShouldBeTrue("the second handling must have run inside the first one's claim");

        await ShouldHaveSentOnceAsync(completed.PaymentId);
    }

    /// <summary>对照：首发认领只管自动发送，管理员手工补发照常发出、照常计数。</summary>
    [Fact]
    public async Task A_manual_resend_after_the_auto_send_still_goes_out()
    {
        var completed = await SeedCompletedPaymentAsync();
        await HandleAsync(completed);
        var invoice = await LoadInvoiceAsync(completed.PaymentId);

        (await SendAsync(invoice.Id)).Succeeded.ShouldBeTrue();

        Notifications.Verify(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        (await LoadInvoiceAsync(completed.PaymentId)).SendCount.ShouldBe(2);
    }

    private async Task<PaymentCompletedEvent> SeedCompletedPaymentAsync()
    {
        var payment = SucceededPayment();
        await SeedAsync(payment);
        return new PaymentCompletedEvent { PaymentId = payment.Id, TradeNo = payment.TradeNo };
    }

    private async Task ShouldHaveSentOnceAsync(Guid paymentId)
    {
        Notifications.Verify(n => n.CreateAndSendAsync(It.IsAny<CreateNotificationRequest>(), It.IsAny<CancellationToken>()), Times.Once());
        (await LoadInvoiceAsync(paymentId)).SendCount.ShouldBe(1);
    }

    private async Task<Invoice> LoadInvoiceAsync(Guid paymentId)
    {
        using var scope = ServiceProvider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRepository<Invoice, Guid>>().SingleAsync(i => i.PaymentId == paymentId);
    }

    private async Task HandleAsync(PaymentCompletedEvent completed)
    {
        using var scope = ServiceProvider.CreateScope();
        await CreateHandler(scope, scope.ServiceProvider.GetRequiredService<IPaymentInvoiceService>()).HandleAsync(completed);
    }

    private static InvoiceIssuingHandler CreateHandler(IServiceScope scope, IPaymentInvoiceService service)
        => new(NullLogger<InvoiceIssuingHandler>.Instance, service, scope.ServiceProvider.GetRequiredService<IOptionsMonitor<InvoiceOptions>>());

    /// <summary>
    /// 在武装之后第一条占发送名额的条件写（<c>SendCount + 1</c>）执行之前，跑一次给定的并发处理。跑之前先解除武装。
    /// </summary>
    private sealed class SendSlotInterleaver : DbCommandInterceptor
    {
        private Func<Task>? _action;

        public bool Ran { get; private set; }

        public void Arm(Func<Task> action)
        {
            _action = action;
            Ran = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_action != null
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"SendCount\" + 1", StringComparison.Ordinal))
            {
                var action = _action;
                _action = null;
                await action();
                Ran = true;
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>真实发票服务的代理：拿到发票（开票或取回已开的那张）之后先跑一次给定的并发处理，再把结果交回去。</summary>
    private sealed class InterleavingInvoiceService(IPaymentInvoiceService inner, Func<Task> afterCreate) : IPaymentInvoiceService
    {
        public async Task<Result<PaymentInvoiceDto>> CreateFromPaymentAsync(Guid paymentId, CreatePaymentInvoiceDto? request = null, CancellationToken cancellationToken = default)
        {
            var created = await inner.CreateFromPaymentAsync(paymentId, request, cancellationToken);
            await afterCreate();
            return created;
        }

        public Task<Result<bool>> SendFirstAsync(Guid invoiceId, CancellationToken cancellationToken = default)
            => inner.SendFirstAsync(invoiceId, cancellationToken);

        public Task<Result<PaymentInvoiceDto>> CreateManualAsync(CreatePaymentInvoiceDto request, CancellationToken cancellationToken = default)
            => inner.CreateManualAsync(request, cancellationToken);

        public Task<Result<PaymentInvoiceDto>> CreateCreditNoteAsync(CreateCreditNoteDto request, CancellationToken cancellationToken = default)
            => inner.CreateCreditNoteAsync(request, cancellationToken);

        public Task<Result> SendAsync(Guid invoiceId, string? recipientEmail = null, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
            => inner.SendAsync(invoiceId, recipientEmail, ownerUserId, cancellationToken);

        public Task<Result<string>> GeneratePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default)
            => inner.GeneratePdfAsync(invoiceId, cancellationToken);

        public Task<Result<string>> GetPdfUrlAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
            => inner.GetPdfUrlAsync(invoiceId, ownerUserId, cancellationToken);

        public Task<Result<InvoiceDocumentDto>> GetPdfContentAsync(Guid invoiceId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
            => inner.GetPdfContentAsync(invoiceId, ownerUserId, cancellationToken);

        public Task<Result> MarkAsPaidAsync(Guid invoiceId, MarkInvoicePaidDto request, CancellationToken cancellationToken = default)
            => inner.MarkAsPaidAsync(invoiceId, request, cancellationToken);

        public Task<Result> CancelAsync(Guid invoiceId, string? reason = null, CancellationToken cancellationToken = default)
            => inner.CancelAsync(invoiceId, reason, cancellationToken);

        public Task<Result<PaymentInvoiceDto>> GetAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
            => inner.GetAsync(id, ownerUserId, cancellationToken);

        public Task<Result<IPagedList<PaymentInvoiceDto>>> GetListAsync(PaymentInvoiceQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
            => inner.GetListAsync(query, ownerUserId, cancellationToken);

        public Task<Result<IPagedList<PaymentInvoiceDto>>> GetUserInvoicesAsync(Guid userId, PagedQueryDto? query = null, CancellationToken cancellationToken = default)
            => inner.GetUserInvoicesAsync(userId, query, cancellationToken);
    }
}

/// <summary>
/// 标记已付 / 作废清掉产物指针时，解除引用的必须是被清掉的那一份。
/// </summary>
/// <remarks>
/// 被保护的缺陷：先读产物 Id、再在一条只按状态过滤的语句里清指针。两步之间落地的一次产物生成写进了新的 <c>PdfFileId</c>，
/// 语句把它清掉，解除引用的却是先读到的旧 Id，新文件的引用永远没人解除。这里在那条语句执行之前把指针换成一份新产物
/// （一次生成落地的形状），然后核对被解除引用的是哪一份。
/// </remarks>
public class InvoiceStatusChangeDocumentRaceTests : InvoiceDocumentFixture
{
    private readonly DocumentSwapInterceptor _swap = new();

    protected override bool WithStorage => true;

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_swap);

    [Fact]
    public async Task Marking_paid_releases_the_document_a_concurrent_generation_put_in_place()
    {
        var (id, stale, landed) = await ArrangeSentInvoiceAsync();

        (await MarkPaidAsync(id)).Succeeded.ShouldBeTrue();

        _swap.Ran.ShouldBeTrue();
        (await ReloadAsync<Invoice>(id))!.PdfFileId.ShouldBeNull();
        VerifyReleased(landed, id, Times.Once());
        VerifyReleased(stale, id, Times.Never());
    }

    [Fact]
    public async Task Cancelling_releases_the_document_a_concurrent_generation_put_in_place()
    {
        var (id, stale, landed) = await ArrangeSentInvoiceAsync();

        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, "Duplicate order"))).Succeeded.ShouldBeTrue();

        _swap.Ran.ShouldBeTrue();
        (await ReloadAsync<Invoice>(id))!.PdfFileId.ShouldBeNull();
        VerifyReleased(landed, id, Times.Once());
        VerifyReleased(stale, id, Times.Never());
    }

    /// <summary>对照：没有并发生成时，被清掉并解除引用的就是发送时生成的那一份。</summary>
    [Fact]
    public async Task Without_a_concurrent_generation_the_sent_document_is_released()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();
        var sent = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;

        (await MarkPaidAsync(id)).Succeeded.ShouldBeTrue();

        VerifyReleased(sent, id, Times.Once());
    }

    private async Task<(Guid Id, Guid Stale, Guid Landed)> ArrangeSentInvoiceAsync()
    {
        var id = await CreateManualInvoiceAsync();
        (await SendAsync(id)).Succeeded.ShouldBeTrue();
        var stale = (await ReloadAsync<Invoice>(id))!.PdfFileId!.Value;
        var landed = Guid.NewGuid();
        _swap.Arm(id, landed);
        return (id, stale, landed);
    }

    /// <summary>
    /// 在武装之后第一条改发票状态的语句执行之前，把这张发票的产物指针换成另一份（一次产物生成恰好落地）。
    /// </summary>
    private sealed class DocumentSwapInterceptor : DbCommandInterceptor
    {
        private Guid? _invoiceId;
        private Guid _landed;

        public bool Ran { get; private set; }

        public void Arm(Guid invoiceId, Guid landed)
        {
            _invoiceId = invoiceId;
            _landed = landed;
            Ran = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_invoiceId is { } invoiceId
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"Status\" =", StringComparison.Ordinal))
            {
                _invoiceId = null;
                var table = eventData.Context!.Model.FindEntityType(typeof(Invoice))!.GetTableName();

                await using var swap = command.Connection!.CreateCommand();
                swap.Transaction = command.Transaction;
                swap.CommandText = $"UPDATE \"{table}\" SET \"PdfFileId\" = $landed WHERE \"Id\" = $id";
                AddParameter(swap, "$landed", _landed.ToString().ToUpperInvariant());
                AddParameter(swap, "$id", invoiceId.ToString().ToUpperInvariant());
                (await swap.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
                Ran = true;
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}

/// <summary>
/// 往内部备注追加一条之后，备注不超过列宽（1000），最新的一条完整留下：原备注以空格结尾时也一样。
/// </summary>
/// <remarks>
/// 此前在 SQL 里按 <c>InternalNotes.Length</c> 判断放不放得下，而 SQL Server 的 <c>LEN</c> 不计末尾空格：
/// 原备注以若干空格结尾、又接近列宽时被判成放得下，拼出来超出列宽，标记已付或作废答 500。
/// 测试库是 SQLite（长度函数计入末尾空格、也不检查列宽），所以这里断言的是拼出来的长度本身。
/// </remarks>
public class InvoiceInternalNotesTests : InvoiceDocumentFixture
{
    private const int ColumnWidth = 1000;

    [Fact]
    public async Task Notes_ending_with_spaces_near_the_column_width_still_fit_after_marking_paid()
    {
        var id = await CreateManualInvoiceAsync();
        var original = new string('a', 975) + new string(' ', 10);
        await SetInternalNotesAsync(id, original);

        (await MarkPaidAsync(id, "late fee waived")).Succeeded.ShouldBeTrue();

        var notes = (await ReloadAsync<Invoice>(id))!.InternalNotes!;
        notes.Length.ShouldBeLessThanOrEqualTo(ColumnWidth);
        notes.ShouldEndWith("\nPaid: late fee waived");
    }

    [Fact]
    public async Task Notes_ending_with_spaces_near_the_column_width_still_fit_after_cancelling()
    {
        var id = await CreateManualInvoiceAsync();
        await SetInternalNotesAsync(id, new string('b', 965) + new string(' ', 15));

        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, "customer withdrew"))).Succeeded.ShouldBeTrue();

        var notes = (await ReloadAsync<Invoice>(id))!.InternalNotes!;
        notes.Length.ShouldBeLessThanOrEqualTo(ColumnWidth);
        notes.ShouldEndWith("\nCancelled: customer withdrew");
    }

    /// <summary>新写入的条目去掉首尾空白：作废原因末尾的空格不进备注。</summary>
    [Fact]
    public async Task A_cancellation_reason_is_recorded_without_surrounding_whitespace()
    {
        var id = await CreateManualInvoiceAsync();

        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, "  wrong customer   "))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.InternalNotes.ShouldBe("Cancelled: wrong customer");
    }

    /// <summary>对照：放得下时原样追加，前面的内容一个字都不丢。</summary>
    [Fact]
    public async Task Short_notes_are_kept_in_full_with_the_new_entry_appended()
    {
        var id = await CreateManualInvoiceAsync();
        await SetInternalNotesAsync(id, "Priority customer  ");

        (await MarkPaidAsync(id, "wire received")).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.InternalNotes.ShouldBe("Priority customer  \nPaid: wire received");
    }

    /// <summary>
    /// 截掉最早的内容时，截断点不落在代理对中间：半个 emoji 不是合法文本（PostgreSQL 的驱动按严格 UTF-8 编码直接抛异常，别的库存成乱码）。
    /// </summary>
    [Fact]
    public async Task Trimming_the_oldest_notes_never_splits_a_character_in_two()
    {
        var id = await CreateManualInvoiceAsync();
        const string entry = "Paid: wire";
        var room = ColumnWidth - entry.Length - 1;
        // 截断点（从末尾数 room 个单元）正好落在 emoji 的两个 UTF-16 单元之间
        await SetInternalNotesAsync(id, "a\U0001F600" + new string('b', room - 1));

        (await MarkPaidAsync(id, "wire")).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.InternalNotes.ShouldBe(new string('b', room - 1) + "\n" + entry);
    }

    /// <summary>超长的作废原因截到列宽时，同样不留下半个字符。</summary>
    [Fact]
    public async Task An_over_long_reason_is_cut_before_a_character_it_would_split()
    {
        var id = await CreateManualInvoiceAsync();
        const string prefix = "Cancelled: ";
        // 第 1000 个单元是 emoji 的前半个
        var reason = new string('r', ColumnWidth - 1 - prefix.Length) + "\U0001F600tail";

        (await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, reason))).Succeeded.ShouldBeTrue();

        (await ReloadAsync<Invoice>(id))!.InternalNotes.ShouldBe(prefix + new string('r', ColumnWidth - 1 - prefix.Length));
    }

    private async Task SetInternalNotesAsync(Guid id, string notes)
    {
        using var scope = ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
        (await db.Set<Invoice>().Where(i => i.Id == id).ExecuteUpdateAsync(s => s.SetProperty(i => i.InternalNotes, notes))).ShouldBe(1);
    }
}

/// <summary>
/// 一次产物生成与标记已付 / 作废交错时，印着旧状态的那份产物不能装上去。
/// </summary>
/// <remarks>
/// 被保护的缺陷：生成只在「指针仍是开头读到的那一个」时换指针。开头读到的指针是空的（还没生成过，或本地回退只有路径）时，
/// 标记已付 / 作废清指针什么都没改变，渲染那几秒里落地的状态变化挡不住这一份：客户下载到的是一张已付发票的待付账单。
/// 这里在生成换指针的那条语句执行之前插入那次状态变化（不装 Storage，本地回退：指针恒为空，正是最常见的形状），
/// 然后看装上去的那份印的是什么状态。
/// </remarks>
public class InvoiceGenerationStatusRaceTests : InvoiceDocumentFixture
{
    // 字段初始化先于基类构造函数执行，ConfigureDbContextOptions 被调用时它已经就位
    private readonly DocumentInstallInterleaver _interleaver = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interleaver);

    [Theory]
    [InlineData("paid", "Status: Paid")]
    [InlineData("cancelled", "Status: Cancelled")]
    public async Task A_document_rendered_before_a_status_change_is_not_installed_after_it(string change, string expected)
    {
        var id = await CreateManualInvoiceAsync();
        _interleaver.Arm(async () =>
        {
            var changed = change == "paid"
                ? await MarkPaidAsync(id)
                : await InScopeAsync<IPaymentInvoiceService, Result>(s => s.CancelAsync(id, "duplicate"));
            changed.Succeeded.ShouldBeTrue(changed.Message);
        });

        var generated = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id));
        _interleaver.Ran.ShouldBeTrue("the status change must have landed between the render and the install");

        // 印着旧状态的那份没装上，生成按新状态重来了一次：调用方拿到的是印着当前状态的产物。
        generated.Succeeded.ShouldBeTrue(generated.Message);
        (await ReloadAsync<Invoice>(id))!.PdfFilePath.ShouldNotBeNullOrWhiteSpace();
        var text = await DownloadAsTextAsync(id);
        text.ShouldContain(expected, Case.Sensitive, "a document printed with the old status must not be installed after the status changed");
        text.ShouldNotContain("Status: Draft");
    }

    /// <summary>对照：发送把 Draft 推进为 Sent 不改变产物内容，这期间落地的发送不挡生成。</summary>
    [Fact]
    public async Task A_send_landing_during_generation_does_not_block_the_install()
    {
        var id = await CreateManualInvoiceAsync();
        _interleaver.Arm(async () =>
        {
            using var scope = ServiceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingTestDbContext>();
            (await db.Set<Invoice>().Where(i => i.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvoiceStatus.Sent))).ShouldBe(1);
        });

        var generated = await InScopeAsync<IPaymentInvoiceService, Result<string>>(s => s.GeneratePdfAsync(id));
        _interleaver.Ran.ShouldBeTrue();

        generated.Succeeded.ShouldBeTrue(generated.Message);
        (await ReloadAsync<Invoice>(id))!.PdfFilePath.ShouldNotBeNull();
    }

    /// <summary>
    /// 在武装之后第一条只换产物指针的语句（写 <c>PdfFilePath</c>、不写 <c>LastModificationTime</c>：标记已付 / 作废那条会写）执行之前，跑一次给定的并发写。跑之前先解除武装。
    /// </summary>
    private sealed class DocumentInstallInterleaver : DbCommandInterceptor
    {
        private Func<Task>? _action;

        public bool Ran { get; private set; }

        public void Arm(Func<Task> action)
        {
            _action = action;
            Ran = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_action != null
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("\"PdfFilePath\" =", StringComparison.Ordinal)
                && !command.CommandText.Contains("\"LastModificationTime\" =", StringComparison.Ordinal))
            {
                var action = _action;
                _action = null;
                await action();
                Ran = true;
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
