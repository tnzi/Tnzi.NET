using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Security.Authorization;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Entities.Configs;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using Tnzi.Storage;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Entities.Configs;
using Tnzi.Storage.Options;
using Tnzi.Storage.Providers;
using Tnzi.Storage.Sanitization;
using Tnzi.Storage.Services;
using Tnzi.TestBase;

namespace Tnzi.Signing.Tests;

/// <summary>签署模块五张表 + 存储模块两张表：密封与读取要真的经过 Storage 的判定链。</summary>
public class SigningStorageDbContext : TnziDbContext<SigningStorageDbContext>
{
    public SigningStorageDbContext(DbContextOptions<SigningStorageDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EnvelopeTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new FieldConfiguration());
        modelBuilder.ApplyConfiguration(new EnvelopeConfiguration());
        modelBuilder.ApplyConfiguration(new SignerConfiguration());
        modelBuilder.ApplyConfiguration(new FieldValueConfiguration());
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 带<b>真实 Storage 栈</b>（<see cref="FileStorageService"/> + <see cref="FileAccessAuthorizer"/>）的签署测试基类。
/// </summary>
/// <remarks>
/// <para>
/// 存在的理由：本模块此前的全部测试都 <c>Mock&lt;IFileStorageService&gt;</c>，真实的读取判定一次都
/// 没跑过，于是「匿名收件人的提交让密封读不到渲染稿」这种事在 40 条绿测试下面藏了整个模块的寿命。
/// 这里把两张存储表装进同一个上下文，存储服务与授权器用生产装配的形状构造，只有 provider 换成内存的。
/// </para>
/// <para>
/// 每个用例里的「一次请求」= 一个 DI 作用域 + 一份授予表 + 一套服务实例，与生产里 Scoped 注册的形状一致。
/// 字节放在跨作用域共用的 <see cref="InMemoryFileStorage"/> 里 —— 那就是「桶」。
/// </para>
/// <para>
/// 当前用户由子类决定：默认沿用基类的「已登录」用户，匿名收件人的用例在 <c>ConfigureServices</c> 里
/// 后注册一个匿名的覆盖掉它（后注册者胜出）。
/// </para>
/// </remarks>
public abstract class SigningStorageTestBase : IntegratedTestBase<SigningStorageDbContext>
{
    protected static readonly byte[] RenderedPdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34];

    protected InMemoryFileStorage Bucket { get; } = new();
    protected CountingStamper Stamper { get; } = new();

    /// <summary>领域侧插进来的合并变量 provider；默认一个都没有。</summary>
    protected virtual IEnumerable<IMergeSourceProvider> MergeProviders => [];
    protected StorageOptions StorageOptions { get; } = new()
    {
        AutoGenerateThumbnail = false,
        AllowedExtensions = [".pdf"],
    };

    /// <summary>每次盖章产出不同字节，免得两份成品哈希碰巧一致。</summary>
    protected sealed class CountingStamper : IPdfStamper
    {
        private int _stamps;

        /// <summary>置位后下一次盖章抛异常（模拟一次瞬时的密封失败），然后自动复位。</summary>
        public bool FailNext { get; set; }

        /// <summary>每一次盖章收到的请求，按先后顺序（发出前的预填、密封各是一次）。</summary>
        public List<PdfStampRequest> Requests { get; } = [];

        public byte[] Stamp(byte[] pdf, PdfStampRequest request)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Simulated stamping failure.");
            }

