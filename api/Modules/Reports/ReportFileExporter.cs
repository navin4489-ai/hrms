using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace Wintech.Hrms.Api.Modules.Reports;

internal enum ReportCellKind
{
    Text,
    Number,
    Boolean
}

internal sealed record ReportCell(string Text, ReportCellKind Kind = ReportCellKind.Text);

internal sealed record ReportTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<ReportCell>> Rows);

/// <summary>Safe CSV, Office Open XML workbook, and PDF exports for report rows.</summary>
internal static class ReportFileExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static ReportTable CreateTable(IReadOnlyList<object> rows)
    {
        var serialized = new List<JsonElement>(rows.Count);
        foreach (var row in rows)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(row, row.GetType(), JsonOptions));
            serialized.Add(document.RootElement.Clone());
        }

        if (serialized.Count == 0 || serialized[0].ValueKind != JsonValueKind.Object)
            return new ReportTable([], []);

        var columns = serialized[0].EnumerateObject().Select(property => property.Name).ToArray();
        var values = serialized.Select(row => (IReadOnlyList<ReportCell>)columns
            .Select(column => row.TryGetProperty(column, out var value) ? ToCell(value) : new ReportCell(string.Empty))
            .ToArray()).ToArray();
        return new ReportTable(columns, values);
    }

    public static byte[] Csv(ReportTable table)
    {
        var content = new StringBuilder();
        if (table.Columns.Count > 0)
        {
            content.AppendLine(string.Join(',', table.Columns.Select(CsvCell)));
            foreach (var row in table.Rows)
                content.AppendLine(string.Join(',', row.Select(cell => CsvCell(cell.Text))));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content.ToString())).ToArray();
    }

    public static byte[] Xlsx(ReportTable table)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddXml(archive, "[Content_Types].xml", new XDocument(
                new XElement(ContentTypes + "Types",
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"),
                        new XAttribute("ContentType", "application/xml")),
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                    new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));

            AddXml(archive, "_rels/.rels", new XDocument(
                new XElement(PackageRelationships + "Relationships",
                    new XElement(PackageRelationships + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml")))));

            AddXml(archive, "xl/workbook.xml", new XDocument(
                new XElement(Spreadsheet + "workbook",
                    new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
                    new XElement(Spreadsheet + "sheets",
                        new XElement(Spreadsheet + "sheet",
                            new XAttribute("name", "Report"),
                            new XAttribute("sheetId", 1),
                            new XAttribute(OfficeRelationships + "id", "rId1"))))));

            AddXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                new XElement(PackageRelationships + "Relationships",
                    new XElement(PackageRelationships + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                        new XAttribute("Target", "worksheets/sheet1.xml")))));

            var sheetRows = new List<XElement>();
            if (table.Columns.Count > 0)
            {
                sheetRows.Add(CreateRow(1, table.Columns.Select(column => new ReportCell(column)).ToArray()));
                for (var index = 0; index < table.Rows.Count; index++)
                    sheetRows.Add(CreateRow(index + 2, table.Rows[index]));
            }

            AddXml(archive, "xl/worksheets/sheet1.xml", new XDocument(
                new XElement(Spreadsheet + "worksheet",
                    new XElement(Spreadsheet + "sheetData", sheetRows))));
        }

        return output.ToArray();
    }

    public static byte[] Pdf(string reportTitle, ReportMeta meta, ReportTable table)
    {
        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Column(header =>
            {
                header.Item().Text(reportTitle).FontSize(18).Bold();
                header.Item().Text($"Generated {meta.GeneratedAt.ToUniversalTime():yyyy-MM-dd HH:mm} UTC by {meta.GeneratedBy}");
                header.Item().Text($"Period {meta.Filters.From:yyyy-MM-dd} to {meta.Filters.To:yyyy-MM-dd} · Scope: {meta.Scope}");
                if (meta.Filters.DepartmentId is { } departmentId)
                    header.Item().Text($"Department: {departmentId}");
                if (meta.Filters.LocationId is { } locationId)
                    header.Item().Text($"Location: {locationId}");
            });
            page.Content().PaddingTop(12).Column(content =>
            {
                if (table.Columns.Count == 0 || table.Rows.Count == 0)
                {
                    content.Item().Text("No report rows for the selected filters.");
                    return;
                }

                content.Item().Table(pdfTable =>
                {
                    pdfTable.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in table.Columns) columns.RelativeColumn();
                    });
                    pdfTable.Header(header =>
                    {
                        foreach (var column in table.Columns)
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(column).Bold();
                    });
                    foreach (var row in table.Rows)
                    foreach (var cell in row)
                        pdfTable.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(4).Text(cell.Text);
                });
            });
            page.Footer().AlignCenter().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        })).GeneratePdf();
    }

    private static ReportCell ToCell(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => new ReportCell(value.GetRawText(), ReportCellKind.Number),
        JsonValueKind.True => new ReportCell("1", ReportCellKind.Boolean),
        JsonValueKind.False => new ReportCell("0", ReportCellKind.Boolean),
        JsonValueKind.String => new ReportCell(value.GetString() ?? string.Empty),
        JsonValueKind.Null or JsonValueKind.Undefined => new ReportCell(string.Empty),
        _ => new ReportCell(value.ToString())
    };

    private static XElement CreateRow(int rowNumber, IReadOnlyList<ReportCell> cells)
        => new(Spreadsheet + "row",
            new XAttribute("r", rowNumber),
            cells.Select((cell, index) => CreateCell(index + 1, rowNumber, cell)));

    private static XElement CreateCell(int columnNumber, int rowNumber, ReportCell value)
    {
        var address = $"{ColumnName(columnNumber)}{rowNumber}";
        return value.Kind switch
        {
            ReportCellKind.Number => new XElement(Spreadsheet + "c",
                new XAttribute("r", address), new XElement(Spreadsheet + "v", value.Text)),
            ReportCellKind.Boolean => new XElement(Spreadsheet + "c",
                new XAttribute("r", address), new XAttribute("t", "b"),
                new XElement(Spreadsheet + "v", value.Text)),
            _ => new XElement(Spreadsheet + "c",
                new XAttribute("r", address), new XAttribute("t", "inlineStr"),
                new XElement(Spreadsheet + "is",
                    new XElement(Spreadsheet + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), value.Text)))
        };
    }

    private static string ColumnName(int column)
    {
        var name = string.Empty;
        while (column > 0)
        {
            column--;
            name = (char)('A' + column % 26) + name;
            column /= 26;
        }
        return name;
    }

    private static string CsvCell(string value)
    {
        var safeValue = value.TrimStart() is { Length: > 0 } trimmed && trimmed[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return $"\"{safeValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AddXml(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }
}
