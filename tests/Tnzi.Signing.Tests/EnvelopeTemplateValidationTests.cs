using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tnzi.Documents.Services;
using Tnzi.EFCore;
using Tnzi.Results;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using Tnzi.Storage;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Services;
using Tnzi.TestBase;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 上传型模板的创建校验。
/// </summary>
/// <remarks>
/// <para>
/// 签署请求拿模板的 <c>RenderedPdfFileId</c> 当底稿（发出时再把发起方的值烧上去）—— <c>EnvelopeService</c> 只对
/// Composed 模板逐份现排版。所以一个只给了原件、没给渲染稿的上传型模板会**保存成功**，
/// 然后建出来的每一份请求都带着一个空的文档引用走完整个流程，直到有人去签才发现没有东西可签。
/// </para>
/// <para>
/// 校验因此把这件事挡在建模板那一刻，而不是留到签署那一刻。
/// </para>
/// </remarks>
public class EnvelopeTemplateValidationTests : IntegratedTestBase<SigningRaceDbContext>
{
    private EnvelopeTemplateService Service => new(
        ServiceProvider,
        new EFCoreRepository<SigningRaceDbContext, EnvelopeTemplate, Guid>(DbContext, serviceProvider: ServiceProvider),
        new EFCoreRepository<SigningRaceDbContext, Field, Guid>(DbContext, serviceProvider: ServiceProvider),
        new EFCoreRepository<SigningRaceDbContext, Envelope, Guid>(DbContext, serviceProvider: ServiceProvider),
        PermissiveFileAccess(),
        Files,
        new SigningSealer(
            new PdfSharpPdfStamper(NullLogger<PdfSharpPdfStamper>.Instance),
            new PdfPigPdfInspector(),
            Files,
            NullLogger<SigningSealer>.Instance));

    /// <summary>任何 id 读出来都是这份 PDF。默认单页；个别用例换成别的文档。</summary>
    private byte[] _document = TestPdfs.SinglePage;

    private IFileStorageService Files => AnyPdfRecord(() => _document);

    /// <summary>一律放行的归属探针：这一组关心的是表单校验，归属那道门在 <c>EnvelopeTemplateFileAccessTests</c>（真实 Storage 栈）。</summary>
    private static IFileReadAccessProbe PermissiveFileAccess()
    {
        var probe = new Mock<IFileReadAccessProbe>();
        probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return probe.Object;
    }

