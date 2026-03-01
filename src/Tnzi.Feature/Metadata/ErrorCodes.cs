namespace Tnzi.Feature.Metadata;

/// <summary>
/// Feature module error code constants.
/// </summary>
public static class ErrorCodes
{
    /// <summary>
    /// Feature definition not found.
    /// </summary>
    public const string FeatureDefinitionNotFound = "FEATURE_DEFINITION_NOT_FOUND";

    /// <summary>
    /// Feature definition already exists.
    /// </summary>
    public const string FeatureDefinitionAlreadyExists = "FEATURE_DEFINITION_ALREADY_EXISTS";

    /// <summary>
    /// Feature value not found.
    /// </summary>
    public const string FeatureValueNotFound = "FEATURE_VALUE_NOT_FOUND";

    /// <summary>
    /// Feature value already exists.
    /// </summary>
    public const string FeatureValueAlreadyExists = "FEATURE_VALUE_ALREADY_EXISTS";

    /// <summary>
    /// Feature is disabled.
    /// </summary>
    public const string FeatureDisabled = "FEATURE_DISABLED";

    /// <summary>
    /// Invalid feature value type.
    /// </summary>
    public const string InvalidFeatureValueType = "FEATURE_INVALID_VALUE_TYPE";
}
