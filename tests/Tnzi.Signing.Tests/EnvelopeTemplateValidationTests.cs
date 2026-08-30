using Tnzi.EFCore;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Metadata;
using Tnzi.TestBase;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 上传型模板的创建校验。
/// </summary>
/// <remarks>
/// <para>
/// 签署请求直接拿模板的 <c>RenderedPdfFileId</c> 当底稿 —— <c>EnvelopeService</c> 只对
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
        new EFCoreRepository<SigningRaceDbContext, Envelope, Guid>(DbContext, serviceProvider: ServiceProvider));

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
}
