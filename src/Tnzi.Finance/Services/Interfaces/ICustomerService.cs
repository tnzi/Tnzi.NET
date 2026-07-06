namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 客户服务
/// </summary>
public interface ICustomerService
{
    /// <summary>分页查询客户</summary>
    Task<Result<IPagedList<CustomerDto>>> GetPagedAsync(CustomerQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取客户</summary>
    Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建客户</summary>
    Task<Result<CustomerDto>> CreateAsync(CreateCustomerDto input, CancellationToken cancellationToken = default);

    /// <summary>更新客户</summary>
    Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken cancellationToken = default);

    /// <summary>删除客户（软删除）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按编码查找（内部/导入用）</summary>
    Task<Customer?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);
}
