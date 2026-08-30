namespace Tnzi.Identity.Services;

/// <summary>
/// 组织架构服务接口
/// </summary>
/// <remarks>
/// ★ <b>契约留在核心，实现在可选包 <c>Tnzi.Identity.Organization</c> 里</b>
/// （<c>OrganizationService</c>）—— 与 Finance 的 <c>ICheckDocumentRenderer</c> /
/// <c>IReceiptExtractor</c> 同一形状。
///
/// 理由是核心自己要拿着它：<c>UserService</c> 与 <c>DefaultUserAdminController</c> 都以
/// <c>IOrganizationService?</c>（可空可选注入）持有它，把接口一起搬走会让核心反过来
/// 引用子模块，接缝当场闭死。DTO（<c>OrganizationDto</c> 等）同理留在核心。
///
/// 未加载该包时容器里没有实现，两处调用方按 null 降级：管理端的两个组织端点答 <b>501</b>
/// 并指名要加载哪个包；用户 DTO 的 <c>OrganizationName</c> 恒为 null。
/// </remarks>
public interface IOrganizationService
{
    /// <summary>
    /// 获取组织树
    /// </summary>
    /// <returns>组织树</returns>
    Task<Result<IEnumerable<OrganizationTreeNodeDto>>> GetTreeAsync();

    /// <summary>
    /// 根据ID获取组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>组织信息</returns>
    Task<Result<OrganizationDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 批量取组织名（Id → Name），用于把一页用户的 <c>OrganizationId</c> 翻译成显示名。
    /// </summary>
    /// <remarks>
    /// 拆分前这一列来自 <c>User → Organization</c> 的 LEFT JOIN；导航属性随实体搬走之后，
    /// 核心改为按整页的 Id 集合问一次（有界集合，一次 IN 查询，不是 N+1）。
    /// 内部方法，故返回原始类型而不是 <c>Result&lt;T&gt;</c>：查不到的 Id 直接不出现在
    /// 结果里，调用方据此把名字留空 —— 一个用户挂在已删组织上不是错误。
    /// </remarks>
    /// <param name="ids">组织 Id 集合，可含重复；空集合直接返回空字典</param>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>
    /// 创建组织
    /// </summary>
    /// <param name="input">组织信息</param>
    /// <returns>创建的组织</returns>
    Task<Result<OrganizationDto>> CreateAsync(CreateOrganizationDto input);

    /// <summary>
    /// 更新组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="input">组织信息</param>
    /// <returns>更新后的组织</returns>
    Task<Result<OrganizationDto>> UpdateAsync(Guid id, UpdateOrganizationDto input);

    /// <summary>
    /// 删除组织
    /// </summary>
    /// <param name="id">组织ID</param>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 移动组织到新的父组织
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="newParentId">新父组织ID</param>
    Task<Result> MoveAsync(Guid id, Guid? newParentId);

    /// <summary>
    /// 获取组织的所有子组织（包括子子组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有子组织</returns>
    Task<Result<IEnumerable<OrganizationDto>>> GetAllChildrenAsync(Guid id);

    /// <summary>
    /// 获取组织的所有父组织（包括父父组织）
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>所有父组织</returns>
    Task<Result<IEnumerable<OrganizationDto>>> GetAllParentsAsync(Guid id);

    /// <summary>
    /// 批量创建组织
    /// </summary>
    /// <param name="inputs">组织信息列表</param>
    /// <returns>创建的组织列表</returns>
    Task<Result<IEnumerable<OrganizationDto>>> CreateManyAsync(IEnumerable<CreateOrganizationDto> inputs);

    /// <summary>
    /// 批量更新组织
    /// </summary>
    /// <param name="inputs">组织更新信息列表（ID和DTO）</param>
    /// <returns>更新后的组织列表</returns>
    Task<Result<IEnumerable<OrganizationDto>>> UpdateManyAsync(IEnumerable<(Guid Id, UpdateOrganizationDto Dto)> inputs);

    /// <summary>
    /// 批量删除组织
    /// </summary>
    /// <param name="ids">组织ID列表</param>
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 获取组织的人员统计
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <returns>人员统计信息（总人数、直接下属人数等）</returns>
    Task<Result<OrganizationStatisticsDto>> GetStatisticsAsync(Guid id);

    /// <summary>
    /// 分配用户到组织
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="organizationId">组织ID</param>
    Task<Result> AssignUserToOrganizationAsync(Guid userId, Guid organizationId);

    /// <summary>
    /// 从组织移除用户
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> RemoveUserFromOrganizationAsync(Guid userId);

    /// <summary>
    /// 获取组织下的用户分页列表
    /// </summary>
    /// <param name="organizationId">组织ID</param>
    /// <param name="query">分页查询参数</param>
    /// <param name="includeChildren">是否包含子组织的用户</param>
    Task<Result<IPagedList<UserListItemDto>>> GetUsersAsync(Guid organizationId, PagedQueryDto query, bool includeChildren = false);

    /// <summary>
    /// 根据名称或代码模糊搜索组织
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="maxResults">最大返回数量（默认20）</param>
    /// <returns>匹配的组织列表</returns>
    Task<Result<IEnumerable<OrganizationDto>>> SearchAsync(string keyword, int maxResults = 20);

    /// <summary>
    /// 更新组织排序
    /// </summary>
    /// <param name="id">组织ID</param>
    /// <param name="newSortOrder">新的排序值</param>
    Task<Result> UpdateSortOrderAsync(Guid id, int newSortOrder);

    /// <summary>
    /// 批量更新组织排序（适用于前端拖拽排序场景）
    /// </summary>
    /// <param name="updates">排序更新列表（组织ID和新排序值）</param>
    Task<Result> BatchUpdateSortOrderAsync(IEnumerable<(Guid Id, int SortOrder)> updates);

    /// <summary>
    /// 重排同一父组织下的组织（拖拽排序）
    /// </summary>
    /// <remarks>
    /// 与 <see cref="BatchUpdateSortOrderAsync"/> 的区别：那个是「把这几条的序号精确设成这些值」，
    /// 调用方自己负责不与范围外的记录撞号；本方法接受一段<b>相对顺序</b>，
    /// 由服务端按槽位保留并入全量序列，范围外的组织不会被挤动。
    /// </remarks>
    /// <param name="ids">按新顺序排列的组织 Id，可以只是当前可见的一段</param>
    /// <param name="parentId">父组织范围；null = 顶级组织</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result> ReorderAsync(IReadOnlyList<Guid> ids, Guid? parentId = null, CancellationToken cancellationToken = default);
}

