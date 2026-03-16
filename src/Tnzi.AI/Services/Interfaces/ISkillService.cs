namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 技能管理服务接口
/// </summary>
public interface ISkillService
{
    /// <summary>
    /// 获取当前用户/租户可用的所有技能列表
    /// </summary>
    Task<Result<List<SkillSummaryDto>>> GetAvailableAsync();

    /// <summary>
    /// 按 slug 获取技能详情
    /// </summary>
    Task<Result<SkillDetailDto>> GetBySlugAsync(string slug);

    /// <summary>
    /// 关键词搜索技能
    /// </summary>
    Task<Result<List<SkillSummaryDto>>> SearchAsync(string query, int maxResults = 10);

    /// <summary>
    /// 激活技能（渲染模板并返回结果）
    /// </summary>
    Task<Result<SkillActivationResult>> ActivateAsync(string slug, Dictionary<string, string>? parameters = null);

    /// <summary>
    /// 创建自定义技能
    /// </summary>
    Task<Result<SkillDetailDto>> CreateAsync(CreateSkillDto input);

    /// <summary>
    /// 更新技能
    /// </summary>
    Task<Result<SkillDetailDto>> UpdateAsync(Guid id, UpdateSkillDto input);

    /// <summary>
    /// 删除技能
    /// </summary>
    Task<Result> DeleteAsync(Guid id);
}
