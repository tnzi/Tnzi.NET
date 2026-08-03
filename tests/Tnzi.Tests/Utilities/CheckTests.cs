namespace Tnzi.Tests.Utilities;

/// <summary>
/// <see cref="Check"/> 的行为与「可空性契约」守卫。
/// </summary>
public class CheckTests
{
    [Fact]
    public void NotNull_WithValue_ReturnsSameInstance()
    {
        var instance = new object();

        Assert.Same(instance, Check.NotNull(instance));
    }

    [Fact]
    public void NotNull_WithNull_ThrowsWithCallerExpressionAsParamName()
    {
        string? candidateName = null;

        var ex = Assert.Throws<ArgumentNullException>(() => Check.NotNull(candidateName));

        // paramName 由 [CallerArgumentExpression] 从调用处推导，不需要调用方手写
        Assert.Equal(nameof(candidateName), ex.ParamName);
    }

    /// <summary>
    /// 契约守卫：传入**可空引用**表达式时，返回值必须是**非空**类型。
    /// </summary>
    /// <remarks>
    /// 这条靠编译期成立而不是靠运行期断言 —— 下面的 <c>object definite = ...</c> 赋值只有在
    /// <c>Check.NotNull</c> 返回非空类型时才不产生 CS8600/CS8604。签名若退回裸 <c>T value</c>，
    /// <c>T</c> 会连同可空注解一起被推断，返回值仍是可空的，于是每个
    /// <c>Check.NotNull(可空表达式)</c> 的调用点都会重新长出空引用警告
    /// （历史形态：<c>WorkflowService</c> 的 <c>Check.NotNull(ServiceProvider)</c> 4 处 CS8604）。
    /// </remarks>
    [Fact]
    public void NotNull_WithNullableReference_YieldsNonNullableResult()
    {
        object? maybe = new();

        object definite = Check.NotNull(maybe);

        Assert.NotNull(definite);
    }

    /// <summary>
    /// 值类型本身照常可用（<c>T</c> 推断为 <c>int</c>，形参 <c>T?</c> 对值类型不产生
    /// <c>Nullable&lt;int&gt;</c>，故 <b>可空值类型不在本方法的适用范围内</b>——见 <see cref="Check.NotNull{T}"/> 注释）。
    /// </summary>
    [Fact]
    public void NotNull_WithValueType_ReturnsSameValue()
    {
        const int value = 42;

        Assert.Equal(42, Check.NotNull(value));
    }
}
