namespace Tnzi.Storage;

/// <summary>
/// 孤立引用验证器接口
/// 用于验证文件引用所指向的实体是否仍然存在
/// 业务层可以实现此接口来提供实体存在性验证逻辑
/// </summary>
public interface IOrphanReferenceValidator
{
    /// <summary>
    /// 检查实体是否存在
    /// </summary>
    /// <param name="entityType">实体类型名称</param>
    /// <param name="entityId">实体ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实体是否存在</returns>
    Task<bool> IsEntityExistsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default);
}
