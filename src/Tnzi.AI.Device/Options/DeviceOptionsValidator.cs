namespace Tnzi.AI.Device.Options;

/// <summary>
/// Validator for <see cref="DeviceOptions"/>
/// </summary>
public class DeviceOptionsValidator : OptionsValidatorBase<DeviceOptions>
{
    protected override void ValidateOptions(DeviceOptions options, List<string> errors)
    {
        if (options.PairingCodeLength < 4 || options.PairingCodeLength > 16)
        {
            errors.Add("PairingCodeLength must be between 4 and 16");
        }

        if (options.PairingCodeTtlMinutes < 1)
        {
            errors.Add("PairingCodeTtlMinutes must be at least 1");
        }

        if (options.MaxPendingPairings < 1)
        {
            errors.Add("MaxPendingPairings must be at least 1");
        }
    }
}
