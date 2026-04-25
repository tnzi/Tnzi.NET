namespace Tnzi.AI.Infrastructure.Helpers;

public static class StringTruncator
{
    public static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value ?? string.Empty;
        return value[..maxLength] + "...";
    }
}
