namespace Tnzi.Storage.Controllers.Admin;

/// <summary>
/// Admin audit endpoints that expose cross-file reads over FileChunk and
/// FileVersion — the data the Phase 3 StorageChunk.vue and StorageVersion.vue
/// admin pages need. These are intentionally NOT layered through
/// IFileChunkUploadService / IFileVersionService because:
/// 1. Those services model user-facing workflows (initiate / upload / complete)
///    and would be bloated by admin-only pagination queries.
/// 2. The queries here are trivial read-only LINQ over a single repository.
/// 3. Adding admin methods to the user services would force updates to every
///    existing mock in tests.
///
/// If more admin audit surface emerges later, the right move is to extract
/// a dedicated IStorageAuditService — but until then, direct repository access
/// at the admin-controller boundary is a pragmatic fit.
/// </summary>
[DefaultController]
[Route("admin/storage/audit")]
[ApiAuthorize(PermissionName = "storage.file.view")]
public class DefaultStorageAuditAdminController : ApiAdminControllerBase
{
    protected readonly IRepository<FileChunk, Guid> ChunkRepository;
    protected readonly IRepository<FileVersion, Guid> VersionRepository;

    public DefaultStorageAuditAdminController(
        IRepository<FileChunk, Guid> chunkRepository,
        IRepository<FileVersion, Guid> versionRepository)
    {
        ChunkRepository = Check.NotNull(chunkRepository);
        VersionRepository = Check.NotNull(versionRepository);
    }

    /// <summary>
    /// Paged list of FileChunk rows across all upload sessions. Filterable
    /// by UploadSessionId when an operator wants to audit a specific upload.
    /// </summary>
    [HttpGet("chunks")]
    public virtual async Task<ApiResult<IPagedList<FileChunkAuditDto>>> GetChunks([FromQuery] FileChunkAuditQueryDto query)
    {
        Check.NotNull(query);

        var filtered = ChunkRepository.Where(c =>
            !query.UploadSessionId.HasValue || c.UploadSessionId == query.UploadSessionId.Value);

        var totalCount = await filtered.CountAsync();

        var pageItems = await filtered
            .OrderByDescending(c => c.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(c => new FileChunkAuditDto
            {
                Id = c.Id,
                UploadSessionId = c.UploadSessionId,
                ChunkIndex = c.ChunkIndex,
                ChunkSize = c.ChunkSize,
                Md5Hash = c.Md5Hash,
                CreationTime = c.CreationTime,
            })
            .ToListAsync();

        var paged = new PagedList<FileChunkAuditDto>(pageItems, query.PageIndex, query.PageSize, totalCount);
        return Result.Success<IPagedList<FileChunkAuditDto>>(paged).ToApiResult();
    }

    /// <summary>
    /// Paged list of FileVersion rows across all files. Supports filtering
    /// to a specific file or to just the current-marked version per file.
    /// </summary>
    [HttpGet("versions")]
    public virtual async Task<ApiResult<IPagedList<FileVersionAuditDto>>> GetVersions([FromQuery] FileVersionAuditQueryDto query)
    {
        Check.NotNull(query);

        var filtered = VersionRepository.Where(v =>
            (!query.FileId.HasValue || v.FileId == query.FileId.Value) &&
            (!query.CurrentOnly.HasValue || !query.CurrentOnly.Value || v.IsCurrent));

        var totalCount = await filtered.CountAsync();

        var pageItems = await filtered
            .OrderByDescending(v => v.CreationTime)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(v => new FileVersionAuditDto
            {
                Id = v.Id,
                FileId = v.FileId,
                Version = v.Version,
                Path = v.Path,
                Size = v.Size,
                Md5Hash = v.Md5Hash,
                Description = v.Description,
                IsCurrent = v.IsCurrent,
                CreationTime = v.CreationTime,
                CreatorId = v.CreatorId,
            })
            .ToListAsync();

        var paged = new PagedList<FileVersionAuditDto>(pageItems, query.PageIndex, query.PageSize, totalCount);
        return Result.Success<IPagedList<FileVersionAuditDto>>(paged).ToApiResult();
    }
}
