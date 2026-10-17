using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Parsing;
using IDS4ALL.Tests.Excel;
using IDS4ALL.Tests.Import;
using static IDS4ALL.Core.Import.ColumnNames;
using static IDS4ALL.Tests.Import.MetadataReaderTests;

namespace IDS4ALL.Tests.Parsing;

/// <summary>
/// Differential tests: the grouping and the facet functions give the same results as custom_functions.py.
/// Python runs the real excel_to_spec_list up to the grouped dataframe and then the real facet functions.
/// A result is {"ok": value} or {"error": text}, like the helper _guarded in the reference script.
/// </summary>
public sealed class ParsingEquivalenceTests
{
    private static TestCell T(string text) => TestCell.Text(text);

    // ---- whole sheets ---------------------------------------------------------------------------------

    public static TheoryData<string> ExampleWorkbooks() => MetadataReaderTests.ExampleWorkbooks();

    [PythonTheory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void ExampleWorkbook_MatchesPython(string workbook)
    {
        var path = RepositoryRoot.ExcelFile(workbook);
        Ids4AllSettings settings;
        using (var opened = XlsxWorkbook.Open(path)) settings = MetadataReader.Read(opened);

        AssertSheetSameAsPython(path, settings);
    }

    [PythonFact]
    public void HandMadeSheets_MatchPython()
    {
        using var directory = new TempDirectory();
        var cases = new (TestCell?[][] Rows, string[] SeparateBy, bool EntityBased)[]
        {
            ([[T("A.Entity"), T("R.Property"), T("R.PropertyValue")], [T("IFCWALL"), T("p"), T("1")], [T("IFCWALL"), T("p"), T("2")], [T("IFCSLAB"), T("p"), T("3")]], [], false),
            ([[T("A.Entity"), T("R.Property"), T("R.AttributeValue"), T("R.Attribute")], [T("IFCWALL"), T("p"), T("x|y"), T("Name")], [T("IFCWALL"), T("p"), T("pattern=a|b"), T("Name")]], [], false),
            ([[T("A.Entity"), T("R.Property")], [T("IFCWALL"), T("p")], [T("IFCWALL"), T("q")], [T("IFCWALL"), T("p")]], [], false),
            ([[T("A.Entity"), T("Phase"), T("Role"), T("R.Property")], [T("IFCWALL"), T("P1"), T("R1"), T("p")], [T("IFCWALL"), T("P2"), T("R1"), T("q")]], ["Phase"], false),
            ([[T("A.Entity"), T("Phase"), T("Role"), T("R.Property")], [T("IFCWALL"), T("P1"), T("R1"), T("p")], [T("IFCWALL"), T("P2"), T("R1"), T("q")]], ["Phase", "Role"], false),
            ([[T("A.Entity"), T("Phase")], [T("IFCWALL"), T("P1")], [T("IFCWALL"), T("P2")]], [], false),
            ([[T("A.Entity"), T("A.Property"), T("R.Property")], [T("IFCWALL"), T("x"), T("p")], [T("IFCWALL"), null, T("q")]], [], true),
            ([[T("A.Entity"), T("R.PropertyValue")]], [], false),
            ([[T("A.Entity")]], [], false),
            ([[T("A.Entity"), T("R.Property"), T("R.PropertySet"), T("R.PropertyDatatype"), T("R.Cardinality"), T("R.Description.Property")],
                [T("IFCWALL"), T("a\\&b"), T("s\\&t"), T("ifclabel\\&ifcreal"), null, T("d1\\&d2")]], [], false),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var path = Path.Combine(directory.Path, "sheet.xlsx");
            new XlsxBuilder().Sheet("Data", cases[i].Rows).Save(path);
            try
            {
                AssertSheetSameAsPython(path, Settings(cases[i].SeparateBy, cases[i].EntityBased));
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
        for (var run = 0; run < Runs; run++)
        {
            var columns = KnownColumns.OrderBy(_ => random.Next()).Take(random.Next(1, 14)).ToList();
            if (random.Next(5) == 0) columns.Insert(random.Next(columns.Count + 1), "Remarks");
            var pools = columns.Select(_ => Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomSheetCell(random)).ToArray()).ToArray();

            var rows = new List<TestCell?[]> { columns.Select(column => (TestCell?)T(column)).ToArray() };
            for (var r = random.Next(0, 9); r > 0; r--)
                rows.Add(columns.Select((_, c) => pools[c][random.Next(pools[c].Length)]).ToArray());

            var separateBy = new[] { "Phase", "Role", "Usecase" }.Where(_ => random.Next(3) == 0).ToArray();
            new XlsxBuilder().Sheet("Data", [.. rows]).Save(path);
            try
            {
                AssertSheetSameAsPython(path, Settings(separateBy, random.Next(4) == 0));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random sheet {run} differs ({string.Join(", ", columns)}): {ex.Message}", ex);
            }
        }
    }

    private static Ids4AllSettings Settings(string[] separateBy, bool entityBased) =>
        new("Data", "IFC4|IFC2X3", separateBy, 0, entityBased, "Title", null, null, null, null);

    private static void AssertSheetSameAsPython(string path, Ids4AllSettings settings)
    {
        var expectedGroup = ParsingReference.Group(path, settings.SheetName, settings.SkippedRows, settings.IfcVersion,
            settings.IsEntityBasedApplicability, settings.SeparateBy);

        SheetColumns? columns = null;
        GroupedSheet? grouped = null;
        string? error = null;
        try
        {
            using var workbook = XlsxWorkbook.Open(path);
            var imported = SheetImporter.Import(workbook, settings);
            columns = imported.Columns;
            grouped = SheetGrouper.Group(imported, settings.SeparateBy);
        }
        catch (Exception ex) when (ex is ImportException or XlsxException)
        {
            error = ex.Message;
        }

        var pythonError = Tree.Get(expectedGroup, "error") as string;
        if (pythonError is not null)
        {
            Assert.True(error is not null, $"Python failed ({pythonError}) but C# returned a grouped sheet.");
            return;
        }

        Assert.True(error is null, $"C# failed ({error}) but Python returned a grouped sheet.");
        AssertGroupedSheet(Tree.Get(expectedGroup, "ok"), grouped!);

        var dictionaryGroups = new[] { columns!.General, columns.Specification }.Where(list => list.Count > 0).ToList();
        var facetGroups = new[]
        {
            columns.ApplicabilityEntity, columns.ApplicabilityProperty, columns.ApplicabilityMaterial, columns.ApplicabilityAttribute,
            columns.ApplicabilityClassification, columns.ApplicabilityPartOf, columns.RequirementEntity, columns.RequirementProperty,
            columns.RequirementMaterial, columns.RequirementAttribute, columns.RequirementClassification, columns.RequirementPartOf,
        }.Where(list => list.Count > 0).ToList();

        var expectedFacets = ParsingReference.Facets(path, settings.SheetName, settings.SkippedRows, settings.IfcVersion,
            settings.IsEntityBasedApplicability, settings.SeparateBy, dictionaryGroups, facetGroups);
        var expectedOk = Tree.Get(expectedFacets, "ok");
        Assert.True(expectedOk is not null, $"Python failed on the facets: {Tree.Get(expectedFacets, "error")}");

        var expectedDictionaries = (List<object?>)Tree.Get(expectedOk, "dictionary")!;
        var expectedFacetRows = (List<object?>)Tree.Get(expectedOk, "facets")!;
        for (var row = 0; row < grouped!.Rows.Count; row++)
        {
            for (var g = 0; g < dictionaryGroups.Count; g++)
            {
                var cells = grouped.Row(row, dictionaryGroups[g]);
                AssertSameResult(((List<object?>)expectedDictionaries[g]!)[row], Guarded(() => Tree.Pairs(FacetParser.RowToDictionary(cells), Tree.Strings)),
                    $"general or specification data {g}, row {row}");
            }

            for (var g = 0; g < facetGroups.Count; g++)
            {
                var cells = grouped.Row(row, facetGroups[g]);
                AssertSameResult(((List<object?>)expectedFacetRows[g]!)[row], Guarded(() => PipelineResult(cells, settings.IsEntityBasedApplicability)),
                    $"facet columns {string.Join(", ", facetGroups[g])}, row {row}");
            }
        }
    }

    private static void AssertGroupedSheet(object? expected, GroupedSheet actual)
    {
        var columns = ((List<object?>)Tree.Get(expected, "columns")!).Cast<string>().ToList();
        Assert.Equal(columns.Order(), actual.Columns.Order());
        var rows = (List<object?>)Tree.Get(expected, "rows")!;
        Assert.Equal(rows.Count, actual.Rows.Count);

        var actualColumns = actual.Columns.ToList();
        for (var r = 0; r < rows.Count; r++)
        {
            var cells = (List<object?>)rows[r]!;
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = (List<object?>)cells[c]!;
                var name = columns[c];
                Assert.True((bool)cell[0]! == actual.AggregatedColumns.Contains(name),
                    $"Row {r}, column {name}: Python has {((bool)cell[0]! ? "a list" : "a single value")}.");
                var difference = Tree.FirstDifference(cell[1], Tree.Strings(actual.Rows[r][actualColumns.IndexOf(name)]), $"row {r}, column {name}");
                Assert.True(difference is null, difference);
            }
        }
    }

    // ---- facet functions on single rows ---------------------------------------------------------------

    private static readonly string[][] FacetColumnLists =
    [
        ["A.Entity"],
        ["A.Property", "A.PropertySet", "A.PropertyValue", "A.PropertyDatatype"],
        ["A.Material"],
        ["A.Attribute", "A.AttributeValue"],
        ["A.Classification", "A.ClassificationSystem"],
        ["A.PartOfEntity", "A.PartOfRelation"],
        ["R.Entity", "R.Description.Entity"],
        ["R.Property", "R.PropertySet", "R.PropertyValue", "R.PropertyDatatype", "R.PropertyURI", "R.Cardinality", "R.Description.Property"],
        ["R.Material", "R.MaterialURI", "R.Cardinality"],
        ["R.Attribute", "R.AttributeValue", "R.Cardinality"],
        ["R.Classification", "R.ClassificationSystem", "R.ClassificationURI", "R.Cardinality"],
        ["R.PartOfEntity", "R.PartOfRelation", "R.Cardinality"],
    ];

    [PythonFact]
    public void RandomRows_GiveTheSameFacetsAsPython()
    {
        var random = new Random(Seed);
        for (var run = 0; run < Math.Max(1, Runs / 5); run++)
        {
            foreach (var entityBased in new[] { false, true })
            {
                var rows = Enumerable.Range(0, 120).Select(_ => RandomRow(random, FacetColumnLists[random.Next(FacetColumnLists.Length)])).ToList();
                var expected = Assert.IsType<List<object?>>(ParsingReference.Pipeline(rows, entityBased));
                for (var i = 0; i < rows.Count; i++)
                {
                    var cells = rows[i].Cells;
                    AssertSameResult(expected[i], Guarded(() => PipelineResult(cells, entityBased)), Describe(rows[i], entityBased));
                }
            }
        }
    }

    [PythonFact]
    public void RandomRows_GiveTheSameDictionariesAsPython()
    {
        var random = new Random(Seed);
        var general = new[] { "Phase", "Role", "Usecase" };
        for (var run = 0; run < Math.Max(1, Runs / 5); run++)
        {
            var rows = Enumerable.Range(0, 200).Select(_ => RandomRow(random, general.Take(random.Next(1, 4)).ToArray())).ToList();
            var expected = Assert.IsType<List<object?>>(ParsingReference.RowToDictionary(rows));
            for (var i = 0; i < rows.Count; i++)
            {
                var cells = rows[i].Cells;
                AssertSameResult(expected[i], Guarded(() => Tree.Pairs(FacetParser.RowToDictionary(cells), Tree.Strings)), Describe(rows[i], false));
            }
        }
    }

    private sealed record SeparateCase(string Combined, string Second, string Delimiter, List<(string Key, List<string> Values)> Entries);

    [PythonFact]
    public void RandomDictionaries_SeparateLikePython()
    {
        var random = new Random(Seed);
        var keys = new[] { Entity, PartOfEntity, Material, Property };
        var seconds = new Dictionary<string, string> { [Entity] = PredefinedType, [PartOfEntity] = PartOfPredefinedType, [Material] = "Second", [Property] = "Second" };
        var delimiters = new[] { ".", ".", ":" };
        for (var run = 0; run < Math.Max(1, Runs / 5); run++)
        {
            var cases = Enumerable.Range(0, 200).Select(_ =>
            {
                var combined = keys[random.Next(keys.Length)];
                var entries = new List<(string Key, List<string> Values)>
                {
                    (combined, Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomEntity(random)).ToList()),
                };
                if (random.Next(3) == 0) entries.Add(("Other", ["x"]));
                return new SeparateCase(combined, seconds[combined], delimiters[random.Next(delimiters.Length)], entries);
            }).ToList();

            var json = cases.Select(c => new
            {
                dict = c.Entries.Select(entry => new object[] { entry.Key, entry.Values }),
                combined = c.Combined, second = c.Second, delimiter = c.Delimiter,
            });
            var expected = Assert.IsType<List<object?>>(ParsingReference.SeparateValue(json));
            for (var i = 0; i < cases.Count; i++)
            {
                var dictionary = ToDictionary(cases[i].Entries);
                var actual = Guarded(() =>
                {
                    FacetParser.SeparateValue(dictionary, cases[i].Combined, cases[i].Second, cases[i].Delimiter);
                    return Tree.Pairs(dictionary, Tree.Strings);
                });
                AssertSameResult(expected[i], actual, $"{cases[i].Combined} with '{cases[i].Delimiter}': {string.Join("; ", cases[i].Entries.Select(e => e.Key + "=" + Tree.Describe(Tree.Strings(e.Values))))}");
            }
        }
    }

