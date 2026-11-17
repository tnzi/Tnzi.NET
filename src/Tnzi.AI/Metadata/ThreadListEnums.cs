namespace Tnzi.AI.Metadata;

/// <summary>
/// 线程列表按归档状态筛选。
/// </summary>
/// <remarks>
/// 默认 <see cref="All"/>：不带这个条件的调用方（管理端列表、早于归档的客户端）看到的与此前相同。
/// 用户端的「最近对话」传 <see cref="Active"/>。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ThreadListStatus>))]
public enum ThreadListStatus
{
    /// <summary>不按归档筛选。</summary>
    All = 0,

    /// <summary>只要未归档的。</summary>
    Active = 1,

    /// <summary>只要已归档的。</summary>
    Archived = 2,
}

/// <summary>
/// 线程列表在每一层里的排序。
/// </summary>
/// <remarks>
/// 不论选哪一种，列表都先分三层：在等主人的线程（<see cref="AgentThreadDto.AwaitingOwner"/>），然后有一轮在跑的
/// （<see cref="AgentThreadDto.Running"/>），然后其余的；这里选的只是层内的顺序。每一种都依次以创建时间（新的在前）与 Id 收尾，
/// 翻页时同值的行不会在两页之间漂移。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ThreadListSort>))]
public enum ThreadListSort
{
    /// <summary>
    /// 主人最近一次发消息的时间在前（<see cref="AgentThreadDto.LastUserMessageTime"/>，没有时用创建时间；默认）。
    /// 只有主人自己的动作挪动一行：AI 的回答、一轮结束、改标题、标记已读都不改变它。
    /// </summary>
    LastUserMessage = 0,

    /// <summary>最近创建的在前。</summary>
    CreationTime = 1,

    /// <summary>按标题字母序（不分大小写）。</summary>
    Title = 2,
}
