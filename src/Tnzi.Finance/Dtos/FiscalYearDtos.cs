namespace Tnzi.Finance.Dtos;

/// <summary>
/// 会计年度 DTO
/// </summary>
public class FiscalYearDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedTime { get; set; }
    public Guid? ClosedById { get; set; }
}

/// <summary>
/// 创建会计年度请求
/// </summary>
public class CreateFiscalYearDto
{
    public string Name { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}
