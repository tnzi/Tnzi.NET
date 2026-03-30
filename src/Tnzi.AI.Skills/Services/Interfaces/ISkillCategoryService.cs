namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 技能分类管理服务接口
/// </summary>
public interface ISkillCategoryService
{
    /// <summary>
    /// 创建技能分类
    /// </summary>
    Task<Result<SkillCategoryDto>> CreateAsync(CreateSkillCategoryDto input);

    /// <summary>
    /// 更新技能分类
    /// </summary>
    Task<Result<SkillCategoryDto>> UpdateAsync(Guid id, UpdateSkillCategoryDto input);

    /// <summary>
    /// 删除技能分类（检查是否有子分类或关联技能）
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 获取分类树形结构
    /// </summary>
    Task<Result<List<SkillCategoryDto>>> GetTreeAsync();

    /// <summary>
    /// 获取指定分类下的技能列表
    /// </summary>
    Task<Result<List<SkillSummaryDto>>> GetSkillsByCategoryAsync(Guid categoryId);
}
