namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 单据附件与讨论：多态寻址、上限、去重、软删。
/// </summary>
public class DocumentCollaborationTests : FinanceIntegrationTestBase
{
    private const string Doc = FinanceSourceTypes.Invoice;

    private Task<Result<DocumentAttachmentDto>> AttachAsync(
        string docId, Guid fileId, string? name = "invoice.pdf", string? type = "application/pdf", long size = 1024)
        => InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(Doc, docId, new CreateDocumentAttachmentDto
            {
                FileId = fileId, FileName = name, ContentType = type, FileSize = size
            }));

    private Task<Result<List<DocumentAttachmentDto>>> ListAsync(string docId)
        => InScopeAsync<IDocumentAttachmentService, Result<List<DocumentAttachmentDto>>>(s => s.ListAsync(Doc, docId));

    [Fact]
    public async Task Attach_ThenList_ReturnsTheSnapshotTakenAtAttachTime()
    {
        var doc = Guid.NewGuid().ToString();
        var file = Guid.NewGuid();

        var attached = await AttachAsync(doc, file, "supplier-invoice.pdf", "application/pdf", 20480);

        attached.Succeeded.ShouldBeTrue(attached.Message);
        var list = await ListAsync(doc);
        list.Data!.Count.ShouldBe(1);
        list.Data[0].FileId.ShouldBe(file);
        // 名字/大小是附加那一刻的快照，列表不必为每一行回 Storage 查一次。
        list.Data[0].FileName.ShouldBe("supplier-invoice.pdf");
        list.Data[0].FileSize.ShouldBe(20480);
    }

    /// <summary>
    /// ★来源令牌是开放词汇：消费应用自己的单据类型也能挂附件。
    /// </summary>
    [Fact]
    public async Task Attach_AcceptsAConsumerDefinedDocumentType()
    {
        var doc = Guid.NewGuid().ToString();

        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync("Demo.ServiceOrder", doc, new CreateDocumentAttachmentDto
            {
                FileId = Guid.NewGuid(), FileName = "x.pdf"
            }));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    [Fact]
    public async Task Attach_IsScopedToItsOwnDocument()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        await AttachAsync(a, Guid.NewGuid());

        (await ListAsync(a)).Data!.Count.ShouldBe(1);
        (await ListAsync(b)).Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Attach_SameFileTwice_Rejected409()
    {
        var doc = Guid.NewGuid().ToString();
        var file = Guid.NewGuid();
        (await AttachAsync(doc, file)).Succeeded.ShouldBeTrue();

        // 同一个文件挂两次多半是重复点击。
        var again = await AttachAsync(doc, file);

        again.Succeeded.ShouldBeFalse();
        again.Code.ShouldBe(409);
        (await ListAsync(doc)).Data!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Attach_BeyondTheCap_Rejected409()
    {
        var doc = Guid.NewGuid().ToString();
        var max = ServiceProvider.GetRequiredService<IOptionsSnapshot<FinanceOptions>>().Value.MaxAttachmentsPerDocument;

        for (var i = 0; i < max; i++)
            (await AttachAsync(doc, Guid.NewGuid())).Succeeded.ShouldBeTrue();

        var overflow = await AttachAsync(doc, Guid.NewGuid());

        overflow.Succeeded.ShouldBeFalse();
        overflow.Code.ShouldBe(409);
        (await ListAsync(doc)).Data!.Count.ShouldBe(max);
    }

    [Fact]
    public async Task Attach_WithoutAFile_Rejected400()
    {
        var result = await AttachAsync(Guid.NewGuid().ToString(), Guid.Empty);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Attach_MissingDocumentKey_Rejected400()
    {
        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(Doc, "  ", new CreateDocumentAttachmentDto { FileId = Guid.NewGuid() }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Remove_TakesItOffTheDocument()
    {
        var doc = Guid.NewGuid().ToString();
        var attached = await AttachAsync(doc, Guid.NewGuid());

        (await InScopeAsync<IDocumentAttachmentService, Result>(s => s.RemoveAsync(attached.Data!.Id)))
            .Succeeded.ShouldBeTrue();

        (await ListAsync(doc)).Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task CountBySource_AnswersAWholePageInOneCall()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var c = Guid.NewGuid().ToString();
        await AttachAsync(a, Guid.NewGuid());
        await AttachAsync(a, Guid.NewGuid());
        await AttachAsync(b, Guid.NewGuid());

        var counts = await InScopeAsync<IDocumentAttachmentService, Result<Dictionary<string, int>>>(
            s => s.CountBySourceAsync(Doc, [a, b, c]));

        counts.Data![a].ShouldBe(2);
        counts.Data[b].ShouldBe(1);
        // 没有附件的单据不出现在结果里（呈现端按缺失当 0，不必回填）。
        counts.Data.ContainsKey(c).ShouldBeFalse();
    }

    // ── 讨论 ────────────────────────────────────────────────

    [Fact]
    public async Task Comments_ReadInTimeOrder()
    {
        var doc = Guid.NewGuid().ToString();
        foreach (var body in new[] { "first", "second", "third" })
        {
            var posted = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
                s => s.PostAsync(Doc, doc, new CreateDocumentCommentDto { Body = body }));
            posted.Succeeded.ShouldBeTrue(posted.Message);
        }

        var list = await InScopeAsync<IDocumentCommentService, Result<List<DocumentCommentDto>>>(
            s => s.ListAsync(Doc, doc));

        list.Data!.Select(c => c.Body).ToList().ShouldBe(new List<string> { "first", "second", "third" });
    }

    [Fact]
    public async Task Comment_Empty_Rejected400()
    {
        var result = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
            s => s.PostAsync(Doc, Guid.NewGuid().ToString(), new CreateDocumentCommentDto { Body = "   " }));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task Comment_Delete_RemovesItFromTheThread()
    {
        var doc = Guid.NewGuid().ToString();
        var posted = await InScopeAsync<IDocumentCommentService, Result<DocumentCommentDto>>(
            s => s.PostAsync(Doc, doc, new CreateDocumentCommentDto { Body = "typo" }));

        (await InScopeAsync<IDocumentCommentService, Result>(s => s.DeleteAsync(posted.Data!.Id)))
            .Succeeded.ShouldBeTrue();

        (await InScopeAsync<IDocumentCommentService, Result<List<DocumentCommentDto>>>(s => s.ListAsync(Doc, doc)))
            .Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Comment_DeleteUnknown_Returns404()
    {
        var result = await InScopeAsync<IDocumentCommentService, Result>(s => s.DeleteAsync(Guid.NewGuid()));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }
}
