namespace Tnzi.AI.Services;

/// <summary>
/// 技能分类管理服务实现
/// </summary>
public partial class SkillCategoryService : ApplicationService, ISkillCategoryService
{
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    private readonly IRepository<SkillCategory, Guid> _repository;
    private readonly IRepository<SkillEntity, Guid> _skillRepository;

    public SkillCategoryService(
        IServiceProvider serviceProvider,
        IRepository<SkillCategory, Guid> repository,
        IRepository<SkillEntity, Guid> skillRepository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _skillRepository = Check.NotNull(skillRepository);
    }

    public async Task<Result<SkillCategoryDto>> CreateAsync(CreateSkillCategoryDto input)
    {
        Check.NotNull(input);

        // 生成或验证 slug
        var slug = string.IsNullOrWhiteSpace(input.Slug)
            ? GenerateSlug(input.Name)
            : input.Slug.Trim().ToLower();

        if (!SlugPattern().IsMatch(slug) || slug.Length > 100)
            return Fail<SkillCategoryDto>("Slug must be 1-100 lowercase letters, digits, or hyphens (cannot start or end with a hyphen).", 400, ErrorCodes.SkillInvalidSlug);

        // 检查同一租户下 slug 是否重复
        var duplicate = await _repository.AnyAsync(e => e.Slug == slug);
        if (duplicate)
            return Fail<SkillCategoryDto>($"A skill category with slug '{slug}' already exists.", 409, ErrorCodes.SkillCategorySlugConflict);

        // 验证父分类是否存在
        if (input.ParentId.HasValue)
        {
            var parentExists = await _repository.AnyAsync(e => e.Id == input.ParentId.Value);
            if (!parentExists)
                return Fail<SkillCategoryDto>("Parent category not found.", 404, ErrorCodes.SkillCategoryNotFound);
        }

        var entity = new SkillCategory
        {
            Name = input.Name,
            Slug = slug,
            Description = input.Description,
            ParentId = input.ParentId,
            SortOrder = input.SortOrder ?? 0,
            Icon = input.Icon
        };

        await _repository.InsertAsync(entity);
        return Ok(entity.MapTo<SkillCategoryDto>());
    }

    public async Task<Result<SkillCategoryDto>> UpdateAsync(Guid id, UpdateSkillCategoryDto input)
    {
        Check.NotNull(input);

        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail<SkillCategoryDto>("Skill category not found.", 404, ErrorCodes.SkillCategoryNotFound);

        if (input.Name != null)
            entity.Name = input.Name;
        if (input.Description != null)
            entity.Description = input.Description;
        if (input.SortOrder.HasValue)
            entity.SortOrder = input.SortOrder.Value;
        if (input.Icon != null)
            entity.Icon = input.Icon;

        await _repository.UpdateAsync(entity);
        return Ok(entity.MapTo<SkillCategoryDto>());
    }

    public async Task<Result> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (entity == null)
            return Fail("Skill category not found.", 404, ErrorCodes.SkillCategoryNotFound);

        // 检查是否有子分类
        var hasChildren = await _repository.AnyAsync(e => e.ParentId == id);
        if (hasChildren)
            return Fail("Cannot delete category with child categories. Please delete or move child categories first.", 400, ErrorCodes.SkillCategoryHasChildren);

        // 检查是否有关联技能
        var hasSkills = await _skillRepository.AnyAsync(e => e.CategoryId == id);
        if (hasSkills)
            return Fail("Cannot delete category with associated skills. Please move or uncategorize skills first.", 400, ErrorCodes.SkillCategoryHasSkills);

        await _repository.DeleteAsync(entity);
        return Ok();
    }

    public async Task<Result<List<SkillCategoryDto>>> GetTreeAsync()
    {
        var allCategories = await _repository.AsQueryable()
            .OrderBy(e => e.SortOrder).ThenBy(e => e.Name)
            .ToListAsync();

        // 统计每个分类下的技能数量
        var categoryIds = allCategories.Select(c => c.Id).ToList();
        var skillCounts = await _skillRepository.AsQueryable()
            .Where(s => s.CategoryId != null && categoryIds.Contains(s.CategoryId.Value))
            .GroupBy(s => s.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        // 构建 DTO 映射
        var dtoMap = allCategories.ToDictionary(
            c => c.Id,
            c =>
            {
                var dto = c.MapTo<SkillCategoryDto>();
                dto.SkillCount = skillCounts.GetValueOrDefault(c.Id);
                dto.Children = [];
                return dto;
            });

        // 构建树形结构
        var rootCategories = new List<SkillCategoryDto>();
        foreach (var dto in dtoMap.Values)
        {
            if (dto.ParentId.HasValue && dtoMap.TryGetValue(dto.ParentId.Value, out var parent))
            {
                parent.Children!.Add(dto);
            }
            else
            {
                rootCategories.Add(dto);
            }
        }

        return Ok(rootCategories);
    }

    public async Task<Result<List<SkillSummaryDto>>> GetSkillsByCategoryAsync(Guid categoryId)
    {
        var categoryExists = await _repository.AnyAsync(e => e.Id == categoryId);
        if (!categoryExists)
            return Fail<List<SkillSummaryDto>>("Skill category not found.", 404, ErrorCodes.SkillCategoryNotFound);

        var skills = await _skillRepository.AsQueryable()
            .Where(e => e.CategoryId == categoryId)
            .OrderByDescending(e => e.Priority).ThenBy(e => e.Name)
            .ToListAsync();

        return Ok(skills.MapToList<SkillSummaryDto>());
    }

    /// <summary>
    /// 从名称生成 URL 友好的 slug
    /// </summary>
    private static string GenerateSlug(string name)
    {
        var slug = name.Trim().ToLower()
            .Replace(' ', '-')
            .Replace('_', '-');

        // 只保留字母、数字和连字符
        slug = SlugCleanPattern().Replace(slug, "");

        // 合并连续的连字符
        slug = SlugMultiHyphenPattern().Replace(slug, "-");

        // 去除首尾连字符
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9-]")]
    private static partial Regex SlugCleanPattern();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugMultiHyphenPattern();
}
