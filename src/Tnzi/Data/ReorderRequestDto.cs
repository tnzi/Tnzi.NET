namespace Tnzi.Data;

/// <summary>
/// 排序重排请求：按新顺序排列的记录 Id。
/// </summary>
/// <remarks>
/// <para>全框架 <c>reorder</c> 端点共用这一个请求体形状，前端因此只需要一个类型、
/// 一个 bridge 方法就能驱动任意模块的拖拽排序。重排的<b>范围</b>（同一父节点下、
/// 同一分组内）不在这里，走各端点自己的查询参数——它是定位而不是载荷。</para>
/// <para>提交的通常是当前可见的一段（一页 / 一次筛选），不必是全量：
/// 服务端按槽位保留并入（见 <see cref="Tnzi.Domain.Entities.SortOrderPlanner"/>），
/// 范围外的记录不会被挤动。</para>
/// <para>主键固定为 <see cref="Guid"/>：当前实现 <see cref="Tnzi.Domain.Entities.IHasOrder"/>
/// 的实体主键全是 Guid。若日后出现 long 主键（Snowflake）的可排序实体，
/// 另加一个泛型版本即可，两者可共存——与其现在就让每个端点和每份前端类型
/// 背上一个泛型参数，不如等到真的需要。</para>
/// </remarks>
public class ReorderRequestDto
{
    /// <summary>按新顺序排列的记录 Id（不得重复，且必须都在重排范围内）</summary>
    [Required]
    public List<Guid> Ids { get; set; } = null!;
}
