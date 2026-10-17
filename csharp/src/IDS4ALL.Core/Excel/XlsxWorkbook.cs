using System.Numerics;
using ClosedXML.Excel;

namespace IDS4ALL.Core.Excel;

/// <summary>
/// Reads the cell values of an .xlsx file with ClosedXML, in the types that pandas.read_excel gets from openpyxl:
/// string, BigInteger (integral number), double, bool, DateTime, TimeOnly, PythonTimedelta, "" for an empty cell, NaN for an error.
/// Known differences from openpyxl are listed in XlsxReaderDifferenceTests.
/// </summary>
public sealed class XlsxWorkbook : IDisposable
{
    private readonly XLWorkbook _workbook;

    private XlsxWorkbook(XLWorkbook workbook) => _workbook = workbook;

    public IReadOnlyList<string> SheetNames => _workbook.Worksheets.Select(sheet => sheet.Name).ToList();

    public static XlsxWorkbook Open(string path)
    {
        try
        {
            // ClosedXML locks the file for writing, so read from a copy of the bytes.
            return new XlsxWorkbook(new XLWorkbook(new MemoryStream(File.ReadAllBytes(path))));
        }
        catch (Exception ex) when (ex is not IOException)
        {
            throw new XlsxException($"The file '{path}' is not a valid .xlsx workbook: {ex.Message}", ex);
        }
    }

    public void Dispose() => _workbook.Dispose();

    public IReadOnlyList<object?[]> ReadRows(string sheetName, int? maxRows = null)
    {
        if (!_workbook.TryGetWorksheet(sheetName, out var sheet))
            throw new XlsxException($"Worksheet named '{sheetName}' not found");

        var used = sheet.RangeUsed(XLCellsUsedOptions.Contents);
        if (used is null) return [];

        var lastRow = maxRows is null ? used.LastRow().RowNumber() : Math.Min(used.LastRow().RowNumber(), maxRows.Value);
        var lastColumn = used.LastColumn().ColumnNumber();
        var epoch = _workbook.Use1904DateSystem ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30);

        var rows = new List<object?[]>();
        var lastRowWithData = -1;
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new List<object?>(lastColumn);
            for (var c = 1; c <= lastColumn; c++) row.Add(ConvertSafe(sheet.Cell(r, c), epoch));
            while (row.Count > 0 && row[^1] is "") row.RemoveAt(row.Count - 1);
            if (row.Count > 0) lastRowWithData = rows.Count;
            rows.Add([.. row]);
        }

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

    // A date that .NET cannot represent is an error cell, like openpyxl does.
    private static object ConvertSafe(IXLCell cell, DateTime epoch)
    {
        try
        {
            return Convert(cell, epoch);
        }
        catch (ArgumentException)
        {
            return double.NaN;
        }
    }

    // ClosedXML reads a time under 24 hours as a time span. openpyxl reads it as a time, unless the format is a duration like [h]:mm:ss.
    private static object ToTimeOrDuration(TimeSpan span, IXLNumberFormat format) =>
        span >= TimeSpan.Zero && span < TimeSpan.FromDays(1) && format.NumberFormatId != 46 && !format.Format.Contains('[')
            ? TimeOnly.FromTimeSpan(span)
            : PythonTimedelta.FromDays(span.TotalDays);

    private static object Convert(IXLCell cell, DateTime epoch)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        if (value.IsBlank) return "";
        // ClosedXML turns every line feed into a carriage return and a line feed.
        if (value.IsText) return value.GetText().Replace("\r\n", "\n");
        if (value.IsBoolean) return value.GetBoolean();
        if (value.IsError) return double.NaN;
        if (value.IsTimeSpan) return ToTimeOrDuration(value.GetTimeSpan(), cell.Style.NumberFormat);
        if (value.IsDateTime)
        {
            var dateTime = value.GetDateTime();
            return dateTime.Date == epoch ? TimeOnly.FromTimeSpan(dateTime.TimeOfDay) : dateTime;
        }

        var number = value.GetNumber();
        return number == Math.Floor(number) && !double.IsInfinity(number) ? new BigInteger(number) : number;
    }
}
