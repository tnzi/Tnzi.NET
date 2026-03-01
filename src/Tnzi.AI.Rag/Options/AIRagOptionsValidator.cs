namespace Tnzi.AI.Rag.Options;

/// <summary>
/// AIRagOptions 配置验证器
/// </summary>
public class AIRagOptionsValidator : OptionsValidatorBase<AIRagOptions>
{
    protected override void ValidateOptions(AIRagOptions options, List<string> errors)
    {
        if (options.DefaultChunkSize <= 0)
        {
            AddError(errors, nameof(options.DefaultChunkSize), "must be greater than 0");
        }

        if (options.EmbeddingBatchSize <= 0)
        {
            AddError(errors, nameof(options.EmbeddingBatchSize), "must be greater than 0");
        }

        if (options.DefaultEmbeddingDimensions <= 0)
        {
            AddError(errors, nameof(options.DefaultEmbeddingDimensions), "must be greater than 0");
        }

        if (options.DefaultChunkOverlap < 0)
        {
            AddError(errors, nameof(options.DefaultChunkOverlap), "must be greater than or equal to 0");
        }

        if (options.DefaultChunkOverlap >= options.DefaultChunkSize)
        {
            AddError(errors, nameof(options.DefaultChunkOverlap), "must be less than DefaultChunkSize");
        }

        if (options.MaxTopK <= 0)
        {
            AddError(errors, nameof(options.MaxTopK), "must be greater than 0");
        }

        if (string.IsNullOrWhiteSpace(options.TableNamePrefix))
        {
            AddError(errors, nameof(options.TableNamePrefix), "must not be empty");
        }
    }
}
