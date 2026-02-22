
namespace Tnzi.AI.Services;

/// <summary>
/// 嵌入服务实现 - 使用 OpenAI SDK 的 EmbeddingClient 生成文本向量
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
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail<float[]>("Text cannot be null or empty", 400, ErrorCodes.EmbeddingFailed);
        }

        try
        {
            var client = _clientFactory.GetEmbeddingClient(options?.Provider, options?.Model);
            var response = await client.GenerateEmbeddingAsync(text, cancellationToken: ct);

            return Ok(response.Value.ToFloats().ToArray());
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "Embedding provider configuration error");
            return Fail<float[]>(ex.Message, 400, ErrorCodes.EmbeddingProviderNotFound);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Embedding generation failed for single text");
            return Fail<float[]>($"Embedding generation failed: {ex.Message}", 500, ErrorCodes.EmbeddingFailed);
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<float[]>>> GenerateEmbeddingsAsync(List<string> texts, EmbeddingOptions? options = null, CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return Fail<List<float[]>>("Texts cannot be null or empty", 400, ErrorCodes.EmbeddingFailed);
        }

        try
        {
            var client = _clientFactory.GetEmbeddingClient(options?.Provider, options?.Model);
            var response = await client.GenerateEmbeddingsAsync(texts, cancellationToken: ct);

            var result = new List<float[]>(response.Value.Count);
            foreach (var embedding in response.Value)
            {
                result.Add(embedding.ToFloats().ToArray());
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
            return Fail<List<float[]>>($"Embedding generation failed: {ex.Message}", 500, ErrorCodes.EmbeddingFailed);
        }
    }
}
