namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 技能分类管理控制器（管理员） — 提供分类 CRUD 和树形查询功能。
/// </summary>
[DefaultController]
[Route("admin/ai/skill-categories")]
public class DefaultSkillCategoryAdminController : ApiAdminControllerBase
{
    protected readonly ISkillCategoryService CategoryService;

    public DefaultSkillCategoryAdminController(ISkillCategoryService categoryService)
    {
        CategoryService = Check.NotNull(categoryService);
    }

    /// <summary>
    /// 获取分类树形结构
    /// </summary>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<List<SkillCategoryDto>>> GetTree()
    {
        var result = await CategoryService.GetTreeAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建技能分类
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<SkillCategoryDto>> Create([FromBody] CreateSkillCategoryDto input)
    {
        var result = await CategoryService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新技能分类
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<SkillCategoryDto>> Update(Guid id, [FromBody] UpdateSkillCategoryDto input)
    {
        var result = await CategoryService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除技能分类
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await CategoryService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取指定分类下的技能列表
    /// </summary>
    [HttpGet("{categoryId:guid}/skills")]
    public virtual async Task<ApiResult<List<SkillSummaryDto>>> GetSkillsByCategory(Guid categoryId)
    {
        var result = await CategoryService.GetSkillsByCategoryAsync(categoryId);
        return result.ToApiResult();
    }
}
