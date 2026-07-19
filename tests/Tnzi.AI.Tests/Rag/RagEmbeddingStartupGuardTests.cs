using Microsoft.Extensions.AI;
using Tnzi.AI.Infrastructure.Providers;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Rag;

/// <summary>
/// C10: AIRagModule.OnApplicationInitializationAsync 启动探测 — 验证嵌入提供商不可解析时 LogWarning（不 throw），
/// 未配置 DefaultEmbeddingProvider 时静默跳过。
/// </summary>
public class RagEmbeddingStartupGuardTests
{
    [Fact]
    public async Task ChatOnlyProvider_NullEmbeddingGenerator_WarnsAndDoesNotThrow()
    {
        // chat-only provider（如 DeepSeek）→ GetEmbeddingGenerator 返回 null
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetEmbeddingGenerator(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((IEmbeddingGenerator<string, Embedding<float>>?)null);

        var (logger, context) = BuildContext(factory.Object, defaultEmbeddingProvider: "deepseek");

        var module = new AIRagModule();
        await Should.NotThrowAsync(() => module.OnApplicationInitializationAsync(context));

        logger.WarningCount.ShouldBe(1);
    }

    [Fact]
    public async Task EmbeddingGeneratorThrows_WarnsAndDoesNotThrow()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetEmbeddingGenerator(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new InvalidOperationException("provider not configured"));

        var (logger, context) = BuildContext(factory.Object, defaultEmbeddingProvider: "deepseek");

        var module = new AIRagModule();
        await Should.NotThrowAsync(() => module.OnApplicationInitializationAsync(context));

        logger.WarningCount.ShouldBe(1);
    }

    [Fact]
    public async Task UnsetDefaultEmbeddingProvider_SilentSkip_NoWarnNoThrow()
    {
        // DefaultEmbeddingProvider 未设置 → per-KB 配置有效 → 跳过探测（不解析 factory）
        var factory = new Mock<IChatClientFactory>(MockBehavior.Strict);

        var (logger, context) = BuildContext(factory.Object, defaultEmbeddingProvider: null);

        var module = new AIRagModule();
        await Should.NotThrowAsync(() => module.OnApplicationInitializationAsync(context));

        logger.WarningCount.ShouldBe(0);
        factory.Verify(
            f => f.GetEmbeddingGenerator(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolvableEmbeddingGenerator_NoWarnNoThrow()
    {
        var factory = new Mock<IChatClientFactory>();
        factory.Setup(f => f.GetEmbeddingGenerator(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Mock.Of<IEmbeddingGenerator<string, Embedding<float>>>());

        var (logger, context) = BuildContext(factory.Object, defaultEmbeddingProvider: "openai");

        var module = new AIRagModule();
        await Should.NotThrowAsync(() => module.OnApplicationInitializationAsync(context));

        logger.WarningCount.ShouldBe(0);
    }

    private static (CapturingLogger<AIRagModule> Logger, ApplicationInitializationContext Context) BuildContext(
        IChatClientFactory factory, string? defaultEmbeddingProvider)
    {
        var services = new ServiceCollection();

        var logger = new CapturingLogger<AIRagModule>();
        services.AddSingleton<ILogger<AIRagModule>>(logger);
        services.AddSingleton(factory);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AIRagOptions
        {
            DefaultEmbeddingProvider = defaultEmbeddingProvider,
            DefaultEmbeddingModel = "text-embedding-3-small"
        }));

        var provider = services.BuildServiceProvider();
        var context = new ApplicationInitializationContext(provider);
        return (logger, context);
    }

    /// <summary>
    /// 轻量日志捕获器 — 记录各级别日志条数，用于断言 LogWarning 行为
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }
}
