using System.Reflection;

namespace Tnzi.AI.Tests.Middleware;

public class AiMiddlewareOrderingTests
{
    /// <summary>
    /// 验证 AiMiddlewareOrders 中所有常量值唯一（无冲突）
    /// </summary>
    [Fact]
    public void AllOrderConstants_HaveUniqueValues()
    {
        var fields = GetOrderFields();

        var duplicates = fields
            .GroupBy(f => f.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"Order {g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
            .ToList();

        duplicates.Count.ShouldBe(0,
            $"Duplicate middleware order values found:\n{string.Join("\n", duplicates)}");
    }

    /// <summary>
    /// 验证所有常量值严格递增（反映执行顺序）
    /// </summary>
    [Fact]
    public void AllOrderConstants_AreStrictlyIncreasing()
    {
        var fields = GetOrderFields()
            .OrderBy(f => f.Value)
            .ToList();

        for (var i = 1; i < fields.Count; i++)
        {
            fields[i].Value.ShouldBeGreaterThan(fields[i - 1].Value,
                $"{fields[i].Name}({fields[i].Value}) must be > {fields[i - 1].Name}({fields[i - 1].Value})");
        }
    }

    /// <summary>
    /// 验证 24 个中间件槽位全部定义
    /// </summary>
    [Fact]
    public void OrderConstants_Has24Slots()
    {
        var count = GetOrderFields().Count;
        count.ShouldBe(24);
    }

    /// <summary>
    /// 验证已知的关键顺序约束
    /// </summary>
    [Theory]
    [InlineData(nameof(AiMiddlewareOrders.ThreadData), nameof(AiMiddlewareOrders.Sandbox))]
    [InlineData(nameof(AiMiddlewareOrders.Sandbox), nameof(AiMiddlewareOrders.FileUpload))]
    [InlineData(nameof(AiMiddlewareOrders.InputGuardrail), nameof(AiMiddlewareOrders.Quota))]
    [InlineData(nameof(AiMiddlewareOrders.Quota), nameof(AiMiddlewareOrders.History))]
    [InlineData(nameof(AiMiddlewareOrders.InputGuardrail), nameof(AiMiddlewareOrders.History))]
    [InlineData(nameof(AiMiddlewareOrders.History), nameof(AiMiddlewareOrders.Summarization))]
    [InlineData(nameof(AiMiddlewareOrders.Summarization), nameof(AiMiddlewareOrders.ContextInjection))]
    [InlineData(nameof(AiMiddlewareOrders.ContextInjection), nameof(AiMiddlewareOrders.Todo))]
    [InlineData(nameof(AiMiddlewareOrders.Todo), nameof(AiMiddlewareOrders.SkillConstraint))]
    [InlineData(nameof(AiMiddlewareOrders.SkillConstraint), nameof(AiMiddlewareOrders.UsageLogging))]
    [InlineData(nameof(AiMiddlewareOrders.DeferredToolFilter), nameof(AiMiddlewareOrders.LoopDetection))]
    [InlineData(nameof(AiMiddlewareOrders.LoopDetection), nameof(AiMiddlewareOrders.ToolGuardrail))]
    [InlineData(nameof(AiMiddlewareOrders.ToolGuardrail), nameof(AiMiddlewareOrders.ToolErrorRecovery))]
    [InlineData(nameof(AiMiddlewareOrders.OutputGuardrail), nameof(AiMiddlewareOrders.Title))]
    [InlineData(nameof(AiMiddlewareOrders.Title), nameof(AiMiddlewareOrders.Memory))]
    [InlineData(nameof(AiMiddlewareOrders.Memory), nameof(AiMiddlewareOrders.Clarification))]
    public void OrderConstraint_FirstBeforeSecond(string firstName, string secondName)
    {
        var firstField = typeof(AiMiddlewareOrders).GetField(firstName, BindingFlags.Public | BindingFlags.Static);
        var secondField = typeof(AiMiddlewareOrders).GetField(secondName, BindingFlags.Public | BindingFlags.Static);

        firstField.ShouldNotBeNull();
        secondField.ShouldNotBeNull();

        var firstValue = (int)firstField.GetRawConstantValue()!;
        var secondValue = (int)secondField.GetRawConstantValue()!;

        firstValue.ShouldBeLessThan(secondValue,
            $"{firstName}({firstValue}) must execute before {secondName}({secondValue})");
    }

    /// <summary>
    /// 验证 Clarification 是最后一个中间件（order 999）
    /// </summary>
    [Fact]
    public void Clarification_IsLast()
    {
        AiMiddlewareOrders.Clarification.ShouldBe(999);

        var maxOrder = GetOrderFields().Max(f => f.Value);
        maxOrder.ShouldBe(999);
    }

    /// <summary>
    /// 验证 CoreExecution(700) 不与任何中间件冲突
    /// </summary>
    [Fact]
    public void CoreExecution_IsDocumentedAt700()
    {
        var allValues = GetOrderFields()
            .Select(f => f.Value)
            .ToHashSet();

        allValues.ShouldNotContain(700);
    }

    private static List<(string Name, int Value)> GetOrderFields()
        => typeof(AiMiddlewareOrders)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (f.Name, Value: (int)f.GetRawConstantValue()!))
            .ToList();
}
