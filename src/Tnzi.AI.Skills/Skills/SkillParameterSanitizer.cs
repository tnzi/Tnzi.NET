namespace Tnzi.AI.Skills;

/// <summary>
/// Structural sanitization for user-supplied skill parameter values prior to
/// template substitution into a trusted SKILL.md body.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally narrow in scope. It protects against the
/// <i>structural</i> family of prompt-injection attacks - chat-template
/// control tokens, system/user role markers, stand-alone Markdown/YAML
/// separators, and control characters - that arise specifically because a
/// parameter value gets spliced into a system prompt at render time.
/// </para>
/// <para>
/// Semantic prompt-injection ("ignore previous instructions", "you are now
/// ...") is handled at a different layer by <c>PromptInjectionGuardrail</c>
/// in <c>InputGuardrailMiddleware</c>, which inspects the user's free-form
/// message rather than a skill's short structured parameter. The two layers
/// are complementary; both fire on real requests.
/// </para>
/// <para>
/// <see cref="DefaultMaxParameterLength"/> additionally caps each value to
/// prevent a malicious caller from blowing up the rendered prompt or the
/// downstream cache.
/// </para>
/// </remarks>
public static partial class SkillParameterSanitizer
{
    /// <summary>
    /// Maximum length of any single parameter value (characters). Skill
    /// parameters are expected to be short structured tokens (a language
    /// name, a severity level, a section title), not free-form prose.
    /// </summary>
    public const int DefaultMaxParameterLength = 4096;

    /// <summary>
    /// Chat-template / role-marker tokens used by major LLM tokenizers.
    /// If any of these appears verbatim in a parameter value the renderer
    /// rejects the activation rather than rendering a poisoned prompt.
    /// </summary>
    private static readonly string[] DeniedSubstrings =
    [
        "<|im_start|>",
        "<|im_end|>",
        "<|system|>",
        "<|user|>",
        "<|assistant|>",
        "<|tool|>",
        "<|function|>",
        "<|endoftext|>",
        "[INST]",
        "[/INST]",
        "<<SYS>>",
        "<</SYS>>",
    ];

    // Lines consisting solely of `---` are commonly interpreted by LLMs as
    // YAML / Markdown / system-block separators and can be used to splice
    // a fake system block into the prompt.
    [GeneratedRegex(@"(?:^|\r?\n)\s*-{3,}\s*(?:\r?\n|$)")]
    private static partial Regex StandaloneSeparatorRegex();

    // C0 and DEL control characters except for the printable-text essentials
    // (\t \n \r). \x09=TAB, \x0A=LF, \x0D=CR are allowed; everything else in
    // 0x00..0x1F and 0x7F is disallowed.
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")]
    private static partial Regex DisallowedControlCharsRegex();

    /// <summary>
    /// Inspects <paramref name="value"/> and returns either an Allow result
    /// (carrying the original value verbatim) or a Reject result with a
    /// human-readable reason suitable for surfacing through
    /// <c>SkillRenderResult.Errors</c>.
    /// </summary>
    public static SkillParameterSanitizationResult Sanitize(
        string parameterName, string? value, int? maxLength = null)
    {
        if (value is null)
            return SkillParameterSanitizationResult.Allow(string.Empty);

        var limit = maxLength ?? DefaultMaxParameterLength;
        if (limit > 0 && value.Length > limit)
        {
            return SkillParameterSanitizationResult.Reject(
                $"Parameter '{parameterName}' exceeds the maximum length of {limit} characters ({value.Length}).");
        }

        if (StandaloneSeparatorRegex().IsMatch(value))
        {
            return SkillParameterSanitizationResult.Reject(
                $"Parameter '{parameterName}' contains a stand-alone separator line ('---') which can break out of the system prompt block.");
        }

        foreach (var denied in DeniedSubstrings)
        {
            if (value.Contains(denied, StringComparison.OrdinalIgnoreCase))
            {
                return SkillParameterSanitizationResult.Reject(
                    $"Parameter '{parameterName}' contains the disallowed chat-template token '{denied}'.");
            }
        }

        if (DisallowedControlCharsRegex().IsMatch(value))
        {
            return SkillParameterSanitizationResult.Reject(
                $"Parameter '{parameterName}' contains a disallowed control character.");
        }

        return SkillParameterSanitizationResult.Allow(value);
    }
}

/// <summary>
/// Outcome of a single <see cref="SkillParameterSanitizer.Sanitize"/> call.
/// </summary>
public sealed class SkillParameterSanitizationResult
{
    /// <summary><c>true</c> when the parameter value is safe to splice into the rendered prompt.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>Rejection message; non-null only when <see cref="IsAllowed"/> is false.</summary>
    public string? Reason { get; init; }

    /// <summary>Value to use during rendering (currently identical to the input on Allow).</summary>
    public string Value { get; init; } = string.Empty;

    public static SkillParameterSanitizationResult Allow(string value)
        => new() { IsAllowed = true, Value = value };

    public static SkillParameterSanitizationResult Reject(string reason)
        => new() { IsAllowed = false, Reason = reason };
}
