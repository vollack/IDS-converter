using IDS4ALL.Core.Import;
using IDS4ALL.Core.Parsing;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Tests.Parsing;

public sealed class FacetParserTests
{
    private static Cell C(string column, params string[] values) => new(column, values);

    private static OrderedDictionary<string, string> Dict(params (string Key, string Value)[] pairs)
    {
        var dictionary = new OrderedDictionary<string, string>();
        foreach (var (key, value) in pairs) dictionary[key] = value;
        return dictionary;
    }

    private static OrderedDictionary<string, List<string>> Lists(params (string Key, string[] Values)[] pairs)
    {
        var dictionary = new OrderedDictionary<string, List<string>>();
        foreach (var (key, values) in pairs) dictionary[key] = [.. values];
        return dictionary;
    }

    private static string Show(OrderedDictionary<string, List<string>> dictionary) =>
        "{" + string.Join("; ", dictionary.Select(pair => $"{pair.Key}=[{string.Join(",", pair.Value)}]")) + "}";

    private static string Show(OrderedDictionary<string, string> dictionary) =>
        "{" + string.Join("; ", dictionary.Select(pair => $"{pair.Key}={pair.Value}")) + "}";

    private static string Show(OrderedDictionary<string, List<List<string>>> dictionary) =>
        "{" + string.Join("; ", dictionary.Select(pair => $"{pair.Key}=[{string.Join(",", pair.Value.Select(or => "[" + string.Join(",", or) + "]"))}]")) + "}";

    // ---- RowToDictionary ------------------------------------------------------------------------------

    [Fact]
    public void RowToDictionary_SplitsAtBothDelimitersAndRemovesDuplicates()
    {
        var result = FacetParser.RowToDictionary([C("Phase", "P1|P2\\&P3|P1"), C("Role", "Architect")]);

        Assert.Equal("{Phase=[P1,P2,P3]; Role=[Architect]}", Show(result));
    }

    [Fact]
    public void RowToDictionary_JoinsAggregatedValuesAndDropsNoneAndMissing()
    {
        var result = FacetParser.RowToDictionary([
            C("Phase", "P1", "P2"), C("Role", KeywordNone), C("Usecase", KeywordMissing), C("Other", $"{KeywordMissing}|x")]);

        Assert.Equal("{Phase=[P1,P2]; Other=[x]}", Show(result));
    }

    [Fact]
    public void RowToDictionary_StripsOnlyTheWholeValue()
    {
        var result = FacetParser.RowToDictionary([C("Phase", "  a | b  ")]);

        Assert.Equal("{Phase=[a , b]}", Show(result));
    }

    [Fact]
    public void RowToDictionary_StripsLikePython()
    {
        // Python counts the group separator U+001F as white space.
        var result = FacetParser.RowToDictionary([C("Phase", DelimiterMergedRows + "a" + DelimiterMergedRows)]);

        Assert.Equal("{Phase=[a]}", Show(result));
    }

    // ---- RowToDictionaries ----------------------------------------------------------------------------

    [Fact]
    public void RowToDictionaries_CreatesOneDictionaryForEachMergedRow()
    {
        var result = FacetParser.RowToDictionaries([
            C("R.Property", "a", "b", "c"), C("R.PropertySet", "s", "s", KeywordMissing)]);

        Assert.Equal(["{R.Property=a; R.PropertySet=s}", "{R.Property=b; R.PropertySet=s}", "{R.Property=c}"], result.Select(Show));
    }

    [Fact]
    public void RowToDictionaries_RemovesARealSubset()
    {
        var result = FacetParser.RowToDictionaries([C("R.Property", "a", "a"), C("R.PropertySet", "s", KeywordMissing)]);

        Assert.Equal(["{R.Property=a; R.PropertySet=s}"], result.Select(Show));
    }

