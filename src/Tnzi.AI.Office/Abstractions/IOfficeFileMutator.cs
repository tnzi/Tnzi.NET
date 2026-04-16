namespace Tnzi.AI.Office;

/// <summary>
/// Mutates an existing Office file (DOCX/XLSX) in place.
/// One implementation per supported output format.
/// </summary>
public interface IOfficeFileMutator
{
    string[] SupportedExtensions { get; }

    Task<Stream> MutateAsync(Stream input, OfficeMutationSpec spec, CancellationToken ct = default);
}

public sealed record OfficeMutationSpec(IReadOnlyList<OfficeMutation> Mutations);

public abstract record OfficeMutation;

/// <summary>Set a cell value at a specific (1-based) row and column in the named sheet.</summary>
public sealed record SetCellMutation(string Sheet, int Row, int Column, object? Value) : OfficeMutation;

/// <summary>Append a new row of values to the named sheet.</summary>
public sealed record AppendRowMutation(string Sheet, IReadOnlyList<object?> Values) : OfficeMutation;

/// <summary>Replace all occurrences of a text string across the document.</summary>
public sealed record ReplaceTextMutation(string Find, string Replace) : OfficeMutation;
