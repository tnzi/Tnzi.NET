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

        if (options.Attachments.MaxFileBytes <= 0)
        {
            errors.Add("AI:Chat:Attachments:MaxFileBytes must be greater than 0.");
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

        if (options.AllowedThreadOrigins?.FirstOrDefault(origin => !ThreadOrigin.IsWellFormed(origin)) is { } badOrigin)
        {
            errors.Add($"AI:Chat:AllowedThreadOrigins contains '{badOrigin}', which is not a well-formed thread origin " +
                       $"(a letter, then letters, digits, '_', '.', ':' or '-', at most {ThreadOrigin.MaxLength} characters).");
        }

        if (options.Queue is null)
        {
            errors.Add("AI:Chat:Queue must not be null.");
        }
        else
        {
            if (options.Queue.MaxMessagesPerThread <= 0)
            {
                errors.Add("AI:Chat:Queue:MaxMessagesPerThread must be greater than 0.");
            }

            if (options.Queue.MaxTextLength <= 0)
            {
                errors.Add("AI:Chat:Queue:MaxTextLength must be greater than 0.");
            }

            if (options.Queue.SweepInterval < TimeSpan.FromSeconds(1))
            {
                errors.Add("AI:Chat:Queue:SweepInterval must be at least 1 second.");
            }

            if (options.Queue.StrandedAfter <= TimeSpan.Zero)
            {
                errors.Add("AI:Chat:Queue:StrandedAfter must be greater than 0.");
            }

            if (options.Queue.DeliveringTimeout <= TimeSpan.Zero)
            {
                errors.Add("AI:Chat:Queue:DeliveringTimeout must be greater than 0.");
            }

            foreach (var (key, ranking) in options.Queue.StrictestValues ?? [])
            {
                if (ranking is not { Count: > 0 } || ranking.Any(string.IsNullOrEmpty))
                {
                    errors.Add($"AI:Chat:Queue:StrictestValues:{key} must list at least one value, none of them empty.");
                }
                else if (ranking.Distinct(StringComparer.Ordinal).Count() != ranking.Count)
                {
                    errors.Add($"AI:Chat:Queue:StrictestValues:{key} lists the same value more than once.");
                }
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

            foreach (var model in models.Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Id)))
            {
                if (ChatModelEfforts.Validate(model.Efforts, model.DefaultEffort) is { } invalid)
                {
                    errors.Add($"AI:Chat:Models '{model.Id.Trim()}': {invalid}");
                }
            }
        }
    }
}
