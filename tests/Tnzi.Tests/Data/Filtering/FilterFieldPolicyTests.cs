namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// 请求来源的 <see cref="FilterGroup"/> 只能引用显式允许的字段。此前 <see cref="FilterExpressionBuilder"/>
/// 对任何字段路径逐段反射且无允许列表：持有列表 <c>.view</c> 码的调用方可以按任意实体列（含导航到别的实体）
/// 构造谓词，用 <c>StartsWith</c> / <c>GreaterThan</c> 二分拖出口令哈希这类从不投影的列；字段写错还会以 500
/// 回吐实体类型名。
/// </summary>
public class FilterFieldPolicyTests
{
    private class Order
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public DateTime? ShippedAt { get; set; }
        public OrderStatus Status { get; set; }
        public Customer? Customer { get; set; }
        public List<OrderLine> Lines { get; set; } = [];
        public byte[]? RowVersion { get; set; }
        public Money? Discount { get; set; }
    }

    private sealed class Money
    {
        public decimal Amount { get; set; }
    }

    private class Customer
    {
        public string Name { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public Customer? ReferredBy { get; set; }
    }

    private class OrderLine
    {
        public int Quantity { get; set; }
    }

    private enum OrderStatus { Draft, Paid }

    private static readonly List<Order> Orders =
    [
        new() { Id = 1, Code = "A-1", Total = 10, Status = OrderStatus.Paid, Customer = new Customer { Name = "Ann", PasswordHash = "AQAAAA" } },
        new() { Id = 2, Code = "B-2", Total = 20, Status = OrderStatus.Draft, Customer = new Customer { Name = "Bob", PasswordHash = "ZZZZZZ" } },
    ];

    [Fact]
    public void Build_RequestPolicy_NavigationPath_Rejected()
    {
        var group = FilterGroup.And().WhereStartsWith("Customer.PasswordHash", "AQ");

        var ex = Assert.Throws<FilterFieldException>(() => FilterExpressionBuilder.Build<Order>(group, FilterFieldPolicy.Request));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.Equal("Customer.PasswordHash", ex.Field);
        Assert.Contains("Customer.PasswordHash", ex.Message);
        Assert.DoesNotContain(nameof(Order), ex.Message);
    }

    [Fact]
    public void Build_RequestPolicy_UnknownField_ReportsWithoutTypeName()
    {
        var group = FilterGroup.And().WhereEqual("NoSuchColumn", 1);

        var ex = Assert.Throws<FilterFieldException>(() => FilterExpressionBuilder.Build<Order>(group, FilterFieldPolicy.Request));

        Assert.Contains("NoSuchColumn", ex.Message);
        Assert.DoesNotContain(nameof(Order), ex.Message);
    }

    [Fact]
    public void Build_RequestPolicy_UnknownAndDisallowedFields_SameMessageShape()
    {
        // 「不存在」与「存在但不允许」必须答同一句：否则消息本身就是隐藏列的存在性预言机
        var unknown = Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("NoSuchColumn"), FilterFieldPolicy.Request));
        var disallowed = Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("Customer"), FilterFieldPolicy.Request));

        Assert.Equal(unknown.Message.Replace("NoSuchColumn", "X"), disallowed.Message.Replace("Customer", "X"));
    }

    [Fact]
    public void Build_RequestPolicy_RootScalar_Allowed()
    {
        var group = FilterGroup.And()
            .WhereEqual("status", OrderStatus.Paid)
            .WhereGreaterThan("Total", 5)
            .WhereIsNull("ShippedAt");

        var predicate = FilterExpressionBuilder.Build<Order>(group, FilterFieldPolicy.Request).Compile();

        Assert.Equal([1], Orders.Where(predicate).Select(o => o.Id));
    }

    [Fact]
    public void Build_RequestPolicy_NestedGroup_IsValidatedToo()
    {
        var group = FilterGroup.And()
            .WhereEqual("Status", OrderStatus.Paid)
            .AddGroup(LogicalOperator.Or, g => g.WhereContains("Customer.Name", "A"));

        Assert.Throws<FilterFieldException>(() => FilterExpressionBuilder.Build<Order>(group, FilterFieldPolicy.Request));
    }

    [Fact]
    public void Build_RequestPolicy_CollectionNavigation_Rejected()
    {
        Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("Lines"), FilterFieldPolicy.Request));
    }

    [Fact]
    public void Build_NavigationPolicy_AllowsWithinDepth()
    {
        var policy = FilterFieldPolicy.Request with { AllowNavigation = true, MaxDepth = 2 };
        var predicate = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Customer.Name", "Ann"), policy).Compile();

        Assert.Equal([1], Orders.Where(predicate).Select(o => o.Id));
        Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Customer.ReferredBy.Name", "x"), policy));
    }

    [Fact]
    public void Build_AllowOnlyPolicy_RejectsEverythingElse()
    {
        var policy = FilterFieldPolicy.AllowOnly("Code", "Customer.Name");

        var ok = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("customer.name", "Bob"), policy).Compile();
        Assert.Equal([2], Orders.Where(ok).Select(o => o.Id));
        Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Total", 1), policy));
    }

    [Fact]
    public void Build_Unrestricted_NavigationStillWorks()
    {
        // 无策略 = 服务端自建过滤器，任意深度的导航路径照常可用
        var deep = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Customer.ReferredBy.Name", "x"));
        Assert.NotNull(deep);

        var predicate = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Customer.Name", "Bob")).Compile();
        Assert.Equal([2], Orders.Where(predicate).Select(o => o.Id));
    }

    private sealed class OrderDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public Customer? Customer { get; set; }
        public string? CustomerName { get; set; }
        public int LineCount { get; set; }
    }

    [Fact]
    public void ProjectedBy_AllowsOnlyRootScalarsTheDtoCarries()
    {
        var policy = FilterFieldPolicy.ProjectedBy(typeof(OrderDto));

        Assert.True(policy.IsAllowed(typeof(Order), "code"));
        Assert.True(policy.IsAllowed(typeof(Order), "Id"));
        // 实体有、DTO 没有的根标量：对请求方不存在
        Assert.False(policy.IsAllowed(typeof(Order), "Total"));
        Assert.False(policy.IsAllowed(typeof(Order), "Status"));
        // DTO 有、实体没有（改名 / 计算属性）：不放行任何东西
        Assert.False(policy.IsAllowed(typeof(Order), "CustomerName"));
        Assert.False(policy.IsAllowed(typeof(Order), "LineCount"));
        // DTO 与实体同名的复杂属性仍然不是标量终点，导航路径照样拒绝
        Assert.False(policy.IsAllowed(typeof(Order), "Customer"));
        Assert.False(policy.IsAllowed(typeof(Order), "Customer.Name"));
        Assert.False(policy.IsAllowed(typeof(Order), "Code.Length"));
    }

    [Fact]
    public void ProjectedBy_DtoWithoutScalars_RejectsEverything()
    {
        var policy = FilterFieldPolicy.ProjectedBy(typeof(Money));
        Assert.True(policy.IsAllowed(typeof(Money), "Amount"));

        var nothing = FilterFieldPolicy.ProjectedBy(typeof(OrderLineHolder));
        Assert.False(nothing.IsAllowed(typeof(Order), "Id"));
        Assert.False(nothing.IsAllowed(typeof(Order), "Code"));
    }

    private sealed class OrderLineHolder
    {
        public List<OrderLine> Lines { get; set; } = [];
    }

    [Fact]
    public void TryValidateOrderBy_ChecksEveryPartAgainstThePolicy()
    {
        var policy = FilterFieldPolicy.ProjectedBy(typeof(OrderDto));

        Assert.True(FilterExpressionBuilder.TryValidateOrderBy<Order>(null, policy, out var error));
        Assert.Null(error);
        Assert.True(FilterExpressionBuilder.TryValidateOrderBy<Order>("  ", policy, out _));
        Assert.True(FilterExpressionBuilder.TryValidateOrderBy<Order>("code DESC, Id ASC", policy, out _));

        Assert.False(FilterExpressionBuilder.TryValidateOrderBy<Order>("Code, Total DESC", policy, out error));
        Assert.Contains("Total", error);
        Assert.DoesNotContain("Code", error);
        Assert.DoesNotContain(nameof(Order), error);

        Assert.False(FilterExpressionBuilder.TryValidateOrderBy<Order>("Customer.PasswordHash", policy, out error));
        Assert.Contains("Customer.PasswordHash", error);
        Assert.False(FilterExpressionBuilder.TryValidateOrderBy<Order>("NoSuchColumn", FilterFieldPolicy.Request, out _));
    }

    [Fact]
    public void Build_Unrestricted_IsNullOnNavigation_StillBuilds()
    {
        // 无策略的 Build 是给服务端自建过滤器（数据授权规则等）用的：一条存量 EntityRole 规则
        // {"Field":"Customer","Operator":"IsNull"} 在策略引入前能编译，引入后也必须能 ——
        // 否则 DataAuth 的 deny-on-fail 会把这条规则安静地变成「该角色看不到任何行」。
        var isNull = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNull("Customer")).Compile();
        var isNotNull = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("Customer")).Compile();

        var orders = Orders.Concat([new Order { Id = 3, Code = "C-3" }]).ToList();
        Assert.Equal([3], orders.Where(isNull).Select(o => o.Id));
        Assert.Equal([1, 2], orders.Where(isNotNull).Select(o => o.Id));
    }

    [Fact]
    public void Build_Unrestricted_NonScalarTerminals_StillBuild()
    {
        // byte[]、集合、拥有的值对象：都不在 IsScalar 的名单里，但服务端自建过滤器此前对它们都可用
        var rowVersion = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("RowVersion")).Compile();
        var lines = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNotNull("Lines")).Compile();
        var discount = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereIsNull("Discount")).Compile();
        var throughCollection = FilterExpressionBuilder.Build<Order>(FilterGroup.And().WhereEqual("Lines.Count", 0)).Compile();

        var order = new Order { Id = 9, RowVersion = [1] };
        Assert.True(rowVersion(order));
        Assert.True(lines(order));
        Assert.True(discount(order));
        Assert.True(throughCollection(order));
    }

    [Fact]
    public void Unrestricted_IsAllowed_OnlyRequiresTheProperty()
    {
        Assert.True(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), "Customer"));
        Assert.True(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), "Customer.ReferredBy.PasswordHash"));
        Assert.True(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), "rowversion"));
        Assert.False(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), "NoSuchColumn"));
        Assert.False(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), "Customer.NoSuchColumn"));
        Assert.False(FilterFieldPolicy.Unrestricted.IsAllowed(typeof(Order), " "));
    }

    [Fact]
    public void Build_UnknownField_IsA400NotA500()
    {
        // 无策略的 Build（服务端自建过滤器）字段写错也不该带实体类型名 500 出去
        var ex = Assert.Throws<FilterFieldException>(() =>
            FilterExpressionBuilder.Build<Order>(new FilterRule("Nope", FilterOperator.Equal, 1)));

        Assert.Equal(400, ex.HttpStatusCode);
        Assert.DoesNotContain(nameof(Order), ex.Message);
    }

    [Fact]
    public void TryValidateFields_ReportsFirstViolation()
    {
        var group = FilterGroup.And().WhereEqual("Code", "A").WhereEqual("Customer.Name", "x");

        Assert.False(FilterExpressionBuilder.TryValidateFields<Order>(group, FilterFieldPolicy.Request, out var error));
        Assert.Contains("Customer.Name", error);
        Assert.True(FilterExpressionBuilder.TryValidateFields<Order>(FilterGroup.And().WhereEqual("Code", "A"), FilterFieldPolicy.Request, out error));
        Assert.Null(error);
        Assert.True(FilterExpressionBuilder.TryValidateFields<Order>(null, FilterFieldPolicy.Request, out _));
    }
}
