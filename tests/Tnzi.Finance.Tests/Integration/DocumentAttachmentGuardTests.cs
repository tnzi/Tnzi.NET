using Moq;
using Tnzi.Storage;

namespace Tnzi.Finance.Tests.Integration;

/// <summary>
/// 单据附件登记前必须问一句 <see cref="IFileReadAccessProbe"/>：「这个人本来就读得到这份文件吗」。
/// </summary>
/// <remarks>
/// <para>
/// ★ 不问就写 = 「按 id 读任意文件」。<c>DocumentAttachment.FileId</c> 是 <c>[FileField]</c>，落库即登记一条
/// <c>FileReference</c>；而 <c>FinanceFileReferenceAccessResolver</c> 对 <c>DocumentAttachment</c> 名下的文件
/// 只问 <c>finance.attachment.view</c>。于是持 <c>finance.attachment.create</c> + <c>.view</c> 的人把任意
/// fileId 挂到任意单据键上（单据类型是开放词汇，单据本身不必存在），就把那份文件变成了自己永久可读的。
/// 与 Chat 2026-09-04 修掉的是同一形态。
/// </para>
/// <para>
/// 探针的实现随 <c>Tnzi.Storage</c> 注册，Finance 核心不引用它（契约在核心 <c>Tnzi</c> 程序集）：
/// 这里用替身回答「能 / 不能」，被测的是服务自己那道门。基类默认注册一律放行的替身，
/// 否则既有的附件用例每一条都会答 501。
/// </para>
/// </remarks>
public class DocumentAttachmentGuardTests : FinanceIntegrationTestBase
{
    private const string Doc = FinanceSourceTypes.Invoice;

    private static CreateDocumentAttachmentDto Attachment(Guid fileId) => new()
    {
        FileId = fileId, FileName = "supplier-invoice.pdf", ContentType = "application/pdf", FileSize = 1024
    };

    private DocumentAttachmentService BuildService(IServiceProvider sp, IFileReadAccessProbe? probe)
        => new(
            sp,
            sp.GetRequiredService<IRepository<DocumentAttachment, Guid>>(),
            sp.GetRequiredService<IOptionsSnapshot<FinanceOptions>>(),
            probe);

    private static IFileReadAccessProbe Answering(bool canRead)
    {
        var probe = new Mock<IFileReadAccessProbe>();
        probe.Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(canRead);
        return probe.Object;
    }

    private async Task<int> AttachmentRowsAsync()
    {
        var all = await InScopeAsync<IDocumentAttachmentService, Result<List<DocumentAttachmentDto>>>(s => s.ListAsync(Doc, "victim-doc"));
        all.Succeeded.ShouldBeTrue(all.Message);
        return all.Data!.Count;
    }

    [Fact]
    public async Task Attach_WhenCallerCannotReadTheFile_Rejected403AndWritesNothing()
    {
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, Answering(canRead: false));

        var result = await service.AttachAsync(Doc, "victim-doc", Attachment(Guid.NewGuid()));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        (await AttachmentRowsAsync()).ShouldBe(0);
    }

    /// <summary>
    /// 存储模块缺席时拒绝，不是跳过：「跳过校验」与「校验通过」在接口上完全一致。
    /// 501 而不是 503：这不是暂时性故障，重试永远不会好。
    /// </summary>
    [Fact]
    public async Task Attach_WithoutStorageModule_Rejected501()
    {
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, probe: null);

        var result = await service.AttachAsync(Doc, "victim-doc", Attachment(Guid.NewGuid()));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Storage");
        (await AttachmentRowsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Attach_WhenCallerCanRead_Succeeds()
    {
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, Answering(canRead: true));

        var result = await service.AttachAsync(Doc, "victim-doc", Attachment(Guid.NewGuid()));

        result.Succeeded.ShouldBeTrue(result.Message);
        (await AttachmentRowsAsync()).ShouldBe(1);
    }

    /// <summary>探针在任何写入之前、也在便宜的表单校验之后：没给文件的请求不该去问存储。</summary>
    [Fact]
    public async Task Attach_WithoutAFile_IsStill400_NotAProbeCall()
    {
        var probe = new Mock<IFileReadAccessProbe>(MockBehavior.Strict);
        using var scope = ServiceProvider.CreateScope();
        var service = BuildService(scope.ServiceProvider, probe.Object);

        var result = await service.AttachAsync(Doc, "victim-doc", Attachment(Guid.Empty));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        probe.VerifyNoOtherCalls();
    }

    /// <summary>基类经 DI 注册的探针真的流到了服务里（可选构造参数没有被静默解析成 null）。</summary>
    [Fact]
    public async Task TheRegisteredProbe_IsWhatTheServiceAsks()
    {
        var result = await InScopeAsync<IDocumentAttachmentService, Result<DocumentAttachmentDto>>(
            s => s.AttachAsync(Doc, "victim-doc", Attachment(Guid.NewGuid())));

        result.Succeeded.ShouldBeTrue(result.Message);
    }
}
