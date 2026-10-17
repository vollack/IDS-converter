using System.Text;
using IDS4ALL.Core.Excel;

namespace IDS4ALL.Tests.Excel;

/// <summary>
/// Differential tests. Each test writes a workbook, reads it with pandas (through Python.NET)
/// and with the C# reader, and compares the result.
/// </summary>
public sealed class PandasEquivalenceTests
{
    private const string Sheet = "Data";

    public static TheoryData<string, TestCell> SingleCells()
    {
        var cases = new Dictionary<string, TestCell>
        {
            // text
            ["text"] = TestCell.Text("Wall"),
            ["text-spaces"] = TestCell.Text("  Wall  "),
            ["text-unicode"] = TestCell.Text("Grüße ä ö ü ß – €"),
            ["text-newline"] = TestCell.Inline("a\nb"),
            ["text-literal-crlf"] = new TestCell("inlineStr", null, 0, "<is><t xml:space=\"preserve\">a\r\nb\rc</t></is>"),
            ["inline-x005f"] = TestCell.Inline("ab_x005F_cd"),
            ["text-numeric-looking"] = TestCell.Text("007"),
            ["text-exponent"] = TestCell.Text("1e5"),
            ["text-empty-shared"] = TestCell.SharedXmlItem("<si><t/></si>"),
            ["text-empty-inline"] = TestCell.Inline(""),
            ["shared-rich"] = TestCell.SharedRich("Hel", "lo ", "world"),
            ["shared-phonetic"] = TestCell.SharedXmlItem("<si><t>base</t><rPh sb=\"0\" eb=\"1\"><t>phonetic</t></rPh></si>"),
            ["shared-empty-run"] = TestCell.SharedXmlItem("<si><r><t/></r><r><t>x</t></r></si>"),
            ["inline-rich"] = TestCell.InlineRich("In", "line"),
            ["formula-text"] = TestCell.FormulaText("cached"),
            ["formula-text-empty"] = new TestCell("str", null),
            ["text-element-no-v"] = new TestCell("s", null, 0, null, "<si><t>x</t></si>"),
            // missing value words
            ["na-NA"] = TestCell.Text("NA"),
            ["na-N/A"] = TestCell.Text("N/A"),
            ["na-n/a"] = TestCell.Text("n/a"),
            ["na-null"] = TestCell.Text("null"),
            ["na-NULL"] = TestCell.Text("NULL"),
            ["na-None"] = TestCell.Text("None"),
            ["na-nan"] = TestCell.Text("nan"),
            ["na-NaN"] = TestCell.Text("NaN"),
            ["na--nan"] = TestCell.Text("-nan"),
            ["na-hash"] = TestCell.Text("#N/A"),
            ["na-hash-na"] = TestCell.Text("#NA"),
            ["na-html"] = TestCell.Text("<NA>"),
            ["na-inf"] = TestCell.Text("1.#IND"),
            ["na-not-na-lower"] = TestCell.Text("Na"),
            ["na-not-na-space"] = TestCell.Text(" NA"),
            ["na-inline"] = TestCell.Inline("NA"),
            ["na-nonna"] = TestCell.Text("none"),
            ["na-space-only"] = TestCell.Text(" "),
            // numbers
            ["int"] = TestCell.Number("5"),
            ["int-negative"] = TestCell.Number("-12"),
            ["int-zero"] = TestCell.Number("0"),
            ["int-negative-zero"] = TestCell.Number("-0"),
            ["int-plus"] = TestCell.Number("+7"),
            ["float-integral"] = TestCell.Number("5.0"),
            ["float-negative-zero"] = TestCell.Number("-0.0"),
            ["float"] = TestCell.Number("1.5"),
            ["float-0.1"] = TestCell.Number("0.1"),
            ["float-long"] = TestCell.Number("3.14159265358979"),
            ["float-17-digits"] = TestCell.Number("0.30000000000000004"),
            ["float-exp-small"] = TestCell.Number("1E-07"),
            ["float-exp-1.5e-7"] = TestCell.Number("1.5e-07"),
            ["float-1e-5"] = TestCell.Number("0.00001"),
            ["float-1e-4"] = TestCell.Number("0.0001"),
            ["float-0.00001234"] = TestCell.Number("0.00001234"),
            ["float-exp-big-integral"] = TestCell.Number("1E+16"),
            ["float-1e15"] = TestCell.Number("1E15"),
            ["float-1e22"] = TestCell.Number("1E22"),
            ["float-12345678901234567"] = TestCell.Number("12345678901234567.0"),
            ["float-1.2345678901234568e20"] = TestCell.Number("1.2345678901234568E+20"),
            ["float-1e300"] = TestCell.Number("1e300"),
            ["float-1e-300"] = TestCell.Number("1e-300"),
            ["float-fraction-large"] = TestCell.Number("123456.789"),
            ["float-9999999999999998"] = TestCell.Number("9999999999999998.0"),
            ["float-0.5e-4"] = TestCell.Number("0.5E-4"),
            ["number-style-0.00"] = TestCell.Number("2.5", 6),
            ["number-style-quoted-letters"] = TestCell.Number("2", 7),
            ["number-no-value"] = new TestCell("n", null),
            ["number-empty-v"] = new TestCell("n", ""),
            // booleans and errors
            ["bool-true"] = TestCell.Bool(true),
            ["bool-false"] = TestCell.Bool(false),
            ["error-na"] = TestCell.Error("#N/A"),
            ["error-div"] = TestCell.Error("#DIV/0!"),
            // dates
            ["date"] = TestCell.Number("45000", 1),
            ["date-fraction"] = TestCell.Number("45000.75", 2),
            ["date-microsecond-round"] = TestCell.Number("45000.123456789", 2),
            ["date-float-integral"] = TestCell.Number("45000.0", 1),
            ["time"] = TestCell.Number("0.5", 3),
            ["date-serial-1"] = TestCell.Number("1", 1),
            ["date-serial-59"] = TestCell.Number("59", 1),
            ["date-serial-61"] = TestCell.Number("61", 1),
            ["date-fractional-30"] = TestCell.Number("30.5", 2),
            ["date-huge"] = TestCell.Number("1E+10", 1),
            ["date-max"] = TestCell.Number("2958465", 1),
            ["date-over-max"] = TestCell.Number("2958466", 1),
            ["date-custom"] = TestCell.Number("45000", 4),
            ["date-locale"] = TestCell.Number("45000", 8),
            ["timedelta"] = TestCell.Number("1.5", 5),
            ["timedelta-negative"] = TestCell.Number("-0.5", 5),
            ["timedelta-zero"] = TestCell.Number("0", 5),
        };

        var data = new TheoryData<string, TestCell>();
        foreach (var (name, cell) in cases) data.Add(name, cell);
        return data;
    }

