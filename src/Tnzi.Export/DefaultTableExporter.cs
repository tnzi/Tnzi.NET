using System.Data;
using Tnzi.DependencyInjection;
using Tnzi.Export.Abstractions;
using Tnzi.Export.Exporters;
using Tnzi.Export.Models;

namespace Tnzi.Export;

public sealed class DefaultTableExporter : ITableExporter, ISingletonDependency
{
    private readonly CsvExporter _csv = new();
    private readonly XlsxExporter _xlsx = new();
    private readonly DocxExporter _docx = new();
    private readonly PdfTableExporter _pdf = new();

    public Task<Stream> ExportAsync(DataTable table, ExportFormat format, CancellationToken ct = default) => format switch
    {
        ExportFormat.Csv => _csv.ExportAsync(table, ct),
        ExportFormat.Xlsx => _xlsx.ExportAsync(table, ct),
        ExportFormat.Docx => _docx.ExportAsync(table, ct),
        ExportFormat.Pdf => _pdf.ExportAsync(table, ct),
        _ => throw new NotSupportedException($"Format {format} not supported")
    };
}
