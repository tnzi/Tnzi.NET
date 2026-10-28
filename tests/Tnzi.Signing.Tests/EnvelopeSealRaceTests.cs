using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using Tnzi.Domain.Repositories;
using Tnzi.Documents.Models;
using Tnzi.Documents.Services;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Results;
using Tnzi.Security.Claims;
using Tnzi.Signing.Dtos;
using Tnzi.Signing.Entities;
using Tnzi.Signing.Entities.Configs;
using Tnzi.Signing.Metadata;
using Tnzi.Signing.Services.Internal;
using Tnzi.Storage.Entities;
using Tnzi.Storage.Services;
using Tnzi.TestBase;

namespace Tnzi.Signing.Tests;

/// <summary>只挂签署模块五张表的测试 DbContext。</summary>
public class SigningRaceDbContext : TnziDbContext<SigningRaceDbContext>
{
    public SigningRaceDbContext(DbContextOptions<SigningRaceDbContext> options, ICurrentUser currentUser)
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
        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 并行签署（<see cref="Envelope.IsSequential"/> = false）时最后两个人同时交签名。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么要有这一组。</b><c>AdvanceAsync</c> 的判据是"所有收件人都已 Signed"，而
/// <c>IRepository.ToListAsync</c> 走 <c>AsNoTracking</c> —— 两个并行的请求
/// 各自把自己那位标成 Signed 之后，都会从库里读到"全签完"。谁也没有拦住第二个人，于是密封执行两次：
/// 两份成品、两个哈希，而<b>先交给宿主模块归档的那一份，不是最后记在请求上的那一份</b>。
/// 哈希对不上就等于没有哈希 —— 这套东西全部的防篡改价值都在那条对应关系上。
/// </para>
/// <para>
/// ★ <b>交错是被控制出来的，不是靠调度器碰运气。</b>用一个夹具侧的仓储装饰器，把 A 的"读收件人"
/// 那一步停在 B 提交之前，放行后<b>重新查一次</b> —— 那正是"A 的读发生在 B 提交之后"这一刻。
/// 生产代码里没有任何为测试开的口子；装饰器只是把一个真实存在的窗口撑开到可控。
/// </para>
/// </remarks>
public class EnvelopeSealRaceTests : IntegratedTestBase<SigningRaceDbContext>
{
    private const string HostType = "TestMatter";

    private readonly InMemoryFiles _files = new();
    private readonly CountingStamper _stamper = new();
    private readonly RecordingSink _sink = new();

    /// <summary>把字节存在内存里的存储，跨作用域共用一份。</summary>
    private sealed class InMemoryFiles
    {
        private readonly Dictionary<Guid, byte[]> _blobs = [];
        private readonly List<(Guid Id, string Name)> _saved = [];
        private readonly Lock _lock = new();

        public IReadOnlyList<(Guid Id, string Name)> Saved
        {
            get { lock (_lock) return [.. _saved]; }
        }

        public byte[] Read(Guid id)
        {
            lock (_lock) return _blobs[id];
        }

        public Guid Save(string name, byte[] bytes)
        {
            var id = Guid.NewGuid();
            lock (_lock)
            {
                _blobs[id] = bytes;
                _saved.Add((id, name));
            }
            return id;
        }

        public IFileStorageService AsService()
        {
            var mock = new Mock<IFileStorageService>(MockBehavior.Loose);
            mock.Setup(f => f.SaveAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns((string name, Stream stream, bool _, bool _) =>
                {
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    var id = Save(name, buffer.ToArray());
                    return Task.FromResult(Result.Success(new FileRecord { Id = id, FileName = name }));
                });
            mock.Setup(f => f.GetAsync(It.IsAny<Guid>()))
                .Returns((Guid id) => Task.FromResult(Result.Success<Stream>(new MemoryStream(Read(id)))));
            return mock.Object;
        }
    }

    /// <summary>
    /// 每次盖章都产出不同的字节 —— 真实的 PDF 也是（里面带生成时刻）。
    /// 若两次密封产出同样的字节，哈希会碰巧一致，这一组断言就会假绿。
    /// </summary>
    private sealed class CountingStamper : IPdfStamper
    {
        private int _stamps;

