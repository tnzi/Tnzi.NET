using System.Text.Json;
using Tnzi.AI.Tools;

namespace Tnzi.AI.Office.Tools;

/// <summary>
/// AI tool provider for creating and mutating Office files (XLSX, DOCX).
/// Phase C wires these tools to Tnzi Storage for file persistence.
/// </summary>
[AIToolGroup("office", "Office Files", "Create and modify Office documents (XLSX, DOCX)")]
public sealed class OfficeFileTools : IAIToolProvider
{
    private readonly IEnumerable<IOfficeFileCreator> _creators;
    private readonly IEnumerable<IOfficeFileMutator> _mutators;
    private readonly ILogger<OfficeFileTools> _logger;

    public OfficeFileTools(
        IEnumerable<IOfficeFileCreator> creators,
        IEnumerable<IOfficeFileMutator> mutators,
        ILogger<OfficeFileTools> logger)
    {
        _creators = creators;
        _mutators = mutators;
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// Create an Office file from structured content.
    /// Returns the file bytes as Base64. Phase C will integrate with Tnzi Storage.
    /// </summary>
    [AIFunction("create_office_file",
        """
        Create an Excel (.xlsx) or Word (.docx) file from structured content.
        Provide sheets as JSON: [{"name":"Sheet1","headers":["A","B"],"rows":[["v1","v2"]]}]
        Returns base64-encoded file bytes.
        """,
        IsConcurrencySafe = true,
        IsReadOnly = true,
        SearchHint = "create excel xlsx word docx office file spreadsheet document")]
    public async Task<object> CreateFileAsync(
        [AIParameter("file_name", "Output file name, e.g. 'report.xlsx'")] string fileName,
        [AIParameter("file_type", "File extension: 'xlsx' or 'docx'")] string fileType,
        [AIParameter("sheets_json", "JSON array of sheet specs: [{\"name\":\"...\",\"headers\":[...],\"rows\":[[...]]}]")] string sheetsJson,
        CancellationToken cancellationToken = default)
    {
        var ext = NormalizeExtension(fileType);

        var creator = _creators.FirstOrDefault(c => c.SupportedExtensions.Contains(ext));
        if (creator is null)
            return new { error = $"Unsupported file type '{fileType}'. Supported: xlsx, docx." };

        IReadOnlyList<OfficeSheetSpec> sheets;
        try
        {
            sheets = ParseSheets(sheetsJson);
        }
        catch (Exception ex)
        {
            return new { error = $"Invalid sheets_json: {ex.Message}" };
        }

        var spec = new OfficeFileSpec(fileName, ext, sheets);

        try
        {
            var stream = await creator.CreateAsync(spec, cancellationToken);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, cancellationToken);
            var base64 = Convert.ToBase64String(ms.ToArray());

            _logger.LogDebug("Created office file '{FileName}' ({Bytes} bytes)", fileName, ms.Length);

            return new
            {
                file_name = fileName,
                file_type = ext,
                size_bytes = ms.Length,
                base64_content = base64,
                _note = "File created. Phase C will integrate with Tnzi Storage for direct download URLs.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create office file '{FileName}'", fileName);
            return new { error = $"File creation failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Apply mutations to an existing Office file provided as Base64.
    /// Returns the mutated file as Base64. Phase C will integrate with Tnzi Storage.
    /// </summary>
    [AIFunction("mutate_office_file",
        """
        Apply mutations to an existing Office file (xlsx/docx).
        Provide the original file as base64, and mutations as JSON.
        Supported mutations:
          {"type":"set_cell","sheet":"Sheet1","row":2,"col":1,"value":"New"}
          {"type":"append_row","sheet":"Sheet1","values":["a","b"]}
          {"type":"replace_text","find":"OldText","replace":"NewText"}
        Returns base64-encoded mutated file bytes.
        """,
        IsConcurrencySafe = true,
        IsReadOnly = false,
        SearchHint = "edit modify update excel xlsx word docx office file cell")]
    public async Task<object> MutateFileAsync(
        [AIParameter("file_type", "File extension: 'xlsx' or 'docx'")] string fileType,
        [AIParameter("base64_content", "Base64-encoded original file bytes")] string base64Content,
        [AIParameter("mutations_json", "JSON array of mutation objects")] string mutationsJson,
        CancellationToken cancellationToken = default)
    {
        var ext = NormalizeExtension(fileType);

        var mutator = _mutators.FirstOrDefault(m => m.SupportedExtensions.Contains(ext));
        if (mutator is null)
            return new { error = $"Unsupported file type '{fileType}'. Supported: xlsx, docx." };

        byte[] fileBytes;
        try
        {
            fileBytes = Convert.FromBase64String(base64Content);
        }
        catch
        {
            return new { error = "Invalid base64_content. Must be valid Base64-encoded file bytes." };
        }

        OfficeMutationSpec spec;
        try
        {
            spec = ParseMutations(mutationsJson);
        }
        catch (Exception ex)
        {
            return new { error = $"Invalid mutations_json: {ex.Message}" };
        }

        try
        {
            using var inputStream = new MemoryStream(fileBytes);
            var outputStream = await mutator.MutateAsync(inputStream, spec, cancellationToken);
            using var ms = new MemoryStream();
            await outputStream.CopyToAsync(ms, cancellationToken);
            var base64Out = Convert.ToBase64String(ms.ToArray());

            return new
            {
                file_type = ext,
                size_bytes = ms.Length,
                base64_content = base64Out,
                _note = "File mutated. Phase C will integrate with Tnzi Storage.",
            };
        }
        catch (NotSupportedException ex)
        {
            return new { error = ex.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mutate office file (type: {FileType})", ext);
            return new { error = $"File mutation failed: {ex.Message}" };
        }
    }

    private static string NormalizeExtension(string fileType)
    {
        var ext = fileType.TrimStart('.').ToLowerInvariant();
        return "." + ext;
    }

    private static IReadOnlyList<OfficeSheetSpec> ParseSheets(string json)
    {
        var raw = JsonSerializer.Deserialize<JsonElement[]>(json)
            ?? throw new JsonException("sheets_json must be a JSON array.");

        return raw.Select(el =>
        {
            var name = el.GetProperty("name").GetString() ?? "Sheet1";
            var headers = el.GetProperty("headers").EnumerateArray()
                .Select(h => h.GetString() ?? "")
                .ToList();
            var rows = el.GetProperty("rows").EnumerateArray()
                .Select(row => (IReadOnlyList<object?>)row.EnumerateArray()
                    .Select(cell => (object?)cell.GetString())
                    .ToList())
                .ToList();
            return new OfficeSheetSpec(name, headers, rows);
        }).ToList();
    }

    private static OfficeMutationSpec ParseMutations(string json)
    {
        var raw = JsonSerializer.Deserialize<JsonElement[]>(json)
            ?? throw new JsonException("mutations_json must be a JSON array.");

        var mutations = raw.Select<JsonElement, OfficeMutation>(el =>
        {
            var type = el.GetProperty("type").GetString()
                ?? throw new JsonException("Each mutation must have a 'type' field.");

            return type switch
            {
                "set_cell" => new SetCellMutation(
                    Sheet: el.GetProperty("sheet").GetString()!,
                    Row: el.GetProperty("row").GetInt32(),
                    Column: el.GetProperty("col").GetInt32(),
                    Value: el.TryGetProperty("value", out var v) ? v.GetString() : null),

                "append_row" => new AppendRowMutation(
                    Sheet: el.GetProperty("sheet").GetString()!,
                    Values: el.GetProperty("values").EnumerateArray()
                        .Select(v => (object?)v.GetString())
                        .ToList()),

                "replace_text" => new ReplaceTextMutation(
                    Find: el.GetProperty("find").GetString()!,
                    Replace: el.GetProperty("replace").GetString()!),

                _ => throw new JsonException($"Unknown mutation type '{type}'.")
            };
        }).ToList();

        return new OfficeMutationSpec(mutations);
    }
}
