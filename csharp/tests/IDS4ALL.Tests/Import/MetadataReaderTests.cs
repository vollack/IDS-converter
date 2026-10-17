using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Tests.Excel;

namespace IDS4ALL.Tests.Import;

public sealed class MetadataReaderTests
{
    private static TestCell T(string text) => TestCell.Text(text);

    private static Ids4AllSettings Read(string path)
    {
        using var workbook = TestWorkbook.Open(path);
        return MetadataReader.Read(workbook);
    }

    private static string Save(string directory, params TestCell?[][] rows)
    {
        var path = Path.Combine(directory, "metadata.xlsx");
        new XlsxBuilder().Sheet("IDS4ALL", rows).Save(path);
        return path;
    }

    private static readonly TestCell?[] Header = [T("Entry"), T("Value")];

    [Fact]
    public void Read_WithRequiredEntriesOnly_UsesDefaults()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4, IFC4X3_ADD2")]);

        var settings = Read(path);

        Assert.Equal("Data", settings.SheetName);
        Assert.Equal("IFC4|IFC4X3_ADD2", settings.IfcVersion);
        Assert.Empty(settings.SeparateBy);
        Assert.Equal(0, settings.SkippedRows);
        Assert.False(settings.IsEntityBasedApplicability);
        Assert.Equal("Not Defined", settings.Title);
        Assert.Null(settings.Copyright);
        Assert.Null(settings.Version);
        Assert.Null(settings.Description);
        Assert.Null(settings.Author);
    }

    [Fact]
    public void Read_ReadsAllEntries()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, Header,
            [T("Sheet name"), T("Data")],
            [T("Skipped rows"), TestCell.Number("2")],
            [T("Title"), T("My IDS")],
            [T("Copyright"), T("Me")],
            [T("Version"), TestCell.Number("1.5")],
            [T("Description"), T("About")],
            [T("Author"), T("me@example.org")],
            [T("IFC version"), T("IFC2X3,IFC4")],
            [T("File separators"), T("Phase, Role")],
            [T("Entity-based applicability"), T("Yes")]);

        var settings = Read(path);

        Assert.Equal(2, settings.SkippedRows);
        Assert.Equal("My IDS", settings.Title);
        Assert.Equal("Me", settings.Copyright);
        Assert.Equal("1.5", settings.Version);
        Assert.Equal("About", settings.Description);
        Assert.Equal("me@example.org", settings.Author);
        Assert.Equal("IFC2X3|IFC4", settings.IfcVersion);
        Assert.Equal(["Phase", "Role"], settings.SeparateBy);
        Assert.True(settings.IsEntityBasedApplicability);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("YES", true)]
    [InlineData("no", false)]
    [InlineData("y", false)]
    public void Read_EntityBasedApplicability_IgnoresCase(string text, bool expected)
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")],
            [T("Entity-based applicability"), T(text)]);

        Assert.Equal(expected, Read(path).IsEntityBasedApplicability);
    }

    [Fact]
    public void Read_SkipsEntriesWithoutValueAndKeepsTheLastDuplicate()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, Header,
            [T("Sheet name"), T("First")],
            [],
            [T("Title"), T("NA")],
            [T("IFC version"), T("IFC4")],
            [T("Sheet name"), T("Second")]);

        var settings = Read(path);

        Assert.Equal("Second", settings.SheetName);
        Assert.Equal("Not Defined", settings.Title);
    }

    [Theory]
    [InlineData("Sheet name")]
    [InlineData("IFC version")]
    public void Read_WithoutRequiredEntry_Throws(string missing)
    {
        using var directory = new TempDirectory();
        var rows = new List<TestCell?[]> { Header };
        if (missing != "Sheet name") rows.Add([T("Sheet name"), T("Data")]);
        if (missing != "IFC version") rows.Add([T("IFC version"), T("IFC4")]);
        var path = Save(directory.Path, [.. rows]);

        var exception = Assert.Throws<ImportException>(() => Read(path));

        Assert.Equal($"{missing} is not defined in the Excel", exception.Message);
    }

    [Fact]
    public void Read_WithNumberAsSheetName_Throws()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, Header, [T("Sheet name"), TestCell.Number("1")], [T("IFC version"), T("IFC4")]);

        Assert.Throws<ImportException>(() => Read(path));
    }

    [Fact]
    public void Read_WithoutMetadataSheet_Throws()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "other.xlsx");
        new XlsxBuilder().Sheet("Other", [T("x")]).Save(path);

        Assert.Throws<XlsxException>(() => Read(path));
    }

    // ---- differential tests against get_metadata of IDS4ALL-main.py -----------------------------

    public static TheoryData<string> ExampleWorkbooks() => new()
    {
        "IDS4ALL-Template.xlsx",
        "Requirements-openBIM-building-permit-City-of-Vienna-extended.xlsx",
    };

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void Read_ExampleWorkbook_MatchesPython(string workbook)
    {
        AssertSameAsPython(RepositoryRoot.ExcelFile(workbook));
    }

    [PythonFact]
    public void Read_HandMadeWorkbooks_MatchPython()
    {
        using var directory = new TempDirectory();
        var cases = new TestCell?[][][]
        {
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC2X3 , IFC4")], [T("File separators"), T("Phase,Role,Usecase")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Skipped rows"), TestCell.Number("3")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Version"), TestCell.Number("2")], [T("Title"), T("T")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Version"), TestCell.Number("2.5")], [T("Author"), T("a@b.c")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Entity-based applicability"), T("YES")]],
            [Header, [T("IFC version"), T("IFC4")]],
            [Header, [T("Sheet name"), T("Data")]],
            [Header, [T("Sheet name"), T("A")], [T("Sheet name"), T("B")], [T("IFC version"), T("IFC4")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Description"), T("null")]],
            [Header, [T("Sheet name"), T("Data")], [T("IFC version"), T("IFC4")], [T("Copyright"), TestCell.Bool(true)]],
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var path = Save(directory.Path, cases[i]);
            try
            {
                AssertSameAsPython(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Metadata case {i} differs: {ex.Message}", ex);
            }
        }
    }

    private static void AssertSameAsPython(string path)
    {
        var expected = PandasReference.GetMetadata(path);
        Ids4AllSettings? actual = null;
        string? error = null;
        try
        {
            actual = Read(path);
        }
        catch (Exception ex) when (ex is ImportException or XlsxException)
        {
            error = ex.Message;
        }

        if (expected.Error is not null)
        {
            Assert.True(error is not null, $"Python failed ({expected.Error}) but the C# reader returned settings.");
            return;
        }

        Assert.True(error is null, $"The C# reader failed ({error}) but Python returned settings.");
        var python = expected.Data!;
        Assert.Equal(python.SheetName, actual!.SheetName);
        Assert.Equal(python.IfcVersion, actual.IfcVersion);
        Assert.Equal(python.SeparateBy, actual.SeparateBy);
        Assert.Equal(python.SkippedRows, actual.SkippedRows.ToString());
        Assert.Equal(python.EntityBased, actual.IsEntityBasedApplicability);
        Assert.Equal(python.Title, actual.Title);
        Assert.Equal(python.Copyright, actual.Copyright);
        Assert.Equal(python.Version, actual.Version);
        Assert.Equal(python.Description, actual.Description);
        Assert.Equal(python.Author, actual.Author);
    }

    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}");

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
