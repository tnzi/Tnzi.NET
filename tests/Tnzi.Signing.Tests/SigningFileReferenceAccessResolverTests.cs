using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tnzi.Modules;
using Tnzi.Security.Authorization;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Permissions;
using Tnzi.Storage;
using Tnzi.Storage.Entities;

namespace Tnzi.Signing.Tests;

/// <summary>
/// 持 <c>signing.request.view</c> 的管理员要能读到成品与完成证书，不必另外拿到 <c>storage.file.view</c>。
/// </summary>
/// <remarks>
/// <para>
/// 成品与证书是在<b>匿名的</b>最后一次提交里存下的，<c>FileRecord.CreatorId</c> 为空，Storage 的归属判据
/// 对它恒假；而本模块此前没有向 Storage 登记任何按引用放行的判据（Chat / Finance 都登记了），
/// 于是请求详情里看得到 <c>finalPdfFileId</c>，点下去却是 404。判据 7 对签署文件一直是空转。
/// </para>
/// <para>
/// 这一组分三层：解析器本身的映射；它接进<b>真实</b> <c>FileAccessAuthorizer</c> 之后的放行与不放行；
/// 以及模块确实把它注册进了容器 —— 前两层再绿，注册漏了就一样是 404，且没有任何测试会红。
/// </para>
/// </remarks>
public class SigningFileReferenceAccessResolverTests : SigningStorageTestBase
{
    private static IPermissionChecker Granting(params string[] permissions)
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(permissions.Contains(p)));
        return checker.Object;
    }

    /// <summary>一条「某个 Envelope / EnvelopeTemplate 引用了这个文件」的引用行。</summary>
    private async Task ReferenceAsync(Guid fileId, string entityType, string fieldName)
    {
        DbContext.Set<FileReference>().Add(new FileReference
        {
            FileId = fileId,
            EntityType = entityType,
            EntityId = Guid.NewGuid(),
            FieldName = fieldName,
        });
        await DbContext.SaveChangesAsync();
    }

    /// <summary>无主文件：成品与证书就是这样存下来的。</summary>
    private async Task<FileRecord> OwnerlessFileAsync()
    {
        var id = await StoreAsync("sealed.pdf", RenderedPdf);
        var record = await DbContext.Set<FileRecord>().FindAsync(id);
        record!.CreatorId = null;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    // ── 解析器自身 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(nameof(Envelope), true)]
    [InlineData(nameof(EnvelopeTemplate), true)]
    [InlineData("ChatMessage", false)]
    [InlineData("Signer", false)]
    public void Handles_only_the_two_file_owning_signing_entities(string entityType, bool expected)
    {
        new SigningFileReferenceAccessResolver().CanHandle(entityType).ShouldBe(expected);
    }

    [Fact]
    public async Task Envelope_files_ask_for_request_view_and_template_files_for_template_view()
    {
        var resolver = new SigningFileReferenceAccessResolver(Granting(SigningPermissionNames.RequestView));

        (await resolver.CanReadAsync(new FileReferenceDescriptor(Guid.NewGuid(), nameof(Envelope), Guid.NewGuid(), "FinalPdfFileId")))
            .ShouldBeTrue();
        (await resolver.CanReadAsync(new FileReferenceDescriptor(Guid.NewGuid(), nameof(EnvelopeTemplate), Guid.NewGuid(), "SourceFileId")))
            .ShouldBeFalse("template files are gated by signing.template.view, not signing.request.view");
    }

    /// <summary>没有权限体系就无从判定 —— 保守拒绝，与 Storage 自己的取舍一致。</summary>
    [Fact]
    public async Task Without_a_permission_checker_it_never_grants()
    {
        var resolver = new SigningFileReferenceAccessResolver(permissionChecker: null);
        (await resolver.CanReadAsync(new FileReferenceDescriptor(Guid.NewGuid(), nameof(Envelope), Guid.NewGuid(), "FinalPdfFileId")))
            .ShouldBeFalse();
    }

    // ── 接进真实的 FileAccessAuthorizer ──────────────────────────────────

    /// <summary>
    /// 已登录、不是创建者、没有 storage.file.view、持 signing.request.view：修复前 404，现在放行。
    /// </summary>
    [Fact]
    public async Task A_signing_admin_can_read_the_sealed_output_without_storage_file_view()
    {
        var sealedFile = await OwnerlessFileAsync();
        await ReferenceAsync(sealedFile.Id, nameof(Envelope), nameof(Envelope.FinalPdfFileId));

        using var request = BeginRequest(
            Granting(SigningPermissionNames.RequestView),
            [new SigningFileReferenceAccessResolver(Granting(SigningPermissionNames.RequestView))]);

        (await request.Authorizer.CanReadAsync(sealedFile)).ShouldBeTrue();
        (await request.Files.GetAsync(sealedFile.Id)).Succeeded.ShouldBeTrue();
    }

    /// <summary>同一个人，没有 signing.request.view：照旧 404。解析器只放行，不放宽。</summary>
    [Fact]
    public async Task Without_request_view_the_same_reference_does_not_help()
    {
        var sealedFile = await OwnerlessFileAsync();
        await ReferenceAsync(sealedFile.Id, nameof(Envelope), nameof(Envelope.FinalPdfFileId));

        using var request = BeginRequest(
            Granting(SigningPermissionNames.TemplateView),
            [new SigningFileReferenceAccessResolver(Granting(SigningPermissionNames.TemplateView))]);

        (await request.Authorizer.CanReadAsync(sealedFile)).ShouldBeFalse();
        (await request.Files.GetAsync(sealedFile.Id)).Code.ShouldBe(404);
    }

    /// <summary>没登记解析器时（修复前的形状），同一个持码管理员读不到 —— 这条钉住「问题确实在登记」。</summary>
    [Fact]
    public async Task Without_the_resolver_registered_the_admin_is_still_turned_away()
    {
        var sealedFile = await OwnerlessFileAsync();
        await ReferenceAsync(sealedFile.Id, nameof(Envelope), nameof(Envelope.FinalPdfFileId));

        using var request = BeginRequest(Granting(SigningPermissionNames.RequestView), referenceResolvers: []);

        (await request.Authorizer.CanReadAsync(sealedFile)).ShouldBeFalse();
    }

    // ── 模块接线 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 前面几条再绿，模块忘了注册也一样是 404，而且没有任何测试会红 —— 所以这条直接看容器。
    /// </summary>
    [Fact]
    public async Task The_module_registers_the_resolver()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        await new SigningModule().ConfigureServicesAsync(context);

        services.ShouldContain(
            d => d.ServiceType == typeof(IFileReferenceAccessResolver)
                 && d.ImplementationType == typeof(SigningFileReferenceAccessResolver),
            "SigningModule must register SigningFileReferenceAccessResolver as an IFileReferenceAccessResolver");
    }
}