    [PythonFact]
    public void RandomDictionaries_CombineLikePython()
    {
        var random = new Random(Seed);
        var keys = new[] { "Entity", "Property", "PropertySet", "Value" };
        for (var run = 0; run < Math.Max(1, Runs / 5); run++)
        {
            var cases = Enumerable.Range(0, 60).Select(_ =>
                Enumerable.Range(0, random.Next(0, 3)).Select(_ =>
                    keys.OrderBy(_ => random.Next()).Take(random.Next(0, 3))
                        .Select(key => (Key: key, Values: Enumerable.Range(0, random.Next(1, 4)).Select(_ => "v" + random.Next(3)).ToList()))
                        .ToList()).ToList()).ToList();

            var json = cases.Select(items => items.Select(entries => entries.Select(entry => new object[] { entry.Key, entry.Values })));
            var expected = Assert.IsType<List<object?>>(ParsingReference.Combinations(json));
            for (var i = 0; i < cases.Count; i++)
            {
                var data = cases[i].Select(ToDictionary).ToList();
                var actual = Guarded(() => Tree.Items(FacetParser.GenerateCombinations(data),
                    combination => Tree.Items(combination, dictionary => Tree.Pairs(dictionary, Tree.Strings))));
                AssertSameResult(expected[i], actual, string.Join(" + ", data.Select(d => Tree.Describe(Tree.Pairs(d, Tree.Strings)))));
            }
        }
    }

