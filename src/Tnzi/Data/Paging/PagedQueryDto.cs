namespace Tnzi.Data;

/// <summary>
/// 分页查询 DTO 基类 / fallback concrete query for endpoints that only
/// need `pageIndex` + `pageSize`.
/// </summary>
/// <remarks>
/// Originally declared <c>abstract</c> so every concrete subclass had to
/// supply its own defaults. That broke ASP.NET model binding for any
/// controller that took <c>[FromQuery] PagedQueryDto query</c> directly -
/// the binder throws <c>InvalidOperationException: Model bound complex
/// types must not be abstract or value types</c> and the request 500s.
///
/// Concrete now so endpoints (e.g. <c>GET /admin/roles/{id}/users</c>,
/// <c>GET /admin/organizations/{id}/users</c>) that don't need any custom
/// filter shape can bind this base type and still get the page-default
/// behaviour. Subclasses override <c>DefaultPageIndex</c> /
/// <c>DefaultPageSize</c> / <c>MaxPageSize</c>, which live on
/// <see cref="PagedQuery"/> so that <c>Skip</c> / <c>Take</c> and the
/// reported page size always come from the same values.
/// </remarks>
public class PagedQueryDto : PagedQuery
{
}
