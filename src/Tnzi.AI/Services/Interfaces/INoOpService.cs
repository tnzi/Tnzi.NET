
namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Marker interface for no-op fallback service implementations.
/// </summary>
/// <remarks>
/// <para>
/// AIModule 使用 TryAdd 注册回退实现（如 NoOpWorkflowService、NoOpTextSearchService），
/// 子模块（如 AIWorkflowModule、RagModule）加载后会替换为真实实现。
/// </para>
/// <para>
/// 启动校验通过此接口检查某个服务是否仍为回退实现，
/// 而无需引用具体的 NoOp 类名，保持核心与子模块解耦。
/// </para>
/// </remarks>
public interface INoOpService;
