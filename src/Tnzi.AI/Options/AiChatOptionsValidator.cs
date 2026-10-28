namespace Tnzi.AI.Options;

/// <summary>
/// <see cref="AiChatOptions"/> 验证器
/// </summary>
public class AiChatOptionsValidator : OptionsValidatorBase<AiChatOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(AiChatOptions options, List<string> errors)
    {
        if (options.Attachments is null)
        {
            errors.Add("AI:Chat:Attachments must not be null.");
            return;
        }

        if (options.Attachments.MaxBytes <= 0)
        {
            errors.Add("AI:Chat:Attachments:MaxBytes must be greater than 0.");
        }

        if (options.Attachments.MaxCount <= 0)
        {
            errors.Add("AI:Chat:Attachments:MaxCount must be greater than 0.");
        }

        if (options.Metadata is null)
        {
            errors.Add("AI:Chat:Metadata must not be null.");
        }
        else
        {
            if (options.Metadata.MaxKeys <= 0)
            {
                errors.Add("AI:Chat:Metadata:MaxKeys must be greater than 0.");
            }

            if (options.Metadata.MaxBytes is <= 0 or > 65536)
            {
                errors.Add("AI:Chat:Metadata:MaxBytes must be between 1 and 65536.");
            }
        }

        if (options.AllowedAgentIds?.Any(id => id == Guid.Empty) == true)
        {
            errors.Add("AI:Chat:AllowedAgentIds must not contain an empty GUID.");
        }

        if (options.Models is { Count: > 0 } models)
        {
            if (models.Any(m => m is null || string.IsNullOrWhiteSpace(m.Id)))
            {
                errors.Add("AI:Chat:Models: every model needs an Id.");
            }

            var duplicates = models
                .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id))
                .GroupBy(m => m.Id.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                errors.Add($"AI:Chat:Models lists the same model more than once: {string.Join(", ", duplicates)}.");
            }
        }
    }
}
