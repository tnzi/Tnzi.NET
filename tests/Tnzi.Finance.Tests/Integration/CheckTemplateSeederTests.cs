using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tnzi.Finance.Documents.Metadata;
using Tnzi.Security.Claims;
using Tnzi.Template.Entities;
using Tnzi.Template.Entities.Configs;
using Tnzi.Template.Services;
using TemplateEntity = Tnzi.Template.Entities.Template;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 模板写入的故障注入开关（单例）
/// </summary>
/// <remarks>
/// ★ 必须是**跨 scope 共享的单例**而不是 DbContext 上的一个属性：播种器
/// <c>CreateAsyncScope()</c> 自建作用域，拿到的是**另一个** <c>DbContext</c> 实例 ——
/// 把开关挂在测试持有的那一个上，故障永远不会发生，而三条用例会以「一切正常」的姿态变绿。
/// </remarks>
public sealed class TemplateWriteFault
{
    /// <summary>
    /// 令这一份模板的写入真的抛 <see cref="DbUpdateException"/>（<see langword="null"/> = 不注入故障）。
    /// </summary>
    /// <remarks>
    /// ★ 故障注在 <c>SaveChanges</c> 这一层而不是用桩替掉仓储：被测的是「播种器写失败之后还能不能继续」，
    /// 而写失败在真实世界里就发生在这一跳（唯一索引、列长上限、连接抖动）。桩掉仓储会连变更跟踪器
    /// 一起换掉，而「失败的实体留在跟踪器里被下一份重放」正是要验的东西。
    /// 真实的一个触发场景：<c>Template.Metadata</c> 有 <c>HasMaxLength(4000)</c> 上限，
    /// 消费方塞满它之后再追加指纹键就会越界。
    /// </remarks>
    public string? ForTemplateName { get; set; }

    /// <summary>
    /// 只让故障发生<b>一次</b>：此后同一实体若被后面某一份的 <c>SaveChanges</c> 重放，就会真的写进去 ——
    /// 这正是「失败的实体没有被丢弃」的可观测形态（我们报告失败，数据库里却成功了）。
    /// </summary>
    public bool Once { get; set; }
}

/// <summary>
/// 模板存储用的最小 DbContext（<see cref="FinanceTestDbContext"/> 不含模板表）
/// </summary>
public class TemplateStoreTestDbContext : TnziDbContext<TemplateStoreTestDbContext>
{
    private readonly TemplateWriteFault _fault;

