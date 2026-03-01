
namespace Tnzi.AI.Services;

/// <summary>
/// 嵌入服务实现 - 使用 MEAI IEmbeddingGenerator 生成文本向量
/// </summary>
public class EmbeddingService : ApplicationService, IEmbeddingService
{
    private readonly IChatClientFactory _clientFactory;

    public EmbeddingService(IChatClientFactory clientFactory, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _clientFactory = Check.NotNull(clientFactory);
    }

    /// <inheritdoc />
    public async Task<Result<float[]>> GenerateEmbeddingAsync(string text, EmbeddingOptions? options = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(text);

        try
        {
            var generator = _clientFactory.GetEmbeddingGenerator(options?.Provider, options?.Model);
            if (generator == null)
            {
                return Fail<float[]>("Embedding generator not available for the specified provider", 400, ErrorCodes.EmbeddingProviderNotFound);
            }

            var embeddings = await generator.GenerateAsync([text], cancellationToken: ct);
            return Ok(embeddings[0].Vector.ToArray());
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Embedding provider configuration error");
            return Fail<float[]>(ex.Message, 400, ErrorCodes.EmbeddingProviderNotFound);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Embedding generation failed for single text");
            return Fail<float[]>("Embedding generation failed.", 500, ErrorCodes.EmbeddingFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<float[]>>> GenerateEmbeddingsAsync(List<string> texts, EmbeddingOptions? options = null, CancellationToken ct = default)
    {
        Check.NotNullOrEmpty(texts);

        try
        {
            var generator = _clientFactory.GetEmbeddingGenerator(options?.Provider, options?.Model);
            if (generator == null)
            {
                return Fail<List<float[]>>("Embedding generator not available for the specified provider", 400, ErrorCodes.EmbeddingProviderNotFound);
            }

            var embeddings = await generator.GenerateAsync(texts, cancellationToken: ct);

            var result = new List<float[]>(embeddings.Count);
            foreach (var embedding in embeddings)
            {
                result.Add(embedding.Vector.ToArray());
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Embedding provider configuration error");
            return Fail<List<float[]>>(ex.Message, 400, ErrorCodes.EmbeddingProviderNotFound);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Embedding generation failed for {Count} texts", texts.Count);
            return Fail<List<float[]>>("Embedding generation failed.", 500, ErrorCodes.EmbeddingFailed);
        }
    }
}
