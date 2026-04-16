using System.Data;
using Tnzi.Export.Models;

namespace Tnzi.Export.Abstractions;

/// <summary>
/// Exports a <see cref="DataTable"/> to one of several formats (CSV/XLSX/DOCX/PDF).
/// Default implementation <see cref="DefaultTableExporter"/> routes based on
/// <see cref="ExportFormat"/>.
/// </summary>
public interface ITableExporter
{
    Task<Stream> ExportAsync(DataTable table, ExportFormat format, CancellationToken ct = default);
}