        public int StampCalls => Volatile.Read(ref _stamps);

        public byte[] Stamp(byte[] pdf, PdfStampRequest request)
            => [.. pdf, .. BitConverter.GetBytes(Interlocked.Increment(ref _stamps))];

        public byte[] Create(PdfStampRequest request) => [0x25, 0x50, 0x44, 0x46];

        public double MeasureTextWidth(string text, double fontSize, bool bold = false, bool italic = false)
            => TestTextMetrics.Width(text, fontSize, bold);
    }

    /// <summary>放行每一条记录的 provider：这一组测的是密封并发，不是绑定授权。</summary>
    private sealed class AllowAllSource : IMergeSourceProvider
    {
        public string EntityType => HostType;

        public Task<IReadOnlyDictionary<string, object?>> ResolveAsync(Guid entityId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    /// <summary>记下宿主模块收到几次归档。</summary>
    private sealed class RecordingSink : IDocumentHostSink
    {
        private readonly List<(Guid EntityId, Guid FileId)> _attached = [];
        private readonly Lock _lock = new();

        public string EntityType => HostType;

        public IReadOnlyList<(Guid EntityId, Guid FileId)> Attached
        {
            get { lock (_lock) return [.. _attached]; }
        }

        public Task AttachAsync(Guid entityId, Guid fileId, string fileName, Guid requestId, CancellationToken cancellationToken = default)
        {
            lock (_lock) _attached.Add((entityId, fileId));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 在某一次"读收件人"上装闸：条件满足时先通知测试、等放行，放行后<b>重新查一次</b>。
    /// </summary>
    private sealed class GatedSignerRepository : EFCoreRepository<SigningRaceDbContext, Signer, Guid>
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _fired;

        public GatedSignerRepository(SigningRaceDbContext dbContext, IServiceProvider serviceProvider)
            : base(dbContext, null, serviceProvider)
        {
        }

        /// <summary>什么样的读要被拦下。按结果的语义判定，不按第几次调用。</summary>
        public Func<List<Signer>, bool>? GateWhen { get; set; }

        /// <summary>
        /// 放行后是否重读。<see langword="true"/>（默认）= 这次读发生在放行那一刻；
        /// <see langword="false"/> = 读早已发生、只是之后的动作被拖到放行之后（交回拦下时的旧结果）。
        /// </summary>
        public bool RereadAfterRelease { get; set; } = true;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _release.TrySetResult();

        public override async Task<List<Signer>> ToListAsync(
            System.Linq.Expressions.Expression<Func<Signer, bool>>? predicate = null,
            CancellationToken cancellationToken = default)
        {
            var list = await base.ToListAsync(predicate, cancellationToken);
            if (_fired || GateWhen?.Invoke(list) != true)
                return list;

            _fired = true;
            Reached.TrySetResult();
            await _release.Task;
            // 放行后重读：这一刻另一方已经提交，于是这次读到的是"全签完"。
            return RereadAfterRelease ? await base.ToListAsync(predicate, cancellationToken) : list;
        }
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<Envelope, Guid>, EFCoreRepository<SigningRaceDbContext, Envelope, Guid>>();
        services.AddScoped<IRepository<Signer, Guid>, EFCoreRepository<SigningRaceDbContext, Signer, Guid>>();
        services.AddScoped<IRepository<FieldValue, Guid>, EFCoreRepository<SigningRaceDbContext, FieldValue, Guid>>();
        services.AddScoped<IReadOnlyRepository<EnvelopeTemplate, Guid>, EFCoreRepository<SigningRaceDbContext, EnvelopeTemplate, Guid>>();
        services.AddScoped<IReadOnlyRepository<Field, Guid>, EFCoreRepository<SigningRaceDbContext, Field, Guid>>();
    }

    private EnvelopeService BuildService(IServiceProvider scoped, GatedSignerRepository? gatedSigners = null)
    {
        var db = scoped.GetRequiredService<SigningRaceDbContext>();
        var files = _files.AsService();
        var inspector = new Mock<IPdfInspector>(MockBehavior.Loose).Object;

        return new EnvelopeService(
            scoped,
            new EFCoreRepository<SigningRaceDbContext, Envelope, Guid>(db, null, scoped),
            gatedSigners ?? (IRepository<Signer, Guid>)new EFCoreRepository<SigningRaceDbContext, Signer, Guid>(db, null, scoped),
            new EFCoreRepository<SigningRaceDbContext, FieldValue, Guid>(db, null, scoped),
            new EFCoreRepository<SigningRaceDbContext, EnvelopeTemplate, Guid>(db, null, scoped),
            new EFCoreRepository<SigningRaceDbContext, Field, Guid>(db, null, scoped),
            // sink 与 provider 成对注册（provider 是绑定的授权点，缺了它创建请求失败关闭）。
            new MergeSourceRegistry([new AllowAllSource()], [_sink]),
            new SigningSealer(_stamper, inspector, files, NullLogger<SigningSealer>.Instance),
            new SigningCertificateBuilder(_stamper, files, NullLogger<SigningCertificateBuilder>.Instance),
            new ComposedDocumentRenderer(_stamper),
            files,
            // 这一组 mock 掉了存储，授予表在这里不参与判定；真实存储栈下的匿名读取
            // 见 AnonymousRecipientStorageAccessTests。
            new FileAccessGrantContext(),
            scoped.GetRequiredService<ICurrentTenant>());
    }

    /// <summary>建一份两方并行签署、已发出的请求，返回两个人的令牌。</summary>
    private async Task<(Guid RequestId, Guid HostId, string TokenA, string TokenB)> ArrangeSentEnvelopeAsync()
    {
        var renderedId = _files.Save("contract.pdf", [0x25, 0x50, 0x44, 0x46, 0x2D]);
        var template = new EnvelopeTemplate
        {
            Name = "Settlement",
            Category = "Litigation",
            Source = TemplateSource.Uploaded,
            RenderedPdfFileId = renderedId,
            IsActive = true,
        };
        DbContext.Set<EnvelopeTemplate>().Add(template);
        await DbContext.SaveChangesAsync();

        // 每一步各用一个作用域：一个 DbContext 里既插入又无跟踪重读，会撞上 EF 的
        // 身份映射冲突。生产里发起与发出本来就是两个请求。
        using var createScope = ServiceProvider.CreateScope();
        using var sendScope = ServiceProvider.CreateScope();

        var hostId = Guid.NewGuid();
        var created = await BuildService(createScope.ServiceProvider).CreateAsync(new CreateEnvelopeDto
        {
            TemplateId = template.Id,
            Title = "Settlement Agreement",
            HostEntityType = HostType,
            HostEntityId = hostId,
            IsSequential = false,
            Recipients =
            [
                new CreateSignerDto { Role = "PartyA", Name = "Alice" },
                new CreateSignerDto { Role = "PartyB", Name = "Bob" },
            ],
        });
        created.Succeeded.ShouldBeTrue(created.Message);

        var sent = await BuildService(sendScope.ServiceProvider).SendAsync(created.Data!.Id);
        sent.Succeeded.ShouldBeTrue(sent.Message);
        sent.Data!.Count.ShouldBe(2);

        return (created.Data.Id, hostId, sent.Data[0].Token, sent.Data[1].Token);
    }

    [Fact]
    public async Task ParallelSigning_TwoLastSignaturesAtOnce_SealsExactlyOnce()
    {
        var (requestId, hostId, tokenA, tokenB) = await ArrangeSentEnvelopeAsync();

        using var scopeA = ServiceProvider.CreateScope();
        using var scopeB = ServiceProvider.CreateScope();

        var gated = new GatedSignerRepository(
            scopeA.ServiceProvider.GetRequiredService<SigningRaceDbContext>(),
            scopeA.ServiceProvider)
        {
            // 拦住 A 在"自己已经签完、还差另一位"这一刻的那次读 —— 正是 AdvanceAsync 拿来
            // 判断该不该密封的那一次。按语义定位，不按第几次调用。
            GateWhen = list => list.Any(s => s.Name == "Alice" && s.Status == SigningRecipientStatus.Signed)
                               && list.Any(s => s.Status != SigningRecipientStatus.Signed),
        };

        var taskA = BuildService(scopeA.ServiceProvider, gated).SubmitAsync(tokenA, new SubmitSigningDto());
        await gated.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // B 走完整条路：它看到"全签完"，于是密封 + 归档 + 出证书。
        var resultB = await BuildService(scopeB.ServiceProvider).SubmitAsync(tokenB, new SubmitSigningDto());
        resultB.Succeeded.ShouldBeTrue(resultB.Message);

        gated.Release();
        var resultA = await taskA;
        resultA.Succeeded.ShouldBeTrue(resultA.Message);

        // ── 密封只能发生一次 ────────────────────────────────────────────────
        var sealedFiles = _files.Saved.Where(f => f.Name.EndsWith("-signed.pdf", StringComparison.Ordinal)).ToList();
        sealedFiles.Count.ShouldBe(1, "the document was sealed more than once");

        // ── 记在案的哈希必须就是归档那份文件的哈希 ──────────────────────────
        var envelope = await DbContext.Set<Envelope>().AsNoTracking().FirstAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Completed);
        envelope.FinalPdfFileId.ShouldNotBeNull();
        envelope.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_files.Read(envelope.FinalPdfFileId!.Value))));

