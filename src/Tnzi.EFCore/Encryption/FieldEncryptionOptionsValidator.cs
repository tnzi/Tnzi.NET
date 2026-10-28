namespace Tnzi.EFCore.Encryption;

/// <summary>
/// <see cref="FieldEncryptionOptions"/> 的启动期验证器。
/// </summary>
/// <remarks>
/// 配置错误在这里 fail-fast,而不是等到第一条数据要落库时才炸。
/// 加密配置写错的代价是数据写不进去或读不出来,越早暴露越好。
/// 配置了 <see cref="FieldEncryptionOptions.KeyFile"/> 时,文件在这里被真的读一遍:缺失或格式错误即启动失败。
/// </remarks>
public class FieldEncryptionOptionsValidator : OptionsValidatorBase<FieldEncryptionOptions>
{
    /// <inheritdoc />
    protected override void ValidateOptions(FieldEncryptionOptions options, List<string> errors)
    {
        // 未启用时不校验密钥环:允许配置里留着空壳节点。
        if (!options.Enabled)
        {
            return;
        }

        var ring = new Dictionary<string, string>(options.Keys);
        string? fileActiveKeyId = null;

        if (!string.IsNullOrWhiteSpace(options.KeyFile))
        {
            if (FieldEncryptionKeyFile.TryLoad(options.KeyFile, out var file, out var fileErrors))
            {
                fileActiveKeyId = file.ActiveKeyId;
                foreach (var (keyId, material) in file.Keys)
                {
                    if (!ring.TryAdd(keyId, material))
                    {
                        AddError(errors, nameof(options.KeyFile), $"key id '{keyId}' is defined both in Keys and in the key file.");
                    }
                }
            }
            else
            {
                foreach (var error in fileErrors)
                {
                    AddError(errors, nameof(options.KeyFile), error);
                }
            }
        }

        if (ring.Count == 0)
        {
            if (errors.Count == 0)
            {
                AddError(errors, nameof(options.Keys), "must contain at least one key (in Keys or in KeyFile) when field encryption is enabled.");
            }

            return;
        }

        var activeKeyId = string.IsNullOrWhiteSpace(options.ActiveKeyId) ? fileActiveKeyId : options.ActiveKeyId;
        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            AddError(errors, nameof(options.ActiveKeyId), "is required when field encryption is enabled (set it in configuration or as activeKeyId in the key file).");
        }
        else if (!ring.ContainsKey(activeKeyId))
        {
            AddError(errors, nameof(options.ActiveKeyId), $"'{activeKeyId}' is not present in the configured key ring.");
        }

        // 文件里的密钥已在加载时校验过,这里只校验配置里的那部分。
        FieldEncryptionKeyValidation.ValidateKeys(options.Keys, message => AddError(errors, nameof(options.Keys), message));
    }
}
