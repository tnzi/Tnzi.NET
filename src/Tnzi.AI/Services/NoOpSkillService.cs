namespace Tnzi.AI.Services;

public class NoOpSkillService : ISkillService, INoOpService
{
    private const string Message =
        "Skill management requires Tnzi.AI.Skills module. Add [DependsOn(typeof(AISkillsModule))] to enable skill features.";

    public Task<Result<List<SkillSummaryDto>>> GetAvailableAsync()
        => Task.FromResult(Result.Failure<List<SkillSummaryDto>>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<SkillDetailDto>> GetBySlugAsync(string slug)
        => Task.FromResult(Result.Failure<SkillDetailDto>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<List<SkillSummaryDto>>> SearchAsync(string query, int maxResults = 10)
        => Task.FromResult(Result.Failure<List<SkillSummaryDto>>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<SkillActivationResult>> ActivateAsync(string slug, Dictionary<string, string>? parameters = null)
        => Task.FromResult(Result.Failure<SkillActivationResult>(Message, 501, ErrorCodes.SkillActivationFailed));

    public Task<Result<SkillDetailDto>> CreateAsync(CreateSkillDto input)
        => Task.FromResult(Result.Failure<SkillDetailDto>(Message, 501, ErrorCodes.SkillUnauthorized));

    public Task<Result<SkillDetailDto>> UpdateAsync(Guid id, UpdateSkillDto input)
        => Task.FromResult(Result.Failure<SkillDetailDto>(Message, 501, ErrorCodes.SkillUnauthorized));

    public Task<Result> DeleteAsync(Guid id)
        => Task.FromResult(Result.Failure(Message, 501, ErrorCodes.SkillUnauthorized));

    public Task<Result<int>> BatchDeleteAsync(List<Guid> ids)
        => Task.FromResult(Result.Failure<int>(Message, 501, ErrorCodes.SkillUnauthorized));

    public Task<Result<int>> BatchSetEnabledAsync(List<Guid> ids, bool enabled)
        => Task.FromResult(Result.Failure<int>(Message, 501, ErrorCodes.SkillUnauthorized));

    public Task<Result<IPagedList<SkillSummaryDto>>> GetPagedAsync(SkillQueryDto query)
        => Task.FromResult(Result.Failure<IPagedList<SkillSummaryDto>>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<SkillUsageStatsDto>> GetUsageStatsAsync()
        => Task.FromResult(Result.Failure<SkillUsageStatsDto>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<List<PopularSkillDto>>> GetPopularSkillsAsync(int topN = 10)
        => Task.FromResult(Result.Failure<List<PopularSkillDto>>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<List<SkillExportDto>>> ExportSkillsAsync(SkillScope? scope = null)
        => Task.FromResult(Result.Failure<List<SkillExportDto>>(Message, 501, ErrorCodes.SkillNotFound));

    public Task<Result<SkillImportResultDto>> ImportSkillsAsync(List<SkillExportDto> skills, SkillScope targetScope)
        => Task.FromResult(Result.Failure<SkillImportResultDto>(Message, 501, ErrorCodes.SkillNotFound));
}
