using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Parsing;
using IDS4ALL.Tests.Excel;
using static IDS4ALL.Core.Import.ColumnNames;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Parsing;

public sealed class SheetGrouperTests
{
    private static TestCell T(string text) => TestCell.Text(text);

    private static GroupedSheet Group(string[] separateBy, params TestCell?[][] rows)
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "sheet.xlsx");
        new XlsxBuilder().Sheet("Data", rows).Save(path);
        var settings = new Ids4AllSettings("Data", "IFC4", separateBy, 0, false, "Title", null, null, null, null);

        using var workbook = TestWorkbook.Open(path);
        return SheetGrouper.Group(SheetImporter.Import(workbook, settings), separateBy);
    }

    private static string Show(GroupedSheet sheet, int row, string column) =>
        string.Join(",", sheet.Rows[row][sheet.Columns.ToList().IndexOf(column)]).Replace(DelimiterMergedRows, "<US>");

    [Fact]
    public void Group_MergesRowsThatDifferOnlyInTheValueColumns()
    {
        var sheet = Group([],
            [T("A.Entity"), T("R.Property"), T("R.PropertyValue")],
            [T("IFCWALL"), T("FireRating"), T("A")],
            [T("IFCWALL"), T("FireRating"), T("B")],
            [T("IFCSLAB"), T("FireRating"), T("C")]);

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("IFCWALL", Show(sheet, 0, "A.Entity"));
        Assert.Equal("FireRating", Show(sheet, 0, "R.Property"));
        Assert.Equal("A<US>B", Show(sheet, 0, "R.PropertyValue"));
        Assert.Equal("IFCSLAB", Show(sheet, 1, "A.Entity"));
        Assert.Equal("C", Show(sheet, 1, "R.PropertyValue"));
    }

    [Fact]
    public void Group_MergesRequirementsOfTheSameApplicabilityIntoLists()
    {
        var sheet = Group([],
            [T("A.Entity"), T("R.Property"), T("R.PropertyValue"), T("R.Cardinality")],
            [T("IFCWALL"), T("FireRating"), T("A"), null],
            [T("IFCWALL"), T("LoadBearing"), T("B"), T("optional")]);

        Assert.Equal(["A.Entity", "SpecificationIfcVersion"], sheet.Columns.Take(2));
        Assert.Equal(new[] { "R.Cardinality", "R.Property", "R.PropertyValue" }, sheet.AggregatedColumns.Order());
        Assert.Single(sheet.Rows);
        Assert.Equal("FireRating,LoadBearing", Show(sheet, 0, "R.Property"));
        Assert.Equal("A,B", Show(sheet, 0, "R.PropertyValue"));
        Assert.Equal("required,optional", Show(sheet, 0, "R.Cardinality"));
        Assert.Equal("IFCWALL", Show(sheet, 0, "A.Entity"));
        Assert.Equal("IFC4", Show(sheet, 0, "SpecificationIfcVersion"));
    }

    [Fact]
    public void Group_KeepsAnApplicabilityValueColumnAsKey()
    {
        var sheet = Group([],
            [T("A.Entity"), T("A.PropertyValue"), T("R.Property")],
            [T("IFCWALL"), T("1"), T("p")],
            [T("IFCWALL"), T("2"), T("p")]);

        Assert.Equal(2, sheet.Rows.Count);
        Assert.DoesNotContain("A.PropertyValue", sheet.AggregatedColumns);
    }

    [Fact]
    public void Group_WithoutValueColumns_RemovesDuplicateRows()
    {
        var sheet = Group([],
            [T("A.Entity"), T("R.Property")],
            [T("IFCWALL"), T("p")],
            [T("IFCWALL"), T("p")],
            [T("IFCSLAB"), T("p")]);

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal("p", Show(sheet, 0, "R.Property"));
        Assert.Equal("p", Show(sheet, 1, "R.Property"));
    }

    [Fact]
    public void Group_WithoutRequirementColumns_HasNoAggregatedColumns()
    {
        var sheet = Group([], [T("A.Entity")], [T("IFCWALL")], [T("IFCWALL")], [T("IFCSLAB")]);

        Assert.Empty(sheet.AggregatedColumns);
        Assert.Equal(2, sheet.Rows.Count);
    }

    [Fact]
    public void Group_KeepsTheSeparationColumnsAsKeys()
    {
        TestCell?[][] rows =
        [
            [T("A.Entity"), T("Phase"), T("Role")],
            [T("IFCWALL"), T("P1"), T("R1")],
            [T("IFCWALL"), T("P2"), T("R1")],
        ];

        var separated = Group(["Phase"], rows);
        var merged = Group([], rows);

        Assert.Equal(2, separated.Rows.Count);
        Assert.Equal("P1", Show(separated, 0, "Phase"));
        Assert.Equal(["Role"], separated.AggregatedColumns);
        Assert.Single(merged.Rows);
        Assert.Equal("P1,P2", Show(merged, 0, "Phase"));
        Assert.Equal("R1,R1", Show(merged, 0, "Role"));
    }

    [Fact]
    public void Group_KeepsTheOrderOfTheFirstRowOfEachGroup()
    {
        var sheet = Group([],
            [T("A.Entity"), T("R.Property")],
            [T("C"), T("1")],
            [T("A"), T("2")],
            [T("C"), T("3")],
            [T("B"), T("4")]);

        Assert.Equal(["C", "A", "B"], Enumerable.Range(0, sheet.Rows.Count).Select(row => Show(sheet, row, "A.Entity")));
        Assert.Equal("1,3", Show(sheet, 0, "R.Property"));
    }

    [Fact]
    public void Group_TreatsMissingCellsAsOneValue()
    {
        var sheet = Group([],
            [T("A.Entity"), T("A.Property"), T("R.Property")],
            [T("IFCWALL"), null, T("p")],
            [T("IFCWALL"), null, T("q")],
            [T("IFCWALL"), T("x"), T("r")]);

        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal($"p,q", Show(sheet, 0, "R.Property"));
        Assert.Equal(KeywordMissing, Show(sheet, 0, "A.Property"));
    }

    [Fact]
    public void Group_WithoutDataRows_ReturnsNoRows()
    {
        var sheet = Group([], [T("A.Entity"), T("R.PropertyValue")]);

        Assert.Empty(sheet.Rows);
    }

    [Fact]
    public void Row_ReturnsTheRequestedColumnsInOrder()
    {
        var sheet = Group([], [T("A.Entity"), T("R.Property")], [T("IFCWALL"), T("p")]);

        var row = sheet.Row(0, ["R.Property", "A.Entity"]);

        Assert.Equal(["R.Property", "A.Entity"], row.Select(cell => cell.Column));
        Assert.Equal(["p"], row[0].Values);
    }
}
