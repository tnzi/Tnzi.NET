using Microsoft.EntityFrameworkCore.Infrastructure;

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
        var mapping = await ForKey(_repository.AsQueryable(), channelName, chatId, topicId)
            .Select(m => (Guid?)m.ThreadId)
            .FirstOrDefaultAsync();

        return mapping;
    }

    public async Task SetThreadIdAsync(string channelName, string chatId, Guid threadId, string? topicId = null, string? userId = null)
    {
        // 使用 tracking 查询，使得"读到现有行后原地更新"返回的就是已跟踪实例，
        // 避免 detached 实例 Attach 时与已跟踪同键实例发生 identity 冲突。
        var existing = await ForKey(_repository.AsQueryable(withTracking: true), channelName, chatId, topicId)
            .FirstOrDefaultAsync();

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

            var winner = await ForKey(_repository.AsQueryable(withTracking: true), channelName, chatId, topicId)
                .FirstOrDefaultAsync();

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
        // 删掉这把键下的全部行：唯一索引修正之前写下的重复行若只删一条，剩下那条会让会话
        // 在「已移除」之后照旧接回原线程。
        await _repository.DeleteAsync(m => m.ChannelName == channelName
            && m.ChatId == chatId
            && m.TopicId == topicId);
    }

    public async Task RemoveByThreadIdAsync(Guid threadId)
    {
        // 走 ThreadId 索引。按线程删全部行，不留任何指向已删线程的映射。
        await _repository.DeleteAsync(m => m.ThreadId == threadId);
    }

    /// <summary>
    /// 按 (channel, chatId, topicId) 取映射，并给出确定的先后：最早写下的那一行优先。
    /// </summary>
    /// <remarks>
    /// 唯一索引保证一把键至多一行，但它修正之前写下的重复行可能还在库里（直到消费方的迁移去重）。
    /// 不排序时 <c>FirstOrDefault</c> 取哪一行由执行计划决定，PostgreSQL 上一次 UPDATE 就可能改变扫描顺序，
    /// 会话于是在两条线程间来回切换；读与写都按同一顺序取，才保证写进去的就是之后读出来的那一行。
    /// </remarks>
    private static IQueryable<ChannelThreadMapping> ForKey(
        IQueryable<ChannelThreadMapping> source, string channelName, string chatId, string? topicId)
        => source
            .Where(m => m.ChannelName == channelName
                && m.ChatId == chatId
                && m.TopicId == topicId)
            .OrderBy(m => m.CreationTime)
            .ThenBy(m => m.Id);
}
