namespace Tnzi.Chat.Services;

/// <summary>
/// 草稿消息服务接口
/// </summary>
public interface IDraftMessageService
{
    /// <summary>
    /// 保存草稿（创建或更新）
    /// </summary>
    Task<Result<MessageDto>> SaveDraftAsync(SaveDraftDto input);

    /// <summary>
    /// 获取当前用户的草稿列表
    /// </summary>
    Task<Result<List<MessageDto>>> GetMyDraftsAsync();

    /// <summary>
    /// 发送草稿（将草稿转为已发送消息）
    /// </summary>
    Task<Result<MessageDto>> SendDraftAsync(Guid draftId);

    /// <summary>
    /// 删除草稿
    /// </summary>
    Task<Result> DeleteDraftAsync(Guid draftId);

    /// <summary>
    /// 获取当前用户草稿数量
    /// </summary>
    Task<Result<int>> GetDraftCountAsync();
}