        // ── 交给宿主模块的，必须就是记在案的那一份 ──────────────────────────
        _sink.Attached.Count.ShouldBe(1, "the sealed document was handed to the host more than once");
        _sink.Attached[0].EntityId.ShouldBe(hostId);
        _sink.Attached[0].FileId.ShouldBe(envelope.FinalPdfFileId!.Value);
    }

    [Fact]
    public async Task ParallelSigning_LosingSubmission_StillRecordsThatSignature()
    {
        var (requestId, _, tokenA, tokenB) = await ArrangeSentEnvelopeAsync();

        using var scopeA = ServiceProvider.CreateScope();
        using var scopeB = ServiceProvider.CreateScope();

        var gated = new GatedSignerRepository(
            scopeA.ServiceProvider.GetRequiredService<SigningRaceDbContext>(),
            scopeA.ServiceProvider)
        {
            GateWhen = list => list.Any(s => s.Name == "Alice" && s.Status == SigningRecipientStatus.Signed)
                               && list.Any(s => s.Status != SigningRecipientStatus.Signed),
        };

        var taskA = BuildService(scopeA.ServiceProvider, gated).SubmitAsync(tokenA, new SubmitSigningDto());
        await gated.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await BuildService(scopeB.ServiceProvider).SubmitAsync(tokenB, new SubmitSigningDto());
        gated.Release();
        var resultA = await taskA;

        // ★ 抢不到密封权不等于这一签没发生：A 的签名早在抢占之前就已落库，
        //   把它回报成失败会让签署人以为自己没签成而重签。
        resultA.Succeeded.ShouldBeTrue(resultA.Message);

        var signers = await DbContext.Set<Signer>().AsNoTracking().Where(s => s.RequestId == requestId).ToListAsync();
        signers.Count.ShouldBe(2);
        signers.ShouldAllBe(s => s.Status == SigningRecipientStatus.Signed);
        signers.ShouldAllBe(s => s.SignedAt != null);
    }
    /// <summary>
    /// ★ 另一种交错：B 先读到信封（并发戳 s0），A 随后签完、读到「B 还没签」、把信封从 Sent 推进到 InProgress（戳变成 s1），
    /// 然后 B 签完、读到「全签完」去抢占。B 手上是旧戳 s0，抢占撞上的并发冲突来自 A 那次与密封无关的推进，
    /// 不是另一个密封者。此前这一下被当成「已经有人在密封」吞掉：全员 Signed、没有人密封，信封停在 InProgress。
    /// </summary>
    [Fact]
    public async Task ParallelSigning_AnEarlierSignersAdvanceBumpingTheStamp_DoesNotCostTheLastSignerTheSeal()
    {
        var (requestId, hostId, tokenA, tokenB) = await ArrangeSentEnvelopeAsync();

        using var scopeA = ServiceProvider.CreateScope();
        using var scopeB = ServiceProvider.CreateScope();

        // 拦住 B 在「自己还没签、谁都还没签」时的那次读：那时 B 手上的信封已经读出来了（戳 s0）。
        var gatedB = new GatedSignerRepository(
            scopeB.ServiceProvider.GetRequiredService<SigningRaceDbContext>(),
            scopeB.ServiceProvider)
        {
            GateWhen = list => list.Count == 2 && list.All(s => s.Status != SigningRecipientStatus.Signed),
        };

        var taskB = BuildService(scopeB.ServiceProvider, gatedB).SubmitAsync(tokenB, new SubmitSigningDto());
        await gatedB.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // A 走完整条路：签完、看到 B 还没签、把信封推进到 InProgress。
        var resultA = await BuildService(scopeA.ServiceProvider).SubmitAsync(tokenA, new SubmitSigningDto());
        resultA.Succeeded.ShouldBeTrue(resultA.Message);
        resultA.Data!.RequestStatus.ShouldBe(EnvelopeStatus.InProgress);

        gatedB.Release();
        var resultB = await taskB;
        resultB.Succeeded.ShouldBeTrue(resultB.Message);

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().FirstAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Completed, "every recipient signed but nobody sealed the document");
        envelope.FinalPdfFileId.ShouldNotBeNull();
        envelope.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_files.Read(envelope.FinalPdfFileId!.Value))));

        _files.Saved.Count(f => f.Name.EndsWith("-signed.pdf", StringComparison.Ordinal)).ShouldBe(1);
        _sink.Attached.Count.ShouldBe(1);
        _sink.Attached[0].EntityId.ShouldBe(hostId);
        _sink.Attached[0].FileId.ShouldBe(envelope.FinalPdfFileId!.Value);
    }

    /// <summary>
    /// 镜像交错：A 签完、读到「B 还没签」之后被拖住；B 这时签完、抢占、密封完成。A 接着把信封从 Sent 推进到 InProgress，
    /// 用的是旧戳，此前这次写的并发冲突一路抛出去，A 收到 500，而它的签名早已落库、信封也已经完成。
    /// </summary>
    [Fact]
    public async Task ParallelSigning_AnAdvanceThatLosesToTheSeal_StillAnswersTheSigner()
    {
        var (requestId, _, tokenA, tokenB) = await ArrangeSentEnvelopeAsync();

        using var scopeA = ServiceProvider.CreateScope();
        using var scopeB = ServiceProvider.CreateScope();

        var gatedA = new GatedSignerRepository(
            scopeA.ServiceProvider.GetRequiredService<SigningRaceDbContext>(),
            scopeA.ServiceProvider)
        {
            GateWhen = list => list.Any(s => s.Name == "Alice" && s.Status == SigningRecipientStatus.Signed)
                               && list.Any(s => s.Status != SigningRecipientStatus.Signed),
            // A 的读发生在 B 提交之前，只是之后的推进被拖到 B 密封之后：交回拦下时的旧结果。
            RereadAfterRelease = false,
        };

        var taskA = BuildService(scopeA.ServiceProvider, gatedA).SubmitAsync(tokenA, new SubmitSigningDto());
        await gatedA.Reached.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var resultB = await BuildService(scopeB.ServiceProvider).SubmitAsync(tokenB, new SubmitSigningDto());
        resultB.Succeeded.ShouldBeTrue(resultB.Message);
        resultB.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);

        gatedA.Release();
        var resultA = await taskA;

        // ★ A 的签名早已落库：回报失败（或 500）会让签署人以为没签成。
        resultA.Succeeded.ShouldBeTrue(resultA.Message);
        resultA.Data!.RequestStatus.ShouldBe(EnvelopeStatus.Completed);

        var envelope = await DbContext.Set<Envelope>().AsNoTracking().FirstAsync(e => e.Id == requestId);
        envelope.Status.ShouldBe(EnvelopeStatus.Completed);
        envelope.CompletedAt.ShouldNotBeNull();
        _files.Saved.Count(f => f.Name.EndsWith("-signed.pdf", StringComparison.Ordinal)).ShouldBe(1);
        _sink.Attached.Count.ShouldBe(1);
    }
}
