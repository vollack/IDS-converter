using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace IDS4ALL.Tests.Excel;

/// <summary>One cell of a test workbook. The cell XML is written as given, so tests control the raw file.</summary>
public sealed record TestCell(string Type, string? Value, int Style = 0, string? InlineXml = null, string? SharedXml = null)
{
    // Style ids of XlsxBuilder: 0 General, 1 date (14), 2 datetime (22), 3 time (21), 4 custom date,
    // 5 timedelta (46), 6 number (0.00), 7 quoted letters only, 8 date with 1900 leap year range text.
    public static TestCell Text(string value) => new("s", value, 0, null, $"<si><t xml:space=\"preserve\">{Escape(value)}</t></si>");
    public static TestCell SharedRich(params string[] runs) => new("s", "rich", 0, null, "<si>" + string.Concat(runs.Select(run => $"<r><rPr><b/></rPr><t xml:space=\"preserve\">{Escape(run)}</t></r>")) + "</si>");
    public static TestCell SharedXmlItem(string siXml) => new("s", "xml", 0, null, siXml);
    public static TestCell Inline(string value) => new("inlineStr", null, 0, $"<is><t xml:space=\"preserve\">{Escape(value)}</t></is>");
    public static TestCell InlineRich(params string[] runs) => new("inlineStr", null, 0, "<is>" + string.Concat(runs.Select(run => $"<r><t xml:space=\"preserve\">{Escape(run)}</t></r>")) + "</is>");
    public static TestCell Number(string lexical, int style = 0) => new("n", lexical, style);
    public static TestCell Bool(bool value) => new("b", value ? "1" : "0");
    public static TestCell FormulaText(string cached) => new("str", cached);
    public static TestCell Error(string code = "#N/A") => new("e", code);
    public static TestCell IsoDate(string text) => new("d", text);
    public static readonly TestCell Empty = new("n", null);

    internal static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

/// <summary>Writes a minimal .xlsx file. A null cell is not written, so the cell is missing in the row.</summary>
public sealed class XlsxBuilder
{
    private readonly List<(string Name, TestCell?[][] Rows)> _sheets = [];
    private readonly List<string> _sharedItems = [];

    public bool Date1904 { get; init; }

    public XlsxBuilder Sheet(string name, params TestCell?[][] rows)
    {
        _sheets.Add((name, rows));
        return this;
    }

    public void Save(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        var sheetXml = _sheets.Select(sheet => SheetXml(sheet.Rows)).ToList();

        var sheetOverrides = string.Concat(_sheets.Select((_, i) =>
            $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        Add(archive, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + sheetOverrides
            + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
            + "<Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/>"
            + "</Types>");
        Add(archive, "_rels/.rels",
            """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");

        var sheetEntries = string.Concat(_sheets.Select((sheet, i) =>
            $"<sheet name=\"{TestCell.Escape(sheet.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"));
        var properties = Date1904 ? "<workbookPr date1904=\"1\"/>" : "<workbookPr/>";
        Add(archive, "xl/workbook.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">{properties}<sheets>{sheetEntries}</sheets></workbook>""");

        var relationships = string.Concat(_sheets.Select((_, i) =>
            $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"));
        var next = _sheets.Count + 1;
        relationships += $"<Relationship Id=\"rId{next}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>";
        relationships += $"<Relationship Id=\"rId{next + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/>";
        Add(archive, "xl/_rels/workbook.xml.rels",
            $"""<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{relationships}</Relationships>""");

        Add(archive, "xl/styles.xml", StylesXml);

        for (var i = 0; i < sheetXml.Count; i++)
            Add(archive, $"xl/worksheets/sheet{i + 1}.xml", sheetXml[i]);

        var items = string.Concat(_sharedItems);
        Add(archive, "xl/sharedStrings.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">{items}</sst>""");
    }

    private string SheetXml(TestCell?[][] rows)
    {
        var builder = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var r = 0; r < rows.Length; r++)
        {
            builder.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++)
            {
                var cell = rows[r][c];
                if (cell is null) continue;
                builder.Append(CellXml(cell, $"{Column(c)}{r + 1}"));
            }
            builder.Append("</row>");
        }
        return builder.Append("</sheetData></worksheet>").ToString();
    }

    private string CellXml(TestCell cell, string coordinate)
    {
        var style = cell.Style == 0 ? "" : $" s=\"{cell.Style}\"";
        var type = cell.Type == "n" ? "" : $" t=\"{cell.Type}\"";
        if (cell.InlineXml is not null) return $"<c r=\"{coordinate}\"{style}{type}>{cell.InlineXml}</c>";
        if (cell.Value is null) return $"<c r=\"{coordinate}\"{style}{type}/>";
        var value = cell.Type == "s" ? SharedIndex(cell.SharedXml!).ToString() : TestCell.Escape(cell.Value);
        return $"<c r=\"{coordinate}\"{style}{type}><v>{value}</v></c>";
    }

    private int SharedIndex(string itemXml)
    {
        var index = _sharedItems.IndexOf(itemXml);
        if (index >= 0) return index;
        _sharedItems.Add(itemXml);
        return _sharedItems.Count - 1;
    }

    private static string Column(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
            name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private static void Add(ZipArchive archive, string path, string xml)
    {
        var entry = archive.CreateEntry(path);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
        _ = XDocument.Parse(xml);
    }

    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="3">
            <numFmt numFmtId="164" formatCode="yyyy\-mm\-dd"/>
            <numFmt numFmtId="165" formatCode="&quot;d&quot;0"/>
            <numFmt numFmtId="166" formatCode="[$-407]dd/mm/yyyy;@"/>
          </numFmts>
          <cellXfs count="9">
            <xf numFmtId="0"/>
            <xf numFmtId="14"/>
            <xf numFmtId="22"/>
            <xf numFmtId="21"/>
            <xf numFmtId="164"/>
            <xf numFmtId="46"/>
            <xf numFmtId="2"/>
            <xf numFmtId="165"/>
            <xf numFmtId="166"/>
          </cellXfs>
        </styleSheet>
        """;
}
