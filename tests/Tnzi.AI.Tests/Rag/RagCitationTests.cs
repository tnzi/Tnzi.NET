using Tnzi.AI.Rag.Dtos;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// 引用摘录（<see cref="RagRetriever.BuildCitations"/>）截到 <c>maxContentLength</c> 个码元。
/// </summary>
public class RagCitationTests
{
    [Fact]
    public void BuildCitations_LongContent_IsCutAndMarked()
    {
        var citations = RagRetriever.BuildCitations(
            [new RetrievalResult { Content = "0123456789abcdef", Score = 0.9 }], maxContentLength: 10);

        citations.ShouldHaveSingleItem().Text.ShouldBe("0123456789...");
    }

    /// <summary>截断点落在代理对中间时整个字符一起舍掉：引用随回答交给前端显示，半个 emoji 是一个替换字符。</summary>
    [Fact]
    public void BuildCitations_CutInsideASurrogatePair_DropsTheWholeCharacter()
    {
        // 第 11 个码元是 emoji 的前半个
        var citations = RagRetriever.BuildCitations(
            [new RetrievalResult { Content = "0123456789\U0001F600tail", Score = 0.9 }], maxContentLength: 11);

        citations.ShouldHaveSingleItem().Text.ShouldBe("0123456789...");
    }
}
