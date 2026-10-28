using System.Linq.Expressions;
using Tnzi.Domain.Specifications;

namespace Tnzi.Tests.Domain;

/// <summary>
/// 规约的 And / Or / Not 组合保留操作数的 Include / OrderBy / Paging（形状不随组合丢失）；两侧都声明了排序或分页时
/// 组合含义不明确，构造时抛 <see cref="InvalidOperationException"/> 要求显式声明。此前组合对象只合并谓词，
/// 交给仓储后静默变成不排序、不分页、不加载导航。
/// </summary>
public class SpecificationCompositionTests
{
    public class Order
    {
        public Guid CustomerId { get; set; }
        public DateTime CreationTime { get; set; }
        public decimal Total { get; set; }
        public List<string> Lines { get; set; } = [];
    }

    private sealed class RecentOrdersSpec : Specification<Order>
    {
        public RecentOrdersSpec()
        {
            Include(o => o.Lines);
            OrderByDescending(o => o.CreationTime);
            WithPaging(0, 20);
        }

        public override Expression<Func<Order, bool>> ToExpression() => o => o.Total > 0;
    }

    private sealed class ByCustomerSpec(Guid customerId) : Specification<Order>
    {
        public override Expression<Func<Order, bool>> ToExpression() => o => o.CustomerId == customerId;
    }

    private sealed class SortedByTotalSpec : Specification<Order>
    {
        public SortedByTotalSpec() => OrderBy(o => o.Total);

        public override Expression<Func<Order, bool>> ToExpression() => o => true;
    }

    private sealed class WithLinesSpec : Specification<Order>
    {
        public WithLinesSpec() => Include(o => o.Lines);

        public override Expression<Func<Order, bool>> ToExpression() => o => true;
    }

    private sealed class FirstPageSpec : Specification<Order>
    {
        public FirstPageSpec() => WithPaging(0, 10);

        public override Expression<Func<Order, bool>> ToExpression() => o => true;
    }

    private static void AssertKeepsRecentOrdersShape(ISpecification<Order> combined)
    {
        Assert.Single(combined.IncludeExpressions);
        var orderBy = Assert.Single(combined.OrderByExpressions);
        Assert.True(orderBy.Descending);
        Assert.Equal((0, 20), combined.Paging);
    }

    [Fact]
    public void And_KeepsTheShapeOfEitherOperand()
    {
        var customer = Guid.NewGuid();

        AssertKeepsRecentOrdersShape(new RecentOrdersSpec() & new ByCustomerSpec(customer));
        AssertKeepsRecentOrdersShape(new ByCustomerSpec(customer) & new RecentOrdersSpec());
        AssertKeepsRecentOrdersShape(new RecentOrdersSpec().And(new ByCustomerSpec(customer)));
        AssertKeepsRecentOrdersShape(((ISpecification<Order>)new ByCustomerSpec(customer)).And(new RecentOrdersSpec()));
    }

    [Fact]
    public void Or_And_Not_KeepTheShape()
    {
        AssertKeepsRecentOrdersShape(new RecentOrdersSpec() | new ByCustomerSpec(Guid.NewGuid()));
        AssertKeepsRecentOrdersShape(!new RecentOrdersSpec());
        AssertKeepsRecentOrdersShape(((ISpecification<Order>)new RecentOrdersSpec()).Not());
    }

    [Fact]
    public void Combination_StillCombinesThePredicates()
    {
        var customer = Guid.NewGuid();
        var combined = (new RecentOrdersSpec() & new ByCustomerSpec(customer)).ToExpression().Compile();

        Assert.True(combined(new Order { CustomerId = customer, Total = 5 }));
        Assert.False(combined(new Order { CustomerId = Guid.NewGuid(), Total = 5 }));
        Assert.False(combined(new Order { CustomerId = customer, Total = 0 }));
    }

    [Fact]
    public void IncludesFromBothSides_AreMerged()
    {
        var combined = new WithLinesSpec() & new RecentOrdersSpec() & new ByCustomerSpec(Guid.NewGuid());

        Assert.Equal(2, combined.IncludeExpressions.Count);
        Assert.Single(combined.OrderByExpressions);
        Assert.Equal((0, 20), combined.Paging);
    }

    [Fact]
    public void BothSidesOrdered_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new RecentOrdersSpec() & new SortedByTotalSpec());
    }

    [Fact]
    public void BothSidesPaged_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new RecentOrdersSpec() | new FirstPageSpec());
    }
}
