using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;

namespace Tnzi.AI.Channels.Store;

/// <summary>
/// 基于数据库的线程映射存储 - 生产环境推荐
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
        // 使用 tracking 查询，使得"读到现有行后原地更新"返回的就是已跟踪实例，
        // 避免 detached 实例 Attach 时与已跟踪同键实例发生 identity 冲突。
        var existing = await _repository.AsQueryable(withTracking: true)
            .FirstOrDefaultAsync(m => m.ChannelName == channelName
                && m.ChatId == chatId
                && m.TopicId == topicId);

        if (existing != null)
        {
            UpdateMapping(existing, threadId, userId);
            await _repository.UpdateAsync(existing);
            return;
        }

        var mapping = new ChannelThreadMapping
        {
            ChannelName = channelName,
            ChatId = chatId,
            TopicId = topicId,
            ThreadId = threadId,
            ChannelUserId = userId
        };

        try
        {
            await _repository.InsertAsync(mapping);
        }
        catch (DbUpdateException)
        {
            // TOCTOU: 并发首条消息下，另一线程/作用域已抢先插入同 (Channel,Chat,Topic) 唯一键的行。
            // 解毒当前 DbContext（剥离插入失败的 Added 实体，否则后续 SaveChanges 会重复抛错），
            // 再以 upsert 语义重新读取已存在行并更新。
            DetachFailedInsert(mapping);

            var winner = await _repository.AsQueryable(withTracking: true)
                .FirstOrDefaultAsync(m => m.ChannelName == channelName
                    && m.ChatId == chatId
                    && m.TopicId == topicId);

            if (winner == null)
            {
                // 极罕见：唯一冲突后行又消失（如并发删除）。重抛以暴露真实异常。
                throw;
            }

            UpdateMapping(winner, threadId, userId);
            await _repository.UpdateAsync(winner);
        }
    }

    private static void UpdateMapping(ChannelThreadMapping mapping, Guid threadId, string? userId)
    {
        mapping.ThreadId = threadId;
        if (userId != null) mapping.ChannelUserId = userId;
    }

    /// <summary>
    /// 将插入失败（唯一冲突）的实体从其 DbContext 变更跟踪器剥离，
    /// 防止其残留为 Added 状态污染后续 SaveChanges。
    /// </summary>
    private void DetachFailedInsert(ChannelThreadMapping failed)
    {
        // 通过 EF 文档化途径从 tracking 查询恢复底层 DbContext（仓储抽象未暴露 DbContext）。
        var infrastructure = (IInfrastructure<IServiceProvider>)_repository.AsQueryable(withTracking: true);
        var dbContext = infrastructure.Instance.GetService<ICurrentDbContext>()?.Context;
        if (dbContext == null) return;

        var entry = dbContext.Entry(failed);
        if (entry.State != EntityState.Detached)
        {
            entry.State = EntityState.Detached;
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
