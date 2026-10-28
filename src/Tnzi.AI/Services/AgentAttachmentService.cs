using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Services;

/// <summary>
/// <see cref="IAgentAttachmentService"/> 的默认实现：附件与字节同行存在 <c>AI_AgentAttachment</c>。
/// </summary>
public class AgentAttachmentService : ApplicationService, IAgentAttachmentService
{
    private readonly IRepository<AgentAttachment, Guid> _repository;
    private readonly IRepository<AgentThreadEntity, Guid> _threadRepository;

    /// <summary>初始化附件服务。</summary>
    public AgentAttachmentService(
        IRepository<AgentAttachment, Guid> repository,
        IRepository<AgentThreadEntity, Guid> threadRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _threadRepository = Check.NotNull(threadRepository);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 判定在服务里而不在控制器里：控制器是可被整体替换的默认控制器。两道条件缺一不可：
    /// 附件归这个用户（<see cref="AgentAttachment.OwnerId"/>），且它所在的线程也归这个用户。
    /// 只看附件的话，线程换了主人（管理端改归属）之后原主人仍读得到；只看线程的话，别人写进这条线程的东西
    /// （今天没有这种写入，但不该靠「今天没有」）就都读得到。拒绝一律 404，不区分「不存在」与「不是你的」。
    /// </remarks>
    public async Task<Result<AttachmentContentDto>> GetContentAsync(
        Guid threadId, Guid attachmentId, Guid userId, CancellationToken cancellationToken = default)
    {
        var ownsThread = await _threadRepository.AsQueryable()
            .AnyAsync(t => t.Id == threadId && t.CreatorId == userId, cancellationToken);
        if (!ownsThread)
        {
            return Fail<AttachmentContentDto>("Attachment not found.", 404, ErrorCodes.AttachmentNotFound);
        }

        var content = await _repository.AsQueryable()
            .Where(a => a.Id == attachmentId && a.ThreadId == threadId && a.OwnerId == userId)
            .Select(a => new AttachmentContentDto { MediaType = a.MediaType, FileName = a.FileName, Content = a.Content })
            .FirstOrDefaultAsync(cancellationToken);

        return content is null
            ? Fail<AttachmentContentDto>("Attachment not found.", 404, ErrorCodes.AttachmentNotFound)
            : Ok(content);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> SaveAsync(
        IReadOnlyList<NewAgentAttachment> attachments, CancellationToken cancellationToken = default)
    {
        Check.NotNull(attachments);
        if (attachments.Count == 0)
        {
            return [];
        }

        var entities = attachments.Select(a => new AgentAttachment
        {
            Id = SequentialGuid.NewGuid(),
            TenantId = a.TenantId,
            ThreadId = a.ThreadId,
            MessageId = a.MessageId,
            RunId = a.RunId,
            ToolCallId = a.ToolCallId,
            OwnerId = a.OwnerId,
            Origin = a.Origin,
            MediaType = a.MediaType,
            FileName = a.FileName,
            Size = a.Content.LongLength,
            Content = a.Content
        }).ToList();

        await _repository.InsertManyAsync(entities, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
        return entities.Select(e => e.Id).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentAttachment>> GetForRunAsync(
        Guid runId, AttachmentOrigin origin, CancellationToken cancellationToken = default)
        => await _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(a => a.RunId == runId && a.Origin == origin)
            .OrderBy(a => a.CreationTime)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentAttachment>> GetForMessageAsync(
        Guid messageId, CancellationToken cancellationToken = default)
        => await _repository.AsQueryable()
            .Where(a => a.MessageId == messageId)
            .OrderBy(a => a.CreationTime)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task BindToMessageAsync(
        Guid runId, AttachmentOrigin origin, Guid messageId, CancellationToken cancellationToken = default)
    {
        await _repository.EnsureTransactionStartedAsync(cancellationToken);
        await _repository.AsQueryable()
            .IgnoreQueryFilters()
            .Where(a => a.RunId == runId && a.Origin == origin && a.MessageId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, messageId), cancellationToken);
    }
}
