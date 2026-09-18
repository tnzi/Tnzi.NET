namespace Tnzi.EFCore.Tests.TestEntities;

/// <summary>
/// 完整审计 + 并发戳的测试实体：软删除必须同时写删除人、删除时间、修改人、修改时间并换并发戳。
/// </summary>
public class TestAuditedDocument : FullAuditedEntity<Guid>, IConcurrencyStamp
{
    public string Title { get; set; } = string.Empty;

    public string ConcurrencyStamp { get; set; } = string.Empty;
}
