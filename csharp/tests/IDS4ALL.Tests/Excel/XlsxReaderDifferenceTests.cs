using IDS4ALL.Core.Excel;

namespace IDS4ALL.Tests.Excel;

/// <summary>
/// The cases in which ClosedXML reads a cell differently from openpyxl (and so from pandas). The owner accepted them,
/// because Excel does not write such cells and the converter reads text (dtype=str). The tests need no Python.
/// </summary>
public sealed class XlsxReaderDifferenceTests
{
    private const string Sheet = "Data";

    public static TheoryData<string, TestCell, string, string> Differences() => new()
    {
        // text
        { "genuine-crlf", new TestCell("inlineStr", null, 0, "<is><t xml:space=\"preserve\">a&#13;&#10;b</t></is>"), "a\r\nb", "a\nb" },
        { "x005f-escape", TestCell.Text("ab_x005F_cd x005F_"), "ab_cd ", "ab_cd x005F_" },
        { "shared-plain-and-run", TestCell.SharedXmlItem("<si><t>plain </t><r><t>rich</t></r></si>"), "plain rich", "rich" },
        { "shared-two-t", TestCell.SharedXmlItem("<si><t>first</t><t>second</t></si>"), "second", "first" },

        // numbers: openpyxl keeps the digits of an integer, ClosedXML reads a double
        { "int-above-2^53", TestCell.Number("123456789012345678901234567890"), "123456789012345678901234567890", "123456789012345677877719597056" },

        // date, time and duration formats
        { "time-under-date-format", TestCell.Number("0.25", 1), "06:00:00", "1899-12-31 06:00:00" },
        { "time-format-with-date-part", TestCell.Number("45000.5", 3), "2023-03-15 12:00:00", "45000 days, 12:00:00" },
        { "time-almost-one", TestCell.Number("0.9999999999", 3), "1900-01-01 00:00:00", "23:59:59.999991" },
        { "serial-60", TestCell.Number("60", 1), "1900-02-28 00:00:00", "1900-03-01 00:00:00" },
        { "serial-negative", TestCell.Number("-1", 1), "1899-12-29 00:00:00", "00:00:00" },
        { "duration-milliseconds", TestCell.Number("0.123456", 5), "2:57:46.598000", "2:57:46.598400" },
        { "duration-microseconds", TestCell.Number("0.0000115741", 5), "0:00:01", "0:00:01.000002" },

        // t="d" cell with a date only: pandas reads a date, ClosedXML a date and time
        { "iso-date-only", TestCell.IsoDate("2025-01-02"), "2025-01-02", "2025-01-02 00:00:00" },
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void Cell_IsReadAsDocumented(string name, TestCell cell, string openpyxlText, string closedXmlText)
    {
        _ = name;
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("h")], [cell]).Save(file.Path);

        using var workbook = XlsxWorkbook.Open(file.Path);
        var text = PandasExcel.ReadStrings(workbook, Sheet, 0, ["h"]).Rows[0][0];

        Assert.Equal(closedXmlText, text);
        Assert.NotEqual(openpyxlText, text);
    }

    [Theory]
    [InlineData("2025-01-02T03:04:05")]
    [InlineData("2025-01-02T03:04:05.5Z")]
    [InlineData("03:04:05")]
    [InlineData("PT1H30M")]
    public void IsoDateCell_FailsToOpen(string value)
    {
        // t="d" cells: openpyxl reads them, ClosedXML rejects the whole workbook. Excel never writes them.
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("h")], [TestCell.IsoDate(value)]).Save(file.Path);

        Assert.Throws<XlsxException>(() => XlsxWorkbook.Open(file.Path));
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}.xlsx");

        public void Dispose()
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
    }
}
