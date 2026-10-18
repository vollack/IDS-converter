using IDS4ALL.Core.Ids;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Core.Model;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Tests.Merging;

public sealed class FacetFactoryTests
{
    private static FacetData Facet(params (string Key, string[] Values)[] entries)
    {
        var data = new FacetData();
        foreach (var (key, values) in entries) data[key] = [.. values];
        return data;
    }

    private static Facet Single(FacetData data, PropertyDescriptions? descriptions = null) =>
        Assert.Single(FacetFactory.Create([data], descriptions ?? new PropertyDescriptions()));

    private static IdsValue PropertyValueOf(string value, string? datatype = null)
    {
        var data = Facet((PropertySet, ["s"]), (Property, ["p"]), (PropertyValue, [value]));
        if (datatype is not null) data[PropertyDatatype] = [datatype];
        return ((PropertyFacet)Single(data)).Value!;
    }

    [Fact]
    public void SeveralValues_BecomeAnEnumeration_WithoutRepeatedValues()
    {
        var facet = (EntityFacet)Single(Facet((Entity, ["IFCWALL", "IFCSLAB", "IFCWALL"])));

        var enumeration = Assert.IsType<EnumerationRestriction>(facet.Name);
        Assert.Equal(["IFCWALL", "IFCSLAB"], enumeration.Options);
    }

    [Fact]
    public void RepeatedValue_BecomesASimpleValue()
    {
        var facet = (EntityFacet)Single(Facet((Entity, ["IFCWALL", "IFCWALL"])));

        Assert.Equal(new SimpleValue("IFCWALL"), facet.Name);
    }

    [Theory]
    [InlineData("TRUE", "true")]
    [InlineData("False", "false")]
    [InlineData("Truely", "Truely")]
    public void Booleans_AreLowerCase(string value, string expected) =>
        Assert.Equal(new SimpleValue(expected), PropertyValueOf(value));

    [Fact]
    public void Pattern_KeepsTheTextAfterTheKey() =>
        Assert.Equal(new PatternRestriction("^W[0-9]+$"), PropertyValueOf("pattern=^W[0-9]+$"));

    [Theory]
    [InlineData("\\<=5", BoundKind.MaxInclusive, 5.0)]
    [InlineData("\\<5,5", BoundKind.MaxExclusive, 5.5)]
    [InlineData("\\>=-1", BoundKind.MinInclusive, -1.0)]
    [InlineData("\\> 2", BoundKind.MinExclusive, 2.0)]
    public void Bounds_UseTheDoubleBase(string value, BoundKind kind, double bound) =>
        Assert.Equal(new BoundRestriction(kind, bound, "double"), PropertyValueOf(value));

    [Fact]
    public void Bounds_KeepTheBaseOfTheDataType() =>
        Assert.Equal(new BoundRestriction(BoundKind.MinInclusive, 3, "integer"), PropertyValueOf("\\>=3", "IFCINTEGER"));

    [Theory]
    [InlineData("length=5", LengthKind.Length, 5)]
    [InlineData("length>=3.9", LengthKind.MinLength, 3)]
    [InlineData("length<=8", LengthKind.MaxLength, 8)]
    public void Lengths_AreWholeNumbers(string value, LengthKind kind, int length) =>
        Assert.Equal(new LengthRestriction(kind, length), PropertyValueOf(value));

    [Fact]
    public void EnumerationOfAPropertyValue_UsesTheBaseOfTheDataType()
    {
        var facet = (PropertyFacet)Single(Facet((PropertySet, ["s"]), (Property, ["p"]), (PropertyValue, ["1", "2"]), (PropertyDatatype, ["IFCREAL"])));

        var enumeration = Assert.IsType<EnumerationRestriction>(facet.Value);
        Assert.Equal(["1", "2"], enumeration.Options);
        Assert.Equal("double", enumeration.Base);
    }

    [Fact]
    public void UnknownDataType_FailsWhenAValueFollows()
    {
        var data = Facet((PropertySet, ["s"]), (Property, ["p"]), (PropertyValue, ["1"]), (PropertyDatatype, ["IFCNOTHING"]));

        Assert.Throws<ImportException>(() => FacetFactory.Create([data], new PropertyDescriptions()));
    }

    [Fact]
    public void Number_ThatIsNoNumber_Fails() =>
        Assert.Throws<ImportException>(() => PropertyValueOf("\\<abc"));

    [Theory]
    [InlineData(PredefinedType)]
    [InlineData(PropertyValue)]
    [InlineData(AttributeValue)]
    [InlineData(PartOfRelation)]
    [InlineData(ClassificationUri)]
    public void FacetWithoutItsMainParameter_Fails(string key)
    {
        var exception = Assert.Throws<ImportException>(() => FacetFactory.Create([Facet((key, ["x"]))], new PropertyDescriptions()));

        Assert.StartsWith("Incomplete IDS facet: ", exception.Message);
    }

    [Fact]
    public void FacetWithOnlyACardinalityOrAMaterialUri_IsSkipped()
    {
        var facets = FacetFactory.Create([Facet((RequirementCardinality, ["required"])), Facet((MaterialUri, ["http://x"]))], new PropertyDescriptions());

        Assert.Empty(facets);
    }

    [Fact]
    public void DataType_LosesSpaces()
    {
        var facet = (PropertyFacet)Single(Facet((PropertySet, ["s"]), (Property, ["p"]), (PropertyDatatype, ["IFC LABEL"])));

        Assert.Equal("IFCLABEL", facet.DataType);
    }

    [Fact]
    public void Description_IsFoundByPropertySetOrByName()
    {
        var descriptions = new PropertyDescriptions();
        descriptions.Set("s1", "p", "first");
        descriptions.Set("s2", "p", "second");

        Assert.Equal("first", ((PropertyFacet)Single(Facet((PropertySet, ["s1"]), (Property, ["p"])), descriptions)).Instructions);
        Assert.Equal("second", ((PropertyFacet)Single(Facet((PropertySet, ["s3"]), (Property, ["p"])), descriptions)).Instructions);
        Assert.Null(((PropertyFacet)Single(Facet((PropertySet, ["s1"]), (Property, ["q"])), descriptions)).Instructions);
    }

    [Fact]
    public void Description_IsNotFoundForSeveralPropertyNames()
    {
        var descriptions = new PropertyDescriptions();
        descriptions.Set("s", "p", "first");

        var facet = (PropertyFacet)Single(Facet((PropertySet, ["s"]), (Property, ["p", "q"])), descriptions);

        Assert.Null(facet.Instructions);
    }

    [Fact]
    public void ParameterThatIsPlainTextInTheFacet_CannotHaveSeveralValues() =>
        Assert.Throws<ImportException>(() => FacetFactory.Create([Facet((PropertySet, ["s"]), (Property, ["p"]), (PropertyUri, ["a", "b"]))], new PropertyDescriptions()));
}