    /// <summary>任何 id 都是一份 PDF 记录，读出来的字节是 <paramref name="document"/> 给的那份。</summary>
    private static IFileStorageService AnyPdfRecord(Func<byte[]> document)
    {
        var files = new Mock<IFileStorageService>();
        files.Setup(f => f.GetRecordAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => Result.Success(new FileRecord { Id = id, FileName = "contract.pdf", ContentType = "application/pdf" }));
        files.Setup(f => f.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => Result.Success<Stream>(new MemoryStream(document())));
        return files.Object;
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

    [Fact]
    public async Task Uploaded_template_without_a_source_file_is_refused()
    {
        var result = await Service.CreateAsync(Uploaded(source: null, rendered: null));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    /// <summary>
    /// ★ 这条是本轮修的：只给原件、不给渲染稿此前会**保存成功**。
    /// </summary>
    [Fact]
    public async Task Uploaded_template_without_a_rendered_pdf_is_refused_at_creation()
    {
        var result = await Service.CreateAsync(Uploaded(source: Guid.NewGuid(), rendered: null));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);

        // 消息要能指导下一步：原件已经是 PDF 就把同一个 id 再传一次，不是的话先转换。
        Assert.Contains("rendered PDF", result.Message, StringComparison.OrdinalIgnoreCase);

        // 同时断言状态：只返回错误却照样落库的实现会骗过前两条。
        DbContext.ChangeTracker.Clear();
        Assert.Empty(DbContext.Set<EnvelopeTemplate>().ToList());
    }

    [Fact]
    public async Task Uploaded_template_with_both_file_ids_is_accepted()
    {
        var fileId = Guid.NewGuid();

        // 原件已经是 PDF 的常见情形：两者是同一个文件，这就是这条路径的全部「转换」。
        var result = await Service.CreateAsync(Uploaded(source: fileId, rendered: fileId));

        Assert.True(result.Succeeded);
        Assert.Equal(fileId, result.Data!.SourceFileId);
        Assert.Equal(fileId, result.Data.RenderedPdfFileId);
    }

    private static TemplateFieldInputDto Field(SigningFieldType type, string? recipientRole) => new()
    {
        Key = "sig",
        Label = "Signature",
        Type = type,
        RecipientRole = recipientRole,
        PlacementMode = FieldPlacementMode.Absolute,
        Page = 1,
        X = 0.1m,
        Y = 0.8m,
        W = 0.3m,
        H = 0.05m,
    };

    private static CreateEnvelopeTemplateDto UploadedWith(TemplateFieldInputDto field)
    {
        var fileId = Guid.NewGuid();
        var dto = Uploaded(source: fileId, rendered: fileId);
        dto.Fields = [field];
        return dto;
    }

    /// <summary>
    /// 签名类字段必须说清谁来签。没有角色的签名字段不属于任何收件人的「我的字段」：
    /// 没人被要求交图，密封时按角色找不到图就静默跳过，成品没有签名却照常出证书、归档。
    /// </summary>
    [Theory]
    [InlineData(SigningFieldType.Signature, null)]
    [InlineData(SigningFieldType.Signature, "")]
    [InlineData(SigningFieldType.Signature, "   ")]
    [InlineData(SigningFieldType.Initials, null)]
    [InlineData(SigningFieldType.Initials, "")]
    public async Task A_signature_field_without_a_recipient_role_is_refused(SigningFieldType type, string? role)
    {
        var result = await Service.CreateAsync(UploadedWith(Field(type, role)));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("sig", result.Message);
        Assert.Contains("recipient role", result.Message, StringComparison.OrdinalIgnoreCase);

        DbContext.ChangeTracker.Clear();
        Assert.Empty(DbContext.Set<EnvelopeTemplate>().ToList());
    }

    [Theory]
    [InlineData(SigningFieldType.Signature)]
    [InlineData(SigningFieldType.Initials)]
    public async Task A_signature_field_with_a_recipient_role_is_accepted(SigningFieldType type)
    {
        var result = await Service.CreateAsync(UploadedWith(Field(type, "Client")));

        Assert.True(result.Succeeded, result.Message);
    }

    /// <summary>发起方预填是文本字段的合法选择 —— 这条规则只管签名类字段。</summary>
    [Theory]
    [InlineData(SigningFieldType.Text)]
    [InlineData(SigningFieldType.Date)]
    [InlineData(SigningFieldType.Number)]
    [InlineData(SigningFieldType.Checkbox)]
    public async Task A_text_field_without_a_recipient_role_is_still_allowed(SigningFieldType type)
    {
        var result = await Service.CreateAsync(UploadedWith(Field(type, null)));

        Assert.True(result.Succeeded, result.Message);
    }

    [Fact]
    public async Task Composed_template_is_unaffected_by_the_uploaded_rules()
    {
        var result = await Service.CreateAsync(new CreateEnvelopeTemplateDto
        {
            Name = "Composed NDA",
            Source = TemplateSource.Composed,
            BodyTemplate = "<p>Hello {{name}}</p>",
            PageCount = 1,
            IsActive = true,
            Fields = [],
        });

        Assert.True(result.Succeeded);
    }
    // ── 渲染稿本身：页数、能不能盖章、锚找不找得到 ───────────────────────────────

    private static TemplateFieldInputDto AbsoluteSignature(int page) => new()
    {
        Key = "clientSig",
        Label = "Client signature",
        Type = SigningFieldType.Signature,
        RecipientRole = "Client",
        PlacementMode = FieldPlacementMode.Absolute,
        Page = page,
        X = 0.1m,
        Y = 0.8m,
        W = 0.3m,
        H = 0.05m,
    };

    private static TemplateFieldInputDto AnchoredSignature(string anchor) => new()
    {
        Key = "clientSig",
        Label = "Client signature",
        Type = SigningFieldType.Signature,
        RecipientRole = "Client",
        PlacementMode = FieldPlacementMode.Anchor,
        AnchorText = anchor,
        W = 0.3m,
        H = 0.05m,
    };

    /// <summary>
    /// ★ 页数取自文档本身：操作员填的数不再是判据。一份三页的文档、页数手填成 1、签名框放在第 3 页，这是合法的模板。
    /// </summary>
    [Fact]
    public async Task An_uploaded_template_takes_its_page_count_from_the_document()
    {
        _document = TestPdfs.Pages(3);
        var dto = UploadedWith(AbsoluteSignature(page: 3));
        dto.PageCount = 1;

        var result = await Service.CreateAsync(dto);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3, result.Data!.PageCount);
    }

