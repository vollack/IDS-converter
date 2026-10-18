using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Tests.Excel;
using IDS4ALL.Tests.Import;
using IDS4ALL.Tests.Parsing;
using static IDS4ALL.Core.Import.ColumnNames;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Merging;

/// <summary>
/// Differential tests: the list of specifications has the same content as the result of excel_to_spec_list in custom_functions.py.
/// </summary>
public sealed class MergingEquivalenceTests
{
    private static TestCell T(string text) => TestCell.Text(text);

    public static TheoryData<string> ExampleWorkbooks() => MetadataReaderTests.ExampleWorkbooks();

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void ExampleWorkbook_MatchesPython(string workbook)
    {
        var path = RepositoryRoot.ExcelFile(workbook);
        Ids4AllSettings settings;
        using (var opened = XlsxWorkbook.Open(path)) settings = MetadataReader.Read(opened);

        AssertSameAsPython(path, settings);
    }

    [PythonFact]
    public void HandMadeSheets_MatchPython()
    {
        using var directory = new TempDirectory();
        var ent = "A.Entity";
        var cases = new (TestCell?[][] Rows, string[] SeparateBy, bool EntityBased)[]
        {
            // the same applicability merges the requirements
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("R.PropertyValue")],
              [T("IFCWALL"), T("p"), T("s"), T("1")], [T("IFCWALL"), T("p"), T("s"), T("2")], [T("IFCWALL"), T("q"), T("s"), T("3")]], [], false),
            // a general specification receives the requirements of the specific one
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("R.PropertyValue")],
              [T("IFCWALL"), T("p"), T("s"), T("1")], [T("IFCWALL.STANDARD"), T("p"), T("s"), T("2")], [T("IFCWALL.STANDARD"), T("q"), T("s"), T("3")]], [], false),
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("R.PropertyValue")],
              [T("IFCWALL.STANDARD"), T("p"), T("s"), T("1")], [T("IFCWALL"), T("p"), T("s"), T("2")]], [], false),
            // OR values in the applicability
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("SpecificationName")],
              [T("IFCWALL|IFCSLAB"), T("p"), T("s"), T("Walls and slabs")], [T("IFCWALL"), T("q"), T("s"), T("Walls")]], [], false),
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("SpecificationCardinality")],
              [T("IFCWALL|IFCSLAB"), T("p"), T("s"), T("required")], [T("IFCWALL"), T("q"), T("s"), T("required")]], [], false),
            // IFC versions
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("SpecificationIfcVersion")],
              [T("IFCWALL"), T("p"), T("s"), T("IFC4|IFC2X3")], [T("IFCWALL"), T("q"), T("s"), T("IFC4")]], [], false),
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("SpecificationIfcVersion")],
              [T("IFCWALL"), T("p"), T("s"), T("IFC4")], [T("IFCWALL"), T("q"), T("s"), T("IFC4|IFC2X3")], [T("IFCWALL"), T("r"), T("s"), T("IFC2X3")]], [], false),
            ([[T(ent), T("R.Property"), T("SpecificationIfcVersion")], [T("IFCWALL"), T("p"), T("IFC5")]], [], false),
            // separation by general data
            ([[T(ent), T("Phase"), T("Role"), T("R.Property")],
              [T("IFCWALL"), T("LP1|LP2"), T("A"), T("p")], [T("IFCWALL"), T("LP2"), T("B"), T("q")], [T("IFCSLAB"), T("LP1"), T("A"), T("p")]], ["Phase"], false),
            ([[T(ent), T("Phase"), T("Role"), T("R.Property")],
              [T("IFCWALL"), T("LP1|LP2"), T("A|B"), T("p")], [T("IFCWALL"), T("LP2"), T("B"), T("q")]], ["Phase", "Role"], false),
            ([[T(ent), T("Phase"), T("R.Property")], [T("IFCWALL"), null, T("p")], [T("IFCWALL"), T("LP1"), T("q")]], ["Phase"], false),
            // descriptions
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("R.Description.Property")],
              [T("IFCWALL"), T("p"), T("s"), T("first")], [T("IFCSLAB"), T("p"), T("s"), null], [T("IFCDOOR"), T("p"), T("t"), T("second")]], [], false),
            // a split applicability matches several earlier specifications: the last one takes the requirements
            ([[T(ent), T("R.Property"), T("R.PropertySet"), T("SpecificationName")],
              [T("IFCWALL"), T("p"), T("s"), T("N1")], [T("IFCWALL"), T("q"), T("s"), T("N2")], [T("IFCWALL|IFCSLAB"), T("r"), T("s"), T("N3")]], [], false),
            // values with repeated items merge with append_unique
            ([[T(ent), T("R.Property"), T("R.PropertyValue"), T("R.Description.Property")],
              [T("IFCWALL"), T("p"), T("x"), T("d1")], [T("IFCWALL"), T("p"), T("a|a"), T("d2")], [T("IFCWALL"), T("p"), T("x|b|b"), T("d3")]], [], false),
            // errors
            ([[T(ent), T("R.PropertyValue"), T("R.Property")], [T("IFCWALL"), T("pattern=a|b"), T("p")], [T("IFCWALL"), T("c|pattern=x"), T("p")]], [], false),
            ([[T(ent), T("R.Property"), T("R.PropertySet")], [T("IFCWALL"), T("a\\&b"), T("s")]], [], false),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var path = Path.Combine(directory.Path, "sheet.xlsx");
            new XlsxBuilder().Sheet("Data", cases[i].Rows).Save(path);
            try
            {
                AssertSameAsPython(path, Settings(cases[i].SeparateBy, cases[i].EntityBased));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Sheet case {i} differs: {ex.Message}", ex);
            }
        }
    }

    [PythonFact]
    public void RandomSheets_MatchPython()
    {
        var random = new Random(Seed);
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "sheet.xlsx");
        var compared = 0;
        for (var run = 0; run < Runs; run++)
        {
            var columns = Pools.Keys.OrderBy(_ => random.Next()).Take(random.Next(2, 14)).ToList();
            if (!columns.Any(column => column.StartsWith("A.", StringComparison.Ordinal))) columns.Add("A.Entity");
            var pools = columns.Select(column => Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomCell(random, column)).ToArray()).ToArray();

            var rows = new List<TestCell?[]> { columns.Select(column => (TestCell?)T(column)).ToArray() };
            for (var r = random.Next(1, 10); r > 0; r--)
                rows.Add(columns.Select((_, c) => pools[c][random.Next(pools[c].Length)]).ToArray());

            var separateBy = new[] { "Phase", "Role", "Usecase" }.Where(_ => random.Next(3) == 0).ToArray();
            new XlsxBuilder().Sheet("Data", [.. rows]).Save(path);
            try
            {
                if (AssertSameAsPython(path, Settings(separateBy, random.Next(5) == 0))) compared++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random sheet {run} differs ({string.Join(", ", columns)}): {ex.Message}", ex);
            }
        }

        // Most sheets must reach the comparison of the specifications. Otherwise the test only compares error messages.
        Assert.True(compared >= Runs / 2, $"Only {compared} of {Runs} random sheets gave specifications in Python.");
    }

    private static Ids4AllSettings Settings(string[] separateBy, bool entityBased) =>
        new("Data", "IFC4|IFC2X3", separateBy, 0, entityBased, "Title", null, null, null, null);

    /// <returns>True if Python gave a list of specifications (and C# the same list).</returns>
    private static bool AssertSameAsPython(string path, Ids4AllSettings settings)
    {
        var expected = MergingReference.Specs(path, settings.SheetName, settings.SkippedRows, settings.IfcVersion,
            settings.IsEntityBasedApplicability, settings.SeparateBy);

        SpecificationList? actual = null;
        string? error = null;
        try
        {
            using var workbook = XlsxWorkbook.Open(path);
            actual = SpecificationBuilder.Build(SheetImporter.Import(workbook, settings), settings);
        }
        catch (Exception ex) when (ex is ImportException or XlsxException)
        {
            error = "Exception: " + ex.Message;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or ArgumentOutOfRangeException or InvalidOperationException)
        {
            error = "Crash: " + ex.GetType().Name + ": " + ex.Message;
        }

        var pythonError = Tree.Get(expected, "error") as string;
        if (pythonError is not null)
        {
            Assert.True(error is not null, $"Python failed ({pythonError}) but C# returned specifications.");
            // Messages of the converter itself must be equal. Other Python errors only need an error in C#.
            if (pythonError.StartsWith("Exception: ", StringComparison.Ordinal) && !error!.StartsWith("Crash", StringComparison.Ordinal))
                Assert.True(pythonError == error || IsImportStageError(pythonError), $"Python failed with '{pythonError}' but C# failed with '{error}'.");
            return false;
        }

        Assert.True(error is null, $"C# failed ({error}) but Python returned specifications.");
        var difference = Tree.FirstDifference(Tree.Get(expected, "ok"), MergingReference.ToTree(actual!));
        Assert.True(difference is null, difference);
        return true;
    }

    /// <summary>Errors of the import stage have other texts in pandas and in the C# reader. Only the presence of the error is compared.</summary>
    private static bool IsImportStageError(string message) =>
        message.Contains("Worksheet", StringComparison.Ordinal) || message.Contains("Usecols", StringComparison.Ordinal);

    // ---- requirement merging ---------------------------------------------------------------------

    [PythonFact]
    public void RequirementDictionaries_MergeLikePython()
    {
        var random = new Random(Seed + 1);
        var cases = new List<(FacetData First, FacetData Second, string[] NotCompared, bool MergeOnlyValues, bool Reverse)>();
        for (var i = 0; i < Runs * 6; i++)
        {
            var first = RandomRequirement(random);
            var second = random.Next(3) == 0 ? RandomRequirement(random) : Variant(random, first);
            string[] notCompared = random.Next(4) switch
            {
                0 => [AttributeValue, PropertyValue],
                1 => [Entity, PredefinedType, AttributeValue, PropertyValue],
                2 => [PropertyValue],
                _ => [],
            };
            cases.Add((first, second, notCompared, random.Next(2) == 0, random.Next(2) == 0));
        }

        var expected = MergingReference.Requirements(cases.Select(c => (object)new
        {
            first = c.First.Select(p => new object[] { p.Key, p.Value }),
            second = c.Second.Select(p => new object[] { p.Key, p.Value }),
            notCompared = c.NotCompared,
            mergeOnlyValues = c.MergeOnlyValues,
            reverse = c.Reverse,
        }));

        for (var i = 0; i < cases.Count; i++)
        {
            var (first, second, notCompared, mergeOnlyValues, reverse) = cases[i];
            var firstCopy = SpecificationDraft.DeepCopy(first);
            var secondCopy = SpecificationDraft.DeepCopy(second);
            var different = RequirementMerger.CompareAndMerge(firstCopy, secondCopy, notCompared, mergeOnlyValues, reverse);
            var actual = Tree.Obj(("ok", Tree.Obj(("different", different), ("first", MergingReference.ToTree(firstCopy)), ("second", MergingReference.ToTree(secondCopy)))));

            var difference = Tree.FirstDifference(expected[i], actual);
            Assert.True(difference is null,
                $"Case {i} (notCompared={string.Join(",", notCompared)}, mergeOnlyValues={mergeOnlyValues}, reverse={reverse}): {difference}\nfirst={string.Join(";", first.Select(p => p.Key + "=" + string.Join("|", p.Value)))}\nsecond={string.Join(";", second.Select(p => p.Key + "=" + string.Join("|", p.Value)))}");
        }
    }

    // ---- random data -------------------------------------------------------------------------------

    private static int Seed => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261018;

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var runs) ? runs : 150;

    private static readonly Dictionary<string, string[]> Pools = new()
    {
        ["A.Entity"] = ["IFCWALL", "IFCSLAB", "IFCWALL.STANDARD", "IFCDOOR|IFCWINDOW", "IFCWALL|IFCSLAB", "IFCWALL.STANDARD|IFCWALL.USERDEFINED", "IFCSLAB.FLOOR"],
        ["A.Property"] = ["IsExternal", "LoadBearing"],
        ["A.PropertySet"] = ["Pset_WallCommon", "Pset_Fire"],
        ["A.PropertyValue"] = ["true", "1", "pattern=a.*"],
        ["A.PropertyDatatype"] = ["IFCBOOLEAN", "IfcLabel"],
        ["A.Material"] = ["Concrete", "Steel"],
        ["A.Attribute"] = ["Name", "Description"],
        ["A.AttributeValue"] = ["W1", "pattern=^W"],
        ["A.Classification"] = ["21.2", "22"],
        ["A.ClassificationSystem"] = ["Uniclass", "OmniClass"],
        ["A.PartOfEntity"] = ["IFCBUILDINGSTOREY", "IFCSPACE.INTERNAL"],
        ["A.PartOfRelation"] = ["IFCRELCONTAINEDINSPATIALSTRUCTURE", "IfcRelAggregates"],
        ["R.Entity"] = ["IFCWALL", "IFCSLAB", "IFCWALL.STANDARD"],
        ["R.Description.Entity"] = ["entity description"],
        ["R.Property"] = ["IsExternal", "FireRating", "LoadBearing", "Width"],
        ["R.PropertySet"] = ["Pset_WallCommon", "Pset_Fire"],
        ["R.PropertyValue"] = ["true", "false", "30", "60", "90", "pattern=^R", "\\>=3", "a|b", "x"],
        ["R.PropertyDatatype"] = ["IFCBOOLEAN", "IfcLabel", "IFCREAL", "IFCINTEGER"],
        ["R.PropertyURI"] = ["http://example.org/1"],
        ["R.Cardinality"] = ["required", "optional", "prohibited"],
        ["R.Description.Property"] = ["first description", "second description"],
        ["R.Material"] = ["Concrete", "Steel|Wood"],
        ["R.MaterialURI"] = ["http://example.org/m"],
        ["R.Attribute"] = ["Name", "Description"],
        ["R.AttributeValue"] = ["W1", "W2", "pattern=^W", "length=3"],
        ["R.Classification"] = ["21.2", "22"],
        ["R.ClassificationSystem"] = ["Uniclass", "OmniClass"],
        ["R.ClassificationURI"] = ["http://example.org/c"],
        ["R.PartOfEntity"] = ["IFCBUILDINGSTOREY", "IFCSPACE.INTERNAL"],
        ["R.PartOfRelation"] = ["IFCRELCONTAINEDINSPATIALSTRUCTURE", "IfcRelAggregates"],
        ["Phase"] = ["LP1", "LP2", "LP1|LP2", "LP3"],
        ["Role"] = ["Architect", "Engineer", "Architect|Engineer"],
        ["Usecase"] = ["UC1", "UC2"],
        ["SpecificationName"] = ["Spec A", "Spec B"],
        ["SpecificationCardinality"] = ["required", "optional", "prohibited", "maybe"],
        ["SpecificationIfcVersion"] = ["IFC4", "IFC2X3", "IFC4|IFC2X3", "IFC4X3_ADD2", "IFC2X3|IFC4|IFC4X3_ADD2"],
    };

    private static TestCell? RandomCell(Random random, string column)
    {
        if (random.Next(8) == 0) return null;
        var pool = Pools[column];
        var text = pool[random.Next(pool.Length)];
        // Rarely two AND values, or a value that is not valid.
        if (column.StartsWith("R.", StringComparison.Ordinal) && random.Next(15) == 0) text += "\\&" + pool[random.Next(pool.Length)];
        if (column == "SpecificationIfcVersion" && random.Next(40) == 0) text = "IFC5";
        return T(text);
    }

    private static readonly string[] RequirementKeys = [Entity, PredefinedType, Property, PropertySet, PropertyValue, PropertyDatatype, ColumnNames.Attribute, AttributeValue, Description, RequirementCardinality];
    private static readonly string[] RequirementValues = ["a", "b", "c", "pattern=x", "\\<5", "length=2", "IFCWALL"];

    private static FacetData RandomRequirement(Random random)
    {
        var data = new FacetData();
        foreach (var key in RequirementKeys.Where(_ => random.Next(3) == 0))
            data[key] = Enumerable.Range(0, random.Next(1, 4)).Select(_ => RequirementValues[random.Next(RequirementValues.Length)]).ToList();
        if (data.Count == 0) data[Property] = ["a"];
        return data;
    }

    private static FacetData Variant(Random random, FacetData data)
    {
        var copy = SpecificationDraft.DeepCopy(data);
        var key = RequirementKeys[random.Next(RequirementKeys.Length)];
        switch (random.Next(4))
        {
            case 0:
                copy.Remove(key);
                break;
            case 1:
                copy[key] = [RequirementValues[random.Next(RequirementValues.Length)]];
                break;
            case 2 when copy.Count > 0:
                copy.GetAt(random.Next(copy.Count)).Value.Add(RequirementValues[random.Next(RequirementValues.Length)]);
                break;
        }
        return copy;
    }
}
