using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.EFCore;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Storage;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Options;
using Tnzi.Storage.Services;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 模板上的两个文件 id（原件 / 渲染稿）来自请求体，写进 <c>[FileField]</c> 之前必须先问
/// <c>IFileReadAccessProbe</c>：「这个人本来就读得到它吗」。
/// </summary>
/// <remarks>
/// <para>
/// ★ 不问就写 = 「按 id 读任意文件」。模板一旦落库，<c>SigningFileReferenceAccessResolver</c> 对
/// <c>EnvelopeTemplate</c> 名下的文件按 <c>signing.template.view</c> 放行 —— 于是持
/// <c>signing.template.create</c> + <c>.view</c>、但不持 <c>storage.file.view</c> 的人，把别人的
/// 文件 id 填进模板保存成功后，就能经 <c>files/{id}/download</c> 取到明文；再往前一步，
/// 把它当渲染稿发起一份请求，令牌就把这份文件原样交给一个匿名的外部收件人。
/// </para>
/// <para>
/// 这一组跑在<b>真实</b> Storage 栈上（<see cref="SigningStorageTestBase"/>）：探针是生产那一个，
/// 归属判据是 <c>FileAccessAuthorizer</c> 自己算的，不是 mock 回答的。
/// </para>
/// </remarks>
public class EnvelopeTemplateFileAccessTests : SigningStorageTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        // 让 [FileField] 的引用登记真的跑起来（生产里随 Tnzi.Storage 注册）：
        // 「拒绝之后一行引用都没有」与「放行之后引用确实在」都要看得见。
        services.AddScoped<IFileReferenceProcessor>(sp =>
        {
            var db = sp.GetRequiredService<SigningStorageDbContext>();
            var options = new Mock<IOptionsMonitor<StorageOptions>>();
            options.Setup(o => o.CurrentValue).Returns(StorageOptions);
            return new FileReferenceProcessor(
                new EFCoreRepository<SigningStorageDbContext, FileReference, Guid>(db, null, sp),
                new EFCoreRepository<SigningStorageDbContext, FileRecord, Guid>(db, null, sp),
                NullLogger<FileReferenceProcessor>.Instance,
                options.Object);
        });
    }

    private static CreateEnvelopeTemplateDto Uploaded(Guid? source, Guid? rendered) => new()
    {
        Name = "Engagement letter",
        Source = TemplateSource.Uploaded,
        SourceFileId = source,
        RenderedPdfFileId = rendered,
        PageCount = 1,
        IsActive = true,
        Fields = [],
    };

    /// <summary>别人那份文件的字节：Storage 按 MD5 去重，与自己那份同字节会拿回同一条记录。</summary>
    private static readonly byte[] TheirBytes = [.. RenderedPdf, 0x0A, 0x25, 0x48, 0x52];

    /// <summary>把一份刚存下的文件改挂到别人名下：当前用户对它既不是创建者，也没有任何按引用的放行。</summary>
    private async Task<Guid> StoreSomeoneElsesAsync(string name)
    {
        var id = await StoreAsync(name, TheirBytes);
        var record = await DbContext.Set<FileRecord>().FirstAsync(r => r.Id == id);
        record.CreatorId = Guid.NewGuid();
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return id;
    }

    private Task<List<FileReference>> TemplateReferencesAsync()
        => DbContext.Set<FileReference>().AsNoTracking()
            .Where(r => r.EntityType == nameof(EnvelopeTemplate))
            .ToListAsync();

    // ── 不能 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 原件与渲染稿各试一次：渲染稿那一栏后面还有一次「是不是 PDF」的记录读取，它自己就会拒掉读不到的文件，
    /// 只测渲染稿会让探针那一问被它遮住（变异验证时确实遮住了）；原件那一栏只有探针这一道门。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_template_cannot_reference_a_file_its_author_cannot_read(bool sourceIsTheirs, bool renderedIsTheirs)
    {
        var mine = await StoreAsync("contract.pdf", RenderedPdf);
        var theirs = await StoreSomeoneElsesAsync("hr-file.pdf");

        using var request = BeginRequest();
        var result = await request.Templates.CreateAsync(Uploaded(
            source: sourceIsTheirs ? theirs : mine,
            rendered: renderedIsTheirs ? theirs : mine));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);

        // 只返回错误却照样落库的实现会骗过上面两条：模板行与引用行都必须不存在。
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<EnvelopeTemplate>().AsNoTracking().ToListAsync()).ShouldBeEmpty();
        (await TemplateReferencesAsync()).ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 探针问的是「这个人」，不是「这一次请求」：一条请求级授予（分享链接 / 签名 URL）能让
    /// <c>GetRecordAsync</c> 放行，却不能把那份文件换成一条永久引用 —— 引用一旦落库，
    /// 它的可见性就由模板说了算，与原来那份凭据的约束再无关系。
    /// </summary>
    [Fact]
    public async Task A_request_scoped_grant_does_not_turn_into_a_permanent_reference()
    {
        var theirs = await StoreSomeoneElsesAsync("shared-with-me.pdf");

        using var request = BeginRequest();
        request.Grants.Grant(theirs);
        (await request.Files.GetRecordAsync(theirs)).Succeeded.ShouldBeTrue("the grant itself must work, or this test proves nothing");

        var result = await request.Templates.CreateAsync(Uploaded(source: theirs, rendered: theirs));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        DbContext.ChangeTracker.Clear();
        (await TemplateReferencesAsync()).ShouldBeEmpty();
    }

    /// <summary>不存在的 id 与读不到的 id 是同一句话：分开回答会让这个端点变成「这个 id 存不存在」的探针。</summary>
    [Fact]
    public async Task A_missing_file_is_refused_with_the_same_answer_as_an_unreadable_one()
    {
        var theirs = await StoreSomeoneElsesAsync("hr-file.pdf");

        using var request = BeginRequest();
        var unreadable = await request.Templates.CreateAsync(Uploaded(source: theirs, rendered: theirs));
        var missing = await request.Templates.CreateAsync(Uploaded(source: Guid.NewGuid(), rendered: Guid.NewGuid()));

        missing.Succeeded.ShouldBeFalse();
        missing.Code.ShouldBe(unreadable.Code);
        missing.Message.ShouldBe(unreadable.Message);
    }

    [Fact]
    public async Task An_empty_file_id_is_refused_as_invalid()
    {
        using var request = BeginRequest();
        var result = await request.Templates.CreateAsync(Uploaded(source: Guid.Empty, rendered: Guid.Empty));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// 更新走同一道门：否则「建模板时用自己的文件、改模板时换成别人的」照样成立。
    /// </summary>
    [Fact]
    public async Task Updating_a_template_probes_the_new_file_ids()
    {
        var mine = await StoreAsync("contract.pdf", RenderedPdf);
        var theirs = await StoreSomeoneElsesAsync("hr-file.pdf");

        Guid templateId;
        using (var create = BeginRequest())
        {
            var created = await create.Templates.CreateAsync(Uploaded(source: mine, rendered: mine));
            created.Succeeded.ShouldBeTrue(created.Message);
            templateId = created.Data!.Id;
        }

        using var update = BeginRequest();
        var result = await update.Templates.UpdateAsync(templateId, new UpdateEnvelopeTemplateDto
        {
            Name = "Engagement letter",
            Source = TemplateSource.Uploaded,
            SourceFileId = theirs,
            RenderedPdfFileId = theirs,
            PageCount = 1,
            IsActive = true,
            Fields = [],
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);

        DbContext.ChangeTracker.Clear();
        var stored = await DbContext.Set<EnvelopeTemplate>().AsNoTracking().FirstAsync(t => t.Id == templateId);
        stored.SourceFileId.ShouldBe(mine);
        stored.RenderedPdfFileId.ShouldBe(mine);
        (await TemplateReferencesAsync()).ShouldAllBe(r => r.FileId == mine);
    }

    /// <summary>
    /// 渲染稿的契约是一份 PDF：收件人端点把它内联交给浏览器，密封器把它交给盖章器。
    /// 一个 <c>.html</c> 在前者是跑在 API 源上的脚本，在后者是一次密封失败。
    /// </summary>
    [Fact]
    public async Task A_rendered_document_must_be_a_pdf()
    {
        StorageOptions.AllowedExtensions = [".pdf", ".html"];
        var html = await StoreAsync("payload.html", "<script>alert(1)</script>"u8.ToArray());
        var pdf = await StoreAsync("contract.pdf", RenderedPdf);

        using var request = BeginRequest();
        var result = await request.Templates.CreateAsync(Uploaded(source: html, rendered: html));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message!.ShouldContain("PDF");

        // 原件可以不是 PDF（DOCX 等待转换），只有渲染稿必须是。
        var converted = await request.Templates.CreateAsync(Uploaded(source: html, rendered: pdf));
        converted.Succeeded.ShouldBeTrue(converted.Message);
    }

    // ── 能 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_template_can_reference_the_authors_own_upload()
    {
        var mine = await StoreAsync("contract.pdf", RenderedPdf);

        using var request = BeginRequest();
        var result = await request.Templates.CreateAsync(Uploaded(source: mine, rendered: mine));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.SourceFileId.ShouldBe(mine);
        result.Data.RenderedPdfFileId.ShouldBe(mine);

        // 引用行确实登记了：这才是「模板名下的文件」按 signing.template.view 放行的依据。
        DbContext.ChangeTracker.Clear();
        (await TemplateReferencesAsync()).ShouldContain(r => r.FileId == mine);
    }

    [Fact]
    public async Task A_composed_template_without_files_is_unaffected()
    {
        using var request = BeginRequest();
        var result = await request.Templates.CreateAsync(new CreateEnvelopeTemplateDto
        {
            Name = "Composed NDA",
            Source = TemplateSource.Composed,
            BodyTemplate = "<p>Hello {{name}}</p>",
            PageCount = 1,
            IsActive = true,
            Fields = [],
        });

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