    public TemplateStoreTestDbContext(
        DbContextOptions<TemplateStoreTestDbContext> options, ICurrentUser currentUser, TemplateWriteFault fault)
        : base(options, currentUser)
    {
        _fault = fault;
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var target = _fault.ForTemplateName;
        if (target != null
            && ChangeTracker.Entries<TemplateEntity>()
                .Any(e => e.State is EntityState.Modified or EntityState.Added && e.Entity.TemplateName == target))
        {
            if (_fault.Once)
                _fault.ForTemplateName = null;
            throw new DbUpdateException($"injected write failure for '{target}'");
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TemplateConfiguration());
        modelBuilder.ApplyConfiguration(new LayoutConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 出厂支票模板的播种语义：刷新出厂版，永不覆盖用户编辑
/// </summary>
/// <remarks>
/// ★ 本组走的是<b>真实播种入口</b>（<c>CheckTemplateSeeder.ExecuteAsync</c>，
/// 生产里由 <c>IPostMigrationStartupTask</c> 在每次启动调用）+ 真实 SQLite 库 + 真实审计拦截器。
/// 判据函数自己的单元测试在 <c>Tnzi.Template.Tests</c> —— 那一组全绿只说明判据算得对，
/// <b>说明不了播种器真的问过它</b>。本仓反复兑现过这条。
/// <para>
/// ★ <c>LastModificationTime</c> 由审计拦截器写，不是测试手工填的：整个机制的存量行判据
/// 就架在「拦截器只在 <c>Modified</c> 时写它」这条上，手工填等于把被测的那条假设也一起假造了。
/// </para>
/// </remarks>
public class CheckTemplateSeederTests : IntegratedTestBase<TemplateStoreTestDbContext>
{
    private const string StaleBody = "<html>an older factory body</html>";
    private const string UserBody = "<html>the shop moved the payee box 2mm left</html>";

    /// <summary>播种器实际写出的日志（基类注册的是 NullLogger，看不见任何东西）。</summary>
    private readonly CapturingLogger<CheckTemplateSeeder> _seederLog = new();

    /// <summary>写入故障注入（播种器自建 scope，故必须是单例才到得了它那一侧）。</summary>
    private readonly TemplateWriteFault _fault = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(_fault);
        services.AddScoped<IRepository<TemplateEntity, Guid>>(sp =>
            new EFCoreRepository<TemplateStoreTestDbContext, TemplateEntity, Guid>(
                sp.GetRequiredService<TemplateStoreTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TemplateEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TemplateEntity, Guid>>());
        // 基类先注册了 NullLogger<>，这一条在其后 → GetRequiredService 取到本条
        services.AddSingleton<ILogger<CheckTemplateSeeder>>(_seederLog);
    }

    // ── A：出厂正文变了，消费应用不必写迁移 ──────────────────

    [Fact]
    public async Task FirstRun_SeedsEveryBuiltInTemplateWithItsFingerprint()
    {
        await SeedAsync();

        var rows = await AllRowsAsync();
        rows.Count.ShouldBe(BuiltInCheckTemplates.All.Count);

        foreach (var row in rows)
        {
            // 指纹与正文必须同一次写入 —— 少了它，这一行下次启动会被当成「存量行」处理
            SeededTemplateRefresh.ReadFingerprint(row.Metadata)
                .ShouldBe(SeededTemplateRefresh.Fingerprint(row.ContentTemplate),
                    $"'{row.TemplateName}' was seeded without a fingerprint that matches its body");
            row.LastModificationTime.ShouldBeNull();
        }
    }

    [Fact]
    public async Task StaleFactoryRow_IsRefreshedToTheBodyShippedWithTheAssembly()
    {
        // 上一版程序集播下去的行：指纹与它当时的正文吻合，只是那份正文已经旧了
        await SeedAsync();
        await MakeStaleAsync(CheckTemplates.Cpa006Canada);

        await SeedAsync();

        var row = await RowAsync(CheckTemplates.Cpa006Canada);
        row.ContentTemplate.ShouldNotBe(StaleBody);
        row.ContentTemplate.ShouldBe(ShippedBody(CheckTemplates.Cpa006Canada));
        row.Version.ShouldBe(2, "the body changed, so the revision must move with it");
    }

    [Fact]
    public async Task RefreshedRow_ActuallyCarriesTheFixesMadeInTheAssembly()
    {
        // A 的落地形态：一个已经播种过的库，光靠启动就拿到了框架里修好的东西。
        // 这两处正是前两轮各写了一条消费方迁移才送达的改动。
        await SeedAsync();
        await MakeStaleAsync(CheckTemplates.Cpa006Canada);

        await SeedAsync();

        var body = (await RowAsync(CheckTemplates.Cpa006Canada)).ContentTemplate;
        body.Contains("stub-extra", StringComparison.Ordinal)
            .ShouldBeTrue("the per-cheque stub lines never reached an already-seeded database");
        body.Contains("specimen", StringComparison.Ordinal)
            .ShouldBeTrue("the specimen shading never reached an already-seeded database");
    }

    // ── B：被人改过的模板绝不被覆盖 ──────────────────────────

    [Fact]
    public async Task UserEditedTemplate_IsNeverOverwritten()
    {
        await SeedAsync();
        await EditThroughTheStoreAsync(CheckTemplates.Cpa006Canada, UserBody);

        await SeedAsync();

        var row = await RowAsync(CheckTemplates.Cpa006Canada);
        row.ContentTemplate.ShouldBe(UserBody, "someone's calibrated layout was silently replaced by the factory one");
    }

    [Fact]
    public async Task UserEditedTemplate_StaysUntouchedAcrossManyRestarts()
    {
        // 一次不覆盖不够 —— 每次启动都跑一遍，得每次都不覆盖
        await SeedAsync();
        await EditThroughTheStoreAsync(CheckTemplates.Cpa006Canada, UserBody);

        for (var i = 0; i < 3; i++)
            await SeedAsync();

        (await RowAsync(CheckTemplates.Cpa006Canada)).ContentTemplate.ShouldBe(UserBody);
    }

    [Fact]
    public async Task RefreshingOneTemplate_DoesNotDisturbTheOthers()
    {
        await SeedAsync();
        await MakeStaleAsync(CheckTemplates.Cpa006Canada);
        var untouchedBefore = await RowAsync(CheckTemplates.VoucherTopUs);
        var versionBefore = untouchedBefore.Version;

        await SeedAsync();

        var untouchedAfter = await RowAsync(CheckTemplates.VoucherTopUs);
        untouchedAfter.Version.ShouldBe(versionBefore);
        untouchedAfter.LastModificationTime.ShouldBeNull("an up-to-date row must not be written at all");
    }

    // ── D：每次启动都跑，不能每次都写一遍 ────────────────────

    [Fact]
    public async Task SecondRunWithNothingToDo_WritesNothing()
    {
        await SeedAsync();
        var before = await AllRowsAsync();

        await SeedAsync();

        var after = await AllRowsAsync();
        after.Count.ShouldBe(before.Count, "a second run must not insert duplicates");
        foreach (var row in after)
        {
            // ★ 空写一遍会把 LastModificationTime 顶成当前时间，
            // 从此再也分不出「这行有没有被人动过」—— 存量行判据当场失效。
            row.LastModificationTime.ShouldBeNull($"'{row.TemplateName}' was rewritten with nothing to change");
            row.Version.ShouldBe(1);
        }
    }

    // ── C：存量行（没有指纹）★★ 本轮最需要想清楚的一处 ──────

    [Fact]
    public async Task LegacyRowNobodyEverEdited_IsAdoptedAndRefreshed()
    {
        // 这就是调用方现在库里的样子：机制诞生前播下去的行，Metadata 为空、从未被更新过。
        await InsertLegacyRowAsync(CheckTemplates.Cpa006Canada, StaleBody);

        await SeedAsync();

        var row = await RowAsync(CheckTemplates.Cpa006Canada);
        row.ContentTemplate.ShouldBe(ShippedBody(CheckTemplates.Cpa006Canada),
            "the legacy rows are exactly the ones the two hand-written migrations existed to fix");
        SeededTemplateRefresh.ReadFingerprint(row.Metadata).ShouldNotBeNull("the row must be adopted, not re-examined every restart");
    }

    [Fact]
    public async Task LegacyRowEditedBeforeTheMechanismExisted_IsNeverTouched()
    {
        // 同样没有指纹，但有人动过 —— 唯一能证明这件事的痕迹就是 LastModificationTime
        await InsertLegacyRowAsync(CheckTemplates.Cpa006Canada, StaleBody);
        await EditThroughTheStoreAsync(CheckTemplates.Cpa006Canada, UserBody);

        await SeedAsync();

        (await RowAsync(CheckTemplates.Cpa006Canada)).ContentTemplate.ShouldBe(UserBody,
            "a layout edited before we started fingerprinting is still someone's work");
    }

    [Fact]
    public async Task OnceAdopted_ALegacyRowKeepsBeingRefreshable()
    {
        // ★★ 采纳那次写入自己会把 LastModificationTime 置上。若判据此后还看它，
        // 这个机制就只会生效一次 —— 而那与「消费方仍要写迁移」几乎没有区别。
        await InsertLegacyRowAsync(CheckTemplates.Cpa006Canada, StaleBody);
        await SeedAsync();
        (await RowAsync(CheckTemplates.Cpa006Canada)).LastModificationTime.ShouldNotBeNull(
            "premise: adopting the row does stamp a modification time");

        await MakeStaleAsync(CheckTemplates.Cpa006Canada);
        await SeedAsync();

        (await RowAsync(CheckTemplates.Cpa006Canada)).ContentTemplate
            .ShouldBe(ShippedBody(CheckTemplates.Cpa006Canada), "the refresh only ever worked once");
    }

    [Fact]
    public async Task LegacyRowWhoseMetadataIsNotAJsonObject_IsLeftAlone()
    {
        // 别人的数据结构，看不懂就不动 —— 更不能为了塞指纹把它覆写掉
        await InsertLegacyRowAsync(CheckTemplates.Cpa006Canada, StaleBody, metadata: "[\"not\",\"an\",\"object\"]");

        await SeedAsync();

        var row = await RowAsync(CheckTemplates.Cpa006Canada);
        row.ContentTemplate.ShouldBe(StaleBody);
        row.Metadata.ShouldBe("[\"not\",\"an\",\"object\"]");
    }

    // ── 刷新只写正文 ─────────────────────────────────────────

    [Fact]
    public async Task Refresh_KeepsTheDescriptionAndTheActiveFlagTheConsumerChose()
    {
        await SeedAsync();
        await MakeStaleAsync(CheckTemplates.Cpa006Canada, deactivateAndRename: true);

        await SeedAsync();

        var row = await RowAsync(CheckTemplates.Cpa006Canada);
        row.ContentTemplate.ShouldBe(ShippedBody(CheckTemplates.Cpa006Canada), "the body is ours to refresh");
        row.Description.ShouldBe("kept by the consumer");
        row.IsActive.ShouldBeFalse("deactivating a layout is a deployment decision, not a stale body");
    }

    [Fact]
    public async Task Refresh_KeepsOtherKeysInTheMetadata()
    {
        await SeedAsync();
        await MakeStaleAsync(CheckTemplates.Cpa006Canada, extraMetadataKey: "printerTray");

        await SeedAsync();

        var metadata = (await RowAsync(CheckTemplates.Cpa006Canada)).Metadata;
        metadata.ShouldNotBeNull();
        metadata.ShouldContain("printerTray");
        SeededTemplateRefresh.ReadFingerprint(metadata)
            .ShouldBe(SeededTemplateRefresh.Fingerprint(ShippedBody(CheckTemplates.Cpa006Canada)));
    }

    // ── 一份写失败不连累其余（类注释所承诺的那句话）★★ ──────

    [Fact]
    public async Task OneTemplateFailingToRefresh_DoesNotStopTheOnesAfterIt()
    {
        // ★ 受害者取清单里的**第二份**，后面还有四份 —— 取最后一份的话，
        // 「后面的被跳过了」与「后面本来就没有」在断言上分不开。
        var victim = BuiltInCheckTemplates.All[1].Name;
        await SeedAllStaleAsync();
        _fault.ForTemplateName = victim;

        // 抛出去就是回归本身：这一整轮会被外层启动任务吞成一条只指向 CheckTemplateSeeder 的错误
        await SeedAsync();

        _fault.ForTemplateName = null;
        (await RowAsync(victim)).ContentTemplate.ShouldBe(StaleBody,
            "premise: the injected failure really did stop this one from being refreshed");
        foreach (var other in BuiltInCheckTemplates.All.Where(t => t.Name != victim))
        {
            (await RowAsync(other.Name)).ContentTemplate.ShouldBe(ShippedBody(other.Name),
                $"'{other.Name}' never got its factory fix because an unrelated layout failed first");
        }
    }

    [Fact]
    public async Task AFailedRefresh_LeavesATraceThatNamesTheTemplate()
    {
        // 吞掉异常之后，日志是唯一的痕迹；而外层那条错误只指向 CheckTemplateSeeder 整体，
        // 看不出是哪一份 —— 那正是 B 要求的东西。
        var victim = BuiltInCheckTemplates.All[1].Name;
        await SeedAllStaleAsync();
        _fault.ForTemplateName = victim;
        _seederLog.Entries.Clear();

        await SeedAsync();

        var failure = _seederLog.Entries.SingleOrDefault(e => e.Level == LogLevel.Error);
        failure.ShouldNotBeNull("a swallowed failure with no trace is worse than the crash it replaced");
        failure.Message.ShouldContain(victim, Case.Sensitive, "the trace must name which layout failed");
        failure.Exception.ShouldNotBeNull("the cause must survive too, not just the fact that something failed");

        // 而且不能把失败记成成功：刷成功的恰好是其余五份
        _seederLog.Entries.Count(e => e.Level == LogLevel.Information)
            .ShouldBe(BuiltInCheckTemplates.All.Count - 1);
        _seederLog.Entries.ShouldNotContain(e => e.Level == LogLevel.Information && e.Message.Contains(victim, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedRefresh_IsNotQuietlyCommittedByTheNextTemplatesWrite()
    {
        // ★★ 丢弃失败实体的真正形态：它仍是 Modified 留在变更跟踪器里，
        // 下一份的 SaveChanges 会连它一起提交 —— 于是一次**我们刚刚报告为失败**的写入
        // 悄悄成功了，日志与数据库互相矛盾。只让故障发生一次才看得见这个形态
        // （一直失败的话，重放会再炸一次，看起来像是被正确挡住了）。
        var victim = BuiltInCheckTemplates.All[1].Name;
        await SeedAllStaleAsync();
        _fault.ForTemplateName = victim;
        _fault.Once = true;

        await SeedAsync();

        var row = await RowAsync(victim);
        row.ContentTemplate.ShouldBe(StaleBody,
            "the write we reported as failed was quietly committed by a later template's SaveChanges");
        row.Version.ShouldBe(1);
    }

    [Fact]
    public async Task ATemplateThatFailedOnce_IsRefreshedOnTheNextStartup()
    {
        // 吞掉不等于放弃：下一次启动照常再试一遍（这也是「后果不重」的全部依据）
        var victim = BuiltInCheckTemplates.All[1].Name;
        await SeedAllStaleAsync();
        _fault.ForTemplateName = victim;
        await SeedAsync();

        _fault.ForTemplateName = null;
        await SeedAsync();

        (await RowAsync(victim)).ContentTemplate.ShouldBe(ShippedBody(victim));
    }

    // ── 夹具 ─────────────────────────────────────────────────

    /// <summary>播下全部六份，再把每一份都做旧 —— 于是下一轮播种六份都要刷新。</summary>
    private async Task SeedAllStaleAsync()
    {
        await SeedAsync();
        foreach (var builtIn in BuiltInCheckTemplates.All)
            await MakeStaleAsync(builtIn.Name);
    }

    private sealed record CapturedLog(LogLevel Level, string Message, Exception? Exception);

    /// <summary>把日志留在内存里的 <see cref="ILogger{T}"/>（基类注册的 NullLogger 什么都不留）。</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new CapturedLog(logLevel, formatter(state, exception), exception));
    }

    /// <summary>跑一次真实的启动期播种。</summary>
    private Task SeedAsync() => new CheckTemplateSeeder().ExecuteAsync(ServiceProvider);

    private static string ShippedBody(string templateName)
    {
        var builtIn = BuiltInCheckTemplates.All.First(t => t.Name == templateName);
        var assembly = typeof(CheckTemplateSeeder).Assembly;
        var suffix = "Templates." + builtIn.ResourceFile;
        var name = Array.Find(assembly.GetManifestResourceNames(), n => n.EndsWith(suffix, StringComparison.Ordinal));
        name.ShouldNotBeNull($"embedded template '{builtIn.ResourceFile}'");

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 把一行变成「上一版程序集播下去的」：正文旧，但指纹与该旧正文吻合 —— 仍归框架所有。
    /// </summary>
    private async Task MakeStaleAsync(string templateName, bool deactivateAndRename = false, string? extraMetadataKey = null)
    {
        var extra = extraMetadataKey == null ? null : $$"""{"{{extraMetadataKey}}":"upper"}""";
        var metadata = SeededTemplateRefresh.TryWriteFingerprint(extra, SeededTemplateRefresh.Fingerprint(StaleBody));

        // ★ 走 ExecuteUpdate 而不是 SaveChanges：模拟的是「上一版程序集写下的行」，
        // 而经 SaveChanges 写就会被审计拦截器盖上修改时间 —— 那样得到的是「有人改过的行」，
        // 换了一个测试对象，且那些用例会因为改错了前提而假绿。
        var rows = await DbContext.Set<TemplateEntity>()
            .Where(t => t.TemplateName == templateName && t.Module == CheckTemplates.Module)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.ContentTemplate, StaleBody)
                .SetProperty(t => t.Metadata, metadata)
                .SetProperty(t => t.LastModificationTime, (DateTime?)null)
                .SetProperty(t => t.Version, 1));
        rows.ShouldBe(1);

        if (deactivateAndRename)
        {
            await DbContext.Set<TemplateEntity>()
                .Where(t => t.TemplateName == templateName && t.Module == CheckTemplates.Module)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Description, "kept by the consumer")
                    .SetProperty(t => t.IsActive, false));
        }

        DbContext.ChangeTracker.Clear();
        (await RowAsync(templateName)).LastModificationTime.ShouldBeNull(
            "premise: this row must look like the previous assembly wrote it, not like a person did");
    }

    /// <summary>机制诞生前播下去的行：没有指纹，且从未被更新过（审计字段自然为 null）。</summary>
    private async Task InsertLegacyRowAsync(string templateName, string body, string? metadata = null)
    {
        var repository = ServiceProvider.GetRequiredService<IRepository<TemplateEntity, Guid>>();
        await repository.InsertAsync(new TemplateEntity
        {
            TemplateName = templateName,
            Module = CheckTemplates.Module,
            Category = CheckTemplates.Category,
            Type = TemplateType.Print,
            ContentTemplate = body,
            SubjectTemplate = string.Empty,
            IsActive = true,
            Metadata = metadata
        });
        await repository.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await RowAsync(templateName)).LastModificationTime.ShouldBeNull(
            "premise: an inserted row carries no modification time");
    }

    /// <summary>经仓储改正文 —— 走的是管理端那条路，审计拦截器会照常盖上修改时间。</summary>
    private async Task EditThroughTheStoreAsync(string templateName, string body)
    {
        var repository = ServiceProvider.GetRequiredService<IRepository<TemplateEntity, Guid>>();
        var row = await repository.AsQueryable(withTracking: true)
            .FirstAsync(t => t.TemplateName == templateName && t.Module == CheckTemplates.Module);
        row.ContentTemplate = body;
        await repository.UpdateAsync(row);
        await repository.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        (await RowAsync(templateName)).LastModificationTime.ShouldNotBeNull(
            "premise: editing through the store does stamp a modification time");
    }

    private async Task<TemplateEntity> RowAsync(string templateName)
    {
        DbContext.ChangeTracker.Clear();
        var repository = ServiceProvider.GetRequiredService<IRepository<TemplateEntity, Guid>>();
        var row = await repository.AsQueryable()
            .FirstOrDefaultAsync(t => t.TemplateName == templateName && t.Module == CheckTemplates.Module);
        return row.ShouldNotBeNull($"template row '{templateName}'");
    }

    private async Task<List<TemplateEntity>> AllRowsAsync()
    {
        DbContext.ChangeTracker.Clear();
        var repository = ServiceProvider.GetRequiredService<IRepository<TemplateEntity, Guid>>();
        return await repository.AsQueryable()
            .Where(t => t.Module == CheckTemplates.Module && t.Category == CheckTemplates.Category)
            .ToListAsync();
    }
}