    [Fact]
    public void RowToDictionaries_KeepsEqualDictionaries()
    {
        var result = FacetParser.RowToDictionaries([C("A.Entity", "IFCWALL", "IFCWALL")]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void RowToDictionaries_WithOnlyMissingValues_ReturnsEmptyDictionaries()
    {
        var result = FacetParser.RowToDictionaries([C("A.Entity", KeywordMissing), C("A.Property", KeywordMissing)]);

        Assert.Equal(["{}"], result.Select(Show));
    }

    // ---- SplitOrAndValues -----------------------------------------------------------------------------

    [Fact]
    public void SplitOrAndValues_NestsOrValuesInAndValues()
    {
        var result = FacetParser.SplitOrAndValues(Dict(("R.Property", "a|b\\&c"), ("R.PropertySet", "s\\&t")), false);

        Assert.Equal("{Property=[[a,b],[c]]; PropertySet=[[s],[t]]}", Show(result));
    }

    [Fact]
    public void SplitOrAndValues_RepeatsASingleCardinalityForAllAndValues()
    {
        var result = FacetParser.SplitOrAndValues(Dict(("R.Property", "a\\&b"), ("R.Cardinality", "required")), false);

        Assert.Equal("{Property=[[a],[b]]; Cardinality=[[required],[required]]}", Show(result));
    }

    [Fact]
    public void SplitOrAndValues_WithDifferentNumbersOfAndValues_Throws()
    {
        var exception = Assert.Throws<ImportException>(() =>
            FacetParser.SplitOrAndValues(Dict(("R.Property", "a\\&b"), ("R.PropertySet", "s")), false));

        Assert.Equal("Number of AND values (seperated by \\&) is invalid for PropertySet s. All columns of one IDS facet require the same number of AND values. Required number of AND values: 2", exception.Message);
    }

    [Fact]
    public void SplitOrAndValues_ChangesDatatypeAndRelationToUpperCase()
    {
        var result = FacetParser.SplitOrAndValues(Dict(("R.PropertyDatatype", "ifcLabel"), ("A.PartOfRelation", "ifcrelaggregates")), false);

        Assert.Equal("{PropertyDatatype=[[IFCLABEL]]; PartOfRelation=[[IFCRELAGGREGATES]]}", Show(result));
    }

    [Fact]
    public void SplitOrAndValues_KeepsAPatternWhole_AndSplitsMergedRows()
    {
        var merged = $"pattern=a|b{DelimiterMergedRows}x|y{DelimiterMergedRows}pattern=c";

        var result = FacetParser.SplitOrAndValues(Dict(("R.PropertyValue", merged)), false);

        Assert.Equal("{PropertyValue=[[pattern=a|b,x,y,pattern=c]]}", Show(result));
    }

    [Fact]
    public void SplitOrAndValues_WithEntityBasedApplicability_RemovesComplexRestrictionsFromEnumerations()
    {
        var merged = $"pattern=a{DelimiterMergedRows}\\<5{DelimiterMergedRows}x";

        Assert.Equal("{AttributeValue=[[x]]}", Show(FacetParser.SplitOrAndValues(Dict(("A.AttributeValue", merged)), true)));
        Assert.Equal("{AttributeValue=[[pattern=a,\\<5,x]]}", Show(FacetParser.SplitOrAndValues(Dict(("A.AttributeValue", merged)), false)));
        Assert.Equal("{AttributeValue=[[pattern=a]]}", Show(FacetParser.SplitOrAndValues(Dict(("A.AttributeValue", "pattern=a")), true)));
    }

    [Fact]
    public void SplitOrAndValues_OmitsMissingNoneAndEmptyValues()
    {
        var result = FacetParser.SplitOrAndValues(Dict(
            ("R.Property", "a\\&\\&b"), ("R.PropertySet", $"{KeywordMissing}\\&|\\&c"), ("R.PropertyURI", KeywordNone), ("R.Material", KeywordMissing)), false);

        Assert.Equal("{Property=[[a],[b]]; PropertySet=[[,],[c]]}", Show(result));
    }

    // ---- SplitAndValues -------------------------------------------------------------------------------

    [Fact]
    public void SplitAndValues_CreatesOneDictionaryForEachAndValue()
    {
        var input = new OrderedDictionary<string, List<List<string>>>
        {
            ["Property"] = [["a", "b"], ["c"]],
            ["PropertySet"] = [["s"], ["t"]],
        };

        var result = FacetParser.SplitAndValues(input);

        Assert.Equal(["{Property=[a,b]; PropertySet=[s]}", "{Property=[c]; PropertySet=[t]}"], result.Select(Show));
    }

    [Fact]
    public void SplitAndValues_WithEmptyInput_ReturnsNothing()
    {
        Assert.Empty(FacetParser.SplitAndValues([]));
    }

    [Fact]
    public void SplitAndValues_WithColumnsOfDifferentLength_Throws()
    {
        var input = new OrderedDictionary<string, List<List<string>>>
        {
            ["Property"] = [["a"], ["b"]],
            ["PropertySet"] = [["s"]],
        };

        Assert.Throws<ImportException>(() => FacetParser.SplitAndValues(input));
    }

    // ---- SeparateValue --------------------------------------------------------------------------------

    [Fact]
    public void SeparateValue_SplitsEntityAndPredefinedType()
    {
        var dictionary = Lists((Entity, ["IfcWall.USERDEFINED"]), (Property, ["p"]));

        FacetParser.SeparateValue(dictionary, Entity, PredefinedType, ".");

        Assert.Equal("{Entity=[IFCWALL]; Property=[p]; PredefinedType=[USERDEFINED]}", Show(dictionary));
    }

    [Fact]
    public void SeparateValue_KeepsTheCaseOfOtherKeys()
    {
        var dictionary = Lists((Material, ["Wood.Oak"]));

        FacetParser.SeparateValue(dictionary, Material, "Second", ".");

        Assert.Equal("{Material=[Wood]; Second=[Oak]}", Show(dictionary));
    }

    [Fact]
    public void SeparateValue_IgnoresValuesWithMoreThanOneDelimiter()
    {
        var dictionary = Lists((Entity, ["a.b.c"]));

        FacetParser.SeparateValue(dictionary, Entity, PredefinedType, ".");

        Assert.Equal("{Entity=[A]}", Show(dictionary));
    }

    [Fact]
    public void SeparateValue_WithoutTheKey_ChangesNothing()
    {
        var dictionary = Lists((Property, ["a.b"]));

        FacetParser.SeparateValue(dictionary, Entity, PredefinedType, ".");

        Assert.Equal("{Property=[a.b]}", Show(dictionary));
    }

    [Fact]
    public void SeparateValue_WithDifferentEntitiesAndPredefinedTypes_Throws()
    {
        var dictionary = Lists((Entity, ["IfcWall.X", "IfcSlab.Y"]));

        var exception = Assert.Throws<ImportException>(() => FacetParser.SeparateValue(dictionary, Entity, PredefinedType, "."));

        Assert.EndsWith("Affected Entity Facet: Entities: ['IFCWALL', 'IFCSLAB'] PredefinedTypes: ['X', 'Y']", exception.Message);
    }

    [Fact]
    public void SeparateValue_WithAndWithoutPredefinedType_Throws()
    {
        var dictionary = Lists((Entity, ["IfcWall.X", "IfcWall"]));

        var exception = Assert.Throws<ImportException>(() => FacetParser.SeparateValue(dictionary, Entity, PredefinedType, "."));

        Assert.EndsWith("Affected values: ['IfcWall.X', 'IfcWall']", exception.Message);
    }

    // ---- GenerateCombinations -------------------------------------------------------------------------

    private static string Show(List<List<OrderedDictionary<string, List<string>>>> combinations) =>
        string.Join(" | ", combinations.Select(combination => string.Join(" + ", combination.Select(Show))));

    [Fact]
    public void GenerateCombinations_WithoutDictionaries_ReturnsOneEmptyCombination()
    {
        var result = FacetParser.GenerateCombinations([]);

        Assert.Empty(Assert.Single(result));
    }

    [Fact]
    public void GenerateCombinations_CreatesOneDictionaryForEachOrValue()
    {
        var result = FacetParser.GenerateCombinations([Lists(("Entity", ["a", "b"]))]);

        Assert.Equal("{Entity=[a]} | {Entity=[b]}", Show(result));
    }

    [Fact]
    public void GenerateCombinations_ChangesTheLastKeyFirst()
    {
        var result = FacetParser.GenerateCombinations([Lists(("Entity", ["a", "b"]), ("Property", ["x", "y"]))]);

        Assert.Equal("{Entity=[a]; Property=[x]} | {Entity=[a]; Property=[y]} | {Entity=[b]; Property=[x]} | {Entity=[b]; Property=[y]}", Show(result));
    }

    [Fact]
    public void GenerateCombinations_CombinesTheDictionaries()
    {
        var result = FacetParser.GenerateCombinations([Lists(("Entity", ["a", "b"])), Lists(("Property", ["x", "y"])), []]);

        Assert.Equal(
            "{Entity=[a]} + {Property=[x]} + {} | {Entity=[a]} + {Property=[y]} + {} | {Entity=[b]} + {Property=[x]} + {} | {Entity=[b]} + {Property=[y]} + {}",
            Show(result));
    }

    [Fact]
    public void GenerateCombinations_SharesTheDictionariesBetweenCombinations()
    {
        var result = FacetParser.GenerateCombinations([Lists(("Entity", ["a", "b"])), Lists(("Property", ["x"]))]);

        Assert.Same(result[0][1], result[1][1]);
        Assert.NotSame(result[0][0], result[1][0]);
    }

    // ---- IsComplexRestriction -------------------------------------------------------------------------

    [Theory]
    [InlineData("pattern=a|b", true)]
    [InlineData("\\<5", true)]
    [InlineData("\\<=5", true)]
    [InlineData("\\>5", true)]
    [InlineData("\\>=5", true)]
    [InlineData("length=5", true)]
    [InlineData("length<=5", true)]
    [InlineData("length>=5", true)]
    [InlineData("length5", false)]
    [InlineData("length<5", false)]
    [InlineData("Pattern=a", false)]
    [InlineData(" pattern=a", false)]
    [InlineData("<5", false)]
    [InlineData("", false)]
    public void IsComplexRestriction_ChecksTheStart(string value, bool expected)
    {
        Assert.Equal(expected, FacetParser.IsComplexRestriction(value));
    }
}
