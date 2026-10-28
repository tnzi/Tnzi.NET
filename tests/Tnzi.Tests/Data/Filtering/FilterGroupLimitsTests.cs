using System.Linq.Expressions;
using ValidationException = Tnzi.Exceptions.ValidationException;

namespace Tnzi.Tests.Data.Filtering;

/// <summary>
/// 请求来源的过滤器有规则总数与分组嵌套层数上限（<see cref="FilterFieldPolicy.MaxRules"/> /
/// <see cref="FilterFieldPolicy.MaxGroupDepth"/>），超出答 400；同一组里的规则按平衡二叉树合并，
/// 表达式深度随规则数对数增长。此前规则数无上限且合并成左深链：一次请求带几万条规则，
/// 就得到几万层深的表达式交给 EF 递归翻译。
/// </summary>
public class FilterGroupLimitsTests
{
    private class Item
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static FilterGroup GroupWithRules(int count, LogicalOperator logic = LogicalOperator.And)
    {
        var group = new FilterGroup { Logic = logic };
        for (var i = 0; i < count; i++)
        {
            group.AddRule("Id", FilterOperator.NotEqual, i);
        }

        return group;
    }

    private static FilterGroup NestedGroups(int depth)
    {
        var root = new FilterGroup();
        var current = root;
        for (var level = 2; level <= depth; level++)
        {
            var child = new FilterGroup();
            current.Groups.Add(child);
            current = child;
        }

        current.AddRule("Id", FilterOperator.Equal, 1);
        return root;
    }

    private static int LogicalDepth(Expression expression) => expression switch
    {
        BinaryExpression { NodeType: ExpressionType.AndAlso or ExpressionType.OrElse } binary
            => 1 + Math.Max(LogicalDepth(binary.Left), LogicalDepth(binary.Right)),
        _ => 0
    };

    [Fact]
    public void RequestPolicy_AcceptsUpToMaxRules()
    {
        Assert.True(FilterExpressionBuilder.TryValidateFields<Item>(GroupWithRules(FilterFieldPolicy.Request.MaxRules), FilterFieldPolicy.Request, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void RequestPolicy_RejectsMoreThanMaxRules()
    {
        var group = GroupWithRules(FilterFieldPolicy.Request.MaxRules + 1);

        Assert.False(FilterExpressionBuilder.TryValidateFields<Item>(group, FilterFieldPolicy.Request, out var error));
        Assert.Contains("rules", error);
        Assert.Throws<ValidationException>(() => FilterExpressionBuilder.Build<Item>(group, FilterFieldPolicy.Request));
    }

    [Fact]
    public void RulesInNestedGroups_CountTowardsTheLimit()
    {
        var group = new FilterGroup();
        group.Groups.Add(GroupWithRules(60));
        group.Groups.Add(GroupWithRules(60));

        Assert.False(FilterExpressionBuilder.TryValidateFields<Item>(group, FilterFieldPolicy.ProjectedBy(typeof(Item)), out _));
    }

    [Fact]
    public void RequestPolicy_RejectsGroupsNestedDeeperThanMaxGroupDepth()
    {
        var policy = FilterFieldPolicy.Request;

        Assert.True(FilterExpressionBuilder.TryValidateFields<Item>(NestedGroups(policy.MaxGroupDepth), policy, out _));
        Assert.False(FilterExpressionBuilder.TryValidateFields<Item>(NestedGroups(policy.MaxGroupDepth + 1), policy, out var error));
        Assert.Contains("nested", error);
    }

    [Fact]
    public void Unrestricted_HasNoRuleLimit()
    {
        var expression = FilterExpressionBuilder.Build<Item>(GroupWithRules(1000));

        Assert.True(expression.Compile()(new Item { Id = 5000 }));
        Assert.False(expression.Compile()(new Item { Id = 7 }));
    }

    [Theory]
    [InlineData(LogicalOperator.And)]
    [InlineData(LogicalOperator.Or)]
    public void ManyRules_AreCombinedIntoABalancedTree(LogicalOperator logic)
    {
        var expression = FilterExpressionBuilder.Build<Item>(GroupWithRules(1000, logic));

        // ceil(log2(1000)) = 10；左深链会是 999
        Assert.True(LogicalDepth(expression.Body) <= 10, $"depth {LogicalDepth(expression.Body)}");
    }

    [Fact]
    public void BalancedCombination_KeepsTheSameMeaning()
    {
        var group = new FilterGroup { Logic = LogicalOperator.Or };
        for (var i = 0; i < 7; i++)
        {
            group.AddRule("Id", FilterOperator.Equal, i * 10);
        }

        var compiled = FilterExpressionBuilder.Build<Item>(group).Compile();

        Assert.All(Enumerable.Range(0, 7), i => Assert.True(compiled(new Item { Id = i * 10 })));
        Assert.False(compiled(new Item { Id = 5 }));
    }
}
