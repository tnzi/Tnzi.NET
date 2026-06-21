using Tnzi.AI.Options;
using Tnzi.AI.Rag.Chunking;
using Tnzi.AI.Rag.Graph;
using Tnzi.AI.Rag.Options;
using Tnzi.AI.Rag.Services;
using Tnzi.AI.Services.Interfaces;
using Tnzi.Results;

namespace Tnzi.AI.Rag.Tests;

/// <summary>
/// <see cref="DocumentIngestionService"/> 的重试幂等性测试。
/// <para>
/// chunk 在文档状态回写之前就已提交；Hangfire 重试时若不先删除旧块会重复插入整套块。
/// 此处验证 <c>IngestAsync</c> 在批量插入前先按 <c>DocumentId</c> 删除已有块（替换语义）。
/// </para>
/// </summary>
public class DocumentIngestionServiceIdempotencyTests
{
    [Fact]
    public async Task IngestAsync_Success_DeletesExistingChunksBeforeInserting()
    {
        var kbId = Guid.NewGuid();
        var docId = Guid.NewGuid();

        var extractor = new Mock<IFileExtractorService>();
        extractor.Setup(e => e.Supports(It.IsAny<string>())).Returns(true);
        extractor.Setup(e => e.ExtractTextAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("some text content");

        var chunking = new Mock<IChunkingStrategy>();
        chunking.Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(["chunk a", "chunk b"]);

        var embedding = new Mock<IEmbeddingService>();
        embedding.Setup(s => s.GenerateEmbeddingsAsync(It.IsAny<List<string>>(), It.IsAny<EmbeddingOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<List<float[]>>.Success([new float[] { 0.1f }, new float[] { 0.2f }]));

        var chunkRepo = new Mock<IRepository<DocumentChunk, Guid>>();
        var kbRepo = new Mock<IRepository<KnowledgeBase, Guid>>();
        kbRepo.Setup(r => r.AsQueryable(It.IsAny<bool>()))
            .Returns(new List<KnowledgeBase>
            {
                new() { Id = kbId, Name = "KB", EmbeddingProvider = "default", ChunkSize = 512, ChunkOverlap = 64 }
            }.BuildMock());

        var graph = new Mock<IGraphExtractor>();
        var options = Microsoft.Extensions.Options.Options.Create(new AIRagOptions());

        var callOrder = new List<string>();
        chunkRepo.Setup(r => r.DeleteAsync(It.IsAny<System.Linq.Expressions.Expression<Func<DocumentChunk, bool>>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("delete"))
            .Returns(Task.CompletedTask);
        chunkRepo.Setup(r => r.InsertManyAsync(It.IsAny<IEnumerable<DocumentChunk>>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("insert"))
            .Returns(Task.CompletedTask);

        var sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        var service = new DocumentIngestionService(
            [extractor.Object],
            chunking.Object,
            embedding.Object,
            chunkRepo.Object,
            kbRepo.Object,
            graph.Object,
            options,
            sp);

        using var stream = new MemoryStream([1, 2, 3]);
        var result = await service.IngestAsync(kbId, docId, stream, "doc.txt");

        result.Succeeded.ShouldBeTrue();
        result.ChunkCount.ShouldBe(2);

        // 删除旧块必须在插入新块之前发生（替换而非叠加）
        callOrder.ShouldBe(["delete", "insert"]);

        // 删除是按本文档 DocumentId 过滤的
        chunkRepo.Verify(r => r.DeleteAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<DocumentChunk, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
