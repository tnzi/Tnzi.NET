namespace Tnzi.AI.Rag.Options;

/// <summary>
/// AIRagOptions 配置验证器
/// </summary>
public class AIRagOptionsValidator : OptionsValidatorBase<AIRagOptions>
{
    protected override void ValidateOptions(AIRagOptions options, List<string> errors)
    {
        // 默认切块参数与知识库级参数同一条规则（ChunkSettings）：重叠超过半个块会让切块退化成逐字符前进
        var chunkSettingsError = ChunkSettings.Validate(options.DefaultChunkSize, options.DefaultChunkOverlap);
        if (chunkSettingsError != null)
        {
            AddError(errors, $"{nameof(options.DefaultChunkSize)}/{nameof(options.DefaultChunkOverlap)}", chunkSettingsError);
        }

        if (options.EmbeddingBatchSize <= 0)
        {
            AddError(errors, nameof(options.EmbeddingBatchSize), "must be greater than 0");
        }

        if (options.DefaultEmbeddingDimensions <= 0)
        {
            AddError(errors, nameof(options.DefaultEmbeddingDimensions), "must be greater than 0");
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