    [PythonTheory]
    [MemberData(nameof(SingleCells))]
    public void SingleCell_ReadsLikePandas(string name, TestCell cell)
    {
        _ = name;
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("h")], [cell]).Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, 0);
    }

    [PythonTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Date1904_ReadsLikePandas(bool date1904)
    {
        using var file = new TempFile();
        new XlsxBuilder { Date1904 = date1904 }
            .Sheet(Sheet, [TestCell.Text("h")], [TestCell.Number("1000", 1)], [TestCell.Number("0.5", 3)], [TestCell.Number("45000.5", 2)])
            .Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, 0);
    }

    [PythonTheory]
    [InlineData("A,B,C")]
    [InlineData("A,,C")]
    [InlineData("A,A,A")]
    [InlineData("A,A.1,A")]
    [InlineData("A,A,A.1")]
    [InlineData("A.1,A,A")]
    [InlineData(",,")]
    [InlineData("Unnamed: 1,,x")]
    [InlineData(",Unnamed: 0")]
    [InlineData("a, a,a")]
    public void Header_ReadsLikePandas(string headers)
    {
        var header = headers.Split(',').Select(h => h.Length == 0 ? null : TestCell.Text(h)).ToArray();
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, header, [TestCell.Text("x"), TestCell.Text("y"), TestCell.Text("z")]).Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, 0);
    }

    [PythonTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void SkippedRowsAndBlankRows_ReadLikePandas(int skip)
    {
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet,
            [TestCell.Text("title")],
            [],
            [TestCell.Text("A"), TestCell.Text("B")],
            [TestCell.Text("1"), TestCell.Text("2")],
            [],
            [null, TestCell.Text("only second")],
            [TestCell.Empty, TestCell.Empty],
            [TestCell.Text("last"), null, TestCell.Text("wide")],
            [],
            []).Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, skip);
    }

    [PythonTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void SingleColumnSheet_ReadsLikePandas(int skip)
    {
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("A")], [], [TestCell.Text("x")], [TestCell.Text(" ")], [TestCell.Text("y")], []).Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, skip);
    }

    [PythonFact]
    public void EmptySheet_ReadsLikePandas()
    {
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet).Save(file.Path);

        AssertSameAsPandas(file.Path, Sheet, 0);
    }

    [PythonFact]
    public void MissingSheet_FailsLikePandas()
    {
        using var file = new TempFile();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("A")]).Save(file.Path);

        AssertSameAsPandas(file.Path, "Other", 0);
    }

    [PythonFact]
    public void RandomWorkbooks_ReadLikePandas()
    {
        var random = new Random(int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261016);
        for (var run = 0; run < FuzzRuns; run++)
        {
            var rows = new List<TestCell?[]>();
            var width = random.Next(1, 6);
            for (var r = 0; r < random.Next(1, 9); r++)
            {
                var length = random.Next(0, width + 1);
                rows.Add(Enumerable.Range(0, length).Select(_ => RandomCell(random)).ToArray());
            }

            using var file = new TempFile();
            new XlsxBuilder().Sheet(Sheet, [.. rows]).Save(file.Path);
            try
            {
                AssertSameAsPandas(file.Path, Sheet, random.Next(0, 3));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random workbook {run} differs: {ex.Message}", ex);
            }
        }
    }

    private static int FuzzRuns =>
        int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var runs) ? runs : 150;

    private static readonly string[] RandomWords =
        ["Wall", "Slab", "NA", "n/a", "null", "None", "", " ", "A", "A", "B.1", "Unnamed: 1", "ä", "007", "TRUE", "1,5"];

    // Numbers under a date or time format (not the duration format, see the single cells). The values for which ClosedXML differs from openpyxl are left out (XlsxReaderDifferenceTests).
    private static readonly string[] RandomStyledNumbers = ["1000", "30.5", "45000", "45000.5", "45000.75", "100000"];

    private static readonly string[] RandomNumbers =
        ["0", "1", "-3", "42", "2.5", "1E-07", "0.1", "100000", "1E+16", "123456789.123", "-0.0", "60", "0.75", "45000.123456", "0.000011574", "1E+9", "999999999.5", "-3.5", "1E+16", "0.9999999", "2958465.5"];

    // Style 3 is the time format. ClosedXML reads a value of 1 or more under it as a duration (XlsxReaderDifferenceTests).
    private static TestCell RandomStyledNumber(Random random)
    {
        var style = random.Next(1, 5);
        return style == 3
            ? TestCell.Number(new[] { "0.25", "0.5", "0.75" }[random.Next(3)], style)
            : TestCell.Number(RandomStyledNumbers[random.Next(RandomStyledNumbers.Length)], style);
    }

    private static TestCell? RandomCell(Random random) => random.Next(12) switch
    {
        0 => null,
        1 => TestCell.Empty,
        2 or 3 or 4 => TestCell.Text(RandomWords[random.Next(RandomWords.Length)]),
        5 => TestCell.Inline(RandomWords[random.Next(RandomWords.Length)]),
        6 or 7 => TestCell.Number(RandomNumbers[random.Next(RandomNumbers.Length)]),
        8 => RandomStyledNumber(random),
        9 => TestCell.Bool(random.Next(2) == 0),
        10 => TestCell.Error(),
        _ => TestCell.FormulaText(RandomWords[random.Next(RandomWords.Length)]),
    };

    // ---- comparison -----------------------------------------------------------------------

    private static void AssertSameAsPandas(string path, string sheet, int skipRows)
    {
        var expectedColumns = PandasReference.ReadColumns(path, sheet, skipRows);
        var (actualColumns, columnError) = Run(() =>
        {
            using var workbook = XlsxWorkbook.Open(path);
            return PandasExcel.ReadColumns(workbook, sheet, skipRows);
        });

        if (expectedColumns.Error is not null)
        {
            Assert.True(columnError is not null,
                $"pandas failed ({expectedColumns.Error}) but the C# reader returned {Describe(actualColumns)}.");
            return;
        }

        Assert.True(columnError is null, $"The C# reader failed ({columnError}) but pandas returned {Describe(expectedColumns.Columns)}.");
        // pandas turns number headers into one number index ("42" becomes "42.0"). The converter
        // never uses such headers, so only text headers are compared.
        if (!expectedColumns.AllText) return;
        Assert.Equal(expectedColumns.Columns, actualColumns);

        var useColumns = expectedColumns.Columns!.Distinct().ToList();
        // The converter never reads without columns: it stops before ("No known columns found").
        if (useColumns.Count == 0) return;

        var expected = PandasReference.ReadStrings(path, sheet, skipRows, useColumns);
        var (actual, error) = Run(() =>
        {
            using var workbook = XlsxWorkbook.Open(path);
            return PandasExcel.ReadStrings(workbook, sheet, skipRows, useColumns);
        });

        if (expected.Error is not null)
        {
            Assert.True(error is not null, $"pandas failed ({expected.Error}) but the C# reader returned data.");
            return;
        }

        Assert.True(error is null, $"The C# reader failed ({error}).");
        Assert.Equal(expected.Data.Columns, actual!.Columns);
        Assert.True(expected.Data.Rows.Count == actual.Rows.Count,
            $"Row count: pandas {expected.Data.Rows.Count}, C# {actual.Rows.Count}.\npandas: {Format(expected.Data.Rows)}\nC#:     {Format(actual.Rows)}");
        for (var i = 0; i < actual.Rows.Count; i++)
            Assert.True(expected.Data.Rows[i].SequenceEqual(actual.Rows[i]),
                $"Row {i} differs.\npandas: {Format([expected.Data.Rows[i]])}\nC#:     {Format([actual.Rows[i]])}");
    }

    private static (T? Value, string? Error) Run<T>(Func<T> action)
    {
        try
        {
            return (action(), null);
        }
        catch (XlsxException ex)
        {
            return (default, ex.Message);
        }
    }

    private static string Describe<T>(T? value) => value is null ? "nothing" : value.ToString() ?? "";

    private static string Format(IEnumerable<string?[]> rows)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.Append('[').Append(string.Join(" | ", row.Select(c => c is null ? "<NA>" : $"\"{c}\""))).Append("] ");
        return builder.ToString();
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
