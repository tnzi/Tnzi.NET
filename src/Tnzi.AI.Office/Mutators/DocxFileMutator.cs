using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Tnzi.AI.Office.Mutators;

/// <summary>
/// Mutates .docx documents by applying <see cref="ReplaceTextMutation"/> operations.
/// <see cref="SetCellMutation"/> and <see cref="AppendRowMutation"/> are not supported for DOCX
/// (cell-based mutations are an Excel concept).
/// Returns a new stream; the original input stream is not modified.
/// </summary>
public sealed class DocxFileMutator : IOfficeFileMutator
{
    public string[] SupportedExtensions => new[] { ".docx" };

    public Task<Stream> MutateAsync(Stream input, OfficeMutationSpec spec, CancellationToken ct = default)
    {
        // Copy input to a writable stream — WordprocessingDocument.Open requires a seekable, writable stream
        var output = new MemoryStream();
        input.CopyTo(output);
        output.Position = 0;

        using (var doc = WordprocessingDocument.Open(output, isEditable: true))
        {
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidOperationException("DOCX document has no body.");

            foreach (var mutation in spec.Mutations)
            {
                switch (mutation)
                {
                    case ReplaceTextMutation replace:
                        ApplyReplaceText(body, replace);
                        break;

                    case SetCellMutation:
                    case AppendRowMutation:
                        throw new NotSupportedException(
                            $"{mutation.GetType().Name} is not supported for DOCX files. " +
                            "Cell/row mutations are only available for XLSX.");
                }
            }

            doc.MainDocumentPart!.Document.Save();
        }

        output.Position = 0;
        return Task.FromResult<Stream>(output);
    }

    private static void ApplyReplaceText(Body body, ReplaceTextMutation mutation)
    {
        // Walk all Text elements and do a simple in-place replacement.
        // Note: text may be split across multiple runs; this handles single-run occurrences.
        foreach (var text in body.Descendants<Text>())
        {
            if (text.Text.Contains(mutation.Find))
                text.Text = text.Text.Replace(mutation.Find, mutation.Replace);
        }
    }
}
