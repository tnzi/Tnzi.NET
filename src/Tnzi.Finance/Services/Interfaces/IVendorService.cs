namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 供应商服务
/// </summary>
public interface IVendorService
{
    /// <summary>分页查询供应商</summary>
    Task<Result<IPagedList<VendorDto>>> GetPagedAsync(VendorQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取供应商</summary>
    Task<Result<VendorDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建供应商</summary>
    Task<Result<VendorDto>> CreateAsync(CreateVendorDto input, CancellationToken cancellationToken = default);

    /// <summary>更新供应商</summary>
    Task<Result<VendorDto>> UpdateAsync(Guid id, UpdateVendorDto input, CancellationToken cancellationToken = default);

    /// <summary>删除供应商（软删除）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按编码查找（内部/导入用）</summary>
    Task<Vendor?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
