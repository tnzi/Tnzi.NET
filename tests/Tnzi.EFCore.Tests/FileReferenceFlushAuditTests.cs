using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 文件引用处理器在 <c>SaveChanges</c> 的引用处理阶段新增的行，同样要套用审计（创建时间、租户）。
/// </summary>
/// <remarks>
/// <para>
/// 事务里（全局工作单元 / <c>ExecuteInUnitOfWorkAsync</c>）仓储检测到事务后一律缓冲，处理器经仓储新增的引用记录
/// 于是只能由 <c>TnziDbContextHelper</c> 在主保存之后补一次保存落库。那次保存为了不递归文件追踪走的是
/// 基类的 <c>SaveChanges</c>，审计管线没跑：多租户下 <c>TenantId</c> 恒为 NULL（严格等值过滤 ⇒ 按引用授权读文件、
/// 删记录时找引用、保存时去重全部查不到它），<c>CreationTime</c> 是 0001-01-01（保留期判断对它失效）。
/// 无事务的自动保存会重入完整管线，所以只有事务里才出问题。
/// </para>
/// </remarks>
public sealed class FileReferenceFlushAuditTests : IDisposable
{
    private readonly RepositoryWriteHost _host = new(multiTenancy: true);
    private readonly Guid _tenantId = Guid.NewGuid();

    public FileReferenceFlushAuditTests()
    {
        _host.Tenant.SetTenant(_tenantId, "tenant");
        _host.User.SetUser(Guid.NewGuid(), "writer", _tenantId);
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task ReferenceAddedDuringSave_InsideTransaction_GetsTenantAndCreationTime()
    {
        var fileId = Guid.NewGuid();

        using (var scope = _host.CreateScope())
        {
            var manager = RepositoryWriteHost.Manager(scope);
            InsertReferencesThrough(scope);
            manager.EnableTransaction();

            await RepositoryWriteHost.Repository<WriteHostDoc>(scope).InsertAsync(new WriteHostDoc { Title = "doc", AttachmentId = fileId });

            await manager.CommitTransactionAsync();
        }

        await AssertReferenceAuditedAsync(fileId);
    }

    /// <summary>对照组：无事务时处理器的写入走完整管线，本来就对。</summary>
    [Fact]
    public async Task ReferenceAddedDuringSave_WithoutTransaction_GetsTenantAndCreationTime()
    {
        var fileId = Guid.NewGuid();

        using (var scope = _host.CreateScope())
        {
            InsertReferencesThrough(scope);
            await RepositoryWriteHost.Repository<WriteHostDoc>(scope).InsertAsync(new WriteHostDoc { Title = "doc", AttachmentId = fileId });
        }

        await AssertReferenceAuditedAsync(fileId);
    }

    /// <summary>让测试用引用处理器像真实处理器那样，经本作用域的仓储为每个新文件新增一条引用记录。</summary>
    private void InsertReferencesThrough(IServiceScope scope)
    {
        var references = RepositoryWriteHost.Repository<WriteHostReference>(scope);
        _host.Processor.OnProcess = async (changes, ct) =>
        {
            foreach (var change in changes.Where(c => c.ChangeType == FileReferenceChangeType.Create))
            {
                await references.InsertAsync(new WriteHostReference { FileId = change.FileId }, ct);
            }
        };
    }

    private async Task AssertReferenceAuditedAsync(Guid fileId)
    {
        using var scope = _host.CreateScope();
        var stored = await RepositoryWriteHost.Context(scope).References
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.FileId == fileId);

        Assert.Equal(_tenantId, stored.TenantId);
        Assert.NotEqual(default, stored.CreationTime);

        // 写入它的租户看得见它
        Assert.True(await RepositoryWriteHost.Context(scope).References.AnyAsync(r => r.FileId == fileId));
    }
}