            Requests.Add(request);
            return [.. pdf, .. BitConverter.GetBytes(Interlocked.Increment(ref _stamps))];
        }

        public byte[] Create(PdfStampRequest request) => [0x25, 0x50, 0x44, 0x46];
    }

    private sealed class NoPublicFields : IPublicFileFieldResolver
    {
        public IReadOnlyCollection<PublicFileField> GetPublicFileFields() => [];
    }

    /// <summary>「一次请求」：同一个作用域、同一份授予表、同一套服务。</summary>
    protected sealed class Request : IDisposable
    {
        private readonly IServiceScope _scope;

        public Request(
            IServiceScope scope,
            FileAccessGrantContext grants,
            FileStorageService files,
            FileAccessAuthorizer authorizer,
            EnvelopeService envelopes,
            EnvelopeTemplateService templates)
        {
            _scope = scope;
            Grants = grants;
            Files = files;
            Authorizer = authorizer;
            Envelopes = envelopes;
            Templates = templates;
        }

        public FileAccessGrantContext Grants { get; }
        public FileStorageService Files { get; }
        public FileAccessAuthorizer Authorizer { get; }
        public EnvelopeService Envelopes { get; }

        /// <summary>模板服务，带真实的 <see cref="FileReadAccessProbe"/>（写入侧的归属探针）。</summary>
        public EnvelopeTemplateService Templates { get; }

        public void Dispose() => _scope.Dispose();
    }

    /// <summary>
    /// 开始「一次请求」。
    /// </summary>
    /// <param name="permissionChecker">权限体系；<c>null</c> = 未加载 Authorization 模块。</param>
    /// <param name="referenceResolvers">向 Storage 登记的按引用放行判据；默认一个都不登记。</param>
    protected Request BeginRequest(
        IPermissionChecker? permissionChecker = null,
        IEnumerable<IFileReferenceAccessResolver>? referenceResolvers = null)
    {
        var scope = ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<SigningStorageDbContext>();

        var options = new Mock<IOptionsMonitor<StorageOptions>>();
        options.Setup(o => o.CurrentValue).Returns(StorageOptions);

        var fileRecords = new EFCoreRepository<SigningStorageDbContext, FileRecord, Guid>(db, null, sp);
        var fileReferences = new EFCoreRepository<SigningStorageDbContext, FileReference, Guid>(db, null, sp);
        var grants = new FileAccessGrantContext();

        var authorizer = new FileAccessAuthorizer(
            sp.GetRequiredService<ICurrentUser>(),
            options.Object,
            fileReferences,
            referenceResolvers ?? [],
            grants,
            permissionChecker);

        var files = new FileStorageService(
            fileRecords,
            fileReferences,
            Bucket,
            options.Object,
            authorizer,
            new NoPublicFields(),
            new Mock<IFileUrlSigner>().Object,
            sp,
            new UploadGuard(options.Object),
            new FileThumbnailGenerator(Bucket, options.Object));

        var envelopes = new EnvelopeService(
            sp,
            new EFCoreRepository<SigningStorageDbContext, Envelope, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, Signer, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, FieldValue, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, EnvelopeTemplate, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, Field, Guid>(db, null, sp),
            new MergeSourceRegistry(MergeProviders, []),
            new SigningSealer(Stamper, new Mock<IPdfInspector>().Object, files, NullLogger<SigningSealer>.Instance),
            new SigningCertificateBuilder(Stamper, files, NullLogger<SigningCertificateBuilder>.Instance),
            new ComposedDocumentRenderer(Stamper),
            files,
            grants,
            sp.GetRequiredService<ICurrentTenant>());

        var templates = new EnvelopeTemplateService(
            sp,
            new EFCoreRepository<SigningStorageDbContext, EnvelopeTemplate, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, Field, Guid>(db, null, sp),
            new EFCoreRepository<SigningStorageDbContext, Envelope, Guid>(db, null, sp),
            new FileReadAccessProbe(fileRecords, authorizer),
            files);

        return new Request(scope, grants, files, authorizer, envelopes, templates);
    }

    /// <summary>存一份文件。存是不经读取判定的（新建不是读）；CreatorId 取自当前用户（匿名时为空）。</summary>
    protected async Task<Guid> StoreAsync(string name, byte[] bytes)
    {
        using var request = BeginRequest();
        var saved = await request.Files.SaveAsync(name, new MemoryStream(bytes));
        saved.Succeeded.ShouldBeTrue(saved.Message);
        return saved.Data!.Id;
    }

    /// <summary>直接写库造一份 Uploaded 模板（不经模板服务的入口校验），返回模板 id 与渲染稿 id。</summary>
    /// <param name="fields">模板字段。</param>
    /// <param name="renderedFileName">
    /// 渲染稿的文件名（决定 Storage 记下的 Content-Type）。模板行直接写库、不经模板服务的入口校验，
    /// 所以这里能造出一份「渲染稿不是 PDF」的存量模板 —— 那正是收件人面要拒绝的形态。
    /// </param>
    protected async Task<(Guid TemplateId, Guid RenderedId)> ArrangeTemplateAsync(
        IReadOnlyList<Field>? fields = null, string renderedFileName = "contract.pdf")
    {
        var renderedId = await StoreAsync(renderedFileName, RenderedPdf);

        var template = new EnvelopeTemplate
        {
            Name = "Engagement Letter",
            Category = "General",
            Source = TemplateSource.Uploaded,
            RenderedPdfFileId = renderedId,
            IsActive = true,
        };
        DbContext.Set<EnvelopeTemplate>().Add(template);
        await DbContext.SaveChangesAsync();

        if (fields is { Count: > 0 })
        {
            foreach (var field in fields)
                field.TemplateId = template.Id;
            DbContext.Set<Field>().AddRange(fields);
            await DbContext.SaveChangesAsync();
        }

        return (template.Id, renderedId);
    }

    /// <summary>默认的单个收件人：角色 Client。</summary>
    protected static List<CreateSignerDto> SingleClient() => [new CreateSignerDto { Role = "Client", Name = "Alice" }];

    /// <summary>建一份已发出的请求，返回渲染稿 id、请求 id 与各收件人的令牌（按收件人顺序）。</summary>
    /// <param name="fields">模板字段。</param>
    /// <param name="renderedFileName">见 <see cref="ArrangeTemplateAsync"/>。</param>
    /// <param name="recipients">收件人；默认一个 Client。</param>
    /// <param name="prefilledValues">发起方预填。</param>
    protected async Task<(Guid RenderedId, Guid RequestId, IReadOnlyList<string> Tokens)> ArrangeSentEnvelopeWithTokensAsync(
        IReadOnlyList<Field>? fields = null,
        string renderedFileName = "contract.pdf",
        List<CreateSignerDto>? recipients = null,
        Dictionary<string, string?>? prefilledValues = null)
    {
        var (templateId, renderedId) = await ArrangeTemplateAsync(fields, renderedFileName);

        Guid requestId;
        using (var create = BeginRequest())
        {
            var created = await create.Envelopes.CreateAsync(new CreateEnvelopeDto
            {
                TemplateId = templateId,
                Title = "Engagement Letter",
                Recipients = recipients ?? SingleClient(),
                PrefilledValues = prefilledValues,
            });
            created.Succeeded.ShouldBeTrue(created.Message);
            requestId = created.Data!.Id;
        }

        using var send = BeginRequest();
        var sent = await send.Envelopes.SendAsync(requestId);
        sent.Succeeded.ShouldBeTrue(sent.Message);
        return (renderedId, requestId, sent.Data!.Select(l => l.Token).ToList());
    }

    /// <summary>建一份单签署人、已发出的请求，返回渲染稿 id、请求 id 与那个人的令牌。</summary>
    protected async Task<(Guid RenderedId, Guid RequestId, string Token)> ArrangeSentEnvelopeAsync(
        IReadOnlyList<Field>? fields = null, string renderedFileName = "contract.pdf")
    {
        var (renderedId, requestId, tokens) = await ArrangeSentEnvelopeWithTokensAsync(fields, renderedFileName);
        return (renderedId, requestId, tokens.Single());
    }

    protected static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }
}
