namespace Tnzi.AI.Rag.Services;

/// <summary>
/// <see cref="IRagAccessAuthorizer"/> 的默认实现：按 <see cref="KnowledgeBase.IsUserQueryable"/> 放行。
/// </summary>
/// <remarks>
/// <para>
/// 两类调用者：
/// </para>
/// <list type="bullet">
///   <item>
///     持 <c>ai.knowledge.view</c> 的运维/管理调用者 —— 原样放行，含 search-all。
///     知识库管理端本来就以这个码为准，这里不额外收紧。
///   </item>
///   <item>
///     普通已登录用户 —— 只能查<b>显式标记为可被用户直查</b>的启用中知识库。
///     未指定 id 时不再走 search-all，而是收敛成"这批"；一个都没有就 403。
///   </item>
/// </list>
/// <para>
/// ★ <b>默认关闭是刻意的</b>：<c>IsUserQueryable</c> 默认 <c>false</c>，所以升级后
/// 用户端 RAG 端点在管理员逐个开启之前查不到东西。反过来（默认开启）等于把这次修复
/// 变成一句注释 —— 每个既有部署仍旧对所有已登录用户敞开全部知识库原文。
/// </para>
/// <para>
/// 请求里点名了一个不被允许的知识库时<b>整条拒绝</b>而不是悄悄剔除：剔除会让
/// "你无权查这个库"和"这个库里没有相关内容"给出同一个回答。不存在的 id 与无权的 id
/// 给同一个答案，因此不泄漏存在性。
/// </para>
/// </remarks>
public class DefaultRagAccessAuthorizer : ApplicationService, IRagAccessAuthorizer
{
    /// <summary>知识库管理面的读权限码，与 admin 控制器类级码同源。</summary>
    public const string KnowledgeViewPermission = "ai.knowledge.view";

    private readonly IRepository<KnowledgeBase, Guid> _kbRepository;

    public DefaultRagAccessAuthorizer(
        IServiceProvider serviceProvider,
        IRepository<KnowledgeBase, Guid> kbRepository) : base(serviceProvider)
    {
        _kbRepository = Check.NotNull(kbRepository);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Guid>>> AuthorizeQueryAsync(
        IReadOnlyList<Guid>? requestedKnowledgeBaseIds,
        CancellationToken ct = default)
    {
        var requested = requestedKnowledgeBaseIds ?? [];

        // 管理调用者：原样放行（空 = search-all 仍然可用）。
        if (await HasKnowledgeViewAsync())
        {
            return Ok<IReadOnlyList<Guid>>(requested);
        }

        var queryable = await _kbRepository.ToListAsync(
            kb => kb.IsEnabled && kb.IsUserQueryable, ct);
        var allowed = queryable.Select(kb => kb.Id).ToHashSet();

        if (requested.Count == 0)
        {
            if (allowed.Count == 0)
            {
                return Fail<IReadOnlyList<Guid>>(
                    "No knowledge base is available for direct user queries. " +
                    "An administrator must mark a knowledge base as user-queryable first.",
                    403,
                    Tnzi.Exceptions.ErrorCodes.FORBIDDEN);
            }

            // search-all 收敛成"允许的这批"——普通用户永远拿不到无过滤的全库检索。
            return Ok<IReadOnlyList<Guid>>(allowed.ToList());
        }

        if (requested.Any(id => !allowed.Contains(id)))
        {
            return Fail<IReadOnlyList<Guid>>(
                "Access denied to one or more of the requested knowledge bases.",
                403,
                Tnzi.Exceptions.ErrorCodes.FORBIDDEN);
        }

        return Ok<IReadOnlyList<Guid>>(requested);
    }

    /// <summary>
    /// 未注册 <c>IPermissionChecker</c> 时返回 false（fail-closed）：没装授权能力的宿主
    /// 拿到的应当是"按 IsUserQueryable 放行"，而不是"人人都是管理员"。
    /// </summary>
    private async Task<bool> HasKnowledgeViewAsync()
    {
        var checker = PermissionChecker;
        return checker is not null && await checker.IsGrantedAsync(KnowledgeViewPermission);
    }
}
