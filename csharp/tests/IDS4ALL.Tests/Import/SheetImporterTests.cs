using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Tests.Excel;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Import;

public sealed class SheetImporterTests
{
    private static TestCell T(string text) => TestCell.Text(text);

    private static Ids4AllSettings Settings(string sheet = "Data", string ifc = "IFC4|IFC2X3", int skip = 0, bool entityBased = false) =>
        new(sheet, ifc, [], skip, entityBased, "Title", null, null, null, null);

    private static ImportedSheet Import(string path, Ids4AllSettings settings)
    {
        using var workbook = XlsxWorkbook.Open(path);
        return SheetImporter.Import(workbook, settings);
    }

    private static string Save(string directory, params TestCell?[][] rows)
    {
        var path = Path.Combine(directory, "sheet.xlsx");
        new XlsxBuilder().Sheet("Data", rows).Save(path);
        return path;
    }

    private static void AssertRow(string?[] actual, params string[] expected) =>
        Assert.True(expected.SequenceEqual(actual), $"Expected [{string.Join(" | ", expected)}] but was [{string.Join(" | ", actual)}].");

    [Fact]
    public void Import_FillsCardinalityIfcVersionAndMissingCells()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path,
            [T("A.Entity"), T("R.Property"), T("R.Cardinality"), T("SpecificationIfcVersion"), T("Other")],
            [T("IFCWALL"), T("FireRating"), null, null, T("x")],
            [T("IFCSLAB"), null, T("optional"), T("IFC4"), T("y")]);

        var imported = Import(path, Settings());

        Assert.Equal(["A.Entity", "R.Property", "R.Cardinality", "SpecificationIfcVersion"], imported.Frame.Columns);
        AssertRow(imported.Frame.Rows[0], "IFCWALL", "FireRating", "required", "IFC4|IFC2X3");
        AssertRow(imported.Frame.Rows[1], "IFCSLAB", "_MISSING_", "optional", "IFC4");
    }

    [Fact]
    public void Import_WithoutIfcVersionColumn_AddsTheColumnLast()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, [T("A.Entity"), T("R.Property")], [T("IFCWALL"), T("FireRating")]);

        var imported = Import(path, Settings());

        Assert.Equal(["A.Entity", "R.Property", "SpecificationIfcVersion"], imported.Frame.Columns);
        AssertRow(imported.Frame.Rows[0], "IFCWALL", "FireRating", "IFC4|IFC2X3");
        Assert.Equal(["SpecificationIfcVersion"], imported.Columns.Specification);
    }

    [Fact]
    public void Import_WithSkippedRows_ReadsTheHeaderBelow()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, [T("Remark")], [T("A.Entity")], [T("IFCWALL")]);

        var imported = Import(path, Settings(skip: 1));

        Assert.Equal("IFCWALL", imported.Frame.Rows[0][0]);
    }

    [Fact]
    public void Import_WithEntityBasedApplicability_IgnoresTheOtherApplicabilityColumns()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, [T("A.Entity"), T("A.Property"), T("R.Property")], [T("IFCWALL"), T("x"), T("y")]);

        var imported = Import(path, Settings(entityBased: true));

        Assert.Equal(["A.Entity", "R.Property", "SpecificationIfcVersion"], imported.Frame.Columns);
    }

    [Fact]
    public void Import_WithUnknownSheet_Throws()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path, [T("A.Entity")]);

        Assert.Throws<XlsxException>(() => Import(path, Settings(sheet: "Missing")));
    }

    [Fact]
    public void Import_WithEmptySheet_ThrowsNoKnownColumns()
    {
        using var directory = new TempDirectory();
        var path = Save(directory.Path);

        Assert.Throws<ImportException>(() => Import(path, Settings()));
    }

    // ---- differential tests against excel_to_spec_list --------------------------------------------

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void Import_ExampleWorkbook_MatchesPython(string workbook)
    {
        var path = RepositoryRoot.ExcelFile(workbook);
        Ids4AllSettings settings;
        using (var opened = XlsxWorkbook.Open(path)) settings = MetadataReader.Read(opened);

        AssertSameAsPython(path, settings);
    }

    public static TheoryData<string> ExampleWorkbooks() => MetadataReaderTests.ExampleWorkbooks();

    [PythonFact]
    public void Import_HandMadeSheets_MatchPython()
    {
        using var directory = new TempDirectory();
        var cases = new (TestCell?[][] Rows, int Skip, bool EntityBased)[]
        {
            ([[T("A.Entity"), T("R.Property"), T("R.Cardinality")], [T("IFCWALL"), T("a"), null], [T("IFCSLAB"), T("b"), T("optional")]], 0, false),
            ([[T("A.Entity"), T("SpecificationIfcVersion")], [T("IFCWALL"), null], [T("IFCSLAB"), T("IFC4")]], 0, false),
            ([[T("x")], [T("A.Entity"), T("A.Property"), T("R.Attribute")], [T("IFCWALL"), T("p"), T("Name")]], 1, true),
            ([[T("A.Entity"), T("R.PropertyValue")], [T("IFCWALL"), TestCell.Number("5")], [T("IFCWALL"), TestCell.Number("2.5")], [T("IFCWALL"), TestCell.Bool(true)]], 0, false),
            ([[T("A.Entity"), T("R.PropertyValue")], [T("IFCWALL"), TestCell.Bool(true)], [T("IFCWALL"), TestCell.Number("1")]], 0, false),
            ([[T("A.Entity"), T("Phase"), T("Role")], [T("IFCWALL"), T("P1"), T("NA")], [], [T("IFCSLAB"), null, T("R1")]], 0, false),
            ([[T("A.Entity"), null, T("R.Property")], [T("IFCWALL"), T("ignored"), T("p")]], 0, false),
            ([[T("A.Entity"), T("R.Property"), T("Unrelated")], [T("IFCWALL"), T("p"), T("q")]], 0, false),
            ([[T("A.Entity"), T("A.Entity")], [T("IFCWALL"), T("IFCSLAB")]], 0, false),
            ([[T("Name"), T("Other")], [T("x"), T("y")]], 0, false),
            ([[T("A.Entity")]], 0, false),
            ([], 0, false),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var path = Save(directory.Path, cases[i].Rows);
            try
            {
                AssertSameAsPython(path, Settings(skip: cases[i].Skip, entityBased: cases[i].EntityBased));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Import case {i} differs: {ex.Message}", ex);
            }
        }
    }

    private static void AssertSameAsPython(string path, Ids4AllSettings settings)
    {
        var expected = PandasReference.ImportFrame(path, settings.SheetName, settings.SkippedRows, settings.IfcVersion, settings.IsEntityBasedApplicability);
        ImportedSheet? actual = null;
        string? error = null;
        try
        {
            actual = Import(path, settings);
        }
        catch (Exception ex) when (ex is ImportException or XlsxException)
        {
            error = ex.Message;
        }

        if (expected.Error is not null)
        {
            Assert.True(error is not null, $"Python failed ({expected.Error}) but the C# importer returned a frame.");
            // Python lists the repeated column names in random order. The other messages are the same.
            if (!expected.Error.Contains("Column names must be unique"))
                Assert.EndsWith(error!, expected.Error);
            return;
        }

        Assert.True(error is null, $"The C# importer failed ({error}).");
        Assert.Equal(expected.Data.Columns, actual!.Frame.Columns);
        Assert.Equal(expected.Data.Rows.Count, actual.Frame.Rows.Count);
        for (var i = 0; i < actual.Frame.Rows.Count; i++)
            Assert.True(expected.Data.Rows[i].SequenceEqual(actual.Frame.Rows[i]!),
                $"Row {i} differs.\nPython: {string.Join(" | ", expected.Data.Rows[i])}\nC#:     {string.Join(" | ", actual.Frame.Rows[i])}");
    }
}