    [PythonFact]
    public void IsComplexRestriction_MatchesPython()
    {
        var random = new Random(Seed);
        var values = new List<string> { "", "pattern=", "pattern", "\\<", "\\<=", "\\>", "\\>=", "\\", "length=", "length<=", "length>=", "length<", "length", "Length=1" };
        values.AddRange(Enumerable.Range(0, 300).Select(_ => RandomAtoms(random, 1)));
        var expected = (List<object?>)ParsingReference.Text(values)!;

        for (var i = 0; i < values.Count; i++)
        {
            var pythonValue = (bool)Tree.Get(expected[i], "complex")!;
            Assert.True(pythonValue == FacetParser.IsComplexRestriction(values[i]), $"'{values[i]}': Python says {pythonValue}.");
        }
    }

    // ---- comparison helpers ---------------------------------------------------------------------------

    private static OrderedDictionary<string, List<string>> ToDictionary(IEnumerable<(string Key, List<string> Values)> entries)
    {
        var dictionary = new OrderedDictionary<string, List<string>>();
        foreach (var (key, values) in entries) dictionary[key] = [.. values];
        return dictionary;
    }

    /// <summary>The same stages as the Python function _row_pipeline in the reference script.</summary>
    private static object? PipelineResult(IReadOnlyList<Cell> row, bool entityBased)
    {
        var dictionaries = FacetParser.RowToDictionaries(row);
        var orAnd = new List<object?>();
        var facets = new List<object?>();
        foreach (var item in dictionaries)
        {
            var split = FacetParser.SplitOrAndValues(item, entityBased);
            orAnd.Add(Tree.Pairs(split, values => Tree.Items(values, Tree.Strings)));
            facets.Add(Tree.Items(FacetParser.SplitAndValues(split), facet => Tree.Pairs(facet, Tree.Strings)));
        }
        return Tree.Obj(("dicts", Tree.Items(dictionaries, item => Tree.Pairs(item, value => (object?)value))), ("orAnd", orAnd), ("facets", facets));
    }