    /// <summary>
    /// ★ 页数手填多了一页、签名框放在那一页上：此前模板存得下、请求发得出、每个人都签得完，直到密封时页码越界抛异常，
    /// 而重新密封失败得一模一样。现在按文档的真实页数拦在保存这一步。
    /// </summary>
    [Fact]
    public async Task A_field_beyond_the_documents_last_page_is_refused_at_save()
    {
        _document = TestPdfs.Pages(3);
        var dto = UploadedWith(AbsoluteSignature(page: 4));
        dto.PageCount = 4;

        var result = await Service.CreateAsync(dto);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("clientSig", result.Message);
        Assert.Contains("3 page", result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Empty(DbContext.Set<EnvelopeTemplate>().ToList());
    }

    /// <summary>
    /// ★ 带所有者密码（禁止编辑）的表格：读得开，但盖章器以修改模式打开时被拒，此前要等到密封才炸，那时每个人都签完了。
    /// </summary>
    [Fact]
    public async Task An_edit_restricted_document_is_refused_at_save()
    {
        _document = TestPdfs.EditRestricted();

        var result = await Service.CreateAsync(UploadedWith(AbsoluteSignature(page: 1)));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("cannot be modified", result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Empty(DbContext.Set<EnvelopeTemplate>().ToList());
    }

    [Fact]
    public async Task A_document_that_is_not_a_readable_pdf_is_refused_at_save()
    {
        _document = "%PDF-1.4 this is not really a PDF"u8.ToArray();

        var result = await Service.CreateAsync(UploadedWith(AbsoluteSignature(page: 1)));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("not a readable PDF", result.Message);
    }

    /// <summary>
    /// ★ 多词锚：很多 PDF 生成器用字距偏移而不是空格字形分词，读出来的文本里两个词之间什么都没有。
    /// 锚文本按字面带空格去匹配就永远找不到；词与词之间必须允许没有空白。首尾的空白不是锚的一部分。
    /// </summary>
    [Fact]
    public async Task A_multi_word_anchor_is_found_even_when_the_document_draws_no_space_between_the_words()
    {
        _document = TestPdfs.Pages(1, new TestPdfs.TextRun("Client", 72, 120), new TestPdfs.TextRun("Signature", 112, 120));

        var result = await Service.CreateAsync(UploadedWith(AnchoredSignature("  Client Signature ")));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Client Signature", Assert.Single(result.Data!.Fields).AnchorText);
    }

    /// <summary>锚文本在文档里根本不存在：密封时那个签名框会被静默跳过，所以在保存这一步就指名拒绝。</summary>
    [Fact]
    public async Task An_anchor_the_document_does_not_contain_is_refused_naming_the_field()
    {
        _document = TestPdfs.Pages(1, new TestPdfs.TextRun("Client", 72, 120), new TestPdfs.TextRun("Signature", 112, 120));

        var result = await Service.CreateAsync(UploadedWith(AnchoredSignature("Witness Signature")));

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("clientSig", result.Message);
        Assert.Contains("Witness Signature", result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Empty(DbContext.Set<EnvelopeTemplate>().ToList());
    }

    /// <summary>拼装型模板没有现成的文档可读：页数沿用请求体，校验照旧对照它。</summary>
    [Fact]
    public async Task A_composed_template_still_checks_pages_against_its_declared_page_count()
    {
        var result = await Service.CreateAsync(new CreateEnvelopeTemplateDto
        {
            Name = "Composed NDA",
            Source = TemplateSource.Composed,
            BodyTemplate = "<p>Hello {{name}}</p>",
            PageCount = 1,
            IsActive = true,
            Fields = [AbsoluteSignature(page: 2)],
        });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }
}
