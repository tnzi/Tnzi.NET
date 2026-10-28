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
/// 线程列表的排序。每一种都以 Id 收尾，翻页时同值的行不会在两页之间漂移。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThreadListSort>))]
public enum ThreadListSort
{
    /// <summary>最近活动在前（默认，与此前相同）。</summary>
    LastActivity = 0,

    /// <summary>最近创建的在前。</summary>
    CreationTime = 1,

    /// <summary>按标题字母序（不分大小写）。</summary>
    Title = 2,
}