    /// <summary>Like the Python helper _guarded: a result, or the error text.</summary>
    private static object? Guarded(Func<object?> function)
    {
        try
        {
            return Tree.Obj(("ok", function()));
        }
        catch (ImportException ex)
        {
            // Python stops with an IndexError where C# gives a message.
            var message = ex.Message.StartsWith("The columns of one IDS facet have different numbers of AND values after", StringComparison.Ordinal)
                ? "IndexError: list index out of range"
                : "Exception: " + ex.Message;
            return Tree.Obj(("error", message));
        }
    }

    private static void AssertSameResult(object? expected, object? actual, string context)
    {
        var pythonError = Tree.Get(expected, "error") as string;
        var csharpError = Tree.Get(actual, "error") as string;
        if (pythonError is not null || csharpError is not null)
        {
            Assert.True(pythonError is not null && csharpError is not null,
                $"{context}: Python {(pythonError is null ? "returned a result" : $"failed ({pythonError})")} but C# {(csharpError is null ? "returned a result" : $"failed ({csharpError})")}.");
            Assert.True(pythonError == csharpError, $"{context}: Python failed with '{pythonError}' but C# failed with '{csharpError}'.");
            return;
        }

        var difference = Tree.FirstDifference(Tree.Get(expected, "ok"), Tree.Get(actual, "ok"));
        Assert.True(difference is null, $"{context}: {difference}");
    }

