using IDS4ALL.Core.Import;

namespace IDS4ALL.Tests.Import;

public sealed class SheetColumnsTests
{
    [Fact]
    public void From_SortsColumnsIntoFacetLists()
    {
        string[] all =
        [
            "A.Entity", "A.PropertySet", "A.Property", "A.PropertyValue", "A.PropertyDatatype", "A.Material",
            "A.Attribute", "A.AttributeValue", "A.Classification", "A.ClassificationSystem", "A.PartOfEntity", "A.PartOfRelation",
            "R.Entity", "R.Description.Entity", "R.Property", "R.PropertySet", "R.PropertyValue", "R.PropertyDatatype",
            "R.PropertyURI", "R.Cardinality", "R.Description.Property", "R.Material", "R.MaterialURI", "R.Attribute",
            "R.AttributeValue", "R.Classification", "R.ClassificationSystem", "R.ClassificationURI", "R.PartOfEntity", "R.PartOfRelation",
            "Phase", "Role", "Usecase", "SpecificationName", "SpecificationCardinality", "SpecificationIfcVersion",
            "Remarks", "Unnamed: 40",
        ];

        var columns = SheetColumns.From(all, false, "Sheet");

        Assert.Equal(["A.Entity"], columns.ApplicabilityEntity);
        Assert.Equal(["A.Property", "A.PropertySet", "A.PropertyValue", "A.PropertyDatatype"], columns.ApplicabilityProperty);
        Assert.Equal(["A.Attribute", "A.AttributeValue"], columns.ApplicabilityAttribute);
        Assert.Equal(["R.Entity", "R.Description.Entity"], columns.RequirementEntity);
        Assert.Equal(["R.Property", "R.PropertySet", "R.PropertyValue", "R.PropertyDatatype", "R.PropertyURI", "R.Cardinality", "R.Description.Property"],
            columns.RequirementProperty);
        Assert.Equal(["R.Material", "R.MaterialURI", "R.Cardinality"], columns.RequirementMaterial);
        Assert.Equal(["Phase", "Role", "Usecase"], columns.General);
        Assert.Equal(["SpecificationCardinality", "SpecificationIfcVersion", "SpecificationName"], columns.Specification);
        Assert.DoesNotContain("Remarks", columns.Relevant);
        Assert.Equal(columns.Relevant.Count, columns.Relevant.Distinct().Count());
        Assert.Equal(36, columns.Relevant.Count);
    }

    [Fact]
    public void From_WithEntityBasedApplicability_DropsTheOtherApplicabilityColumns()
    {
        string[] all = ["A.Entity", "A.Property", "A.PropertySet", "A.Material", "A.Attribute", "A.AttributeValue", "A.PartOfEntity", "R.Property"];

        var columns = SheetColumns.From(all, true, "Sheet");

        Assert.Equal(["A.Entity"], columns.ApplicabilityEntity);
        Assert.Empty(columns.ApplicabilityProperty);
        Assert.Empty(columns.ApplicabilityMaterial);
        Assert.Empty(columns.ApplicabilityPartOf);
        Assert.Equal(["A.Attribute", "A.AttributeValue"], columns.ApplicabilityAttribute);
        Assert.Equal(["R.Property"], columns.RequirementProperty);
    }

    [Fact]
    public void From_WithRepeatedColumnName_ThrowsAndNamesTheColumn()
    {
        var exception = Assert.Throws<ImportException>(() => SheetColumns.From(["A.Entity", "R.Property", "A.Entity.1"], false, "Sheet"));

        Assert.Equal("Column names must be unique. The following column names occur multiple times: A.Entity", exception.Message);
    }

    [Fact]
    public void From_WithoutKnownColumn_Throws()
    {
        var exception = Assert.Throws<ImportException>(() => SheetColumns.From(["Name", "Unnamed: 1"], false, "Data"));

        Assert.Equal("No known columns found in sheet \"Data\". Check the sheet name, the column names, and the number of skipped rows.", exception.Message);
    }

    [Fact]
    public void From_WithOnlyEntityBasedRemovedColumns_Throws()
    {
        Assert.Throws<ImportException>(() => SheetColumns.From(["A.Property", "A.Material"], true, "Data"));
    }
}
