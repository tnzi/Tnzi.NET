namespace Tnzi.Tests.Extensions;

/// <summary>
/// 本项目同时全局导入 <c>System.Linq</c> 与 <c>Tnzi.Extensions</c>，与按文档配置的消费方一样。
/// <c>Tnzi.Extensions</c> 不能再声明与 LINQ 同签名的扩展方法：此前的 <c>DistinctBy</c> / <c>Shuffle</c>
/// 让这两个调用在这种文件里编译不过（CS0121 二义性调用）。这个文件能编译就是断言本身。
/// </summary>
public class LinqNameCollisionTests
{
    [Fact]
    public void DistinctBy_And_Shuffle_BindToLinq_WhenBothNamespacesAreImported()
    {
        int[] source = [1, 2, 3, 4, 5, 6];

        var distinct = source.DistinctBy(x => x % 3).ToArray();
        var shuffled = source.Shuffle().ToArray();

        Assert.Equal([1, 2, 3], distinct);
        Assert.Equal(source, shuffled.Order().ToArray());
    }
}
