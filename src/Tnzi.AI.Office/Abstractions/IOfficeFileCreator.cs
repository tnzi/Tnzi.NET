namespace Tnzi.AI.Office;

/// <summary>
/// Creates an Office file (DOCX/XLSX) from a structured specification.
/// One implementation per supported output format.
/// </summary>
public interface IOfficeFileCreator
{
    string[] SupportedExtensions { get; }

    Task<Stream> CreateAsync(OfficeFileSpec spec, CancellationToken ct = default);
}

public sealed record OfficeFileSpec(
    string FileName,
    string Extension,
    IReadOnlyList<OfficeSheetSpec> Sheets,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record OfficeSheetSpec(
    string Name,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows);
