namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// 从请求拿到的过滤器 JSON 应用到查询时可以带 <see cref="FilterFieldPolicy"/>。不带策略的重载按
/// <see cref="FilterFieldPolicy.Unrestricted"/> 放行任何公共属性路径，只给服务端自建的过滤器用：
/// 拿它处理请求 JSON，调用方就能按从不投影的列（口令哈希）用 <c>StartsWith</c> 二分探测。
/// </summary>
public class FilterQueryablePolicyTests
{
    public class Account
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public Account? Manager { get; set; }
    }

    public class AccountDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static readonly FilterFieldPolicy Policy = FilterFieldPolicy.ProjectedBy(typeof(AccountDto));

    private static IQueryable<Account> Data() => new List<Account>
    {
        new() { Id = 1, Name = "alice", PasswordHash = "$2a$hash-one" },
        new() { Id = 2, Name = "bob", PasswordHash = "$2b$hash-two" },
    }.AsQueryable();

    private const string ProjectedFilter = """{"rules":[{"field":"name","operator":"Equal","value":"bob"}]}""";
    private const string HiddenColumnFilter = """{"rules":[{"field":"PasswordHash","operator":"StartsWith","value":"$2a"}]}""";
    private const string NavigationFilter = """{"rules":[{"field":"Manager.PasswordHash","operator":"StartsWith","value":"$2a"}]}""";

    [Fact]
    public void ApplyFilterFromJson_WithPolicy_AppliesAllowedFields()
    {
        var result = Data().ApplyFilterFromJson(ProjectedFilter, Policy).ToList();

        Assert.Equal([2], result.Select(a => a.Id));
    }

    [Theory]
    [InlineData(HiddenColumnFilter)]
    [InlineData(NavigationFilter)]
    public void ApplyFilterFromJson_WithPolicy_RejectsFieldsOutsideThePolicy(string json)
    {
        Assert.Throws<FilterFieldException>(() => Data().ApplyFilterFromJson(json, Policy).ToList());
    }

    [Fact]
    public void ApplyFilter_WithPolicy_RejectsFieldsOutsideThePolicy()
    {
        var group = new FilterGroup().AddRule("PasswordHash", FilterOperator.StartsWith, "$2a");

        Assert.Throws<FilterFieldException>(() => Data().ApplyFilter(group, Policy).ToList());
    }

    [Fact]
    public void TryApplyFilterFromJson_WithPolicy_ReportsTheRejectedField_AndLeavesTheQueryUnfiltered()
    {
        var query = Data();

        var result = query.TryApplyFilterFromJson(HiddenColumnFilter, Policy, out var error);

        Assert.Same(query, result);
        Assert.Contains("PasswordHash", error);
    }

    [Fact]
    public void TryApplyFilterFromJson_WithPolicy_AppliesAllowedFields()
    {
        var result = Data().TryApplyFilterFromJson(ProjectedFilter, Policy, out var error).ToList();

        Assert.Null(error);
        Assert.Equal([2], result.Select(a => a.Id));
    }

    [Fact]
    public void TryApplyFilterFromJson_WithPolicy_ReportsTooManyRules()
    {
        var rules = string.Join(",", Enumerable.Range(0, Policy.MaxRules + 1)
            .Select(i => $$"""{"field":"Id","operator":"NotEqual","value":{{i}}}"""));

        Data().TryApplyFilterFromJson($$"""{"rules":[{{rules}}]}""", Policy, out var error);

        Assert.Contains("too many rules", error);
    }

    [Fact]
    public void PolicylessOverloads_StayUnrestricted_ForServerBuiltFilters()
    {
        var result = Data().ApplyFilterFromJson(HiddenColumnFilter).ToList();

        Assert.Equal([1], result.Select(a => a.Id));
    }
}
