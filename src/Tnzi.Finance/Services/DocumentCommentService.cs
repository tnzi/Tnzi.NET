namespace Tnzi.Finance.Services;

/// <summary>
/// 单据讨论服务
/// </summary>
public class DocumentCommentService : ApplicationService, IDocumentCommentService
{
    private readonly IRepository<DocumentComment, Guid> _repository;
    private readonly IUserDisplayNameProvider? _displayNames;

    /// <param name="serviceProvider">服务提供者（基类延迟解析用）</param>
    /// <param name="repository">单据评论仓储</param>
    /// <param name="displayNames">
    /// 可选：Identity 未加载时为 null，作者名留空、呈现端回落到"某人"。Finance
    /// 刻意零 Identity 引用，这是它拿到名字的唯一方式。
    /// </param>
    public DocumentCommentService(
        IServiceProvider serviceProvider,
        IRepository<DocumentComment, Guid> repository,
        IUserDisplayNameProvider? displayNames = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _displayNames = displayNames;
    }

    public async Task<Result<List<DocumentCommentDto>>> ListAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceId))
            return Fail<List<DocumentCommentDto>>("A document type and id are required.", 400);

        var type = sourceType.Trim();
        var id = sourceId.Trim();

        var list = await _repository.AsNoTracking()
            .Where(c => c.SourceType == type && c.SourceId == id)
            .OrderBy(c => c.CreationTime)
            .Select(c => new DocumentCommentDto
            {
                Id = c.Id,
                SourceType = c.SourceType,
                SourceId = c.SourceId,
                Body = c.Body,
                CreatorId = c.CreatorId,
                CreationTime = c.CreationTime
            })
            .ToListAsync(cancellationToken);

        // 「我能不能删这一条」由服务端判定后随行下发：让呈现端自己拼这条规则，
        // 迟早会和后端的判定漂移，然后按钮显示了却点不动。
        var currentUserId = CurrentUser?.Id;
        var canDeleteAny = await CanDeleteAnyAsync();
        foreach (var dto in list)
            dto.CanDelete = canDeleteAny || (currentUserId.HasValue && dto.CreatorId == currentUserId);

        // 作者名一次批量解析：一条讨论线上十条评论逐个解析就是十次往返。
        if (_displayNames != null && list.Count > 0)
        {
            var authorIds = list.Where(c => c.CreatorId.HasValue).Select(c => c.CreatorId!.Value).Distinct().ToList();
            if (authorIds.Count > 0)
            {
                var names = await _displayNames.GetDisplayNamesAsync(authorIds, cancellationToken);
                foreach (var dto in list)
                {
                    if (dto.CreatorId.HasValue && names.TryGetValue(dto.CreatorId.Value, out var name))
                        dto.CreatorName = name;
                }
            }
        }

        return Ok(list);
    }

    public async Task<Result<DocumentCommentDto>> PostAsync(
        string sourceType, string sourceId, CreateDocumentCommentDto input, CancellationToken cancellationToken = default)
    {
        Check.NotNull(input);

        if (string.IsNullOrWhiteSpace(sourceType) || string.IsNullOrWhiteSpace(sourceId))
            return Fail<DocumentCommentDto>("A document type and id are required.", 400);
        if (string.IsNullOrWhiteSpace(input.Body))
            return Fail<DocumentCommentDto>("A comment cannot be empty.", 400);

        var comment = new DocumentComment
        {
            SourceType = sourceType.Trim(),
            SourceId = sourceId.Trim(),
            Body = input.Body.Trim(),
        };

        await _repository.InsertAsync(comment, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentCommentDto
        {
            Id = comment.Id,
            SourceType = comment.SourceType,
            SourceId = comment.SourceId,
            Body = comment.Body,
            CreatorId = comment.CreatorId,
            CreationTime = comment.CreationTime,
            CanDelete = true,
        });
    }

    /// <summary>
    /// 能否删他人的评论。
    /// </summary>
    /// <remarks>
    /// Authorization 是可选模块：没加载时 <c>PermissionChecker</c> 为 null，此处
    /// 按**没有**这项权限处理。可选契约缺失的缺省方向只能是"少给权限"——反过来
    /// 就等于"没装授权模块 = 人人可删别人的讨论"。
    /// </remarks>
    private async Task<bool> CanDeleteAnyAsync()
    {
        var checker = PermissionChecker;
        return checker != null && await checker.IsGrantedAsync("finance.comment.delete");
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var comment = await _repository.AsQueryable(true).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (comment == null)
            return Fail("Comment not found.", 404);

        // 作者删自己的那条不需要额外授权（谁都可能写错一句），删别人的才要
        // finance.comment.delete。软删保留行——一条能被悄悄抹掉的讨论线在审计
        // 语境里等于没有。
        var currentUserId = CurrentUser?.Id;
        var isAuthor = currentUserId.HasValue && comment.CreatorId == currentUserId;
        if (!isAuthor && !await CanDeleteAnyAsync())
            return Fail("You can only delete your own comments.", 403);

        await _repository.DeleteAsync(comment, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return Ok();
    }
}
