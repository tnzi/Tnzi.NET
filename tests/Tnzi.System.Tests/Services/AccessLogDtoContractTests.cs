namespace Tnzi.System.Tests.Services;

/// <summary>
/// <see cref="AccessLogDto.TenantId"/> 只在服务端流转：不出现在响应里，也不能经请求体写进来。
/// </summary>
/// <remarks>
/// 手工上报的入口（<c>POST admin/access-logs</c>）收的就是这个 DTO：如果租户能从请求体写进来，
/// 一个租户管理员就可以往别的租户名下塞访问日志；如果它出现在响应里，线缆形状就平白多了一个字段。
/// </remarks>
public class AccessLogDtoContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TenantId_IsNeverSerialized()
    {
        var json = JsonSerializer.Serialize(new AccessLogDto { Path = "/api/a", Method = "GET", TenantId = Guid.NewGuid() }, Web);

        json.ShouldNotContain("tenant", Case.Insensitive);
    }

    [Fact]
    public void TenantId_CannotBeSetFromARequestBody()
    {
        var dto = JsonSerializer.Deserialize<AccessLogDto>(
            """{"path":"/api/a","method":"GET","tenantId":"bbbbbbbb-0000-0000-0000-000000000002"}""", Web);

        dto.ShouldNotBeNull().TenantId.ShouldBeNull();
    }
}
