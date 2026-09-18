using Microsoft.Extensions.Logging;

namespace Tnzi.Identity.Organization.Services;

// 见 GlobalUsings.cs 顶部：`Organization` 的 using 必须写在命名空间体内。
using Tnzi.Identity.Organization.Entities;

/// <summary>
/// 存量修复：把 <see cref="Organization.Path"/> 重建成「祖先 Id 链」。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么会有这样的行。</b>自初始提交起，<c>CreateAsync</c> 与 <c>CreateManyAsync</c> 往 Path 末段写的是
/// 一枚<b>随机 GUID</b>而不是实体自己的 Id（Id 由 SaveChanges 才生成、不回填），而 <c>GetAllParentsAsync</c>
/// 把路径段当祖先 Id 解析 —— 于是 <c>GET admin/organizations/{id}/parents</c> 对经 API 建出来的每一个节点
/// 恒返回空数组。写路径已修，但库里的行还是旧形状；<c>MoveAsync</c> 一直写真 Id，所以两种形状会混存。
/// </para>
/// <para>
/// 每次启动跑一遍、幂等：按 <c>ParentId</c> 链重算每一行的 Path 与 Level，只改写不一致的行（修完之后 0 行命中）。
/// 忽略查询过滤器 —— 已软删的节点与每一个租户的节点都要修，否则被过滤掉的祖先会让子节点的链条断在半路。
/// 环（ParentId 绕回自己）不改写，只记 Warning：那是数据本身坏了，不是这次修复该猜的事。
/// </para>
/// </remarks>
internal sealed class OrganizationPathRepairStartupTask : IPostMigrationStartupTask
{
    public async Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        Check.NotNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Organization, Guid>>();
        var cache = scope.ServiceProvider.GetService<ICache>();
        var multiTenancyEnabled = scope.ServiceProvider.GetService<IOptions<MultiTenancyOptions>>()?.Value.Enabled ?? false;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OrganizationPathRepairStartupTask>>();

        await RepairAsync(repository, cache, multiTenancyEnabled, logger, cancellationToken);
    }

    /// <summary>
    /// 重建路径段不是祖先 Id 链的行；返回改写的行数。
    /// </summary>
    internal static async Task<int> RepairAsync(
        IRepository<Organization, Guid> repository,
        ICache? cache,
        bool multiTenancyEnabled,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(repository);
        Check.NotNull(logger);

        var rows = await repository.AsQueryable(withTracking: true)
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        var byId = rows.ToDictionary(o => o.Id);
        var changed = new List<Organization>();

        foreach (var row in rows)
        {
            var chain = AncestorChain(row, byId);
            if (chain == null)
            {
                logger.LogWarning("Organization {OrganizationId} has a cyclic or dangling ParentId chain; its Path was left as is.", row.Id);
                continue;
            }

            var path = "/" + string.Join("/", chain.Select(id => id.ToString())) + "/";
            var level = chain.Count;
            if (row.Path == path && row.Level == level)
            {
                continue;
            }

            row.Path = path;
            row.Level = level;
            changed.Add(row);
        }

        if (changed.Count == 0)
        {
            return 0;
        }

        await repository.UpdateManyAsync(changed, cancellationToken);
        await InvalidateCachesAsync(cache, multiTenancyEnabled, changed);

        logger.LogWarning(
            "Rebuilt the Path of {Count} organization(s) whose segments were not their ancestors' ids; "
            + "GET admin/organizations/{{id}}/parents answers for them from now on.",
            changed.Count);

        return changed.Count;
    }

    /// <summary>从根到本节点的 Id 链（含本节点）；父节点缺失或成环时为 <c>null</c>。</summary>
    private static List<Guid>? AncestorChain(Organization node, IReadOnlyDictionary<Guid, Organization> byId)
    {
        var chain = new List<Guid>();
        var visited = new HashSet<Guid>();
        var current = node;
        while (true)
        {
            if (!visited.Add(current.Id))
            {
                return null;
            }

            chain.Add(current.Id);
            if (current.ParentId == null)
            {
                break;
            }

            if (!byId.TryGetValue(current.ParentId.Value, out var parent))
            {
                return null;
            }

            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// 树缓存按租户分键（与 <c>OrganizationService</c> 同一形状），只有被改写的行知道自己属于哪个租户。
    /// </summary>
    private static async Task InvalidateCachesAsync(ICache? cache, bool multiTenancyEnabled, IReadOnlyCollection<Organization> changed)
    {
        if (cache == null)
        {
            return;
        }

        if (!multiTenancyEnabled)
        {
            await cache.RemoveAsync(CacheKeys.Identity.OrganizationTree);
            foreach (var row in changed)
            {
                await cache.RemoveAsync(CacheKeys.Identity.Organization(row.Id));
            }

            return;
        }

        foreach (var tenantPart in changed.Select(o => o.TenantId?.ToString() ?? "host").Distinct())
        {
            await cache.RemoveAsync($"{CacheKeys.Identity.OrganizationTree}:{tenantPart}");
        }

        foreach (var row in changed)
        {
            await cache.RemoveAsync($"{CacheKeys.Identity.Organization(row.Id)}:{row.TenantId?.ToString() ?? "host"}");
        }
    }
}
