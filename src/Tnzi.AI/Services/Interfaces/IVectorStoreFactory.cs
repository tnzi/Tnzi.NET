
namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 向量存储工厂接口 - 用于创建 VectorStore 实例
/// </summary>
/// <remarks>
/// <para>
/// 定义向量存储的标准工厂接口，供 TnziChatHistoryMemoryProvider 使用。
/// 用户需要实现此接口来连接到具体的向量存储（如 Redis、Qdrant、Pinecone、InMemory 等）。
/// </para>
/// <para>
/// 框架不提供默认实现，因为向量存储的选择取决于用户的基础设施。
/// 如果未注册此接口的实现，ChatHistoryMemoryProvider 将不会被启用。
/// </para>
/// <para>
/// 使用示例：
/// <code>
/// // 使用 Redis 向量存储
/// services.AddSingleton&lt;IVectorStoreFactory, RedisVectorStoreFactory&gt;();
///
/// // 使用 Qdrant 向量存储
/// services.AddSingleton&lt;IVectorStoreFactory, QdrantVectorStoreFactory&gt;();
///
/// // 使用内存向量存储（仅用于开发/测试）
/// services.AddSingleton&lt;IVectorStoreFactory, InMemoryVectorStoreFactory&gt;();
/// </code>
/// </para>
/// </remarks>
public interface IVectorStoreFactory
{
    /// <summary>
    /// 创建 VectorStore 实例
    /// </summary>
    /// <returns>VectorStore 实例</returns>
    VectorStore CreateVectorStore();
}