    // ---- random data ----------------------------------------------------------------------------------

    private static int Seed => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_SEED"), out var seed) ? seed : 20261017;

    private static int Runs => int.TryParse(Environment.GetEnvironmentVariable("IDS4ALL_FUZZ_RUNS"), out var runs) ? runs : 150;

    private static readonly string[] KnownColumns =
    [
        "A.Entity", "A.Property", "A.PropertySet", "A.PropertyValue", "A.PropertyDatatype", "A.Material", "A.Attribute", "A.AttributeValue",
        "A.Classification", "A.ClassificationSystem", "A.PartOfEntity", "A.PartOfRelation",
        "R.Entity", "R.Description.Entity", "R.Property", "R.PropertySet", "R.PropertyValue", "R.PropertyDatatype", "R.PropertyURI",
        "R.Cardinality", "R.Description.Property", "R.Material", "R.MaterialURI", "R.Attribute", "R.AttributeValue",
        "R.Classification", "R.ClassificationSystem", "R.ClassificationURI", "R.PartOfEntity", "R.PartOfRelation",
        "Phase", "Role", "Usecase", "SpecificationName", "SpecificationCardinality", "SpecificationIfcVersion",
    ];

    private static readonly string[] Atoms =
    [
        "", " ", "x", "y", "Wall", "IfcWall", "IFCWALL.USERDEFINED", "IfcWall.", ".X", "a.b.c", "_none_", "_MISSING_",
        "pattern=^a|b$", "pattern=a", "\\<5", "\\>=3", "length=5", "length<=3", "required", "optional", "ifclabel", "ifcRelAggregates",
        "1", "ä", "  pad  ", "P1", "P2", "IFC4", "IFC2X3",
    ];

    private static readonly string[] Separators = ["|", "|", "\\&", "\\&", DelimiterMergedRows, " | ", ""];

    private static string RandomAtoms(Random random, int maxParts, bool includeMergeDelimiter = true)
    {
        var separators = includeMergeDelimiter ? Separators : Separators.Where(s => s != DelimiterMergedRows).ToArray();
        var parts = Enumerable.Range(0, random.Next(1, maxParts + 1)).Select(_ => Atoms[random.Next(Atoms.Length)]).ToList();
        var text = parts[0];
        for (var i = 1; i < parts.Count; i++) text += separators[random.Next(separators.Length)] + parts[i];
        return text;
    }

    /// <summary>A cell of a sheet. The delimiter of merged rows cannot be part of an XML file, so sheets never contain it.</summary>
    private static TestCell? RandomSheetCell(Random random) => random.Next(10) switch
    {
        0 or 1 => null,
        2 => TestCell.Number(random.Next(3).ToString()),
        _ => T(RandomAtoms(random, 3, includeMergeDelimiter: false)),
    };

    private static SyntheticRow RandomRow(Random random, string[] columns)
    {
        var isList = random.Next(3) > 0;
        var length = isList ? random.Next(1, 4) : 1;
        var cells = columns.Select(column =>
            new Cell(column, Enumerable.Range(0, length).Select(_ => random.Next(4) == 0 ? Atoms[random.Next(Atoms.Length)] : RandomAtoms(random, 3)).ToList()))
            .ToList();
        return new SyntheticRow(cells, isList);
    }

    private static string RandomEntity(Random random)
    {
        var names = new[] { "IfcWall", "IfcSlab", "IFCWALL", "ifcwall", "Wood", "" };
        var types = new[] { "", ".USERDEFINED", ".X", ".Y", ".A.B", "." };
        return names[random.Next(names.Length)] + types[random.Next(types.Length)];
    }

    private static string Describe(SyntheticRow row, bool entityBased) =>
        $"{(entityBased ? "entity-based " : "")}row {string.Join("; ", row.Cells.Select(cell => $"{cell.Column}=[{string.Join(", ", cell.Values.Select(v => Tree.Describe(v)))}]"))}";
}
