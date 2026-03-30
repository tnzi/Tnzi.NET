namespace Tnzi.AI.Channels.Store;

/// <summary>
/// 基于数据库的线程映射存储 — 生产环境推荐
/// </summary>
public class DatabaseChannelThreadStore : IChannelThreadStore
{
    private readonly IRepository<ChannelThreadMapping, Guid> _repository;

    public DatabaseChannelThreadStore(IRepository<ChannelThreadMapping, Guid> repository)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Guid?> GetThreadIdAsync(string channelName, string chatId, string? topicId = null)
    {
        var mapping = await _repository.AsQueryable()
            .Where(m => m.ChannelName == channelName
                && m.ChatId == chatId
                && m.TopicId == topicId)
            .Select(m => (Guid?)m.ThreadId)
            .FirstOrDefaultAsync();

        return mapping;
    }

    public async Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
    {
        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(m => m.ChannelName == channelName
                && m.ChatId == chatId
                && m.TopicId == topicId);

        if (existing != null)
        {
            // 原地更新现有映射（避免 delete+insert 非原子问题）
            existing.ThreadId = threadId;
            if (userId != null) existing.ChannelUserId = userId;
            await _repository.UpdateAsync(existing);
        }
        else
        {
            var mapping = new ChannelThreadMapping
            {
                ChannelName = channelName,
                ChatId = chatId,
                TopicId = topicId,
                ThreadId = threadId,
                ChannelUserId = userId
            };
            await _repository.InsertAsync(mapping);
        }
    }

    public async Task RemoveAsync(string channelName, string chatId, string? topicId = null)
    {
        var existing = await _repository.AsQueryable()
            .FirstOrDefaultAsync(m => m.ChannelName == channelName
                && m.ChatId == chatId
                && m.TopicId == topicId);

        if (existing != null)
        {
            await _repository.DeleteAsync(existing.Id);
        }
    }
}
