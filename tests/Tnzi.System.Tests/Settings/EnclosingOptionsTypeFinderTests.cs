namespace Tnzi.System.Tests.Settings;

/// <summary>
/// 外层 Options 的判定是结构性的：属性类型对得上，且外层配置段 + 属性名等于内层配置段。
/// </summary>
public class EnclosingOptionsTypeFinderTests
{
    [ConfigSection("Root")]
    public sealed class RootOptions
    {
        public MiddleOptions Middle { get; set; } = new();
    }

    [ConfigSection("Root:Middle")]
    public sealed class MiddleOptions
    {
        public LeafOptions? Leaf { get; set; }
    }

    [ConfigSection("Root:Middle:Leaf")]
    public sealed class LeafOptions
    {
        public bool Enabled { get; set; }
    }

    /// <summary>同样持有 <see cref="LeafOptions"/>，但绑定的段不是 Root:Middle:Leaf（例如一个 DTO）。</summary>
    public sealed class UnrelatedHolder
    {
        public LeafOptions? Leaf { get; set; }
    }

    [Fact]
    public void Finds_Every_Enclosing_Level_Nearest_First()
        => EnclosingOptionsTypeFinder.Find(typeof(LeafOptions))
            .ShouldBe([typeof(MiddleOptions), typeof(RootOptions)]);

    [Fact]
    public void A_Holder_Whose_Section_Does_Not_Line_Up_Is_Not_An_Enclosing_Options_Type()
        => EnclosingOptionsTypeFinder.Find(typeof(LeafOptions)).ShouldNotContain(typeof(UnrelatedHolder));

    [Fact]
    public void A_Root_Options_Type_Has_No_Enclosing_Type()
        => EnclosingOptionsTypeFinder.Find(typeof(RootOptions)).ShouldBeEmpty();
}
