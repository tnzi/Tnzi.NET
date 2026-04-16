using System.Data;
using System.Text;

namespace Tnzi.Export.Exporters;

public sealed class CsvExporter
{
    public async Task<Stream> ExportAsync(DataTable table, CancellationToken ct = default)
    {
        var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true))
        {
            for (int i = 0; i < table.Columns.Count; i++)
            {
                if (i > 0) await writer.WriteAsync(",");
                await writer.WriteAsync(Escape(table.Columns[i].ColumnName));
            }
            await writer.WriteLineAsync();
            foreach (DataRow row in table.Rows)
            {
                ct.ThrowIfCancellationRequested();
                for (int i = 0; i < table.Columns.Count; i++)
                {
                    if (i > 0) await writer.WriteAsync(",");
                    await writer.WriteAsync(Escape(row[i]?.ToString() ?? ""));
                }
                await writer.WriteLineAsync();
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static string Escape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
