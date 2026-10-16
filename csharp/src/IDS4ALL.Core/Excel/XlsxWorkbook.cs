using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace IDS4ALL.Core.Excel;

/// <summary>
/// Reads the cell values of an .xlsx file like pandas.read_excel does with openpyxl
/// (read-only mode, data_only=True). Cell values are:
/// string, BigInteger (integral number), double, bool, DateTime, DateOnly, TimeOnly, PythonTimedelta.
/// An empty cell is "" and an error cell is double.NaN.
/// </summary>
public sealed partial class XlsxWorkbook : IDisposable
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly DateTime WindowsEpoch = new(1899, 12, 30);
    private static readonly DateTime MacEpoch = new(1904, 1, 1);

    private static readonly Dictionary<int, string> BuiltinFormats = new()
    {
        [0] = "General", [1] = "0", [2] = "0.00", [3] = "#,##0", [4] = "#,##0.00",
        [5] = "\"$\"#,##0_);(\"$\"#,##0)", [6] = "\"$\"#,##0_);[Red](\"$\"#,##0)",
        [7] = "\"$\"#,##0.00_);(\"$\"#,##0.00)", [8] = "\"$\"#,##0.00_);[Red](\"$\"#,##0.00)",
        [9] = "0%", [10] = "0.00%", [11] = "0.00E+00", [12] = "# ?/?", [13] = "# ??/??",
        [14] = "mm-dd-yy", [15] = "d-mmm-yy", [16] = "d-mmm", [17] = "mmm-yy", [18] = "h:mm AM/PM",
        [19] = "h:mm:ss AM/PM", [20] = "h:mm", [21] = "h:mm:ss", [22] = "m/d/yy h:mm",
        [37] = "#,##0_);(#,##0)", [38] = "#,##0_);[Red](#,##0)", [39] = "#,##0.00_);(#,##0.00)",
        [40] = "#,##0.00_);[Red](#,##0.00)",
        [41] = "_(* #,##0_);_(* \\(#,##0\\);_(* \"-\"_);_(@_)",
        [42] = "_(\"$\"* #,##0_);_(\"$\"* \\(#,##0\\);_(\"$\"* \"-\"_);_(@_)",
        [43] = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)",
        [44] = "_(\"$\"* #,##0.00_)_(\"$\"* \\(#,##0.00\\)_(\"$\"* \"-\"??_)_(@_)",
        [45] = "mm:ss", [46] = "[h]:mm:ss", [47] = "mmss.0", [48] = "##0.0E+0", [49] = "@",
    };

    private readonly ZipArchive _archive;
    private readonly Dictionary<string, string> _sheetPaths = new(StringComparer.Ordinal);
    private readonly List<string> _sheetNames = [];
    private readonly List<string> _sharedStrings = [];
    private readonly HashSet<int> _dateStyles = [];
    private readonly HashSet<int> _timedeltaStyles = [];
    private readonly DateTime _epoch;

    private XlsxWorkbook(ZipArchive archive)
    {
        _archive = archive;
        var workbookPath = FindWorkbookPath();
        var relationships = ReadRelationships(RelationshipsPathOf(workbookPath));
        _epoch = ReadWorkbook(workbookPath, relationships);

        foreach (var (type, target) in relationships.Values)
        {
            if (type.EndsWith("/sharedStrings", StringComparison.Ordinal)) ReadSharedStrings(Resolve(workbookPath, target));
            else if (type.EndsWith("/styles", StringComparison.Ordinal)) ReadStyles(Resolve(workbookPath, target));
        }
    }

    public IReadOnlyList<string> SheetNames => _sheetNames;

    public static XlsxWorkbook Open(string path)
    {
        try
        {
            return new XlsxWorkbook(new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read));
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw new XlsxException($"The file '{path}' is not a valid .xlsx workbook: {ex.Message}", ex);
        }
    }

    public void Dispose() => _archive.Dispose();

    /// <summary>
    /// Returns the rows of a sheet as pandas builds them before the header is read: trailing empty
    /// cells and trailing empty rows are removed, then all rows get the width of the widest row.
    /// With <paramref name="maxRows"/> only the first rows are read, as pandas does for nrows.
    /// </summary>
    public IReadOnlyList<object?[]> ReadRows(string sheetName, int? maxRows = null)
    {
        if (!_sheetPaths.TryGetValue(sheetName, out var path))
            throw new XlsxException($"Worksheet named '{sheetName}' not found");

        var rows = new List<object?[]>();
        var lastRowWithData = -1;
        var counter = 1;

        foreach (var (rowIndex, cells) in ParseRows(path))
        {
            // openpyxl fills missing rows with empty rows and drops rows that go backwards.
            for (; counter < rowIndex; counter++)
            {
                rows.Add([]);
                if (rows.Count >= maxRows) goto done;
            }
            if (counter > rowIndex) continue;

            var row = BuildRow(cells);
            counter++;
            while (row.Count > 0 && row[^1] is "") row.RemoveAt(row.Count - 1);
            if (row.Count > 0) lastRowWithData = rows.Count;
            rows.Add([.. row]);
            if (rows.Count >= maxRows) break;
        }

        done:
        rows = rows.GetRange(0, lastRowWithData + 1);
        if (rows.Count == 0) return rows;

        var width = rows.Max(r => r.Length);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Length == width) continue;
            var padded = new object?[width];
            Array.Copy(rows[i], padded, rows[i].Length);
            Array.Fill<object?>(padded, "", rows[i].Length, width - rows[i].Length);
            rows[i] = padded;
        }
        return rows;
    }

    // ---- rows and cells -------------------------------------------------------------------

    private readonly record struct RawCell(int Column, object Value);

    /// <summary>openpyxl: the row is as wide as its last cell. A cell right of it is dropped.</summary>
    private static List<object?> BuildRow(List<RawCell> cells)
    {
        if (cells.Count == 0) return [];
        var width = cells[^1].Column;
        var row = new List<object?>(Enumerable.Repeat<object?>("", Math.Max(width, 0)));
        foreach (var cell in cells)
            if (cell.Column >= 1 && cell.Column <= width)
                row[cell.Column - 1] = cell.Value;
        return row;
    }

    private IEnumerable<(int RowIndex, List<RawCell> Cells)> ParseRows(string path)
    {
        using var stream = Entry(path).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var rowCounter = 0;

        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "row" || reader.NamespaceURI != Main.NamespaceName)
            {
                reader.Read();
                continue;
            }

            var row = (XElement)XNode.ReadFrom(reader);
            var rowAttribute = (string?)row.Attribute("r");
            rowCounter = rowAttribute is null ? rowCounter + 1 : ParseRowNumber(rowAttribute);

            var cells = new List<RawCell>();
            var columnCounter = 0;
            foreach (var cell in row.Elements())
                cells.Add(ParseCell(cell, ref columnCounter, rowCounter));
            yield return (rowCounter, cells);
        }
    }

    private static int ParseRowNumber(string text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value == Math.Floor(value))
            return (int)value;
        throw new XlsxException($"{text} is not a valid row number");
    }

    private RawCell ParseCell(XElement cell, ref int columnCounter, int rowNumber)
    {
        var type = (string?)cell.Attribute("t") ?? "n";
        var coordinate = (string?)cell.Attribute("r");
        var styleText = (string?)cell.Attribute("s");
        var styleId = string.IsNullOrEmpty(styleText) ? 0 : int.Parse(styleText, CultureInfo.InvariantCulture);

        int column;
        if (coordinate is not null) column = columnCounter = ColumnOf(coordinate);
        else column = ++columnCounter;

        object? value = null;
        if (type == "inlineStr")
        {
            var inline = cell.Element(Main + "is");
            if (inline is not null) value = TextContent(inline);
        }
        else
        {
            // openpyxl: findtext(<v>) or None
            var valueText = cell.Element(Main + "v")?.Value;
            if (!string.IsNullOrEmpty(valueText))
            {
                value = type switch
                {
                    "n" => ConvertNumber(valueText, styleId, coordinate ?? $"row {rowNumber}"),
                    "s" => ReadSharedString(valueText),
                    "b" => int.Parse(valueText, CultureInfo.InvariantCulture) != 0,
                    "str" => valueText,
                    "d" => ParseIso8601(valueText),
                    "e" => double.NaN,
                    _ => valueText,
                };
            }
        }

        return new RawCell(column, value ?? "");
    }

    private string ReadSharedString(string indexText)
    {
        var index = int.Parse(indexText, CultureInfo.InvariantCulture);
        if (index < 0 || index >= _sharedStrings.Count)
            throw new XlsxException($"The shared string {index} does not exist.");
        return _sharedStrings[index];
    }

    private object ConvertNumber(string text, int styleId, string coordinate)
    {
        // openpyxl _cast_number: a float when the text has ".", "e" or "E", else an int.
        object number;
        try
        {
            number = text.Contains('.') || text.Contains('e') || text.Contains('E')
                ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)
                : BigInteger.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture);
        }
        catch (FormatException ex)
        {
            throw new XlsxException($"Cell {coordinate} has the invalid number '{text}'.", ex);
        }

        if (_dateStyles.Contains(styleId))
        {
            try
            {
                return FromExcel(number, _epoch, _timedeltaStyles.Contains(styleId));
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
            {
                // openpyxl treats the cell as an error. pandas then reads NaN.
                return double.NaN;
            }
        }

        // pandas: the int if the number is integral, else the float.
        if (number is BigInteger) return number;
        var floating = (double)number;
        if (double.IsNaN(floating) || double.IsInfinity(floating))
            throw new XlsxException($"Cell {coordinate} has the invalid number '{text}'.");
        return floating == Math.Floor(floating) ? new BigInteger(floating) : floating;
    }

    private static object FromExcel(object number, DateTime epoch, bool timedelta)
    {
        if (timedelta)
        {
            var td = number is BigInteger days ? PythonTimedelta.FromDays(days) : PythonTimedelta.FromDays((double)number);
            var microseconds = td.MicrosecondsField;
            if (microseconds == 0) return td;
            // openpyxl rounds the microseconds to whole milliseconds (half to even).
            var rounded = (long)Math.Round(microseconds / 1000.0, MidpointRounding.ToEven) * 1000;
            return PythonTimedelta.FromSeconds(Math.Floor(td.TotalSeconds), rounded);
        }

        var value = number is BigInteger big ? (double)big : (double)number;
        var day = Math.Floor(value);
        var fraction = value - day;
        var diff = TimeSpan.FromMilliseconds(RoundHalfEven(fraction * 86_400.0 * 1000.0));
        if (value >= 0 && value < 1 && diff.Days == 0)
            return new TimeOnly(diff.Ticks);
        if (value > 0 && value < 60 && epoch == WindowsEpoch) day += 1;
        return epoch.AddDays(day) + diff;
    }

    private static double RoundHalfEven(double value) => Math.Round(value, MidpointRounding.ToEven);

    [GeneratedRegex(@"^(?<date>(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}))?T?(?<time>(?<hour>\d{2}):(?<minute>\d{2})(:(?<second>\d{2})(?<microsecond>\.\d{1,3})?)?)?Z?")]
    private static partial Regex IsoPattern();

    [GeneratedRegex(@"^PT((?<hours>\d+)H)?((?<minutes>\d+)M)?((?<seconds>\d+(\.\d{1,3})?)S)?")]
    private static partial Regex IsoDurationPattern();

    private static object ParseIso8601(string text)
    {
        var match = IsoPattern().Match(text);
        if (match.Success && match.Groups.Cast<Group>().Skip(1).Any(g => g.Success && g.Length > 0))
        {
            int Part(string name) => match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
            var microsecond = match.Groups["microsecond"].Success
                ? (int)(double.Parse(match.Groups["microsecond"].Value, CultureInfo.InvariantCulture) * 1_000_000)
                : 0;
            try
            {
                if (!match.Groups["date"].Success)
                    return new TimeOnly(Part("hour"), Part("minute"), Part("second")).Add(TimeSpan.FromTicks(microsecond * 10L));
                if (!match.Groups["time"].Success)
                    return new DateOnly(Part("year"), Part("month"), Part("day"));
                return new DateTime(Part("year"), Part("month"), Part("day"), Part("hour"), Part("minute"), Part("second"))
                    .AddTicks(microsecond * 10L);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new XlsxException($"Invalid datetime value {text}", ex);
            }
        }

        var duration = IsoDurationPattern().Match(text);
        if (duration.Success && duration.Groups.Cast<Group>().Skip(1).Any(g => g.Success && g.Length > 0))
        {
            double Number(string name) => duration.Groups[name].Success
                ? double.Parse(duration.Groups[name].Value, CultureInfo.InvariantCulture)
                : 0;
            return PythonTimedelta.FromDays((Number("hours") * 3600 + Number("minutes") * 60 + Number("seconds")) / 86400);
        }

        throw new XlsxException($"Invalid datetime value {text}");
    }

    private static int ColumnOf(string coordinate)
    {
        var digitStart = 0;
        while (digitStart < coordinate.Length && !char.IsAsciiDigit(coordinate[digitStart])) digitStart++;
        var letters = coordinate[..digitStart];
        var error = $"'{letters}' is not a valid column name. Column names are from A to ZZZ";
        if (letters.Length is 0 or > 3) throw new XlsxException(error);
        var index = 0;
        foreach (var letter in letters.ToUpperInvariant())
        {
            if (letter is < 'A' or > 'Z') throw new XlsxException(error);
            index = index * 26 + (letter - 'A' + 1);
        }
        return index;
    }

    // ---- shared strings, styles, workbook ---------------------------------------------------

    /// <summary>
    /// Text of a shared string item or an inline string: the last &lt;t&gt; plus the text of all runs.
    /// Phonetic text is not part of it. An empty &lt;t&gt; counts as no text.
    /// </summary>
    private static string TextContent(XElement item)
    {
        var plain = item.Elements(Main + "t").LastOrDefault()?.Value;
        var runs = item.Elements(Main + "r")
            .Select(run => run.Elements(Main + "t").LastOrDefault()?.Value)
            .Where(text => !string.IsNullOrEmpty(text));
        return (string.IsNullOrEmpty(plain) ? "" : plain) + string.Concat(runs);
    }

    private void ReadSharedStrings(string path)
    {
        using var stream = Entry(path).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si" && reader.NamespaceURI == Main.NamespaceName)
                _sharedStrings.Add(TextContent((XElement)XNode.ReadFrom(reader)).Replace("x005F_", ""));
            else
                reader.Read();
        }
    }

    [GeneratedRegex("\".*?\"|\\[(?!hh?\\]|mm?\\]|ss?\\])[^\\]]*\\]")]
    private static partial Regex StripPattern();

    [GeneratedRegex(@"(?<![_\\])[dmhysDMHYS]")]
    private static partial Regex DateCharacterPattern();

    [GeneratedRegex(@"\[hh?\](:mm(:ss(\.0*)?)?)?|\[mm?\](:ss(\.0*)?)?|\[ss?\](\.0*)?", RegexOptions.IgnoreCase)]
    private static partial Regex TimedeltaPattern();

    private static bool IsDateFormat(string? format)
    {
        if (format is null) return false;
        var first = format.Split(';')[0];
        return DateCharacterPattern().IsMatch(StripPattern().Replace(first, ""));
    }

    private static bool IsTimedeltaFormat(string? format) =>
        format is not null && TimedeltaPattern().IsMatch(format.Split(';')[0]);

    private void ReadStyles(string path)
    {
        var document = LoadDocument(path);
        var custom = new Dictionary<int, string>();
        foreach (var format in document.Root!.Elements(Main + "numFmts").Elements(Main + "numFmt"))
            custom[int.Parse((string)format.Attribute("numFmtId")!, CultureInfo.InvariantCulture)] = (string?)format.Attribute("formatCode") ?? "";

        var index = 0;
        foreach (var style in document.Root!.Elements(Main + "cellXfs").Elements(Main + "xf"))
        {
            var idText = (string?)style.Attribute("numFmtId");
            var id = string.IsNullOrEmpty(idText) ? 0 : int.Parse(idText, CultureInfo.InvariantCulture);
            var format = custom.TryGetValue(id, out var code) ? code : BuiltinFormats.GetValueOrDefault(id);
            if (IsDateFormat(format)) _dateStyles.Add(index);
            if (IsTimedeltaFormat(format)) _timedeltaStyles.Add(index);
            index++;
        }
    }

    private DateTime ReadWorkbook(string path, Dictionary<string, (string Type, string Target)> relationships)
    {
        var document = LoadDocument(path);
        var date1904 = (string?)document.Root!.Element(Main + "workbookPr")?.Attribute("date1904");
        var epoch = date1904 is "1" or "true" ? MacEpoch : WindowsEpoch;

        foreach (var sheet in document.Root!.Elements(Main + "sheets").Elements(Main + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? "";
            var relationshipId = (string?)sheet.Attribute(Relationship + "id") ?? "";
            if (!relationships.TryGetValue(relationshipId, out var relationship)) continue;
            if (!relationship.Type.EndsWith("/worksheet", StringComparison.Ordinal)) continue;
            _sheetNames.Add(name);
            _sheetPaths[name] = Resolve(path, relationship.Target);
        }
        return epoch;
    }

    private string FindWorkbookPath()
    {
        if (_archive.GetEntry("_rels/.rels") is null) throw new XlsxException("The workbook has no package relationships.");
        var document = LoadDocument("_rels/.rels");
        foreach (var relationship in document.Root!.Elements(Package + "Relationship"))
            if (((string?)relationship.Attribute("Type") ?? "").EndsWith("/officeDocument", StringComparison.Ordinal))
                return ((string?)relationship.Attribute("Target") ?? "").TrimStart('/');
        throw new XlsxException("The workbook part is missing.");
    }

    private static string RelationshipsPathOf(string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        return slash < 0 ? $"_rels/{partPath}.rels" : $"{partPath[..slash]}/_rels/{partPath[(slash + 1)..]}.rels";
    }

    private Dictionary<string, (string Type, string Target)> ReadRelationships(string path)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        if (_archive.GetEntry(path) is null) return result;
        foreach (var relationship in LoadDocument(path).Root!.Elements(Package + "Relationship"))
            result[(string?)relationship.Attribute("Id") ?? ""] =
                ((string?)relationship.Attribute("Type") ?? "", (string?)relationship.Attribute("Target") ?? "");
        return result;
    }

    private static string Resolve(string basePart, string target)
    {
        if (target.StartsWith('/')) return target.TrimStart('/');
        var baseDirectory = basePart.Contains('/') ? basePart[..basePart.LastIndexOf('/')] : "";
        var parts = new List<string>(baseDirectory.Length == 0 ? [] : baseDirectory.Split('/'));
        foreach (var segment in target.Split('/'))
        {
            if (segment == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
            else if (segment != "." && segment.Length > 0) parts.Add(segment);
        }
        return string.Join('/', parts);
    }

    private XDocument LoadDocument(string path)
    {
        using var stream = Entry(path).Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        return XDocument.Load(reader);
    }

    private ZipArchiveEntry Entry(string path) =>
        _archive.GetEntry(path) ?? throw new XlsxException($"The workbook part '{path}' is missing.");
}
